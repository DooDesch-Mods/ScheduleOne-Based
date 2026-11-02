using Based.Core;
using UnityEngine;

namespace Based.Content;

internal enum PlanRoomKind { Existing, New, Opening }

// One rectangle of the plan drawing, in world metres. A label key names the room it sits in.
internal readonly record struct PlanRoom(PlanRoomKind Kind, float MinX, float MinZ, float MaxX, float MaxZ, string? Label = null);

// A stage ordered in the project folder once the first expansion is done. Text fields hold the English key.
internal sealed record SiteFollowUp(string UpgradeId, string Title, string Done, IReadOnlyList<string> Notes);

internal interface ISiteCrew
{
    void Initialize(Action<string> info, Action<string> warn);
    void Update(UpgradeStage stage, string? phase);
    void Tick();
    void Cleanup();
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
    public Type QuestType { get; init; } = null!;
    public string OrderEntry { get; init; } = "";
    public Vector3 QuestMarker { get; init; }
    public IReadOnlyDictionary<string, string> PhaseTitles { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<string> Restrictions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<Bounds> KeepClear { get; init; } = Array.Empty<Bounds>();
    public IReadOnlyList<PlanRoom> Plan { get; init; } = Array.Empty<PlanRoom>();
    // Where the additional employees of the finished room wait, in world metres.
    public IReadOnlyList<Vector3> StaffPlaces { get; init; } = Array.Empty<Vector3>();
    public Action RegisterWorld { get; init; } = () => { };
    public ISiteCrew? Crew { get; init; }
    // In order of appearance in the folder; each needs the one before it.
    public IReadOnlyList<SiteFollowUp> FollowUps { get; init; } = Array.Empty<SiteFollowUp>();

    public UpgradeDefinition Upgrade => Base.Upgrades.First(u => u.Id == UpgradeId);
    public UpgradeDefinition UpgradeOf(string upgradeId) => Base.Upgrades.First(u => u.Id == upgradeId);

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

    public void Validate()
    {
        if (!Base.Upgrades.Any(u => u.Id == UpgradeId)) throw new InvalidOperationException(Base.Id + " has no upgrade " + UpgradeId);
        foreach (var followUp in FollowUps)
        {
            var upgrade = Base.Upgrades.FirstOrDefault(u => u.Id == followUp.UpgradeId)
                ?? throw new InvalidOperationException(Base.Id + " has no follow-up upgrade " + followUp.UpgradeId);
            if (upgrade.Phases.Count > 0) throw new InvalidOperationException(followUp.UpgradeId + ": follow-ups have no phases.");
            if (!upgrade.Prerequisites.Contains(UpgradeId)) throw new InvalidOperationException(followUp.UpgradeId + " must need " + UpgradeId + ".");
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
}

internal static class UpgradeEffectKinds
{
    public const string WorldState = "world-state";
}
