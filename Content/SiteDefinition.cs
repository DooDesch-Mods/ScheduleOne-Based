using Based.Core;
using UnityEngine;

namespace Based.Content;

internal enum PlanRoomKind { Existing, New, Opening }

// One rectangle of the plan drawing, in world metres. A label key names the room it sits in.
internal readonly record struct PlanRoom(PlanRoomKind Kind, float MinX, float MinZ, float MaxX, float MaxZ, string? Label = null);

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
    public string VendorNpcId { get; init; } = "";
    public string VendorContainerId { get; init; } = "";
    public string RequestChoice { get; init; } = "";
    public string ProgressChoice { get; init; } = "";
    public string VendorLine { get; init; } = "";
    public string Title { get; init; } = "";
    public IReadOnlyDictionary<string, string> PhaseTitles { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<string> Restrictions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<Bounds> KeepClear { get; init; } = Array.Empty<Bounds>();
    public IReadOnlyList<PlanRoom> Plan { get; init; } = Array.Empty<PlanRoom>();
    public Action RegisterWorld { get; init; } = () => { };
    public ISiteCrew? Crew { get; init; }

    public UpgradeDefinition Upgrade => Base.Upgrades.First(u => u.Id == UpgradeId);

    public void Validate()
    {
        if (!Base.Upgrades.Any(u => u.Id == UpgradeId)) throw new InvalidOperationException(Base.Id + " has no upgrade " + UpgradeId);
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
