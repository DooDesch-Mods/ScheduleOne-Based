using Based.Content.Ming;
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
    ("cancellation retains reservation until debit or no-debit acknowledgement", Cancellation),
    ("rejections and declines leave no stored record", RejectionsNotStored),
    ("settled refunds leave no stored record and stay idempotent", RefundsNotStored),
    ("released reservation frees the upgrade and refunds a late acknowledgement", ReleasedPayment),
    ("same payer with a new request releases the old reservation", SamePayerRequest),
    ("another payer does not release a reservation", OtherPayerRequest),
    ("acknowledgement of an unknown command asks for a refund", UnknownAcknowledgement),
    ("applied purchase answers a late acknowledgement idempotently", AppliedStaysIdempotent),
    ("purchase version moves on changes and stays on retransmits", PurchaseVersion),
    ("snapshot carries no purchase records", SnapshotWithoutPurchases),
    ("construction limit applies per base", ConstructionPerBase),
    ("saved reservations and construction are validated per base", RestorePerBase),
    ("unknown saved upgrade is dropped with a warning", MigrateUnknownUpgrade),
    ("changed cycle counts adapt with a warning", MigrateCycleCount),
    ("stored rejections from an older save are dropped", MigrateRejections),
    ("pending payment on removed content is released on load", MigratePendingOnRemovedContent),
    ("restored upgrades keep their state when a prerequisite was added", MigrateNewPrerequisite),
    ("employee limit: one becomes three, every other limit doubles", StaffLimits),
    ("an upgrade without a rights price is ordered directly after its prerequisite", DirectOrder),
    ("the real Ming definition passes DefinitionRegistry with its saved ids, prices and phases", MingThroughRegistry),
    ("a permit completes on its rights payment and unlocks a menu of builds, one at a time", PermitMenu),
    ("definitions reject a permit with construction, phases or effects", PermitDefinitions),
    ("a tip counts once, only during construction, only on the host, and survives save/load", Tips),
    ("rain delays a build once and it completes one night later", RainDelaysOnce),
    ("rain only delays a build that stands open to rain", RainNeedsOpenBuild),
    ("the rain delay survives save/load and reaches clients", RainDelaySaved),
    ("rain delay is host only and ignores processed or invalid cycles", RainDelayGuards),
    ("a rainy slot counts only once fully in place inside the night", RainWindowSlots),
    ("a saved item on an old grid moves onto the new grid with its tile shifted, other items stay as saved", GridMoves)
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
static PurchaseCommand Command(string id, string upgrade, PurchaseKind kind, string payer = "buyer", string baseId = "fixture") => new(id, baseId, upgrade, kind, payer);
static void GridMoves()
{
    var upper = Guid.Parse("c74e0e82-ec6a-f51d-c753-2db2eac0ce5e");
    var loft = Guid.Parse("130b18d7-2fce-c8d7-704a-848c74f8684b");
    var south = Guid.Parse("b6a48b82-00f1-38bb-7ad5-18db7e797829");
    var old = new Dictionary<Guid, GridMove.OldGrid> { [loft] = new("loft", 11, 0), [south] = new("south room", 0, 20) };

    // A mushroom bed as vanilla's JsonUtility writes it, with the fields of its own data type after the grid's.
    string bed = "{\"DataType\":\"MushroomBedData\",\"DataVersion\":0,\"GameVersion\":\"0.4.7f12\",\"GUID\":\"0b5e7c3a-1111-2222-3333-444455556666\",\"ItemString\":\"{\\\"ID\\\":\\\"mushroombed\\\"}\",\"GridGUID\":\"" + loft + "\",\"OriginCoordinate\":{\"x\":6.0,\"y\":3.0},\"Rotation\":90,\"Contents\":{\"Items\":[]}}";
    var moved = GridMove.Rewrite(bed, upper, old) ?? throw new Exception("the loft bed was not moved");
    Equal("0b5e7c3a-1111-2222-3333-444455556666", moved.ItemGuid);
    Equal("loft", moved.OldGridName);
    Equal((6f, 3f, 17f, 3f), (moved.FromX, moved.FromY, moved.ToX, moved.ToY));
    using (var doc = JsonDocument.Parse(moved.Json))
    {
        var root = doc.RootElement;
        Equal(upper.ToString(), root.GetProperty("GridGUID").GetString());
        Equal(17.0, root.GetProperty("OriginCoordinate").GetProperty("x").GetDouble());
        Equal(3.0, root.GetProperty("OriginCoordinate").GetProperty("y").GetDouble());
        Equal(90, root.GetProperty("Rotation").GetInt32());
        Equal("{\"ID\":\"mushroombed\"}", root.GetProperty("ItemString").GetString());
        Equal(JsonValueKind.Object, root.GetProperty("Contents").ValueKind);
    }

    string station = "{\"GUID\":\"aa\",\"GridGUID\":\"" + south + "\",\"OriginCoordinate\":{\"x\":4,\"y\":12}}";
    var down = GridMove.Rewrite(station, upper, old) ?? throw new Exception("the south station was not moved");
    Equal((4f, 32f), (down.ToX, down.ToY));

    // On the upper grid already, on another grid, or no grid item at all: left as saved.
    Equal<GridMove.Moved?>(null, GridMove.Rewrite("{\"GUID\":\"b\",\"GridGUID\":\"" + upper + "\",\"OriginCoordinate\":{\"x\":1,\"y\":1}}", upper, old));
    Equal<GridMove.Moved?>(null, GridMove.Rewrite("{\"GUID\":\"c\",\"GridGUID\":\"" + Guid.NewGuid() + "\",\"OriginCoordinate\":{\"x\":1,\"y\":1}}", upper, old));
    Equal<GridMove.Moved?>(null, GridMove.Rewrite("{\"GUID\":\"d\",\"Position\":{\"x\":1,\"y\":2,\"z\":3}}", upper, old));
    Equal<GridMove.Moved?>(null, GridMove.Rewrite("", upper, old));
    // On an old grid without a readable tile: reported, not guessed.
    Throws<FormatException>(() => GridMove.Rewrite("{\"GUID\":\"e\",\"GridGUID\":\"" + loft + "\"}", upper, old));
}

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
static BaseDefinition PermitFixture() => new("fixture", "isolated-property", new[]
{
    new UpgradeDefinition("permit", 1500, 0, 0, Array.Empty<string>(), Array.Empty<ConstructionPhase>(), Array.Empty<UpgradeEffect>()),
    new UpgradeDefinition("storey", 0, 6000, 3, new[] { "permit" }, new[] { new ConstructionPhase("frame", 1, "frame"), new ConstructionPhase("roof", 2, "roof") },
        new[] { new UpgradeEffect("world-state", "storey", "complete") }),
    new UpgradeDefinition("carport", 0, 1500, 1, new[] { "permit" }, Array.Empty<ConstructionPhase>(), new[] { new UpgradeEffect("world-state", "carport", "complete") })
});
static void PermitMenu()
{
    var engine = Engine(PermitFixture());
    Equal("prerequisite-incomplete", engine.BeginPurchase(Command("early", "storey", PurchaseKind.Construction)).Reason);
    Equal("permit-has-no-construction", engine.BeginPurchase(Command("permit-build", "permit", PurchaseKind.Construction)).Reason);
    Equal(PurchaseStatus.Applied, Pay(engine, "permit", "permit", PurchaseKind.Rights).Status);
    Equal(UpgradeStage.Complete, engine.GetUpgrade("fixture", "permit").Stage);
    Equal(1500, engine.GetUpgrade("fixture", "permit").RightsPaid);
    Equal("rights-not-sold", engine.BeginPurchase(Command("storey-rights", "storey", PurchaseKind.Rights)).Reason);
    Equal(PurchaseStatus.Applied, Pay(engine, "storey", "storey", PurchaseKind.Construction).Status);
    Equal("construction-active", engine.BeginPurchase(Command("carport-early", "carport", PurchaseKind.Construction)).Reason);
    engine = RoundTrip(engine, PermitFixture());
    Equal(UpgradeStage.Complete, engine.GetUpgrade("fixture", "permit").Stage);
    engine.AdvanceSleep(1); engine.AdvanceSleep(2); engine.AdvanceSleep(3);
    Equal(UpgradeStage.Complete, engine.GetUpgrade("fixture", "storey").Stage);
    Equal(PurchaseStatus.Applied, Pay(engine, "carport", "carport", PurchaseKind.Construction).Status);
    engine.AdvanceSleep(4);
    engine = RoundTrip(engine, PermitFixture());
    Equal(2, engine.CompletedEffects("fixture").Count);
}
static void PermitDefinitions()
{
    UpgradeDefinition Permit(int rights, int construction, ConstructionPhase[] phases, UpgradeEffect[] effects) =>
        new("permit", rights, construction, 0, Array.Empty<string>(), phases, effects);
    var none = Array.Empty<ConstructionPhase>();
    var noEffects = Array.Empty<UpgradeEffect>();
    foreach (var bad in new[]
    {
        Permit(0, 0, none, noEffects),
        Permit(100, 50, none, noEffects),
        Permit(100, 0, new[] { new ConstructionPhase("p", 1, "s") }, noEffects),
        Permit(100, 0, none, new[] { new UpgradeEffect("world-state", "t", "s") })
    })
        Throws<DefinitionException>(() => Engine(new BaseDefinition("fixture", "isolated-property", new[] { bad })));
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
// The definition the mod ships, not a fixture: the ids are in player saves and must not move.
static void MingThroughRegistry()
{
    var registry = DefinitionRegistry.Create(new[] { MingDefinition.Create() });
    var ming = registry["ming.sweatshop"];
    Equal("sweatshop", ming.PropertyId);
    Equal("ming.restaurant-wing,ming.loading-dock", string.Join(",", ming.Upgrades.Select(u => u.Id)));
    var wing = ming.Upgrades[0];
    var dock = ming.Upgrades[1];
    Equal(1000, wing.RightsPrice);
    Equal(3000, wing.ConstructionPrice);
    Equal(4, wing.ConstructionCycles);
    Equal("breakthrough@1,frame@2,roof@3", string.Join(",", wing.Phases.Select(p => p.Id + "@" + p.AfterSleepCycles)));
    Equal("world-state:based_ming_upper:complete", string.Join(",", wing.Effects.Select(e => e.Kind + ":" + e.TargetId + ":" + e.StateId)));
    Equal(0, dock.RightsPrice);
    Equal(1500, dock.ConstructionPrice);
    Equal(1, dock.ConstructionCycles);
    Equal("ming.restaurant-wing", string.Join(",", dock.Prerequisites));
    Equal("world-state:based_ming_dock:complete", string.Join(",", dock.Effects.Select(e => e.Kind + ":" + e.TargetId + ":" + e.StateId)));
    // The engine accepts it, and a new save starts both upgrades without rights.
    var engine = new UpgradeEngine(new[] { MingDefinition.Create() }, new AuthorityFlag(true));
    Equal(UpgradeStage.RightsMissing, engine.GetUpgrade("ming.sweatshop", "ming.restaurant-wing").Stage);
    Equal(UpgradeStage.RightsMissing, engine.GetUpgrade("ming.sweatshop", "ming.loading-dock").Stage);
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
    Equal("purchase-pending", engine.BeginPurchase(Command("blocked-until-ack", "A", PurchaseKind.Rights, "other")).Reason);
    Equal("cancelled-before-payment", engine.AcknowledgePayment("cancel", new(false, "", "buyer", 0)).Reason);
    engine.BeginPurchase(Command("cancel-race", "A", PurchaseKind.Rights));
    engine.CancelPurchase("cancel-race");
    Equal(PurchaseStatus.RefundRequired, engine.AcknowledgePayment("cancel-race", new(true, "late-debit", "buyer", 100)).Status);
    Equal(UpgradeStage.RightsMissing, engine.GetUpgrade("fixture", "A").Stage);
    Equal("refunded", engine.AcknowledgeRefund("cancel-race", "late-debit").Reason);
}
static void RejectionsNotStored()
{
    var engine = Engine();
    Equal("prerequisite-incomplete", engine.BeginPurchase(Command("rejected", "B", PurchaseKind.Rights)).Reason);
    Equal(0, engine.ExportState().Purchases.Count);
    engine.BeginPurchase(Command("declined", "A", PurchaseKind.Rights));
    Equal("payment-declined", engine.AcknowledgePayment("declined", new(false, "", "buyer", 0)).Reason);
    Equal(0, engine.ExportState().Purchases.Count);
    Equal("payment-declined", engine.AcknowledgePayment("declined", new(false, "", "buyer", 0)).Reason);
}
static void RefundsNotStored()
{
    var engine = Engine(); engine.BeginPurchase(Command("overpaid", "A", PurchaseKind.Rights));
    engine.AcknowledgePayment("overpaid", new(true, "receipt", "buyer", 120));
    Equal(1, engine.ExportState().Purchases.Count);
    Equal("refunded", engine.AcknowledgeRefund("overpaid", "receipt").Reason);
    Equal(0, engine.ExportState().Purchases.Count);
    Equal(PurchaseStatus.Rejected, engine.AcknowledgeRefund("overpaid", "receipt").Status);
    Equal("refunded", engine.AcknowledgeRefund("overpaid", "receipt").Reason);
}
static void ReleasedPayment()
{
    var engine = Engine(); engine.BeginPurchase(Command("gone", "A", PurchaseKind.Rights));
    Equal("purchase-pending", engine.BeginPurchase(Command("rival", "A", PurchaseKind.Rights, "other")).Reason);
    Equal("payment-released", engine.ReleasePurchase("gone").Reason);
    Equal(0, engine.PendingPurchases().Count);
    Equal("unknown-command", engine.ReleasePurchase("gone").Reason);
    var late = engine.AcknowledgePayment("gone", new(true, "late-receipt", "buyer", 100));
    Equal(PurchaseStatus.RefundRequired, late.Status);
    Equal("late-receipt", late.ReceiptId);
    Equal(100, late.Payment!.Amount);
    Equal(UpgradeStage.RightsMissing, engine.GetUpgrade("fixture", "A").Stage);
    Equal("refunded", engine.AcknowledgeRefund("gone", "late-receipt").Reason);
    Equal(PurchaseStatus.PaymentPending, engine.BeginPurchase(Command("rival", "A", PurchaseKind.Rights, "other")).Status);
    Equal(PurchaseStatus.Applied, engine.AcknowledgePayment("rival", new(true, "rival-receipt", "other", 100)).Status);
    Equal(PurchaseStatus.Applied, engine.ReleasePurchase("rival").Status);
    Equal(PurchaseStatus.Applied, engine.FindPurchase("rival")!.Result.Status);
}
static void SamePayerRequest()
{
    var engine = Engine(); engine.BeginPurchase(Command("old", "A", PurchaseKind.Rights));
    Equal(PurchaseStatus.PaymentPending, engine.BeginPurchase(Command("new", "A", PurchaseKind.Rights)).Status);
    Equal<PurchaseRecord?>(null, engine.FindPurchase("old"));
    Equal(1, engine.PendingPurchases().Count);
    Equal(PurchaseStatus.RefundRequired, engine.AcknowledgePayment("old", new(true, "old-receipt", "buyer", 100)).Status);
    Equal(PurchaseStatus.Applied, engine.AcknowledgePayment("new", new(true, "new-receipt", "buyer", 100)).Status);
    Equal(100, engine.GetUpgrade("fixture", "A").RightsPaid);
}
static void OtherPayerRequest()
{
    var engine = Engine(); engine.BeginPurchase(Command("first", "A", PurchaseKind.Rights));
    Equal("purchase-pending", engine.BeginPurchase(Command("second", "A", PurchaseKind.Rights, "other")).Reason);
    Equal(PurchaseStatus.PaymentPending, engine.FindPurchase("first")!.Result.Status);
}
static void UnknownAcknowledgement()
{
    var engine = Engine();
    Equal(PurchaseStatus.RefundRequired, engine.AcknowledgePayment("never-seen", new(true, "receipt", "buyer", 70)).Status);
    Equal("receipt-required", engine.AcknowledgePayment("never-seen", new(true, "", "buyer", 70)).Reason);
    Equal("invalid-payment-acknowledgement", engine.AcknowledgePayment("never-seen", new(true, "receipt", "buyer", -1)).Reason);
    Equal(0, engine.ExportState().Purchases.Count);
    Pay(engine, "paid", "A", PurchaseKind.Rights);
    Equal("receipt-already-used", engine.AcknowledgePayment("never-seen", new(true, "receipt-paid", "buyer", 100)).Reason);
}
static void AppliedStaysIdempotent()
{
    var engine = Engine(); var applied = Pay(engine, "paid", "A", PurchaseKind.Rights);
    engine = RoundTrip(engine);
    Equal(applied, engine.AcknowledgePayment("paid", new(true, "receipt-paid", "buyer", 100)));
    Equal(applied, engine.AcknowledgeRefund("paid", "receipt-paid"));
    Equal(1L, engine.Revision);
}
static void PurchaseVersion()
{
    var engine = Engine(); var command = Command("one", "A", PurchaseKind.Rights); long version = engine.PurchaseVersion;
    engine.BeginPurchase(command);
    Equal(version + 1, engine.PurchaseVersion);
    engine.BeginPurchase(command);
    engine.BeginPurchase(Command("rejected", "B", PurchaseKind.Rights));
    Equal(version + 1, engine.PurchaseVersion);
    var acknowledgement = new PaymentAcknowledgement(true, "receipt", "buyer", 100);
    engine.AcknowledgePayment("one", acknowledgement);
    Equal(version + 2, engine.PurchaseVersion);
    engine.AcknowledgePayment("one", acknowledgement);
    engine.AcknowledgePayment("unknown", new(true, "other-receipt", "buyer", 100));
    Equal(version + 2, engine.PurchaseVersion);
}
static void SnapshotWithoutPurchases()
{
    var host = Engine(); Order(host, "A", "a");
    var snapshot = host.ExportSnapshot();
    Equal(0, snapshot.Purchases.Count);
    Equal(host.Revision, snapshot.Revision);
    Equal(2, host.ExportState().Purchases.Count);
    var client = Engine(host: false); client.RestoreState(JsonSerializer.Deserialize<BasedState>(JsonSerializer.Serialize(snapshot))!);
    Equal(UpgradeStage.Constructing, client.GetUpgrade("fixture", "A").Stage);
}
static UpgradeEngine TwoBases() => new(new[] { Fixture(), Fixture() with { Id = "second" } }, new AuthorityFlag(true));
static void ConstructionPerBase()
{
    var engine = TwoBases();
    foreach (string baseId in new[] { "fixture", "second" })
    {
        var rights = engine.BeginPurchase(Command("r-" + baseId, "A", PurchaseKind.Rights, baseId: baseId));
        engine.AcknowledgePayment("r-" + baseId, new(true, "receipt-r-" + baseId, "buyer", rights.Payment!.Amount));
    }
    Equal(PurchaseStatus.PaymentPending, engine.BeginPurchase(Command("c-fixture", "A", PurchaseKind.Construction)).Status);
    Equal(PurchaseStatus.PaymentPending, engine.BeginPurchase(Command("c-second", "A", PurchaseKind.Construction, baseId: "second")).Status);
    Equal(PurchaseStatus.Applied, engine.AcknowledgePayment("c-fixture", new(true, "receipt-c1", "buyer", 500)).Status);
    Equal(PurchaseStatus.Applied, engine.AcknowledgePayment("c-second", new(true, "receipt-c2", "buyer", 500)).Status);
    Equal(UpgradeStage.Constructing, engine.GetUpgrade("fixture", "A").Stage);
    Equal(UpgradeStage.Constructing, engine.GetUpgrade("second", "A").Stage);
    engine.AdvanceSleep(1);
    Equal(1, engine.GetUpgrade("second", "A").CompletedCycles);
    engine.RestoreState(engine.ExportState());
    Equal(UpgradeStage.Constructing, engine.GetUpgrade("second", "A").Stage);
}
static void RestorePerBase()
{
    var definition = Fixture() with { Upgrades = new[] { Upgrade("A", Array.Empty<string>(), "a", 2), Upgrade("C", Array.Empty<string>(), "c", 2) } };
    var engine = Engine(definition); var state = engine.ExportState();
    foreach (var upgrade in state.Upgrades) { upgrade.Stage = UpgradeStage.Constructing; upgrade.RightsPaid = 100; upgrade.ConstructionPaid = 500; }
    Throws<ArgumentException>(() => engine.RestoreState(state));
}
static List<string> Warnings(UpgradeEngine engine) { var warnings = new List<string>(); engine.MigrationWarning += warnings.Add; return warnings; }
static void MigrateUnknownUpgrade()
{
    var engine = Engine(); Order(engine, "A", "a"); var state = engine.ExportState();
    state.Upgrades.Add(new UpgradeState { BaseId = "fixture", UpgradeId = "removed", Stage = UpgradeStage.Complete, CompletedCycles = 2, RightsPaid = 100, ConstructionPaid = 500 });
    state.Upgrades.Add(new UpgradeState { BaseId = "removed-base", UpgradeId = "A", Stage = UpgradeStage.RightsOwned, RightsPaid = 100 });
    var restored = Engine(); var warnings = Warnings(restored);
    restored.RestoreState(state);
    Equal(2, warnings.Count);
    Equal(UpgradeStage.Constructing, restored.GetUpgrade("fixture", "A").Stage);
    Equal(false, restored.ExportState().Upgrades.Any(u => u.UpgradeId == "removed" || u.BaseId == "removed-base"));
}
static BaseDefinition WithCycles(int cycles) => new("fixture", "p", new[] { Upgrade("A", Array.Empty<string>(), "a", cycles) with { Phases = Array.Empty<ConstructionPhase>() } });
static void MigrateCycleCount()
{
    var source = Engine(WithCycles(4)); Order(source, "A", "a");
    source.AdvanceSleep(1); source.AdvanceSleep(2);
    var running = source.ExportState();
    source.AdvanceSleep(3); source.AdvanceSleep(4);
    var complete = source.ExportState();

    var grown = Engine(WithCycles(6)); var warnings = Warnings(grown);
    grown.RestoreState(running);
    Equal(0, warnings.Count);
    Equal(UpgradeStage.Constructing, grown.GetUpgrade("fixture", "A").Stage);
    grown.RestoreState(complete);
    Equal(1, warnings.Count);
    Equal(UpgradeStage.Complete, grown.GetUpgrade("fixture", "A").Stage);
    Equal(6, grown.GetUpgrade("fixture", "A").CompletedCycles);

    var shrunk = Engine(WithCycles(2)); warnings = Warnings(shrunk);
    shrunk.RestoreState(running);
    Equal(1, warnings.Count);
    Equal(UpgradeStage.Complete, shrunk.GetUpgrade("fixture", "A").Stage);
    Equal(2, shrunk.GetUpgrade("fixture", "A").CompletedCycles);
    Equal(1, shrunk.CompletedEffects("fixture").Count);
    shrunk.RestoreState(complete);
    Equal(2, shrunk.GetUpgrade("fixture", "A").CompletedCycles);
}
static void MigrateRejections()
{
    var engine = Engine(); Order(engine, "A", "a"); var state = engine.ExportState();
    for (int i = 0; i < 200; i++)
        state.Purchases.Add(new PurchaseRecord { Command = Command("old-" + i, "B", PurchaseKind.Rights), Result = new(PurchaseStatus.Rejected, "purchase-pending", 0) });
    state.Purchases.Add(new PurchaseRecord { Command = Command("old-refund", "B", PurchaseKind.Rights), Result = new(PurchaseStatus.Rejected, "refunded", 0, null, "old-receipt") });
    var restored = Engine(); var warnings = Warnings(restored);
    restored.RestoreState(JsonSerializer.Deserialize<BasedState>(JsonSerializer.Serialize(state))!);
    Equal(1, warnings.Count);
    Equal(2, restored.ExportState().Purchases.Count);
    Equal("refunded", restored.AcknowledgeRefund("old-refund", "old-receipt").Reason);
}
static void MigratePendingOnRemovedContent()
{
    var engine = Engine(Fixture()); Order(engine, "A", "a"); engine.AdvanceSleep(1); engine.AdvanceSleep(2);
    Order(engine, "B", "b"); engine.AdvanceSleep(3);
    engine.BeginPurchase(Command("removed", "dock", PurchaseKind.Rights));
    var state = engine.ExportState();
    var restored = Engine(Fixture(false)); var warnings = Warnings(restored);
    restored.RestoreState(state);
    Equal(2, warnings.Count);
    Equal(0, restored.PendingPurchases().Count);
    Equal(PurchaseStatus.RefundRequired, restored.AcknowledgePayment("removed", new(true, "late", "buyer", 100)).Status);
}
static void StaffLimits()
{
    Equal(3, StaffLimit.Expanded(1));
    Equal(4, StaffLimit.Expanded(2));
    Equal(20, StaffLimit.Expanded(10));
    Equal(0, StaffLimit.Expanded(0));
}

static void MigrateNewPrerequisite()
{
    var engine = Engine(new("fixture", "p", new[] { Upgrade("A", Array.Empty<string>(), "a", 2), Upgrade("B", Array.Empty<string>(), "b", 1) }));
    Order(engine, "B", "b"); engine.AdvanceSleep(1);
    var restored = Engine(); var warnings = Warnings(restored);
    restored.RestoreState(engine.ExportState());
    Equal(1, warnings.Count);
    Equal(UpgradeStage.Complete, restored.GetUpgrade("fixture", "B").Stage);
}

static void DirectOrder()
{
    var definition = Fixture(includeDock: false) with { Upgrades = new[]
    {
        Upgrade("A", Array.Empty<string>(), "area-a", 2),
        new UpgradeDefinition("dock", 0, 1500, 1, new[] { "A" }, Array.Empty<ConstructionPhase>(), new[] { new UpgradeEffect("world-state", "dock", "complete") })
    } };
    var engine = Engine(definition);
    Equal("prerequisite-incomplete", engine.BeginPurchase(Command("early", "dock", PurchaseKind.Construction)).Reason);
    Order(engine, "A", "a");
    Equal("prerequisite-incomplete", engine.BeginPurchase(Command("busy", "dock", PurchaseKind.Construction)).Reason);
    engine.AdvanceSleep(1); engine.AdvanceSleep(2);
    Equal("rights-not-sold", engine.BeginPurchase(Command("rights", "dock", PurchaseKind.Rights)).Reason);
    var applied = Pay(engine, "dock-order", "dock", PurchaseKind.Construction);
    Equal(PurchaseStatus.Applied, applied.Status);
    Equal(1500, applied.Payment!.Amount);
    engine = RoundTrip(engine, definition);
    Equal(UpgradeStage.Constructing, engine.GetUpgrade("fixture", "dock").Stage);
    Equal(0, engine.GetUpgrade("fixture", "dock").RightsPaid);
    engine.AdvanceSleep(3);
    Equal(UpgradeStage.Complete, engine.GetUpgrade("fixture", "dock").Stage);
    Equal(2, engine.CompletedEffects("fixture").Count);
    engine = RoundTrip(engine, definition);
    Equal(UpgradeStage.Complete, engine.GetUpgrade("fixture", "dock").Stage);
}
static void Tips()
{
    var engine = Engine();
    Equal("not-constructing", engine.AddTip("early", "fixture", "A", 50).Reason);
    Order(engine, "A", "a");
    Equal("invalid-amount", engine.AddTip("zero", "fixture", "A", 0).Reason);
    Equal("invalid-amount", engine.AddTip("huge", "fixture", "A", 1_000_001).Reason);
    Equal("invalid-amount", engine.AddTip("", "fixture", "A", 10).Reason);
    Equal("unknown-upgrade", engine.AddTip("other", "fixture", "missing", 10).Reason);
    var revision = engine.Revision;
    Equal("tipped", engine.AddTip("t1", "fixture", "A", 30).Reason);
    Equal(revision + 1, engine.Revision);
    Equal("duplicate", engine.AddTip("t1", "fixture", "A", 30).Reason);
    Equal("tipped", engine.AddTip("t2", "fixture", "A", 20).Reason);
    Equal(50, engine.GetUpgrade("fixture", "A").Tips);
    Equal("not-constructing", engine.AddTip("t3", "fixture", "B", 10).Reason);
    engine = RoundTrip(engine);
    Equal(50, engine.GetUpgrade("fixture", "A").Tips);
    Equal("duplicate", engine.AddTip("t2", "fixture", "A", 20).Reason);
    Equal("tips-set", engine.SetTips("fixture", "A", 7).Reason);
    Equal(7, engine.GetUpgrade("fixture", "A").Tips);
    Equal("invalid-amount", engine.SetTips("fixture", "A", -1).Reason);
    engine.AdvanceSleep(1); engine.AdvanceSleep(2);
    Equal(UpgradeStage.Complete, engine.GetUpgrade("fixture", "A").Stage);
    Equal("not-constructing", engine.AddTip("late", "fixture", "A", 10).Reason);
    Equal(7, engine.GetUpgrade("fixture", "A").Tips);
    var client = Engine(host: false);
    Equal("host-required", client.AddTip("c1", "fixture", "A", 10).Reason);
    Equal("host-required", client.SetTips("fixture", "A", 10).Reason);
}

// Three nights: order day and the first phase are open to rain, the second phase has a roof.
static BaseDefinition RainFixture(bool openOnOrderDay = true) => new("fixture", "isolated-property", new[]
{
    new UpgradeDefinition("A", 100, 500, 3, Array.Empty<string>(),
        new[] { new ConstructionPhase("open", 1, "open-state", OpenToRain: true), new ConstructionPhase("roofed", 2, "roofed-state") },
        new[] { new UpgradeEffect("space", "area-a", "ready") }, OpenToRainOnOrderDay: openOnOrderDay)
});

static void RainDelaysOnce()
{
    var engine = Engine(RainFixture()); Order(engine, "A", "a");
    Equal(true, engine.IsOpenToRain("fixture", "A"));
    Equal("rain-delayed", engine.DelayForRain("fixture", "A", 1).Reason);
    Equal("already-delayed", engine.DelayForRain("fixture", "A", 1).Reason);
    Equal(true, engine.AdvanceSleep(1).Applied);
    var state = engine.GetUpgrade("fixture", "A");
    Equal(0, state.CompletedCycles); Equal(UpgradeStage.Constructing, state.Stage); Equal(true, state.WasRainedOut());
    Equal(true, engine.IsOpenToRain("fixture", "A"));
    // A second rain does not delay again.
    Equal("already-delayed", engine.DelayForRain("fixture", "A", 2).Reason);
    engine.AdvanceSleep(2);
    state = engine.GetUpgrade("fixture", "A");
    Equal(1, state.CompletedCycles); Equal(false, state.WasRainedOut()); Equal(true, state.RainDelayed);
    Equal("open", engine.CurrentPhase("fixture", "A"));
    engine.AdvanceSleep(3);
    Equal(2, engine.GetUpgrade("fixture", "A").CompletedCycles); Equal(UpgradeStage.Constructing, engine.GetUpgrade("fixture", "A").Stage);
    engine.AdvanceSleep(4);
    state = engine.GetUpgrade("fixture", "A");
    Equal(UpgradeStage.Complete, state.Stage); Equal(false, state.RainDelayed); Equal(3, state.CompletedCycles);
    Equal("not-constructing", engine.DelayForRain("fixture", "A", 5).Reason);
}

static void RainNeedsOpenBuild()
{
    var closed = Engine(RainFixture(openOnOrderDay: false)); Order(closed, "A", "a");
    Equal(false, closed.IsOpenToRain("fixture", "A"));
    Equal("not-open-to-rain", closed.DelayForRain("fixture", "A", 1).Reason);
    Equal(false, closed.GetUpgrade("fixture", "A").RainDelayed);
    closed.AdvanceSleep(1);
    Equal(true, closed.IsOpenToRain("fixture", "A"));
    closed.AdvanceSleep(2);
    Equal(false, closed.IsOpenToRain("fixture", "A"));
    Equal("not-open-to-rain", closed.DelayForRain("fixture", "A", 3).Reason);
    Equal("not-constructing", Engine(RainFixture()).DelayForRain("fixture", "A", 1).Reason);
    Equal(false, Engine(RainFixture()).IsOpenToRain("fixture", "A"));
}

static void RainDelaySaved()
{
    var engine = Engine(RainFixture()); Order(engine, "A", "a");
    engine.DelayForRain("fixture", "A", 1); engine.AdvanceSleep(1);
    var restored = RoundTrip(engine, RainFixture());
    var state = restored.GetUpgrade("fixture", "A");
    Equal(true, state.RainDelayed); Equal(true, state.WasRainedOut());
    Equal("already-delayed", restored.DelayForRain("fixture", "A", 2).Reason);
    var client = Engine(RainFixture(), host: false); client.RestoreState(engine.ExportSnapshot());
    Equal(true, client.GetUpgrade("fixture", "A").RainDelayed);
    // A delay left on an upgrade that is not constructing is cleared on load.
    var saved = engine.ExportState(); var upgrade = saved.Upgrades.Single(u => u.UpgradeId == "A");
    upgrade.Stage = UpgradeStage.RightsOwned; upgrade.CompletedCycles = 0; upgrade.ConstructionPaid = 0;
    var warnings = new List<string>(); var loaded = Engine(RainFixture()); loaded.MigrationWarning += warnings.Add;
    loaded.RestoreState(saved);
    Equal(false, loaded.GetUpgrade("fixture", "A").RainDelayed); Equal(true, warnings.Any(w => w.Contains("rain delay")));
}

static void RainDelayGuards()
{
    var engine = Engine(RainFixture()); Order(engine, "A", "a");
    var client = Engine(RainFixture(), host: false); client.RestoreState(engine.ExportSnapshot());
    Equal("host-required", client.DelayForRain("fixture", "A", 1).Reason);
    Equal("invalid-cycle", engine.DelayForRain("fixture", "A", -1).Reason);
    engine.AdvanceSleep(1);
    Equal("cycle-already-processed", engine.DelayForRain("fixture", "A", 1).Reason);
    Throws<KeyNotFoundException>(() => engine.DelayForRain("fixture", "missing", 2));
}

static void RainWindowSlots()
{
    Equal(0, RainWindow.MinuteOfWeatherDay(700)); Equal(900, RainWindow.MinuteOfWeatherDay(2200));
    Equal(1140, RainWindow.MinuteOfWeatherDay(200)); Equal(1439, RainWindow.MinuteOfWeatherDay(659));
    // 07:00-12:00 dry, 12:00-14:00 rain moving in over 30 minutes, then dry; the night is 22:00 to 07:00.
    var dryNight = new[] { new WeatherSlot(300, 0, false), new WeatherSlot(90, 30, true), new WeatherSlot(1020, 0, false) };
    Equal(false, RainWindow.Rains(dryNight, 900, 1440));
    // Rain from 02:00 to 03:40 on the next calendar day (minute 1140 to 1240).
    var wetNight = new[] { new WeatherSlot(1100, 0, false), new WeatherSlot(100, 40, true), new WeatherSlot(200, 0, false) };
    Equal(true, RainWindow.Rains(wetNight, 900, 1440));
    // Rain already over when the player goes to bed.
    Equal(false, RainWindow.Rains(wetNight, 1300, 1440));
    // Only the moving-in transition lies in the night: not counted.
    Equal(false, RainWindow.Rains(new[] { new WeatherSlot(1100, 0, false), new WeatherSlot(0, 340, true) }, 900, 1200));
    Equal(false, RainWindow.Rains(Array.Empty<WeatherSlot>(), 0, 1440));
}

sealed class AuthorityFlag(bool canMutate) : IAuthority { public bool CanMutate => canMutate; }
