using Based.Core;
using UnityEngine;

namespace Based.Content;

internal enum PlanRoomKind { Existing, New, Opening }

// One rectangle of the plan drawing, in world metres. A label key names the room it sits in.
internal readonly record struct PlanRoom(PlanRoomKind Kind, float MinX, float MinZ, float MaxX, float MaxZ, string? Label = null);

// A build ordered in the project folder once the first upgrade is done. Text fields hold the English key.
// A build with nights of work between its stages has a journal page of its own, so it names its quest class and
// order entry. KeepClear holds what must be free before it is ordered, Staff the employees its finished room adds.
internal sealed record SiteFollowUp(string UpgradeId, string Title, string Done, IReadOnlyList<string> Notes)
{
    // The line in the folder's list of builds; the title when empty.
    public string Name { get; init; } = "";
    public Type? QuestType { get; init; }
    public string OrderEntry { get; init; } = "";
    public IReadOnlyList<Bounds> KeepClear { get; init; } = Array.Empty<Bounds>();
    public int Staff { get; init; }
    // The plan drawing of this build; the site's plan when empty.
    public IReadOnlyList<PlanRoom> Plan { get; init; } = Array.Empty<PlanRoom>();
    public string ListName => Name.Length > 0 ? Name : Title;
}

// One journal page: the upgrade it follows, the saved quest class and its two fixed texts.
internal sealed record SiteQuest(string UpgradeId, Type QuestType, string Title, string OrderEntry);

#if DEBUG
// What the room acceptance probe needs: the expansion with its finished state, the extension that adds the floor tiles,
// the new area, and every surface of the finished state as (extension id, surface id).
internal sealed record SiteRoom(string ExpansionId, string CompleteState, string FloorExtensionId, Bounds Area,
    Func<IEnumerable<(string Extension, string Surface)>> Surfaces);
#endif

internal interface ISiteCrew
{
    void Initialize(Action<string> info, Action<string> warn);
    // The build the crew follows: the one under construction, otherwise the site's main upgrade.
    void Update(string upgrade, UpgradeStage stage, string? phase);
    void Tick();
    void Cleanup();
    int ActiveCount { get; }
#if DEBUG
    void Diagnostics();
    float HammerContactNormalizedTime { get; set; }
    void ProbeSwing(float seconds);
#endif
}

// Everything the core needs from one piece of content: who sells it, what must stay free and how the plan draws it.
// Text fields hold the English key; the UI translates them when it shows them.
internal sealed class SiteDefinition
{
    public BaseDefinition Base { get; init; } = null!;
    public string UpgradeId { get; init; } = "";
    public string PropertyName { get; init; } = "";
    public string VendorNpcId { get; init; } = "";
    public string VendorContainerId { get; init; } = "";
    public string RequestChoice { get; init; } = "";
    public string ProgressChoice { get; init; } = "";
    public string VendorLine { get; init; } = "";
    public string Title { get; init; } = "";
    // Saved ids: the project folder item and the Sideload surface of its page. Renaming either loses folders in saves.
    public string FolderItemId { get; init; } = "";
    public string BoardSurfaceId { get; init; } = "";
    public Type QuestType { get; init; } = null!;
    public string OrderEntry { get; init; } = "";
    // The notice while the player does not own the property yet.
    public string BuyFirst { get; init; } = "";
    public Vector3 QuestMarker { get; init; }
    public IReadOnlyDictionary<string, string> PhaseTitles { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<string> Restrictions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<Bounds> KeepClear { get; init; } = Array.Empty<Bounds>();
    public IReadOnlyList<PlanRoom> Plan { get; init; } = Array.Empty<PlanRoom>();
    // Where the additional employees of the finished room wait, in world metres.
    public IReadOnlyList<Vector3> StaffPlaces { get; init; } = Array.Empty<Vector3>();
    public Action RegisterWorld { get; init; } = () => { };
    public ISiteCrew? Crew { get; init; }
#if DEBUG
    public SiteRoom? Room { get; init; }
#endif
    // In order of appearance in the folder. Each needs the first upgrade, directly or through another follow-up.
    public IReadOnlyList<SiteFollowUp> FollowUps { get; init; } = Array.Empty<SiteFollowUp>();
    // The folder lists every follow-up to pick from, instead of showing the next one in line. Needs a permit as the
    // first upgrade; the vendor's single entry then serves all of his menu sites.
    public bool Menu { get; init; }

    public UpgradeDefinition Upgrade => Base.Upgrades.First(u => u.Id == UpgradeId);
    public UpgradeDefinition UpgradeOf(string upgradeId) => Base.Upgrades.First(u => u.Id == upgradeId);

    public IEnumerable<string> BuildIds => new[] { UpgradeId }.Concat(FollowUps.Select(f => f.UpgradeId));

    // A permit has no work and no page; every other first upgrade and each follow-up with stages has one.
    public IEnumerable<SiteQuest> Quests
    {
        get
        {
            if (!Upgrade.IsPermit) yield return new SiteQuest(UpgradeId, QuestType, Title, OrderEntry);
            foreach (var followUp in FollowUps)
                if (followUp.QuestType != null) yield return new SiteQuest(followUp.UpgradeId, followUp.QuestType, followUp.Title, followUp.OrderEntry);
        }
    }

    public SiteQuest Quest(string upgradeId) => Quests.First(q => q.UpgradeId == upgradeId);

    public IReadOnlyList<Bounds> KeepClearOf(string upgradeId) =>
        upgradeId == UpgradeId ? KeepClear : FollowUp(upgradeId)?.KeepClear ?? Array.Empty<Bounds>();

    public IReadOnlyList<PlanRoom> PlanOf(string upgradeId) =>
        FollowUp(upgradeId) is { Plan.Count: > 0 } followUp ? followUp.Plan : Plan;

    // The engine allows one construction per base, so there is at most one.
    public string? ConstructingId(Func<string, UpgradeStage> stageOf) =>
        BuildIds.FirstOrDefault(id => stageOf(id) == UpgradeStage.Constructing);

    // Employees on top of the game's limit: the first upgrade's room doubles it, each finished follow-up adds its own.
    public int StaffLimit(int vanilla, Func<string, UpgradeStage> stageOf)
    {
        int limit = !Upgrade.IsPermit && stageOf(UpgradeId) == UpgradeStage.Complete ? Core.StaffLimit.Expanded(vanilla) : vanilla;
        foreach (var followUp in FollowUps)
            if (stageOf(followUp.UpgradeId) == UpgradeStage.Complete) limit += followUp.Staff;
        return limit;
    }

    // The most the site can ever hold, for places a joining client needs before the host's state arrives.
    public int MaxStaffLimit(int vanilla) => StaffLimit(vanilla, _ => UpgradeStage.Complete);

    // The stage the folder shows: the first expansion until it is done, then the first follow-up not yet done,
    // and the last one when everything is built.
    public string ActiveUpgradeId(Func<string, UpgradeStage> stageOf)
    {
        if (stageOf(UpgradeId) != UpgradeStage.Complete || FollowUps.Count == 0) return UpgradeId;
        foreach (var followUp in FollowUps)
            if (stageOf(followUp.UpgradeId) != UpgradeStage.Complete) return followUp.UpgradeId;
        return FollowUps[^1].UpgradeId;
    }

    public SiteFollowUp? FollowUp(string upgradeId) => FollowUps.FirstOrDefault(f => f.UpgradeId == upgradeId);

    // The ids every site needs for itself: S1API rebuilds quests by class name and the game finds items by id.
    public static void ValidateAll(IReadOnlyList<SiteDefinition> sites)
    {
        foreach (var site in sites) site.Validate();
        void Distinct(string what, Func<SiteDefinition, object> key)
        {
            var twice = sites.GroupBy(key).FirstOrDefault(g => g.Count() > 1);
            if (twice != null) throw new InvalidOperationException(string.Join(", ", twice.Select(s => s.Base.Id)) + " share the " + what + " " + twice.Key + ".");
        }
        Distinct("base id", s => s.Base.Id);
        Distinct("property", s => s.Base.PropertyId);
        Distinct("folder item", s => s.FolderItemId);
        Distinct("board surface", s => s.BoardSurfaceId);
        Distinct("vendor conversation", s => s.VendorContainerId);
        var quests = sites.SelectMany(s => s.Quests.Select(q => (Site: s, Quest: q))).ToArray();
        var sharedClass = quests.GroupBy(q => q.Quest.QuestType).FirstOrDefault(g => g.Count() > 1);
        if (sharedClass != null)
            throw new InvalidOperationException(string.Join(", ", sharedClass.Select(q => q.Site.Base.Id + "/" + q.Quest.UpgradeId)) + " share the quest class " + sharedClass.Key + ".");
        // The journal finds a quest by its title.
        var sharedTitle = quests.GroupBy(q => q.Quest.Title).FirstOrDefault(g => g.Count() > 1);
        if (sharedTitle != null)
            throw new InvalidOperationException(string.Join(", ", sharedTitle.Select(q => q.Site.Base.Id + "/" + q.Quest.UpgradeId)) + " share the quest title " + sharedTitle.Key + ".");
    }

    public void Validate()
    {
        if (!Base.Upgrades.Any(u => u.Id == UpgradeId)) throw new InvalidOperationException(Base.Id + " has no upgrade " + UpgradeId);
        if (string.IsNullOrWhiteSpace(FolderItemId) || string.IsNullOrWhiteSpace(BoardSurfaceId))
            throw new InvalidOperationException(Base.Id + " needs a folder item and a board surface.");
        if (string.IsNullOrWhiteSpace(BuyFirst)) throw new InvalidOperationException(Base.Id + " needs the text BuyFirst.");
        if (!Upgrade.IsPermit && (QuestType == null || string.IsNullOrWhiteSpace(OrderEntry)))
            throw new InvalidOperationException(Base.Id + " needs a quest class and the text OrderEntry for " + UpgradeId + ".");
        if (Menu && !Upgrade.IsPermit) throw new InvalidOperationException(Base.Id + ": a menu site starts with a permit.");
        if (FollowUps.Select(f => f.UpgradeId).Distinct().Count() != FollowUps.Count) throw new InvalidOperationException(Base.Id + " lists a follow-up twice.");
        foreach (var followUp in FollowUps)
        {
            var upgrade = Base.Upgrades.FirstOrDefault(u => u.Id == followUp.UpgradeId)
                ?? throw new InvalidOperationException(Base.Id + " has no follow-up upgrade " + followUp.UpgradeId);
            if (upgrade.IsPermit) throw new InvalidOperationException(followUp.UpgradeId + ": a follow-up is built, not a permit.");
            if (upgrade.Phases.Count > 0 && (followUp.QuestType == null || string.IsNullOrWhiteSpace(followUp.OrderEntry)))
                throw new InvalidOperationException(followUp.UpgradeId + " has stages, so it needs a quest class and the text OrderEntry.");
            if (followUp.Staff < 0) throw new InvalidOperationException(followUp.UpgradeId + ": staff must not be negative.");
            if (!Needs(upgrade, UpgradeId)) throw new InvalidOperationException(followUp.UpgradeId + " must need " + UpgradeId + ".");
        }
        foreach (var upgrade in Base.Upgrades)
        {
            foreach (var effect in upgrade.Effects)
                if (effect.Kind != UpgradeEffectKinds.WorldState)
                    throw new InvalidOperationException(upgrade.Id + " uses the unknown effect kind " + effect.Kind);
            foreach (var phase in upgrade.Phases)
                if (!PhaseTitles.ContainsKey(phase.Id))
                    throw new InvalidOperationException(upgrade.Id + " has no title for phase " + phase.Id);
        }
    }

    // The engine has already refused cycles, so the walk ends.
    private bool Needs(UpgradeDefinition upgrade, string id) =>
        upgrade.Prerequisites.Any(p => p == id || Needs(UpgradeOf(p), id));
}

internal static class UpgradeEffectKinds
{
    public const string WorldState = "world-state";
}
