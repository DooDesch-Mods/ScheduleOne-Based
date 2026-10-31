using Based.Core;
using System.Text.Json;

var checks = new (string Name, Action Run)[]
{
    ("sequential upgrades retain geometry and dock effects across save/load", Sequential),
    ("four sleep events produce three phases then completion", FourMornings),
    ("duplicate commands and acknowledgements never pay or start twice", DuplicateCommands),
    ("payment decline leaves rights absent and permits a new command", DeclinedPayment),
    ("bad payment amount requires acknowledged refund", Refund),
    ("client cannot mutate host state", Authority),
    ("one active construction and one pending order", Reservations),
    ("sleep tokens are monotonic with no calendar catch-up", SleepTokens),
    ("definitions reject missing dependencies, cycles and invalid phases", Definitions),
    ("invalid restore leaves live state intact", RestoreFailure),
    ("save switch resets all state and command receipts", Reset),
    ("new definitions add defaults without erasing completed work", AddContent),
    ("pending payment survives reload without requesting a second debit", PendingRestore),
    ("cancellation retains reservation until debit or no-debit acknowledgement", Cancellation)
};
int failed = 0;
foreach (var check in checks)
{
    try { check.Run(); Console.WriteLine("PASS " + check.Name); }
    catch (Exception e) { failed++; Console.Error.WriteLine("FAIL " + check.Name + ": " + e); }
}
Console.WriteLine($"Core checks: {checks.Length - failed}/{checks.Length} passed.");
return failed == 0 ? 0 : 1;

static BaseDefinition Fixture(bool includeDock = true) => new("fixture", "isolated-property", new[]
{
    Upgrade("A", Array.Empty<string>(), "area-a", 2),
    Upgrade("B", new[] { "A" }, "area-b", 1)
}.Concat(includeDock ? new[] { Upgrade("dock", new[] { "B" }, "dock-extra", 1, "loading-dock") } : Array.Empty<UpgradeDefinition>()).ToArray());

static UpgradeDefinition Upgrade(string id, string[] prerequisites, string effect, int cycles, string kind = "space") =>
    new(id, 100, 500, cycles, prerequisites,
        cycles > 1 ? new[] { new ConstructionPhase("work", 1, "work-state") } : Array.Empty<ConstructionPhase>(),
        new[] { new UpgradeEffect(kind, effect, "ready") });

static UpgradeEngine Engine(BaseDefinition? definition = null, bool host = true) => new(new[] { definition ?? Fixture() }, new AuthorityFlag(host));
static PurchaseCommand Command(string id, string upgrade, PurchaseKind kind, long? revision = null) => new(id, "fixture", upgrade, kind, "buyer", revision);
static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; actual {actual}."); }
static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
static PurchaseResult Pay(UpgradeEngine engine, string id, string upgrade, PurchaseKind kind)
{
    var pending = engine.BeginPurchase(Command(id, upgrade, kind));
    Equal(PurchaseStatus.PaymentPending, pending.Status);
    return engine.AcknowledgePayment(id, new(true, "receipt-" + id, "buyer", pending.Payment!.Amount));
}
static void Order(UpgradeEngine engine, string upgrade, string prefix)
{
    Equal(PurchaseStatus.Applied, Pay(engine, prefix + "-rights", upgrade, PurchaseKind.Rights).Status);
    Equal(PurchaseStatus.Applied, Pay(engine, prefix + "-order", upgrade, PurchaseKind.Construction).Status);
}
static UpgradeEngine RoundTrip(UpgradeEngine engine, BaseDefinition? definition = null)
{
    var restored = Engine(definition);
    restored.RestoreState(JsonSerializer.Deserialize<BasedState>(JsonSerializer.Serialize(engine.ExportState()))!);
    return restored;
}
static void Sequential()
{
    var engine = Engine();
    Equal("prerequisite-incomplete", engine.BeginPurchase(Command("early-b", "B", PurchaseKind.Rights)).Reason);
    Order(engine, "A", "a");
    engine.AdvanceSleep(1); engine.AdvanceSleep(2);
    Order(engine, "B", "b");
    Equal(1, engine.CompletedEffects("fixture").Count);
    engine = RoundTrip(engine);
    Equal(UpgradeStage.Complete, engine.GetUpgrade("fixture", "A").Stage);
    engine.AdvanceSleep(3);
    Order(engine, "dock", "dock");
    engine.AdvanceSleep(4);
    engine = RoundTrip(engine);
    Equal(3, engine.CompletedEffects("fixture").Count);
    Equal("loading-dock", engine.CompletedEffects("fixture")[2].Effect.Kind);
    Equal(600, engine.GetUpgrade("fixture", "dock").RightsPaid + engine.GetUpgrade("fixture", "dock").ConstructionPaid);
}
static void FourMornings()
{
    var definition = new BaseDefinition("fixture", "ming", new[] { new UpgradeDefinition("ming", 100, 500, 4,
        Array.Empty<string>(), new[] { new ConstructionPhase("breakthrough", 1, "p1"), new ConstructionPhase("shell", 2, "p2"), new ConstructionPhase("roof", 3, "p3") },
        new[] { new UpgradeEffect("space", "ming-room", "finished") }) });
    var engine = Engine(definition);
    Order(engine, "ming", "m");
    Equal<string?>(null, engine.CurrentPhase("fixture", "ming"));
    string[] phases = { "breakthrough", "shell", "roof" };
    for (int i = 0; i < 3; i++)
    {
        engine.AdvanceSleep(i + 1);
        Equal(phases[i], engine.CurrentPhase("fixture", "ming"));
        engine = RoundTrip(engine, definition);
    }
    engine.AdvanceSleep(4);
    Equal(UpgradeStage.Complete, engine.GetUpgrade("fixture", "ming").Stage);
    Equal(1, engine.CompletedEffects("fixture").Count);
}
static void DuplicateCommands()
{
    var engine = Engine(); var command = Command("unique", "A", PurchaseKind.Rights);
    var first = engine.BeginPurchase(command);
    Equal(first, engine.BeginPurchase(command));
    Equal("command-id-conflict", engine.BeginPurchase(command with { PayerId = "other" }).Reason);
    var acknowledgement = new PaymentAcknowledgement(true, "receipt", "buyer", 100);
    var applied = engine.AcknowledgePayment(command.CommandId, acknowledgement);
    Equal(applied, engine.AcknowledgePayment(command.CommandId, acknowledgement));
    Equal(applied, engine.BeginPurchase(command));
    Equal(1L, engine.Revision);
    engine = RoundTrip(engine);
    Equal(applied, engine.BeginPurchase(command));
    Equal("stale-revision", engine.BeginPurchase(Command("stale", "A", PurchaseKind.Construction, 0)).Reason);
}
static void DeclinedPayment()
{
    var engine = Engine(); engine.BeginPurchase(Command("decline", "A", PurchaseKind.Rights));
    Equal("payment-declined", engine.AcknowledgePayment("decline", new(false, "", "buyer", 0)).Reason);
    Equal(UpgradeStage.RightsMissing, engine.GetUpgrade("fixture", "A").Stage);
    Equal(PurchaseStatus.Applied, Pay(engine, "retry", "A", PurchaseKind.Rights).Status);
}
static void Refund()
{
    var engine = Engine(); engine.BeginPurchase(Command("overpaid", "A", PurchaseKind.Rights));
    var result = engine.AcknowledgePayment("overpaid", new(true, "refund-receipt", "buyer", 120));
    Equal(PurchaseStatus.RefundRequired, result.Status); Equal(120, result.Payment!.Amount);
    Equal(UpgradeStage.RightsMissing, engine.GetUpgrade("fixture", "A").Stage);
    engine = RoundTrip(engine);
    Equal("receipt-mismatch", engine.AcknowledgeRefund("overpaid", "wrong").Reason);
    Equal("refunded", engine.AcknowledgeRefund("overpaid", "refund-receipt").Reason);
    Equal("refunded", engine.AcknowledgeRefund("overpaid", "refund-receipt").Reason);
    Equal(PurchaseStatus.Applied, Pay(engine, "correct-price", "A", PurchaseKind.Rights).Status);
}
static void Authority()
{
    var host = Engine(); Order(host, "A", "a");
    var client = Engine(host: false); client.RestoreState(host.ExportState());
    Equal("host-required", client.AdvanceSleep(1).Reason);
    Equal("host-required", client.BeginPurchase(Command("client", "A", PurchaseKind.Construction)).Reason);
    Equal("host-required", client.AcknowledgePayment("a-order", new(true, "x", "buyer", 500)).Reason);
    Equal(0, client.GetUpgrade("fixture", "A").CompletedCycles);
}
static void Reservations()
{
    var definition = Fixture() with { Upgrades = new[] { Upgrade("A", Array.Empty<string>(), "a", 2), Upgrade("C", Array.Empty<string>(), "c", 1) } };
    var engine = Engine(definition);
    Pay(engine, "ar", "A", PurchaseKind.Rights); Pay(engine, "cr", "C", PurchaseKind.Rights);
    engine.BeginPurchase(Command("ao", "A", PurchaseKind.Construction));
    Equal("purchase-pending", engine.BeginPurchase(Command("co-early", "C", PurchaseKind.Construction)).Reason);
    engine.AcknowledgePayment("ao", new(true, "receipt-ao", "buyer", 500));
    Equal("construction-active", engine.BeginPurchase(Command("co-active", "C", PurchaseKind.Construction)).Reason);
    engine.AdvanceSleep(1); engine.AdvanceSleep(2);
    Equal(PurchaseStatus.Applied, Pay(engine, "co", "C", PurchaseKind.Construction).Status);
}
static void SleepTokens()
{
    var engine = Engine(); Order(engine, "A", "a");
    Equal(true, engine.AdvanceSleep(20).Applied);
    Equal(false, engine.AdvanceSleep(20).Applied); Equal(false, engine.AdvanceSleep(19).Applied);
    Equal(1, engine.GetUpgrade("fixture", "A").CompletedCycles);
    engine = RoundTrip(engine); Equal(false, engine.AdvanceSleep(20).Applied);
    engine.AdvanceSleep(100);
    Equal(2, engine.GetUpgrade("fixture", "A").CompletedCycles);
}
static void Definitions()
{
    Throws<DefinitionException>(() => Engine(new("fixture", "p", new[] { Upgrade("A", new[] { "missing" }, "a", 1) })));
    Throws<DefinitionException>(() => Engine(new("fixture", "p", new[] { Upgrade("A", new[] { "B" }, "a", 1), Upgrade("B", new[] { "A" }, "b", 1) })));
    Throws<DefinitionException>(() => Engine(new("fixture", "p", new[] { Upgrade("A", Array.Empty<string>(), "a", 1) with { RightsPrice = -1 } })));
    Throws<DefinitionException>(() => Engine(new("fixture", "p", new[] { Upgrade("A", Array.Empty<string>(), "a", 1) with { Phases = new[] { new ConstructionPhase("late", 1, "state") } } })));
}
static void RestoreFailure()
{
    var engine = Engine(); Order(engine, "A", "a"); var bad = engine.ExportState();
    bad.Upgrades[0].Stage = UpgradeStage.Complete;
    Throws<ArgumentException>(() => engine.RestoreState(bad));
    Equal(UpgradeStage.Constructing, engine.GetUpgrade("fixture", "A").Stage);
    var exported = engine.ExportState(); exported.Upgrades[0].RightsPaid = 999;
    Equal(100, engine.GetUpgrade("fixture", "A").RightsPaid);
}
static void Reset()
{
    var engine = Engine(); Order(engine, "A", "a"); engine.AdvanceSleep(5); engine.Reset();
    Equal(UpgradeStage.RightsMissing, engine.GetUpgrade("fixture", "A").Stage);
    Equal(0, engine.ExportState().Purchases.Count); Equal(-1L, engine.ExportState().LastSleepCycle);
    Equal(PurchaseStatus.Applied, Pay(engine, "a-rights", "A", PurchaseKind.Rights).Status);
}
static void AddContent()
{
    var engine = Engine(Fixture(false)); Order(engine, "A", "a"); engine.AdvanceSleep(1); engine.AdvanceSleep(2);
    var upgraded = RoundTrip(engine);
    Equal(UpgradeStage.Complete, upgraded.GetUpgrade("fixture", "A").Stage);
    Equal(UpgradeStage.RightsMissing, upgraded.GetUpgrade("fixture", "dock").Stage);
}
static void PendingRestore()
{
    var engine = Engine(); var command = Command("pending", "A", PurchaseKind.Rights);
    var original = engine.BeginPurchase(command);
    engine = RoundTrip(engine);
    Equal(original, engine.BeginPurchase(command));
    Equal(PurchaseStatus.Applied, engine.AcknowledgePayment("pending", new(true, "receipt-pending", "buyer", 100)).Status);
    Equal(1L, engine.Revision);
}
static void Cancellation()
{
    var engine = Engine();
    engine.BeginPurchase(Command("cancel", "A", PurchaseKind.Rights));
    Equal(PurchaseStatus.PaymentPending, engine.CancelPurchase("cancel").Status);
    Equal("purchase-pending", engine.BeginPurchase(Command("blocked-until-ack", "A", PurchaseKind.Rights)).Reason);
    Equal("cancelled-before-payment", engine.AcknowledgePayment("cancel", new(false, "", "buyer", 0)).Reason);
    engine.BeginPurchase(Command("cancel-race", "A", PurchaseKind.Rights));
    engine.CancelPurchase("cancel-race");
    Equal(PurchaseStatus.RefundRequired, engine.AcknowledgePayment("cancel-race", new(true, "late-debit", "buyer", 100)).Status);
    Equal(UpgradeStage.RightsMissing, engine.GetUpgrade("fixture", "A").Stage);
    Equal("refunded", engine.AcknowledgeRefund("cancel-race", "late-debit").Reason);
}

sealed class AuthorityFlag(bool canMutate) : IAuthority { public bool CanMutate => canMutate; }
