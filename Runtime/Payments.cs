using System.Text.Json;
using Based.Core;
using MelonLoader;
using MelonLoader.Utils;
using S1API.Money;

namespace Based.Runtime;

public static class Payments
{
    private const string Prefix = "BASED-PAY1:";
    private static UpgradeEngine? _engine;
    private static Action? _stateChanged;
    private static bool _wired;
    private static bool _loaded;
    private static bool _journalFailed;
    private static string _session = "";
    private static string _status = "";
    private static List<PaymentJournalEntry> _journal = new();
    private static readonly Dictionary<string, DateTime> LastSent = new();
    private static readonly HashSet<string> WarnedWaiting = new();
    private static readonly Dictionary<string, (string Reason, int Count)> SendFailures = new();

    public static event Action<string, PurchaseResult>? ResultChanged;
    public static Func<PurchaseCommand, string?>? ValidatePurchase { get; set; }
    public static string StatusText => _journalFailed ? "Zahlungsjournal konnte nicht gelesen oder gesichert werden." :
        _journal.Any(e => e.State == LocalPaymentState.ReconcileRequired) ? "Eine Zahlung muss wiederhergestellt werden. Keine weitere Abbuchung für diesen Auftrag." : _status;
    public static string SessionId => _session;
    public static string JournalPath => Path.Combine(MelonEnvironment.UserDataDirectory, "Based", "payment-journal.json");

    public static void Initialize(UpgradeEngine engine, Action stateChanged)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _stateChanged = stateChanged ?? throw new ArgumentNullException(nameof(stateChanged));
        LoadJournal();
        if (_wired) return;
        NativeBus.HostReceived += OnHostPacket;
        NativeBus.ClientReceived += OnClientPacket;
        _wired = true;
    }

    public static void SetSession(string saveId)
    {
        if (string.IsNullOrWhiteSpace(saveId)) throw new ArgumentException("Based payments require a stable save ID.");
        if (_session == saveId) return;
        _session = saveId;
        foreach (var entry in _journal.Where(e => e.SessionId != saveId && IsUnsettled(e)))
        {
            if (entry.State == LocalPaymentState.Debited || entry.State == LocalPaymentState.RefundStarted)
            {
                entry.State = LocalPaymentState.ReconcileRequired;
                entry.Detail = "Different save opened before payment settlement.";
                MelonLogger.Warning("[Based/Pay] unresolved payment retained for another save: " + entry.Command.CommandId);
            }
        }
        PersistJournal();
        LastSent.Clear();
    }

    public static string Request(PurchaseKind kind, string baseId, string upgradeId)
    {
        EnsureReady();
        string payer = NativeBus.LocalPlayerId;
        if (string.IsNullOrWhiteSpace(payer)) throw new InvalidOperationException("The paying player is not available.");
        if (_journal.Any(e => e.SessionId == _session && e.Command.PayerId == payer && e.Command.BaseId == baseId &&
            e.Command.UpgradeId == upgradeId && e.Command.Kind == kind && IsUnsettled(e)))
            throw new InvalidOperationException("This purchase already has an unresolved payment.");
        var command = new PurchaseCommand(Guid.NewGuid().ToString("N"), baseId, upgradeId, kind, payer);
        var entry = new PaymentJournalEntry { SessionId = _session, Command = command, State = LocalPaymentState.Requested };
        _journal.Add(entry);
        PersistJournal();
        _status = "Zahlung wird beim Host geprüft.";
        Send(entry);
        return command.CommandId;
    }

    public static void Tick()
    {
        if (_engine == null || string.IsNullOrEmpty(_session) || _journalFailed || !NativeBus.IsConnected) return;
        foreach (var entry in _journal.Where(e => e.SessionId == _session && e.Command.PayerId == NativeBus.LocalPlayerId &&
                     IsUnsettled(e) && e.State != LocalPaymentState.ReconcileRequired).ToArray())
        {
            if (LastSent.TryGetValue(entry.Command.CommandId, out var sent) && DateTime.UtcNow - sent < TimeSpan.FromSeconds(3)) continue;
            if (entry.CreatedUtc != default && DateTime.UtcNow - entry.CreatedUtc > TimeSpan.FromSeconds(15) && WarnedWaiting.Add(entry.Command.CommandId))
                MelonLogger.Warning("[Based/Pay] awaiting host settlement; journal retained: " + entry.Command.CommandId);
            Send(entry);
        }
    }

    public static void ResetForMenu()
    {
        _session = "";
        LastSent.Clear();
        int pending = _journal.Count(IsUnsettled);
        if (pending > 0) MelonLogger.Warning("[Based/Pay] menu entered with " + pending + " unresolved payment(s); journal retained.");
        _status = pending > 0 ? "Unbestätigte Zahlung bleibt gespeichert." : "";
    }

    private static void EnsureReady()
    {
        if (_engine == null || _journalFailed || string.IsNullOrEmpty(_session)) throw new InvalidOperationException("Based payments are not ready for this save.");
        if (!NativeBus.Start() || !NativeBus.IsConnected) throw new InvalidOperationException("Based payment transport is not connected.");
    }

    private static bool IsUnsettled(PaymentJournalEntry entry) => entry.State != LocalPaymentState.Settled && entry.State != LocalPaymentState.Declined;
    private static void Send(PaymentJournalEntry entry)
    {
        LastSent[entry.Command.CommandId] = DateTime.UtcNow;
        var packet = new PaymentPacket { SessionId = entry.SessionId, Command = entry.Command };
        if (entry.State == LocalPaymentState.Debited)
        { packet.Op = "ack"; packet.Acknowledgement = new(true, entry.ReceiptId, entry.Command.PayerId, entry.Amount); }
        else if (entry.State == LocalPaymentState.Refunded)
        { packet.Op = "refund-ack"; packet.ReceiptId = entry.ReceiptId; }
        else if (entry.State == LocalPaymentState.PaymentDeclined)
        { packet.Op = "ack"; packet.Acknowledgement = new(false, "", entry.Command.PayerId, 0); }
        else packet.Op = "request";
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
        if (!payload.StartsWith(Prefix, StringComparison.Ordinal) || !NativeBus.IsHost || _engine == null) return;
        var packet = Decode(payload);
        if (packet.Command == null) throw new ArgumentException("Payment packet has no command.");
        string payer = NativeBus.PeerPlayerId(peer);
        if (string.IsNullOrWhiteSpace(payer) || payer != packet.Command.PayerId)
        { MelonLogger.Warning("[Based/Pay] rejected payment with a payer outside its authenticated connection."); return; }
        if (string.IsNullOrEmpty(_session) || packet.SessionId != _session)
        {
            Reply(peer, packet.Command, new(PurchaseStatus.Rejected, "save-session-mismatch", _engine.Revision));
            return;
        }
        PurchaseResult result;
        var before = _engine.ExportState().Purchases.Find(p => p.Command.CommandId == packet.Command.CommandId);
        if (before != null && before.Command != packet.Command)
        { Reply(peer, packet.Command, new(PurchaseStatus.Rejected, "command-id-conflict", _engine.Revision)); return; }
        switch (packet.Op)
        {
            case "request":
                string? refusal = before == null ? ValidatePurchase?.Invoke(packet.Command) : null;
                result = refusal == null ? _engine.BeginPurchase(packet.Command) : new(PurchaseStatus.Rejected, refusal, _engine.Revision);
                break;
            case "ack":
                if (before == null || packet.Acknowledgement == null || packet.Acknowledgement.PayerId != payer)
                { MelonLogger.Warning("[Based/Pay] rejected acknowledgement without matching host authorization."); return; }
                // Native cash belongs to the payer. This acknowledgement trusts that authenticated peer's readback.
                if (packet.Acknowledgement.Succeeded && ValidatePurchase?.Invoke(packet.Command) != null)
                    _engine.CancelPurchase(packet.Command.CommandId);
                result = _engine.AcknowledgePayment(packet.Command.CommandId, packet.Acknowledgement);
                break;
            case "refund-ack":
                if (before == null) return;
                result = _engine.AcknowledgeRefund(packet.Command.CommandId, packet.ReceiptId);
                break;
            default: throw new ArgumentException("Unknown Based payment operation: " + packet.Op);
        }
        _stateChanged?.Invoke();
        Reply(peer, packet.Command, result);
    }

    private static void Reply(int peer, PurchaseCommand command, PurchaseResult result)
    {
        string op = result.Status == PurchaseStatus.PaymentPending ? "authorize" : result.Status == PurchaseStatus.RefundRequired ? "refund" : "result";
        NativeBus.SendToPeer(peer, Encode(new PaymentPacket { Op = op, SessionId = _session, Command = command, Result = result }));
    }

    private static void OnClientPacket(string payload)
    {
        if (!payload.StartsWith(Prefix, StringComparison.Ordinal)) return;
        var packet = Decode(payload);
        if (packet.Command == null || packet.Result == null) throw new ArgumentException("Incomplete host payment packet.");
        var entry = _journal.Find(e => e.Command.CommandId == packet.Command.CommandId && e.Command == packet.Command);
        if (entry == null) { MelonLogger.Warning("[Based/Pay] ignored host payment result without a local request."); return; }
        if (packet.SessionId != entry.SessionId || packet.SessionId != _session || entry.Command.PayerId != NativeBus.LocalPlayerId)
        {
            if (entry.State == LocalPaymentState.Debited || entry.State == LocalPaymentState.RefundStarted) MarkReconcile(entry, "Payment answer belongs to a different save or player.");
            _status = "Zahlung gehört zu einem anderen Spielstand.";
            return;
        }
        switch (packet.Op)
        {
            case "authorize":
                if (entry.State == LocalPaymentState.ReconcileRequired) return;
                if (entry.State == LocalPaymentState.Requested)
                {
                    var request = packet.Result.Payment ?? throw new ArgumentException("Host authorization has no payment request.");
                    if (request.CommandId != entry.Command.CommandId || request.PayerId != entry.Command.PayerId || request.Amount < 0)
                        throw new ArgumentException("Host authorization does not match this purchase.");
                    Debit(entry, request.Amount);
                }
                ResultChanged?.Invoke(entry.Command.CommandId, packet.Result);
                if (entry.State != LocalPaymentState.ReconcileRequired) Send(entry);
                return;
            case "refund":
                if (entry.State == LocalPaymentState.ReconcileRequired) return;
                if (entry.State == LocalPaymentState.Debited) Refund(entry, packet.Result);
                ResultChanged?.Invoke(entry.Command.CommandId, packet.Result);
                if (entry.State == LocalPaymentState.Refunded) Send(entry);
                return;
            case "result":
                if (packet.Result.Status == PurchaseStatus.Applied)
                {
                    if (entry.State != LocalPaymentState.Debited && entry.State != LocalPaymentState.Settled)
                    { MarkReconcile(entry, "Host applied purchase without a confirmed local debit."); return; }
                    entry.State = LocalPaymentState.Settled;
                    _status = "Zahlung bestätigt.";
                }
                else if (packet.Result.Status == PurchaseStatus.Rejected)
                {
                    if (entry.State == LocalPaymentState.Debited)
                    { MarkReconcile(entry, "Host rejected a locally debited payment without refund authorization."); return; }
                    if (entry.State == LocalPaymentState.ReconcileRequired) return;
                    entry.State = LocalPaymentState.Declined;
                    _status = packet.Result.Reason == "refunded" ? "Zahlung zurückerstattet." : "Auftrag abgelehnt: " + packet.Result.Reason;
                }
                PersistJournal();
                break;
            default: throw new ArgumentException("Unknown host payment operation: " + packet.Op);
        }
        ResultChanged?.Invoke(entry.Command.CommandId, packet.Result);
    }

    private static void Debit(PaymentJournalEntry entry, int amount)
    {
        if (_journalFailed) throw new InvalidOperationException("The payment journal is unavailable.");
        float before = Money.GetCashBalance();
        if (!float.IsFinite(before) || before < amount)
        {
            entry.State = LocalPaymentState.PaymentDeclined;
            entry.Detail = "Insufficient local cash.";
            PersistJournal();
            return;
        }
        entry.Amount = amount; entry.Before = before; entry.After = before - amount;
        entry.ReceiptId = Guid.NewGuid().ToString("N");
        entry.State = LocalPaymentState.DebitStarted;
        PersistJournal();
        try
        {
            Money.ChangeCashBalance(-amount, true, true);
            float after = Money.GetCashBalance();
            if (Math.Abs(after - entry.After) > 0.01f) { MarkReconcile(entry, "Cash debit readback differed from the authorized amount."); return; }
            entry.State = LocalPaymentState.Debited;
            PersistJournal();
            _status = "Geld abgebucht. Host-Bestätigung ausstehend.";
        }
        catch (Exception e) { MarkReconcile(entry, "Debit failed or journal acknowledgement failed: " + e.GetType().Name); throw; }
    }

    private static void Refund(PaymentJournalEntry entry, PurchaseResult result)
    {
        if (result.ReceiptId != entry.ReceiptId || result.Payment?.Amount != entry.Amount || result.Payment.PayerId != entry.Command.PayerId)
        { MarkReconcile(entry, "Refund does not match the confirmed debit receipt."); return; }
        float before = Money.GetCashBalance();
        if (!float.IsFinite(before)) { MarkReconcile(entry, "Cash balance unavailable for refund."); return; }
        entry.RefundBefore = before; entry.RefundAfter = before + entry.Amount;
        entry.State = LocalPaymentState.RefundStarted;
        PersistJournal();
        try
        {
            Money.ChangeCashBalance(entry.Amount, true, true);
            if (Math.Abs(Money.GetCashBalance() - entry.RefundAfter) > 0.01f)
            { MarkReconcile(entry, "Cash refund readback differed from the confirmed debit."); return; }
            entry.State = LocalPaymentState.Refunded;
            PersistJournal();
        }
        catch (Exception e) { MarkReconcile(entry, "Refund failed or journal acknowledgement failed: " + e.GetType().Name); throw; }
    }

    private static void MarkReconcile(PaymentJournalEntry entry, string detail)
    {
        entry.State = LocalPaymentState.ReconcileRequired;
        entry.Detail = detail;
        MelonLogger.Error("[Based/Pay] reconciliation required for " + entry.Command.CommandId + ": " + detail + "; journal=" + JournalPath);
        PersistJournal();
    }

    private static void LoadJournal()
    {
        if (_loaded) return;
        _loaded = true;
        if (!File.Exists(JournalPath)) return;
        try
        {
            var entries = JsonSerializer.Deserialize<List<PaymentJournalEntry>>(File.ReadAllText(JournalPath)) ?? throw new InvalidDataException("Journal is null.");
            if (entries.Any(e => e == null || e.Command == null || !Enum.IsDefined(typeof(LocalPaymentState), e.State) || e.Amount < 0 ||
                    string.IsNullOrWhiteSpace(e.SessionId) || string.IsNullOrWhiteSpace(e.Command.CommandId)) ||
                entries.GroupBy(e => e.Command.CommandId).Any(g => g.Count() != 1)) throw new InvalidDataException("Journal identities are invalid.");
            _journal = entries;
            foreach (var entry in _journal.Where(e => e.State == LocalPaymentState.DebitStarted || e.State == LocalPaymentState.Debited ||
                         e.State == LocalPaymentState.RefundStarted || e.State == LocalPaymentState.Refunded))
            {
                entry.State = LocalPaymentState.ReconcileRequired;
                entry.Detail = "Process restarted before confirmed settlement; native save and journal may have different commit points.";
                MelonLogger.Warning("[Based/Pay] retained crash recovery payment: " + entry.Command.CommandId);
            }
            PersistJournal();
        }
        catch (Exception e)
        {
            _journalFailed = true;
            MelonLogger.Error("[Based/Pay] journal load failed; cash mutations blocked: " + JournalPath + " " + e);
        }
    }

    private static void PersistJournal()
    {
        if (_journalFailed) throw new InvalidOperationException("Based payment journal is blocked after an earlier failure.");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(JournalPath)!);
            string temporary = JournalPath + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, _journal, new JsonSerializerOptions { WriteIndented = true });
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(JournalPath)) File.Replace(temporary, JournalPath, JournalPath + ".bak");
            else File.Move(temporary, JournalPath);
        }
        catch (Exception e)
        {
            _journalFailed = true;
            MelonLogger.Error("[Based/Pay] journal write failed; cash mutations blocked: " + JournalPath + " " + e);
            throw;
        }
    }

    private static string Encode(PaymentPacket packet) => Prefix + JsonSerializer.Serialize(packet);
    private static PaymentPacket Decode(string packet) => JsonSerializer.Deserialize<PaymentPacket>(packet[Prefix.Length..]) ?? throw new ArgumentException("Payment packet is null.");
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
