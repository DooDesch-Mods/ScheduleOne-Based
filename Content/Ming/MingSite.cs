using Based.Core;

namespace Based.Content.Ming;

internal static class MingSite
{
    public static SiteDefinition Create() => new()
    {
        Base = MingDefinition.Create(),
        UpgradeId = MingDefinition.UpgradeId,
        VendorNpcId = "ming",
        VendorContainerId = "based_ming_plan",
        RequestChoice = "I would like more space upstairs.",
        ProgressChoice = "How is the construction going?",
        VendorLine = "Here is the plan for the rest of the floor.",
        Title = "More room above Ming",
        PhaseTitles = new Dictionary<string, string>
        {
            ["breakthrough"] = "Breakthrough",
            ["frame"] = "Frame",
            ["roof"] = "Roof and interior"
        },
        Restrictions = new[] { "Your room stays usable during construction.", "Keep the marked walls clear." },
        KeepClear = MingWorld.KeepClear,
        Plan = new PlanRoom[]
        {
            new(PlanRoomKind.Existing, -65f, 135f, -60f, 141f, "Existing room"),
            new(PlanRoomKind.Existing, -60f, 137.9f, -56f, 141.1f),
            new(PlanRoomKind.New, -60f, 135.9f, -54f, 137.9f),
            new(PlanRoomKind.New, -56f, 137.9f, -54f, 141.1f),
            new(PlanRoomKind.New, -60f, 141.1f, -54f, 145.1f, "New production space"),
            new(PlanRoomKind.Opening, -60.06f, 135.9f, -59.94f, 137.9f),
            new(PlanRoomKind.Opening, -60f, 137.84f, -56f, 137.96f),
            new(PlanRoomKind.Opening, -56.06f, 137.9f, -55.94f, 141.1f),
            new(PlanRoomKind.Opening, -60f, 141.04f, -56f, 141.16f)
        },
        RegisterWorld = MingWorld.Register,
        Crew = new Crew()
    };

    private sealed class Crew : ISiteCrew
    {
        public void Initialize(Action<string> info, Action<string> warn) => MingWorkers.Initialize(info, warn);
        public void Update(UpgradeStage stage, string? phase) => MingWorkers.Update(stage, phase);
        public void Tick() => MingWorkers.Tick();
        public void Cleanup() => MingWorkers.Cleanup();
    }
}
