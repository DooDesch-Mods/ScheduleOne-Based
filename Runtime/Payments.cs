using System.Text.Json;
using Based.Core;
using Based.UI;
using MelonLoader;
using MelonLoader.Utils;
using S1API.Money;

namespace Based.Runtime;

// Cash belongs to the payer's machine, the purchase decision to the host. The journal records every
// local cash step before it happens, so a restart can tell a finished debit from an interrupted one.
public static class Payments
{
    private const string Prefix = "BASED-PAY1:";
    private const int KeptSettledEntries = 50;
    private const string InsufficientCashDetail = "Insufficient local cash.";
    // CashInstance.SetBalance clamps every balance to its MAX_BALANCE, so a refund can land below the sum.
    private const float CashCap = 1E+09f;
    private static readonly TimeSpan SettledEntryLifetime = TimeSpan.FromDays(30);
    private static readonly TimeSpan AbsentPayerGrace = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ResendInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan JournalRetryInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ReleaseScanInterval = TimeSpan.FromSeconds(1);
    private static UpgradeEngine? _engine;
    private static Action? _stateChanged;
    private static bool _wired;
    private static bool _loaded;
    // The journal on disk could not be read. It stays untouched so nothing recoverable gets overwritten.
    private static bool _journalBlocked;
    // The last write failed. Tick retries it, and the next successful write clears this.
    private static bool _journalWriteFailed;
    private static DateTime _nextJournalRetry;
    private static DateTime _nextReleaseScan;
    private static string _lastTickFailure = "";
    private static string _session = "";
    private static string _status = "";
    private static List<PaymentJournalEntry> _journal = new();
    private static readonly Dictionary<string, DateTime> LastSent = new();
    private static readonly HashSet<string> WarnedWaiting = new();
    private static readonly Dictionary<string, (string Reason, int Count)> SendFailures = new();
    private static readonly Dictionary<string, DateTime> PayerLastSeen = new();

    // Replaced by the adapter checks to step over the resend and release timeouts.
    internal static Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;
    public static Func<PurchaseCommand, string?>? ValidatePurchase { get; set; }
    public static string StatusText => JournalUnavailable ? Text.T("The payment journal is unavailable.") :
        _journal.Any(NeedsAttention) ? Text.T("A payment could not be confirmed.") : _status;
    public static string JournalPath => Path.Combine(MelonEnvironment.UserDataDirectory, "Based", "payment-journal.json");
    private static bool JournalUnavailable => _journalBlocked || _journalWriteFailed;

    public static void Initialize(UpgradeEngine engine, Action stateChanged)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _stateChanged = stateChanged ?? throw new ArgumentNullException(nameof(stateChanged));
        _engine.MigrationWarning -= LogMigrationWarning;
        _engine.MigrationWarning += LogMigrationWarning;
        LoadJournal();
        if (_wired) return;
        NativeBus.HostReceived += OnHostPacket;
        NativeBus.ClientReceived += OnClientPacket;
        _wired = true;
    }

    // Entries keep their own session and simply wait while another save is open.
    public static void SetSession(string saveId)
    {
        if (string.IsNullOrWhiteSpace(saveId)) { MelonLogger.Warning("[Based/Pay] session unchanged: the save ID is empty."); return; }
        if (_session == saveId) return;
        _session = saveId;
        LastSent.Clear();
        PayerLastSeen.Clear();
        _status = _journal.Any(e => e.SessionId == saveId && IsOpen(e)) ? Text.T("An unconfirmed payment is being retried.") : "";
        MelonLogger.Msg("[Based/Pay] session set: " + saveId);
    }

    // Every refusal is an InvalidOperationException with player text, which the plan shows as its notice.
    public static string Request(PurchaseKind kind, string baseId, string upgradeId)
    {
        try
        {
            EnsureReady();
            string payer = NativeBus.LocalPlayerId;
            if (string.IsNullOrWhiteSpace(payer)) throw Refuse("the local player has no ID", "The paying player is not available.");
            // A reconcile entry does not count: the host reservation is what prevents a double charge.
            if (_journal.Any(e => e.SessionId == _session && e.Command.PayerId == payer && e.Command.BaseId == baseId &&
                e.Command.UpgradeId == upgradeId && e.Command.Kind == kind && IsOpen(e)))
                throw Refuse("an open payment exists for " + baseId + "/" + upgradeId, "This purchase already has an open payment.");
            var command = new PurchaseCommand(Guid.NewGuid().ToString("N"), baseId, upgradeId, kind, payer);
            var entry = new PaymentJournalEntry { SessionId = _session, Command = command, State = LocalPaymentState.Requested, CreatedUtc = Clock() };
            _journal.Add(entry);
            if (!PersistJournal())
            {
                _journal.Remove(entry);
                throw Refuse("the request could not be journaled", "The payment journal is unavailable.");
            }
            _status = Text.T("Checking the payment with the host.");
            Send(entry);
            return command.CommandId;
        }
        catch (PurchaseRefused) { throw; }
        catch (Exception e)
        {
            MelonLogger.Error("[Based/Pay] purchase request failed: " + e);
            throw new InvalidOperationException(Text.T("The purchase could not be started."), e);
        }
    }

    // Runs every frame. Nothing may escape: the caller treats an exception as a broken session and blocks saving.
    public static void Tick()
    {
        try
        {
            if (_engine == null || string.IsNullOrEmpty(_session)) return;
            if (_journalWriteFailed && Clock() >= _nextJournalRetry) PersistJournal();
            if (!NativeBus.IsConnected) return;
            ReleaseAbandonedPayments();
            ResendOpenEntries();
            _lastTickFailure = "";
        }
        catch (Exception e)
        {
            // Logged once per distinct failure, since this runs every frame.
            string failure = e.GetType().Name + ": " + e.Message;
            if (failure != _lastTickFailure) MelonLogger.Error("[Based/Pay] tick failed; payment retries continue: " + e);
            _lastTickFailure = failure;
        }
    }

    public static void ResetForMenu()
    {
        int open = _journal.Count(e => e.SessionId == _session && IsOpen(e));
        if (open > 0) MelonLogger.Warning("[Based/Pay] menu entered with " + open + " open payment(s); journal retained.");
        _session = "";
        LastSent.Clear();
        PayerLastSeen.Clear();
        _status = "";
    }

    private static void LogMigrationWarning(string message) => MelonLogger.Warning("[Based/Save] " + message);

    private static void EnsureReady()
    {
        if (_engine == null) throw Refuse("payments are not initialized", "Payments are not ready yet.");
        if (string.IsNullOrEmpty(_session)) throw Refuse("no save session is set", "Payments are not ready yet.");
        if (JournalUnavailable) throw Refuse("the journal is unavailable", "The payment journal is unavailable.");
        if (!NativeBus.Start() || !NativeBus.IsConnected) throw Refuse("the transport is not connected", "The payment connection is not ready.");
    }

    private static PurchaseRefused Refuse(string logReason, string playerKey)
    {
        MelonLogger.Warning("[Based/Pay] purchase refused: " + logReason + ".");
        return new PurchaseRefused(Text.T(playerKey));
    }

    private static bool IsSettled(PaymentJournalEntry entry) => entry.State == LocalPaymentState.Settled || entry.State == LocalPaymentState.Declined;
    // Open entries are still being worked on. A reconcile entry needs a person and is left alone.
    private static bool IsOpen(PaymentJournalEntry entry) => !IsSettled(entry) && entry.State != LocalPaymentState.ReconcileRequired;
    private static bool NeedsAttention(PaymentJournalEntry entry) => entry.SessionId == _session &&
        entry.State == LocalPaymentState.ReconcileRequired && !entry.AttentionCleared;

    // Reason codes are engine vocabulary. Anything unmapped with a space is a refusal from
    // ValidatePurchase, which is already player text.
    private static string ReasonText(string reason) => reason switch
    {
        "refunded" => Text.T("Payment refunded."),
        "payment-declined" => Text.T("Not enough cash."),
        "cancelled-before-payment" => Text.T("Purchase cancelled."),
        "save-session-mismatch" => Text.T("This payment belongs to a different save."),
        "prerequisite-incomplete" => Text.T("Finish the previous upgrade first."),
        "rights-already-owned" => Text.T("The rights are already owned."),
        "construction-unavailable" => Text.T("Construction cannot be ordered right now."),
        "construction-active" => Text.T("Construction is already running here."),
        "purchase-pending" => Text.T("Another payment for this is still open."),
        "unknown-base" or "unknown-upgrade" or "invalid-purchase-kind" => Text.T("This upgrade is not available."),
        _ => reason.Contains(' ') ? reason : Text.T("The host declined this purchase.")
    };

    private static void ResendOpenEntries()
    {
        string local = NativeBus.LocalPlayerId;
        foreach (var entry in _journal.Where(e => e.SessionId == _session && e.Command.PayerId == local && IsOpen(e)).ToArray())
        {
            // A new debit must not start while the journal cannot record it. Acknowledgements report
            // cash that already moved, so they keep going.
            if (JournalUnavailable && entry.State == LocalPaymentState.Requested) continue;
            if (LastSent.TryGetValue(entry.Command.CommandId, out var sent) && Clock() - sent < ResendInterval) continue;
            if (entry.CreatedUtc != default && Clock() - entry.CreatedUtc > TimeSpan.FromSeconds(15) && WarnedWaiting.Add(entry.Command.CommandId))
                MelonLogger.Warning("[Based/Pay] awaiting host settlement; journal retained: " + entry.Command.CommandId);
            Send(entry);
        }
    }

    // A reservation blocks that upgrade for every player, so it must not outlive its payer. Records restored
    // from a save get a full grace period, because their payer has had no chance to join yet.
    private static void ReleaseAbandonedPayments()
    {
        if (!NativeBus.IsHost || _engine == null || Clock() < _nextReleaseScan) return;
        _nextReleaseScan = Clock() + ReleaseScanInterval;
        var pending = _engine.PendingPurchases();
        foreach (string id in PayerLastSeen.Keys.Where(id => pending.All(p => p.Command.CommandId != id)).ToArray()) PayerLastSeen.Remove(id);
        // Without a local player the roster is still loading and says nothing about who is absent.
        if (pending.Count == 0 || string.IsNullOrEmpty(NativeBus.LocalPlayerId)) return;
        DateTime now = Clock();
        foreach (var record in pending)
        {
            string id = record.Command.CommandId;
            if (NativeBus.IsPlayerConnected(record.Command.PayerId) || !PayerLastSeen.TryGetValue(id, out var seen)) { PayerLastSeen[id] = now; continue; }
            if (now - seen < AbsentPayerGrace) continue;
            _engine.ReleasePurchase(id);
            PayerLastSeen.Remove(id);
            MelonLogger.Warning("[Based/Pay] released reservation of absent payer " + record.Command.PayerId + ": " + id);
            NotifyStateChanged();
        }
    }

    private static void Send(PaymentJournalEntry entry)
    {
        var packet = new PaymentPacket { SessionId = entry.SessionId, Command = entry.Command };
        switch (entry.State)
        {
            case LocalPaymentState.Requested: packet.Op = "request"; break;
            case LocalPaymentState.Debited:
                packet.Op = "ack"; packet.Acknowledgement = new(true, entry.ReceiptId, entry.Command.PayerId, entry.Amount); break;
            case LocalPaymentState.Refunded: packet.Op = "refund-ack"; packet.ReceiptId = entry.ReceiptId; break;
            case LocalPaymentState.PaymentDeclined:
            case LocalPaymentState.Declined:
                packet.Op = "ack"; packet.Acknowledgement = new(false, "", entry.Command.PayerId, 0); break;
            default:
                MelonLogger.Warning("[Based/Pay] nothing to send for " + entry.Command.CommandId + " in state " + entry.State + ".");
                return;
        }
        LastSent[entry.Command.CommandId] = Clock();
        try
        {
            NativeBus.SendToHost(Encode(packet));
            if (SendFailures.Remove(entry.Command.CommandId)) MelonLogger.Msg("[Based/Pay] transport send recovered; settlement still requires host acknowledgement: " + entry.Command.CommandId);
        }
        catch (Exception e)
        {
            string reason = e.GetType().Name + ": " + e.Message;
            var previous = SendFailures.TryGetValue(entry.Command.CommandId, out var value) ? value : (Reason: "", Count: 0);
            int count = previous.Reason == reason ? previous.Count + 1 : 1;
            SendFailures[entry.Command.CommandId] = (reason, count);
            if (count == 1 || count % 20 == 0) MelonLogger.Warning("[Based/Pay] send failed; payment retained: " + entry.Command.CommandId + " attempts=" + count + " " + reason);
        }
    }

    private static void OnHostPacket(int peer, string payload)
    {
        if (!payload.StartsWith(Prefix, StringComparison.Ordinal)) return;
        if (!NativeBus.IsHost) { MelonLogger.Warning("[Based/Pay] ignored a host-bound payment packet on a client."); return; }
        if (_engine == null) { MelonLogger.Warning("[Based/Pay] ignored a payment packet before initialization."); return; }
        var packet = Decode(payload);
        if (packet.Command == null) throw new ArgumentException("Payment packet has no command.");
        string payer = NativeBus.PeerPlayerId(peer);
        if (string.IsNullOrWhiteSpace(payer) || payer != packet.Command.PayerId)
        { MelonLogger.Warning("[Based/Pay] rejected payment with a payer outside its authenticated connection."); return; }
        // Declining is final for the client, so a host that does not know its save yet stays quiet and lets it retry.
        if (string.IsNullOrEmpty(_session))
        { MelonLogger.Warning("[Based/Pay] host save session is not known yet; the client retries: " + packet.Command.CommandId); return; }
        if (packet.SessionId != _session)
        {
            MelonLogger.Warning("[Based/Pay] payment from another save session: " + packet.Command.CommandId);
            Reply(peer, packet.Command, new(PurchaseStatus.Rejected, "save-session-mismatch", _engine.Revision));
            return;
        }
        var before = _engine.FindPurchase(packet.Command.CommandId);
        if (before != null && before.Command != packet.Command)
        {
            MelonLogger.Warning("[Based/Pay] command ID conflict: " + packet.Command.CommandId);
            Reply(peer, packet.Command, new(PurchaseStatus.Rejected, "command-id-conflict", _engine.Revision));
            return;
        }
        long revision = _engine.Revision, version = _engine.PurchaseVersion;
        PurchaseResult result;
        switch (packet.Op)
        {
            case "request":
                string? refusal = before == null ? ValidatePurchase?.Invoke(packet.Command) : null;
                result = refusal == null ? _engine.BeginPurchase(packet.Command) : new(PurchaseStatus.Rejected, refusal, _engine.Revision);
                break;
            case "ack":
                if (packet.Acknowledgement == null || packet.Acknowledgement.PayerId != payer)
                { MelonLogger.Warning("[Based/Pay] rejected acknowledgement without matching host authorization."); return; }
                // Native cash belongs to the payer. This acknowledgement trusts that authenticated peer's readback.
                if (packet.Acknowledgement.Succeeded && before?.Result.Status == PurchaseStatus.PaymentPending && ValidatePurchase?.Invoke(packet.Command) != null)
                    _engine.CancelPurchase(packet.Command.CommandId);
                result = _engine.AcknowledgePayment(packet.Command.CommandId, packet.Acknowledgement);
                if (before == null) MelonLogger.Warning("[Based/Pay] acknowledgement without a reservation answered with " + result.Reason + ": " + packet.Command.CommandId);
                break;
            case "refund-ack":
                result = _engine.AcknowledgeRefund(packet.Command.CommandId, packet.ReceiptId);
                break;
            default: throw new ArgumentException("Unknown Based payment operation: " + packet.Op);
        }
        // Retransmits arrive every few seconds and change nothing; each change resends full snapshots.
        if (_engine.Revision != revision || _engine.PurchaseVersion != version) NotifyStateChanged();
        Reply(peer, packet.Command, result);
    }

    private static void NotifyStateChanged()
    {
        try { _stateChanged?.Invoke(); }
        catch (Exception e) { MelonLogger.Error("[Based/Pay] state change handler failed: " + e); }
    }

    private static void Reply(int peer, PurchaseCommand command, PurchaseResult result)
    {
        string op = result.Status == PurchaseStatus.PaymentPending ? "authorize" : result.Status == PurchaseStatus.RefundRequired ? "refund" : "result";
        try { NativeBus.SendToPeer(peer, Encode(new PaymentPacket { Op = op, SessionId = _session, Command = command, Result = result })); }
        catch (Exception e) { MelonLogger.Warning("[Based/Pay] reply to peer " + peer + " failed; the client retries: " + e.GetType().Name + ": " + e.Message); }
    }

    private static void OnClientPacket(string payload)
    {
        if (!payload.StartsWith(Prefix, StringComparison.Ordinal)) return;
        var packet = Decode(payload);
        if (packet.Command == null || packet.Result == null) throw new ArgumentException("Incomplete host payment packet.");
        var entry = _journal.Find(e => e.Command.CommandId == packet.Command.CommandId && e.Command == packet.Command);
        if (entry == null) { MelonLogger.Warning("[Based/Pay] ignored host payment result without a local request."); return; }
        if (packet.SessionId != entry.SessionId || packet.SessionId != _session) { SettleForeignSession(entry); return; }
        if (entry.Command.PayerId != NativeBus.LocalPlayerId)
        { MelonLogger.Warning("[Based/Pay] ignored host payment result for another player: " + entry.Command.CommandId); return; }
        switch (packet.Op)
        {
            case "authorize": OnAuthorize(entry, packet.Result); return;
            case "refund": OnRefund(entry, packet.Result); return;
            case "result": OnResult(entry, packet.Result); return;
            default: throw new ArgumentException("Unknown host payment operation: " + packet.Op);
        }
    }

    // The host works on another save and will never settle this entry. Without cash in flight the
    // entry ends here, otherwise the difference needs a person.
    private static void SettleForeignSession(PaymentJournalEntry entry)
    {
        MelonLogger.Warning("[Based/Pay] host answered from another save for " + entry.Command.CommandId + " in state " + entry.State + ".");
        switch (entry.State)
        {
            case LocalPaymentState.Requested:
            case LocalPaymentState.PaymentDeclined:
            case LocalPaymentState.Refunded:
                entry.State = LocalPaymentState.Declined;
                entry.Detail = "Host answered from a different save.";
                PersistJournal();
                _status = Text.T("This payment belongs to a different save.");
                break;
            case LocalPaymentState.DebitStarted:
            case LocalPaymentState.Debited:
            case LocalPaymentState.RefundStarted:
                MarkReconcile(entry, "Payment answer belongs to a different save.");
                break;
        }
    }

    private static void OnAuthorize(PaymentJournalEntry entry, PurchaseResult result)
    {
        if (entry.State == LocalPaymentState.Requested)
        {
            var request = result.Payment;
            if (request == null || request.CommandId != entry.Command.CommandId || request.PayerId != entry.Command.PayerId || request.Amount < 0)
            {
                MelonLogger.Error("[Based/Pay] host authorization does not match purchase " + entry.Command.CommandId + "; declining it.");
                entry.State = LocalPaymentState.PaymentDeclined;
                entry.Detail = "Host authorization did not match this purchase.";
                PersistJournal();
            }
            else Debit(entry, request.Amount);
        }
        // Answered so the host can free its reservation, also when this entry ended before the authorization arrived.
        if (entry.State == LocalPaymentState.Debited || entry.State == LocalPaymentState.PaymentDeclined || entry.State == LocalPaymentState.Declined) Send(entry);
        else MelonLogger.Msg("[Based/Pay] authorization needs no answer in state " + entry.State + ": " + entry.Command.CommandId);
    }

    private static void OnRefund(PaymentJournalEntry entry, PurchaseResult result)
    {
        if (entry.State == LocalPaymentState.Debited) Refund(entry, result);
        if (entry.State == LocalPaymentState.Refunded) Send(entry);
        else MelonLogger.Warning("[Based/Pay] refund request not carried out in state " + entry.State + ": " + entry.Command.CommandId);
    }

    private static void OnResult(PaymentJournalEntry entry, PurchaseResult result)
    {
        if (result.Status == PurchaseStatus.Applied)
        {
            if (entry.State != LocalPaymentState.Debited && entry.State != LocalPaymentState.Settled && entry.State != LocalPaymentState.ReconcileRequired)
            { MarkReconcile(entry, "Host applied purchase without a confirmed local debit."); return; }
            // The host only applies after a successful acknowledgement, which also closes a reconcile entry.
            if (entry.State == LocalPaymentState.ReconcileRequired) MelonLogger.Msg("[Based/Pay] host applied a payment under reconciliation: " + entry.Command.CommandId);
            entry.State = LocalPaymentState.Settled;
            _status = Text.T("Payment confirmed.");
            ClearOlderAttention(entry);
            PersistJournal();
            return;
        }
        if (result.Status != PurchaseStatus.Rejected)
        { MelonLogger.Warning("[Based/Pay] ignored result status " + result.Status + " for " + entry.Command.CommandId); return; }
        switch (entry.State)
        {
            case LocalPaymentState.Debited:
                MarkReconcile(entry, "Host rejected a locally debited payment without refund authorization: " + result.Reason);
                return;
            case LocalPaymentState.ReconcileRequired:
            case LocalPaymentState.Settled:
                MelonLogger.Warning("[Based/Pay] rejection " + result.Reason + " ignored in state " + entry.State + ": " + entry.Command.CommandId);
                return;
        }
        MelonLogger.Msg("[Based/Pay] purchase closed: " + entry.Command.CommandId + " " + result.Reason);
        // The host only hears "declined". Only this entry knows whether cash was the reason.
        bool otherDecline = result.Reason == "payment-declined" && entry.Detail != InsufficientCashDetail;
        entry.State = LocalPaymentState.Declined;
        _status = otherDecline ? Text.T("The purchase could not be started.") : ReasonText(result.Reason);
        PersistJournal();
    }

    // Buying the same thing again ends the banner of an earlier unclear attempt. The entry stays in the
    // journal and the log, because the earlier money remains unaccounted for.
    private static void ClearOlderAttention(PaymentJournalEntry settled)
    {
        foreach (var entry in _journal.Where(e => e != settled && NeedsAttention(e) && e.Command.PayerId == settled.Command.PayerId &&
                     e.Command.BaseId == settled.Command.BaseId && e.Command.UpgradeId == settled.Command.UpgradeId && e.Command.Kind == settled.Command.Kind))
        {
            entry.AttentionCleared = true;
            MelonLogger.Warning("[Based/Pay] banner closed by a later purchase; unresolved entry kept in the journal: " + entry.Command.CommandId);
        }
    }

    private static void Debit(PaymentJournalEntry entry, int amount)
    {
        if (JournalUnavailable)
        {
            MelonLogger.Error("[Based/Pay] debit declined because the journal is unavailable: " + entry.Command.CommandId);
            entry.State = LocalPaymentState.PaymentDeclined;
            entry.Detail = "Payment journal unavailable.";
            return;
        }
        float before = Money.GetCashBalance();
        if (!float.IsFinite(before) || before < amount)
        {
            MelonLogger.Msg("[Based/Pay] debit declined; cash " + before + " for amount " + amount + ": " + entry.Command.CommandId);
            entry.State = LocalPaymentState.PaymentDeclined;
            entry.Detail = InsufficientCashDetail;
            _status = Text.T("Not enough cash.");
            PersistJournal();
            return;
        }
        entry.Amount = amount; entry.Before = before; entry.After = before - amount;
        entry.ReceiptId = Guid.NewGuid().ToString("N");
        entry.State = LocalPaymentState.DebitStarted;
        if (!PersistJournal())
        {
            // No cash has moved, so this can still end cleanly.
            entry.State = LocalPaymentState.PaymentDeclined;
            entry.Amount = 0; entry.ReceiptId = "";
            entry.Detail = "Payment journal could not record the debit.";
            return;
        }
        try
        {
            Money.ChangeCashBalance(-amount, true, true);
            float after = Money.GetCashBalance();
            if (Math.Abs(after - entry.After) > 0.01f) { MarkReconcile(entry, "Cash debit readback " + after + " differed from the expected " + entry.After + "."); return; }
        }
        catch (Exception e) { MarkReconcile(entry, "Debit failed: " + e.GetType().Name + ": " + e.Message, e); return; }
        // The cash has moved. A failed write here is retried by Tick and must not stop the acknowledgement.
        entry.State = LocalPaymentState.Debited;
        PersistJournal();
        _status = Text.T("Cash deducted, waiting for the host.");
        MelonLogger.Msg("[Based/Pay] debited " + amount + ": " + entry.Command.CommandId);
    }

    private static void Refund(PaymentJournalEntry entry, PurchaseResult result)
    {
        if (result.ReceiptId != entry.ReceiptId || result.Payment?.Amount != entry.Amount || result.Payment.PayerId != entry.Command.PayerId)
        { MarkReconcile(entry, "Refund does not match the confirmed debit receipt."); return; }
        float before = Money.GetCashBalance();
        if (!float.IsFinite(before)) { MarkReconcile(entry, "Cash balance unavailable for refund."); return; }
        entry.RefundBefore = before; entry.RefundAfter = Math.Min(before + entry.Amount, CashCap);
        entry.State = LocalPaymentState.RefundStarted;
        if (!PersistJournal())
        {
            // No cash has moved. The acknowledgement resend brings the refund request back.
            MelonLogger.Warning("[Based/Pay] refund postponed until the journal can record it: " + entry.Command.CommandId);
            entry.State = LocalPaymentState.Debited;
            return;
        }
        try
        {
            Money.ChangeCashBalance(entry.Amount, true, true);
            float after = Money.GetCashBalance();
            if (after < entry.RefundAfter - 0.01f) { MarkReconcile(entry, "Cash refund readback " + after + " is below the expected " + entry.RefundAfter + "."); return; }
            // Cash above the expected value hurts nobody, and the refund itself did land.
            if (after > entry.RefundAfter + 0.01f) MelonLogger.Warning("[Based/Pay] refund readback " + after + " is above the expected " + entry.RefundAfter + "; accepted: " + entry.Command.CommandId);
            if (before + entry.Amount > CashCap) MelonLogger.Warning("[Based/Pay] refund clipped by the cash cap at " + CashCap + ": " + entry.Command.CommandId);
        }
        catch (Exception e) { MarkReconcile(entry, "Refund failed: " + e.GetType().Name + ": " + e.Message, e); return; }
        entry.State = LocalPaymentState.Refunded;
        PersistJournal();
        MelonLogger.Msg("[Based/Pay] refunded " + entry.Amount + ": " + entry.Command.CommandId);
    }

    // Called from catch blocks and packet handlers, so it must not throw and hide the original failure.
    // PersistJournal reports a failed write by itself and never throws.
    private static void MarkReconcile(PaymentJournalEntry entry, string detail, Exception? cause = null)
    {
        entry.State = LocalPaymentState.ReconcileRequired;
        entry.Detail = detail;
        MelonLogger.Error("[Based/Pay] reconciliation required for " + entry.Command.CommandId + ": " + detail + "; journal=" + JournalPath + (cause == null ? "" : " " + cause));
        PersistJournal();
    }

    private static void LoadJournal()
    {
        if (_loaded) return;
        _loaded = true;
        string backup = JournalPath + ".bak";
        if (!File.Exists(JournalPath) && !File.Exists(backup)) return;
        try
        {
            if (!File.Exists(JournalPath)) throw new FileNotFoundException("Journal is missing but a backup exists.", JournalPath);
            _journal = ReadJournal(JournalPath);
            foreach (var entry in _journal.Where(e => e.State == LocalPaymentState.DebitStarted || e.State == LocalPaymentState.RefundStarted))
                FreezeForReconcile(entry, "Process stopped during a cash change; it is unknown whether the cash moved.");
            MelonLogger.Msg("[Based/Pay] journal loaded: " + _journal.Count + " entries.");
            PersistJournal();
            return;
        }
        catch (Exception e) { MelonLogger.Error("[Based/Pay] journal load failed; trying the backup: " + JournalPath + " " + e); }
        try
        {
            var entries = ReadJournal(backup);
            // Moved aside first, or the next write would rotate the broken file over the good backup.
            if (File.Exists(JournalPath)) File.Move(JournalPath, JournalPath + ".corrupt", overwrite: true);
            _journal = entries;
            // The backup is one write behind, so any open entry may have moved further than it shows.
            foreach (var entry in _journal.Where(IsOpen)) FreezeForReconcile(entry, "Recovered from the journal backup, which can be one step behind.");
            MelonLogger.Warning("[Based/Pay] journal recovered from backup: " + _journal.Count + " entries.");
            PersistJournal();
        }
        catch (Exception e)
        {
            _journalBlocked = true;
            MelonLogger.Error("[Based/Pay] journal backup unusable; purchases blocked until the file is repaired: " + backup + " " + e);
        }
    }

    private static List<PaymentJournalEntry> ReadJournal(string path)
    {
        var entries = JsonSerializer.Deserialize<List<PaymentJournalEntry>>(File.ReadAllText(path)) ?? throw new InvalidDataException("Journal is null.");
        if (entries.Any(e => e == null || e.Command == null || !Enum.IsDefined(typeof(LocalPaymentState), e.State) || e.Amount < 0 ||
                string.IsNullOrWhiteSpace(e.SessionId) || string.IsNullOrWhiteSpace(e.Command.CommandId)) ||
            entries.GroupBy(e => e.Command.CommandId).Any(g => g.Count() != 1)) throw new InvalidDataException("Journal identities are invalid.");
        return entries;
    }

    private static void FreezeForReconcile(PaymentJournalEntry entry, string detail)
    {
        MelonLogger.Warning("[Based/Pay] payment needs reconciliation after " + entry.State + ": " + entry.Command.CommandId + " " + detail);
        entry.State = LocalPaymentState.ReconcileRequired;
        entry.Detail = detail;
    }

    // Reports failure through its result and never throws, so a journal problem blocks purchases and
    // nothing else. Failed writes are retried from Tick.
    private static bool PersistJournal()
    {
        if (_journalBlocked) { MelonLogger.Warning("[Based/Pay] journal write skipped; the journal on disk could not be read."); return false; }
        try
        {
            PruneJournal();
            Directory.CreateDirectory(Path.GetDirectoryName(JournalPath)!);
            string temporary = JournalPath + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, _journal, new JsonSerializerOptions { WriteIndented = true });
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(JournalPath)) File.Replace(temporary, JournalPath, JournalPath + ".bak");
            else File.Move(temporary, JournalPath);
            if (_journalWriteFailed) MelonLogger.Msg("[Based/Pay] journal write recovered: " + JournalPath);
            _journalWriteFailed = false;
            return true;
        }
        catch (Exception e)
        {
            // Logged once per outage, since Tick keeps retrying.
            if (!_journalWriteFailed) MelonLogger.Error("[Based/Pay] journal write failed; purchases blocked until it recovers: " + JournalPath + " " + e);
            _journalWriteFailed = true;
            _nextJournalRetry = Clock() + JournalRetryInterval;
            return false;
        }
    }

    private static void PruneJournal()
    {
        DateTime cutoff = Clock() - SettledEntryLifetime;
        var dropped = _journal.Where(IsSettled).OrderByDescending(e => e.CreatedUtc)
            .Where((e, index) => index >= KeptSettledEntries || e.CreatedUtc < cutoff).ToHashSet();
        if (dropped.Count == 0) return;
        _journal.RemoveAll(dropped.Contains);
        foreach (var entry in dropped)
        {
            LastSent.Remove(entry.Command.CommandId);
            WarnedWaiting.Remove(entry.Command.CommandId);
            SendFailures.Remove(entry.Command.CommandId);
        }
        MelonLogger.Msg("[Based/Pay] pruned " + dropped.Count + " settled journal entries.");
    }

    private static string Encode(PaymentPacket packet) => Prefix + JsonSerializer.Serialize(packet);
    private static PaymentPacket Decode(string packet) => JsonSerializer.Deserialize<PaymentPacket>(packet[Prefix.Length..]) ?? throw new ArgumentException("Payment packet is null.");

    // Carries player text; only this type may pass through Request unchanged.
    private sealed class PurchaseRefused : InvalidOperationException
    {
        public PurchaseRefused(string playerText) : base(playerText) { }
    }
}

public enum LocalPaymentState { Requested, DebitStarted, Debited, RefundStarted, Refunded, Settled, Declined, ReconcileRequired, PaymentDeclined }
public sealed class PaymentJournalEntry
{
    public string SessionId { get; set; } = "";
    public PurchaseCommand Command { get; set; } = null!;
    public LocalPaymentState State { get; set; }
    public int Amount { get; set; }
    public string ReceiptId { get; set; } = "";
    public float Before { get; set; }
    public float After { get; set; }
    public float RefundBefore { get; set; }
    public float RefundAfter { get; set; }
    public string Detail { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public bool AttentionCleared { get; set; }
}
public sealed class PaymentPacket
{
    public string Op { get; set; } = "";
    public string SessionId { get; set; } = "";
    public PurchaseCommand? Command { get; set; }
    public PaymentAcknowledgement? Acknowledgement { get; set; }
    public PurchaseResult? Result { get; set; }
    public string ReceiptId { get; set; } = "";
}
