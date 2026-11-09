using Based.Core;

namespace Based.Content.Ming;

internal static class MingSite
{
    // Read by the missing-mods notice, which must not run Create: the site types need S1API.
    internal const string VendorNpcId = "ming";
    internal const string VendorContainerId = "based_ming_plan";
    internal const string RequestChoice = "I want to expand the room.";

    public static SiteDefinition Create() => new()
    {
        Base = MingDefinition.Create(),
        UpgradeId = MingDefinition.UpgradeId,
        PropertyName = "Sweatshop",
        VendorNpcId = VendorNpcId,
        VendorContainerId = VendorContainerId,
        RequestChoice = RequestChoice,
        ProgressChoice = "How is the construction going?",
        VendorLine = "Oh yes, very good! Bigger room, yes? Here is the plan.",
        Title = "Expansion: Sweatshop",
        FolderItemId = "based_project_folder",
        BoardSurfaceId = "based-plan-board",
        QuestType = typeof(MingConstructionQuest),
        OrderEntry = "Order the construction from Mrs. Ming",
        BuyFirst = "Buy the Sweatshop first.",
        QuestMarker = new UnityEngine.Vector3(-57f, MingWorld.FloorY, 140f),
        PhaseTitles = new Dictionary<string, string>
        {
            ["breakthrough"] = "Breakthrough",
            ["frame"] = "Frame",
            ["roof"] = "Roof and interior"
        },
        Restrictions = new[] { "You can keep using the room.", "Keep the red walls clear." },
        KeepClear = MingWorld.KeepClear,
        Plan = new PlanRoom[]
        {
            new(PlanRoomKind.Existing, -65f, 135f, -60f, 141.1f, "Sweatshop"),
            new(PlanRoomKind.Existing, -60f, 137.9f, -56f, 141.1f),
            new(PlanRoomKind.New, -60f, 135.9f, -54f, 137.9f),
            new(PlanRoomKind.New, -56f, 137.9f, -54f, 141.1f),
            new(PlanRoomKind.New, -60f, 141.1f, -54f, 145.1f, "Expansion"),
            new(PlanRoomKind.Opening, -60.06f, 135.9f, -59.94f, 137.9f),
            new(PlanRoomKind.Opening, -60f, 137.84f, -56f, 137.96f),
            new(PlanRoomKind.Opening, -56.06f, 137.9f, -55.94f, 141.1f),
            new(PlanRoomKind.Opening, -60f, 141.04f, -56f, 141.16f)
        },
        // Beside the game's own waiting place at the foot of the outside stairs, in the yard. Inside the room the
        // player's furniture stands wherever there is floor, and employees waiting there stand in the way.
        StaffPlaces = new UnityEngine.Vector3[]
        {
            new(-62.9f, -4f, 144.7f),
            new(-63.9f, -4f, 144.3f)
        },
        RegisterWorld = () =>
        {
            MingWorld.Register();
            MingDock.Register();
        },
        Crew = new Crew(),
#if DEBUG
        // A lambda, so MapExpansions types load only when the probe runs.
        Room = new SiteRoom(MingDefinition.ExpansionId, "complete", "ming-wing-floor", MingWorld.Wing,
            () => MingWorld.Create().WorldStates.Single(x => x.Id == "complete").PropertyExtensions
                .SelectMany(extension => extension.Surfaces.Select(surface => (extension.Id, surface.Id))).ToArray()),
#endif
        FollowUps = new[]
        {
            new SiteFollowUp(MingDefinition.DockUpgradeId, "Loading dock: Sweatshop", "Ready for deliveries",
                new[] { "Deliveries park in the yard behind the restaurant.", "Employees can unload the van from there." })
        }
    };

    private sealed class Crew : ISiteCrew
    {
        public void Initialize(Action<string> info, Action<string> warn) => MingWorkers.Initialize(info, warn);
        public void Update(UpgradeStage stage, string? phase) => MingWorkers.Update(stage, phase);
        public void Tick() => MingWorkers.Tick();
        public void Cleanup() => MingWorkers.Cleanup();
        public int ActiveCount => MingWorkers.ActiveCount;
#if DEBUG
        public void Diagnostics() => MingWorkers.Diagnostics();
        public float HammerContactNormalizedTime
        {
            get => MingWorkers.HammerContactNormalizedTime;
            set => MingWorkers.HammerContactNormalizedTime = value;
        }
        public void ProbeSwing(float seconds) => MingWorkers.ProbeSwing(seconds);
#endif
    }
}

public sealed class MingConstructionQuest : Based.Runtime.ConstructionQuest
{
    protected override string BaseId => MingDefinition.BaseId;
}
