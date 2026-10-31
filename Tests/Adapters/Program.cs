using Based.Core;
using Based.Runtime;
using S1API.Money;

if (args.Length == 0) throw new ArgumentException("Expected an adapter scenario.");
string scenario = args[0];
var engine = new UpgradeEngine(new[] { new BaseDefinition("b", "property", new[] {
    new UpgradeDefinition("a", 100, 500, 4, Array.Empty<string>(), Array.Empty<ConstructionPhase>(), Array.Empty<UpgradeEffect>()) }) }, new Host());
Payments.Initialize(engine, () => { });
Payments.SetSession("durable-save-id");
PurchaseStatus? lastResult = null;
Payments.ResultChanged += (_, result) => lastResult = result.Status;
void Check(bool pass, string message) { if (!pass) throw new Exception(message); }

switch (scenario)
{
    case "normal":
        int replayValidations = 0;
        Payments.ValidatePurchase = _ => ++replayValidations > 2 ? "site-changed-after-commit" : null;
        Payments.Request(PurchaseKind.Rights, "b", "a");
        Check(Money.Balance == 900 && Money.Changes == 1, "Normal purchase did not debit once.");
        Check(engine.GetUpgrade("b", "a").Stage == UpgradeStage.RightsOwned && lastResult == PurchaseStatus.Applied, "Normal purchase did not confirm.");
        NativeBus.ReplayHost(NativeBus.LastAuthorization);
        Check(Money.Balance == 900 && Money.Changes == 1, "Repeated authorization debited again.");
        Check(lastResult == PurchaseStatus.Applied, "A settled command changed result after site conditions changed.");
        break;
    case "lost-ack":
        NativeBus.DropAcknowledgements = true;
        Payments.Request(PurchaseKind.Rights, "b", "a");
        string ack = NativeBus.LastClient;
        Check(Money.Balance == 900 && engine.GetUpgrade("b", "a").Stage == UpgradeStage.RightsMissing, "Lost acknowledgement applied early.");
        Payments.ResetForMenu(); Payments.SetSession("durable-save-id");
        NativeBus.DropAcknowledgements = false;
        NativeBus.ReplayClient(ack); NativeBus.ReplayClient(ack);
        Check(Money.Balance == 900 && Money.Changes == 1 && lastResult == PurchaseStatus.Applied, "Retry repeated debit or lost settlement.");
        break;
    case "refund-race":
        int validations = 0;
        Payments.ValidatePurchase = _ => ++validations > 1 ? "site-no-longer-safe" : null;
        Payments.Request(PurchaseKind.Rights, "b", "a");
        Check(Money.Balance == 1000 && Money.Changes == 2, "Rejected post-debit purchase was not refunded once.");
        Check(engine.GetUpgrade("b", "a").Stage == UpgradeStage.RightsMissing && lastResult == PurchaseStatus.Rejected, "Rejected payment applied or ended pending.");
        NativeBus.ReplayClient(NativeBus.LastClient);
        Check(Money.Balance == 1000 && Money.Changes == 2, "Duplicate refund acknowledgement repeated refund.");
        break;
    case "insufficient":
        Money.Balance = 50;
        Payments.Request(PurchaseKind.Rights, "b", "a");
        Check(Money.Balance == 50 && Money.Changes == 0 && lastResult == PurchaseStatus.Rejected, "Insufficient cash changed money or left unresolved reservation.");
        break;
    case "readback":
        Money.SuppressMutation = true;
        Payments.Request(PurchaseKind.Rights, "b", "a");
        NativeBus.ReplayHost(NativeBus.LastAuthorization);
        Check(Money.Changes == 1 && Money.Balance == 1000 && engine.GetUpgrade("b", "a").Stage == UpgradeStage.RightsMissing, "Readback mismatch was treated as a paid purchase.");
        Check(Payments.StatusText.Contains("wiederhergestellt"), "Readback mismatch is not visible.");
        break;
    case "crash-seed":
        NativeBus.DropAcknowledgements = true;
        Payments.Request(PurchaseKind.Rights, "b", "a");
        Check(Money.Balance == 900 && Money.Changes == 1, "Crash seed did not persist pending debit.");
        break;
    case "crash-read":
        Check(Payments.StatusText.Contains("wiederhergestellt"), "Process restart did not retain an unresolved debit.");
        Check(Money.Balance == 1000 && Money.Changes == 0, "Crash recovery changed native cash without proof.");
        bool blocked = false;
        try { Payments.Request(PurchaseKind.Rights, "b", "a"); } catch (InvalidOperationException) { blocked = true; }
        Check(blocked, "Ambiguous crash payment permitted a second debit.");
        break;
    case "payer":
        NativeBus.DropAcknowledgements = true;
        Payments.Request(PurchaseKind.Rights, "b", "a");
        NativeBus.ReplayClient(NativeBus.LastClient, peer: 2);
        Check(engine.GetUpgrade("b", "a").Stage == UpgradeStage.RightsMissing, "Another authenticated peer could use the buyer receipt.");
        break;
    default: throw new ArgumentException("Unknown scenario: " + scenario);
}
Console.WriteLine("PASS adapter " + scenario);
return 0;

sealed class Host : IAuthority { public bool CanMutate => true; }
