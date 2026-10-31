using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Based.Core;
using Based.Runtime;
using Based.UI;
using DooDesch.Localization;
using S1API.Money;

// Payments holds static state and loads its journal once, so each check is its own process.
// A check with several steps runs them in one journal folder, which models a game restart.
(string Name, string[] Steps)[] checks =
{
    ("normal purchase debits once and ignores a replayed authorization", new[] { "normal" }),
    ("lost acknowledgement settles on retry without a second debit", new[] { "lost-ack" }),
    ("post-debit refusal refunds once", new[] { "refund-race" }),
    ("insufficient cash shows a status and frees the reservation", new[] { "insufficient" }),
    ("readback mismatch reconciles with a banner scoped to its save", new[] { "readback" }),
    ("restart keeps Debited and resends its acknowledgement", new[] { "crash-seed", "crash-read" }),
    ("restart keeps Refunded and resends its acknowledgement", new[] { "refunded-restart" }),
    ("restart freezes DebitStarted without blocking a new purchase", new[] { "debit-started-restart" }),
    ("another peer cannot use the buyer receipt", new[] { "payer" }),
    ("absent payer is released after 60 s and a late acknowledgement is refunded", new[] { "payer-gone" }),
    ("same payer with a new request releases the old reservation", new[] { "same-payer" }),
    ("acknowledgement of an unknown command is refunded", new[] { "unknown-ack" }),
    ("retransmits do not fire the state change", new[] { "retransmit-quiet" }),
    ("corrupt journal recovers from its backup", new[] { "corrupt-with-backup" }),
    ("corrupt journal without backup blocks only purchases", new[] { "corrupt-no-backup" }),
    ("failed journal write blocks purchases and recovers", new[] { "write-fails" }),
    ("failed write after the debit still acknowledges", new[] { "write-fails-after-debit" }),
    ("failed debit with a failed write stays reconciled without throwing", new[] { "debit-throws" }),
    ("save mismatch declines a requested entry and ends the resends", new[] { "session-mismatch" }),
    ("journal prunes old and excess settled entries", new[] { "prune" }),
    ("refund accepts the cash cap and an upward readback", new[] { "refund-readback" }),
    ("refusals are localized and raw errors stay out of the plan", new[] { "refusals" }),
    ("reason codes map to player text", new[] { "reasons" }),
};
if (args.Length == 0) return RunAll(checks);

const string Session = "durable-save-id";
string scenario = args[0];
DateTime now = DateTime.UtcNow;
var engine = new UpgradeEngine(new[] { new BaseDefinition("b", "property", new[] {
    new UpgradeDefinition("a", 100, 500, 4, Array.Empty<string>(), Array.Empty<ConstructionPhase>(), Array.Empty<UpgradeEffect>()),
    new UpgradeDefinition("c", 100, 500, 4, Array.Empty<string>(), Array.Empty<ConstructionPhase>(), Array.Empty<UpgradeEffect>()) }) }, new Host());
int stateChanges = 0;
Payments.Clock = () => now;
string StatePath = Payments.JournalPath + ".test-state.json";
void Boot() { Text.Register(); Payments.Initialize(engine, () => stateChanges++); Payments.SetSession(Session); }
void Check(bool pass, string message) { if (!pass) throw new Exception(message); }
List<PaymentJournalEntry> Journal() => JsonSerializer.Deserialize<List<PaymentJournalEntry>>(File.ReadAllText(Payments.JournalPath))!;
void Seed(params PaymentJournalEntry[] entries) { Directory.CreateDirectory(Path.GetDirectoryName(Payments.JournalPath)!); File.WriteAllText(Payments.JournalPath, JsonSerializer.Serialize(entries)); }
PaymentJournalEntry Entry(string id, LocalPaymentState state, string session = Session, string upgrade = "a") => new()
{
    SessionId = session, Command = new(id, "b", upgrade, PurchaseKind.Rights, "buyer"), State = state, Amount = state == LocalPaymentState.Requested ? 0 : 100, ReceiptId = "receipt-" + id, CreatedUtc = now
};
void Later(int seconds) { now = now.AddSeconds(seconds); Payments.Tick(); }
string Refused(Action action) { try { action(); } catch (InvalidOperationException e) { return e.Message; } throw new Exception("Expected a refusal."); }
string HostResult(PurchaseCommand command, string session, string reason) => "BASED-PAY1:" + JsonSerializer.Serialize(new PaymentPacket
    { Op = "result", SessionId = session, Command = command, Result = new(PurchaseStatus.Rejected, reason, 0) });
const string Banner = "Eine Zahlung konnte nicht bestätigt werden.";
const string JournalDown = "Das Zahlungsjournal ist nicht verfügbar.";

switch (scenario)
{
    case "normal":
        Boot();
        int replayValidations = 0;
        Payments.ValidatePurchase = _ => ++replayValidations > 2 ? "site-changed-after-commit" : null;
        Payments.Request(PurchaseKind.Rights, "b", "a");
        Check(Money.Balance == 900 && Money.Changes == 1, "Normal purchase did not debit once.");
        Check(engine.GetUpgrade("b", "a").Stage == UpgradeStage.RightsOwned && Journal()[0].State == LocalPaymentState.Settled, "Normal purchase did not confirm.");
        Check(Payments.StatusText == "Zahlung bestätigt.", "Unexpected status: " + Payments.StatusText);
        NativeBus.ReplayHost(NativeBus.LastAuthorization);
        Check(Money.Balance == 900 && Money.Changes == 1, "Repeated authorization debited again.");
        Check(Journal()[0].State == LocalPaymentState.Settled, "A settled command changed state after site conditions changed.");
        break;
    case "lost-ack":
        Boot();
        NativeBus.DropAcknowledgements = true;
        Payments.Request(PurchaseKind.Rights, "b", "a");
        string ack = NativeBus.LastClient;
        Check(Money.Balance == 900 && engine.GetUpgrade("b", "a").Stage == UpgradeStage.RightsMissing, "Lost acknowledgement applied early.");
        Payments.ResetForMenu(); Payments.SetSession(Session);
        Check(Payments.StatusText == "Eine unbestätigte Zahlung wird erneut versucht.", "Unexpected status: " + Payments.StatusText);
        NativeBus.DropAcknowledgements = false;
        NativeBus.ReplayClient(ack); NativeBus.ReplayClient(ack);
        Check(Money.Balance == 900 && Money.Changes == 1 && Journal()[0].State == LocalPaymentState.Settled, "Retry repeated debit or lost settlement.");
        break;
    case "refund-race":
        Boot();
        int validations = 0;
        Payments.ValidatePurchase = _ => ++validations > 1 ? "site-no-longer-safe" : null;
        Payments.Request(PurchaseKind.Rights, "b", "a");
        Check(Money.Balance == 1000 && Money.Changes == 2, "Rejected post-debit purchase was not refunded once.");
        Check(engine.GetUpgrade("b", "a").Stage == UpgradeStage.RightsMissing && Journal()[0].State == LocalPaymentState.Declined, "Rejected payment applied or ended pending.");
        Check(Payments.StatusText == "Zahlung zurückerstattet.", "Unexpected status: " + Payments.StatusText);
        NativeBus.ReplayClient(NativeBus.LastClient);
        Check(Money.Balance == 1000 && Money.Changes == 2, "Duplicate refund acknowledgement repeated refund.");
        break;
    case "insufficient":
        Boot();
        Money.Balance = 50;
        Payments.Request(PurchaseKind.Rights, "b", "a");
        Check(Money.Balance == 50 && Money.Changes == 0 && Journal()[0].State == LocalPaymentState.Declined, "Insufficient cash changed money or left an open entry.");
        Check(Payments.StatusText == "Nicht genug Bargeld.", "Unexpected status: " + Payments.StatusText);
        Check(engine.PendingPurchases().Count == 0 && engine.ExportState().Purchases.Count == 0, "A declined payment stayed stored on the host.");
        Money.Balance = 500;
        Payments.Request(PurchaseKind.Rights, "b", "a");
        Check(Money.Balance == 400 && engine.GetUpgrade("b", "a").Stage == UpgradeStage.RightsOwned, "A decline blocked the next attempt.");
        break;
    case "readback":
        Boot();
        Money.SuppressMutation = true;
        Payments.Request(PurchaseKind.Rights, "b", "a");
        NativeBus.ReplayHost(NativeBus.LastAuthorization);
        Check(Money.Changes == 1 && Money.Balance == 1000 && engine.GetUpgrade("b", "a").Stage == UpgradeStage.RightsMissing, "Readback mismatch was treated as a paid purchase.");
        Check(Payments.StatusText == Banner, "Readback mismatch is not visible: " + Payments.StatusText);
        Payments.SetSession("another-save");
        Check(Payments.StatusText != Banner, "The banner appeared in another save.");
        Payments.SetSession(Session);
        Check(Payments.StatusText == Banner, "The banner left its own save.");
        break;
    case "crash-seed":
        Boot();
        NativeBus.DropAcknowledgements = true;
        Payments.Request(PurchaseKind.Rights, "b", "a");
        Check(Money.Balance == 900 && Money.Changes == 1, "Crash seed did not persist pending debit.");
        File.WriteAllText(StatePath, JsonSerializer.Serialize(engine.ExportState()));
        break;
    case "crash-read":
        Money.Balance = 900;
        engine.RestoreState(JsonSerializer.Deserialize<BasedState>(File.ReadAllText(StatePath))!);
        Boot();
        Check(Journal()[0].State == LocalPaymentState.Debited, "Process restart froze a completed debit.");
        Check(Payments.StatusText != Banner, "A completed debit raised the reconcile banner.");
        Payments.Tick();
        Check(Money.Balance == 900 && Money.Changes == 0, "Crash recovery changed native cash.");
        Check(engine.GetUpgrade("b", "a").Stage == UpgradeStage.RightsOwned && Journal()[0].State == LocalPaymentState.Settled, "The resent acknowledgement did not settle.");
        break;
    case "refunded-restart":
        var overpaid = Entry("refunded", LocalPaymentState.Refunded);
        engine.BeginPurchase(overpaid.Command);
        engine.AcknowledgePayment("refunded", new(true, overpaid.ReceiptId, "buyer", 120));
        Seed(overpaid);
        Boot();
        Check(Journal()[0].State == LocalPaymentState.Refunded, "Process restart froze a completed refund.");
        Payments.Tick();
        Check(Journal()[0].State == LocalPaymentState.Declined && Money.Changes == 0, "The resent refund acknowledgement did not settle.");
        Check(engine.ExportState().Purchases.Count == 0, "The host kept the settled refund.");
        break;
    case "debit-started-restart":
        Seed(Entry("started", LocalPaymentState.DebitStarted), Entry("stuck", LocalPaymentState.RefundStarted, upgrade: "c"), Entry("elsewhere", LocalPaymentState.DebitStarted, session: "another-save"));
        Boot();
        Check(Journal().All(e => e.State == LocalPaymentState.ReconcileRequired), "An interrupted cash change was not frozen.");
        Check(Payments.StatusText == Banner, "Interrupted debit shows no banner.");
        int sent = NativeBus.SentToHost;
        Later(10);
        Check(NativeBus.SentToHost == sent, "A frozen entry was resent.");
        Payments.Request(PurchaseKind.Rights, "b", "a");
        Check(Money.Balance == 900 && engine.GetUpgrade("b", "a").Stage == UpgradeStage.RightsOwned, "A reconcile entry blocked a new purchase.");
        Check(Payments.StatusText == Banner, "The banner of the other upgrade closed too early.");
        Payments.Request(PurchaseKind.Rights, "b", "c");
        Check(Payments.StatusText == "Zahlung bestätigt.", "A later purchase did not close the banner: " + Payments.StatusText);
        Check(Journal().Count(e => e.State == LocalPaymentState.ReconcileRequired) == 3, "A reconcile entry left the journal.");
        break;
    case "payer":
        Boot();
        NativeBus.DropAcknowledgements = true;
        Payments.Request(PurchaseKind.Rights, "b", "a");
        NativeBus.ReplayClient(NativeBus.LastClient, peer: 2);
        Check(engine.GetUpgrade("b", "a").Stage == UpgradeStage.RightsMissing, "Another authenticated peer could use the buyer receipt.");
        break;
    case "payer-gone":
        Boot();
        NativeBus.DropAcknowledgements = true;
        Payments.Request(PurchaseKind.Rights, "b", "a");
        Check(Money.Balance == 900 && engine.PendingPurchases().Count == 1, "Seed did not leave a pending reservation.");
        var blocked = new PurchaseCommand("rival", "b", "a", PurchaseKind.Rights, "other");
        Check(engine.BeginPurchase(blocked).Reason == "purchase-pending", "The reservation did not block another player.");
        Later(1);
        Check(engine.PendingPurchases().Count == 1, "Reservation released although the payer is connected.");
        Later(120);
        Check(engine.PendingPurchases().Count == 1, "Reservation released while the payer stayed connected.");
        NativeBus.ConnectedPlayers.Remove("buyer");
        Later(30);
        Later(29);
        Check(engine.PendingPurchases().Count == 1, "Reservation released before 60 s.");
        int changesBeforeRelease = stateChanges;
        Later(2);
        Check(engine.PendingPurchases().Count == 0, "Reservation of an absent payer stayed.");
        Check(stateChanges == changesBeforeRelease + 1, "Release did not report a state change.");
        Check(Money.Balance == 900, "Release moved cash on its own.");
        NativeBus.ConnectedPlayers.Add("buyer");
        NativeBus.DropAcknowledgements = false;
        Later(4);
        Check(Money.Balance == 1000 && Money.Changes == 2, "Late acknowledgement was not refunded.");
        Check(Journal()[0].State == LocalPaymentState.Declined && Payments.StatusText == "Zahlung zurückerstattet.", "Late refund did not settle.");
        Check(engine.GetUpgrade("b", "a").Stage == UpgradeStage.RightsMissing, "Late acknowledgement applied a released purchase.");
        break;
    case "same-payer":
        Boot();
        engine.BeginPurchase(new PurchaseCommand("abandoned", "b", "a", PurchaseKind.Rights, "buyer"));
        Payments.Request(PurchaseKind.Rights, "b", "a");
        Check(engine.GetUpgrade("b", "a").Stage == UpgradeStage.RightsOwned && Money.Balance == 900, "A new request from the payer stayed blocked.");
        Check(engine.FindPurchase("abandoned") == null, "The abandoned reservation stayed.");
        break;
    case "unknown-ack":
        Seed(Entry("unknown", LocalPaymentState.Debited));
        Boot();
        Later(1);
        Check(Money.Balance == 1100 && Money.Changes == 1, "Acknowledgement of an unknown command was not refunded once.");
        Later(4);
        Check(Money.Changes == 1 && Journal()[0].State == LocalPaymentState.Declined, "Unknown command did not settle.");
        break;
    case "retransmit-quiet":
        Boot();
        NativeBus.DropAcknowledgements = true;
        Payments.Request(PurchaseKind.Rights, "b", "a");
        string request = "BASED-PAY1:" + JsonSerializer.Serialize(new PaymentPacket { Op = "request", SessionId = Session, Command = engine.PendingPurchases()[0].Command });
        string lastAck = NativeBus.LastClient;
        int afterRequest = stateChanges;
        Check(afterRequest == 1, "The first request did not report a state change.");
        NativeBus.ReplayClient(request);
        Later(4); Later(4);
        Check(stateChanges == afterRequest, "Retransmitting a pending request fired the state change.");
        NativeBus.ReplayClient(lastAck);
        int afterApply = stateChanges;
        Check(afterApply == afterRequest + 1, "Applying a purchase did not report a state change.");
        NativeBus.ReplayClient(lastAck); NativeBus.ReplayClient(request);
        Check(stateChanges == afterApply, "Retransmitting a settled purchase fired the state change.");
        break;
    case "corrupt-with-backup":
        Seed(Entry("open", LocalPaymentState.Debited), Entry("done", LocalPaymentState.Settled, upgrade: "c"));
        File.Move(Payments.JournalPath, Payments.JournalPath + ".bak");
        File.WriteAllText(Payments.JournalPath, "{ not json");
        Boot();
        Check(File.Exists(Payments.JournalPath + ".corrupt"), "The broken journal was not kept for diagnosis.");
        Check(Journal().Single(e => e.Command.CommandId == "open").State == LocalPaymentState.ReconcileRequired, "A backup entry resumed although it may be one step behind.");
        Check(Journal().Single(e => e.Command.CommandId == "done").State == LocalPaymentState.Settled, "A settled backup entry changed.");
        Payments.Tick();
        Payments.Request(PurchaseKind.Rights, "b", "a");
        Check(engine.GetUpgrade("b", "a").Stage == UpgradeStage.RightsOwned, "The recovered journal does not allow purchases.");
        break;
    case "corrupt-no-backup":
        Directory.CreateDirectory(Path.GetDirectoryName(Payments.JournalPath)!);
        File.WriteAllText(Payments.JournalPath, "{ not json");
        Boot();
        Payments.ResetForMenu(); Payments.SetSession(Session); Payments.SetSession("another-save"); Payments.Tick();
        Check(Payments.StatusText == JournalDown, "Unexpected status: " + Payments.StatusText);
        Check(Refused(() => Payments.Request(PurchaseKind.Rights, "b", "a")) == JournalDown, "Purchase was not refused with player text.");
        Check(File.ReadAllText(Payments.JournalPath) == "{ not json", "The unreadable journal was overwritten.");
        Check(Money.Changes == 0, "A blocked journal moved cash.");
        break;
    case "write-fails":
        Boot();
        Directory.CreateDirectory(Path.GetDirectoryName(Payments.JournalPath)!);
        Directory.CreateDirectory(Payments.JournalPath + ".tmp");
        Check(Refused(() => Payments.Request(PurchaseKind.Rights, "b", "a")) == JournalDown, "Purchase was not refused with player text.");
        Check(Payments.StatusText == JournalDown && Money.Changes == 0, "A failed write allowed a purchase.");
        Payments.Tick(); Payments.ResetForMenu(); Payments.SetSession(Session); Later(6);
        Directory.Delete(Payments.JournalPath + ".tmp");
        Later(6);
        Check(Payments.StatusText != JournalDown, "The journal did not recover after the write path returned.");
        Payments.Request(PurchaseKind.Rights, "b", "a");
        Check(engine.GetUpgrade("b", "a").Stage == UpgradeStage.RightsOwned, "A recovered journal does not allow purchases.");
        break;
    case "write-fails-after-debit":
        Boot();
        Money.BeforeChange = () => Directory.CreateDirectory(Payments.JournalPath + ".tmp");
        Payments.Request(PurchaseKind.Rights, "b", "a");
        Check(Money.Balance == 900 && engine.GetUpgrade("b", "a").Stage == UpgradeStage.RightsOwned, "A failed journal write stopped the acknowledgement.");
        Check(Payments.StatusText == JournalDown, "A failed write is not visible.");
        Directory.Delete(Payments.JournalPath + ".tmp");
        Later(6);
        Check(Journal()[0].State == LocalPaymentState.Settled, "The retried write did not store the settlement.");
        break;
    case "debit-throws":
        Boot();
        Money.BeforeChange = () => { Directory.CreateDirectory(Payments.JournalPath + ".tmp"); throw new ArgumentException("Native cash failure."); };
        Payments.Request(PurchaseKind.Rights, "b", "a");
        Check(Money.Changes == 0 && engine.GetUpgrade("b", "a").Stage == UpgradeStage.RightsMissing, "A failed debit counted as paid.");
        Money.BeforeChange = null;
        Directory.Delete(Payments.JournalPath + ".tmp");
        Later(6);
        Check(Journal()[0].State == LocalPaymentState.ReconcileRequired && Journal()[0].Detail.Contains("Native cash failure."), "The original failure was lost.");
        break;
    case "session-mismatch":
        Boot();
        NativeBus.DropEverything = true;
        Payments.Request(PurchaseKind.Rights, "b", "a");
        Check(Journal()[0].State == LocalPaymentState.Requested, "Seed did not leave a requested entry.");
        NativeBus.ReplayHost(HostResult(Journal()[0].Command, "host-save", "save-session-mismatch"));
        Check(Journal()[0].State == LocalPaymentState.Declined, "A mismatched requested entry stayed open.");
        Check(Payments.StatusText == "Diese Zahlung gehört zu einem anderen Spielstand.", "Unexpected status: " + Payments.StatusText);
        int beforeResend = NativeBus.SentToHost;
        Later(4); Later(4);
        Check(NativeBus.SentToHost == beforeResend, "A declined entry was resent.");
        break;
    case "prune":
        var seeded = Enumerable.Range(0, 60).Select(i => Entry("settled-" + i, i % 2 == 0 ? LocalPaymentState.Settled : LocalPaymentState.Declined)).ToList();
        for (int i = 0; i < seeded.Count; i++) seeded[i].CreatedUtc = now.AddMinutes(-i);
        var ancient = Entry("ancient", LocalPaymentState.Settled); ancient.CreatedUtc = now.AddDays(-31);
        var ancientOpen = Entry("ancient-open", LocalPaymentState.Debited, session: "another-save"); ancientOpen.CreatedUtc = now.AddDays(-400);
        var ancientFrozen = Entry("ancient-frozen", LocalPaymentState.ReconcileRequired, session: "another-save"); ancientFrozen.CreatedUtc = now.AddDays(-400);
        Seed(seeded.Append(ancient).Append(ancientOpen).Append(ancientFrozen).ToArray());
        Boot();
        var pruned = Journal();
        Check(pruned.Count(e => e.State == LocalPaymentState.Settled || e.State == LocalPaymentState.Declined) == 50, "Settled entries were not cut to 50.");
        Check(pruned.Any(e => e.Command.CommandId == "settled-0") && pruned.All(e => e.Command.CommandId != "settled-59"), "Pruning kept the wrong entries.");
        Check(pruned.All(e => e.Command.CommandId != "ancient"), "An old settled entry stayed.");
        Check(pruned.Any(e => e.Command.CommandId == "ancient-open") && pruned.Any(e => e.Command.CommandId == "ancient-frozen"), "Pruning removed an unresolved entry.");
        break;
    case "refund-readback":
        Boot();
        Money.Balance = 1E+09f;
        Money.Cap = 1E+09f;
        int capValidations = 0;
        Payments.ValidatePurchase = _ => ++capValidations > 1 ? "site-no-longer-safe" : null;
        Payments.Request(PurchaseKind.Rights, "b", "a");
        Check(Journal()[0].State == LocalPaymentState.Declined, "A refund capped by the game was not accepted: " + Journal()[0].State);
        Money.Balance = 1000;
        Money.Cap = float.MaxValue;
        Money.RefundBonus = 7;
        capValidations = 0;
        Payments.Request(PurchaseKind.Rights, "b", "a");
        Check(Money.Balance == 1007 && Journal()[1].State == LocalPaymentState.Declined, "An upward refund readback was not accepted.");
        Money.RefundBonus = -50;
        capValidations = 0;
        Payments.Request(PurchaseKind.Rights, "b", "a");
        Check(Journal()[2].State == LocalPaymentState.ReconcileRequired, "A short refund was accepted.");
        break;
    case "refusals":
        Boot();
        Payments.SetSession(""); Payments.SetSession(" ");
        NativeBus.IsConnected = false;
        Check(Refused(() => Payments.Request(PurchaseKind.Rights, "b", "a")) == "Die Zahlungsverbindung ist nicht bereit.", "Disconnected refusal is not player text.");
        NativeBus.IsConnected = true;
        NativeBus.StartThrows = true;
        Check(Refused(() => Payments.Request(PurchaseKind.Rights, "b", "a")) == "Der Kauf konnte nicht gestartet werden.", "A raw transport error reached the plan.");
        NativeBus.StartThrows = false;
        NativeBus.LocalPlayerId = "";
        Check(Refused(() => Payments.Request(PurchaseKind.Rights, "b", "a")) == "Der zahlende Spieler ist nicht verfügbar.", "Missing player refusal is not player text.");
        NativeBus.LocalPlayerId = "buyer";
        NativeBus.DropEverything = true;
        Payments.Request(PurchaseKind.Rights, "b", "a");
        Check(Refused(() => Payments.Request(PurchaseKind.Rights, "b", "a")) == "Für diesen Kauf läuft bereits eine Zahlung.", "Open payment refusal is not player text.");
        Payments.ResetForMenu();
        Check(Refused(() => Payments.Request(PurchaseKind.Rights, "b", "a")) == "Zahlungen sind noch nicht bereit.", "Missing session refusal is not player text.");
        break;
    case "reasons":
        Boot();
        foreach (var (reason, expected) in new[] {
            ("purchase-pending", "Dafür läuft noch eine andere Zahlung."),
            ("construction-active", "Hier läuft bereits ein Bau."),
            ("receipt-required", "Der Host hat diesen Kauf abgelehnt."),
            ("Räume den Durchbruch frei.", "Räume den Durchbruch frei.") })
        {
            NativeBus.DropEverything = true;
            Payments.Request(PurchaseKind.Rights, "b", "a");
            var command = Journal().Last().Command;
            NativeBus.ReplayHost(HostResult(command, Session, reason));
            Check(Payments.StatusText == expected, reason + " became: " + Payments.StatusText);
        }
        break;
    default: throw new ArgumentException("Unknown scenario: " + scenario);
}
Check(L10n.Missing.Count == 0, "Text without a German entry: " + string.Join(" | ", L10n.Missing));
Console.WriteLine("PASS adapter " + scenario);
return 0;

static int RunAll((string Name, string[] Steps)[] all)
{
    int passed = 0;
    foreach (var (name, steps) in all)
    {
        string folder = Directory.CreateTempSubdirectory("based-adapter-").FullName;
        string? failure = null;
        foreach (string step in steps)
        {
            failure = RunStep(step, folder);
            if (failure != null) break;
        }
        try { Directory.Delete(folder, recursive: true); }
        catch (Exception e) { Console.WriteLine("warn could not delete " + folder + ": " + e.Message); }
        if (failure == null) { passed++; Console.WriteLine("PASS " + name); }
        else Console.Error.WriteLine("FAIL " + name + ":\n" + failure);
    }
    Console.WriteLine($"Adapter checks: {passed}/{all.Length} passed.");
    return passed == all.Length ? 0 : 1;
}

static string? RunStep(string step, string folder)
{
    var start = new ProcessStartInfo(Environment.ProcessPath!, step) { RedirectStandardOutput = true, RedirectStandardError = true };
    start.Environment["BASED_TEST_USERDATA"] = folder;
    var output = new StringBuilder();
    using var process = new Process { StartInfo = start };
    process.OutputDataReceived += (_, line) => output.AppendLine(line.Data);
    process.ErrorDataReceived += (_, line) => output.AppendLine(line.Data);
    process.Start();
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();
    if (!process.WaitForExit(30000)) { process.Kill(); return "Timed out in step " + step + ".\n" + output; }
    process.WaitForExit();
    return process.ExitCode == 0 ? null : "Step " + step + " exited " + process.ExitCode + ".\n" + output;
}

sealed class Host : IAuthority { public bool CanMutate => true; }
