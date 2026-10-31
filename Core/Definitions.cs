namespace Based.Core;

public sealed record BaseDefinition(string Id, string PropertyId, IReadOnlyList<UpgradeDefinition> Upgrades);

public sealed record UpgradeDefinition(
    string Id,
    int RightsPrice,
    int ConstructionPrice,
    int ConstructionCycles,
    IReadOnlyList<string> Prerequisites,
    IReadOnlyList<ConstructionPhase> Phases,
    IReadOnlyList<UpgradeEffect> Effects);

public sealed record ConstructionPhase(string Id, int AfterSleepCycles, string WorldStateId);
public sealed record UpgradeEffect(string Kind, string TargetId, string StateId);
public sealed record AppliedEffect(string BaseId, string UpgradeId, UpgradeEffect Effect);

public sealed class DefinitionException : ArgumentException
{
    public DefinitionException(string message) : base(message) { }
}

internal static class DefinitionRegistry
{
    internal static Dictionary<string, BaseDefinition> Create(IEnumerable<BaseDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var result = new Dictionary<string, BaseDefinition>(StringComparer.Ordinal);
        foreach (var source in definitions)
        {
            if (source == null) throw new DefinitionException("A base definition is null.");
            RequireId(source.Id, "base");
            RequireId(source.PropertyId, source.Id + " property");
            if (result.ContainsKey(source.Id)) throw new DefinitionException("Duplicate base: " + source.Id);
            if (source.Upgrades == null) throw new DefinitionException(source.Id + ": upgrades are null.");
            var upgrades = new Dictionary<string, UpgradeDefinition>(StringComparer.Ordinal);
            foreach (var upgrade in source.Upgrades)
            {
                if (upgrade == null) throw new DefinitionException(source.Id + ": upgrade is null.");
                RequireId(upgrade.Id, "upgrade");
                string key = source.Id + "/" + upgrade.Id;
                if (upgrades.ContainsKey(upgrade.Id)) throw new DefinitionException("Duplicate upgrade: " + key);
                if (upgrade.RightsPrice < 0 || upgrade.ConstructionPrice < 0 || upgrade.ConstructionCycles < 1)
                    throw new DefinitionException(key + ": prices must be nonnegative and construction needs at least one cycle.");
                if (upgrade.Prerequisites == null || upgrade.Phases == null || upgrade.Effects == null)
                    throw new DefinitionException(key + ": definition lists must not be null.");
                var prerequisites = upgrade.Prerequisites.ToArray();
                foreach (var prerequisite in prerequisites) RequireId(prerequisite, key + " prerequisite");
                if (prerequisites.Distinct(StringComparer.Ordinal).Count() != prerequisites.Length)
                    throw new DefinitionException(key + ": duplicate prerequisite.");
                var phaseIds = new HashSet<string>(StringComparer.Ordinal);
                int previous = 0;
                foreach (var phase in upgrade.Phases)
                {
                    if (phase == null) throw new DefinitionException(key + ": phase is null.");
                    RequireId(phase.Id, key + " phase");
                    RequireId(phase.WorldStateId, key + " world state");
                    if (!phaseIds.Add(phase.Id) || phase.AfterSleepCycles <= previous || phase.AfterSleepCycles >= upgrade.ConstructionCycles)
                        throw new DefinitionException(key + ": phases need unique IDs and increasing cycle thresholds before completion.");
                    previous = phase.AfterSleepCycles;
                }
                var effectIds = new HashSet<(string, string)>();
                foreach (var effect in upgrade.Effects)
                {
                    if (effect == null) throw new DefinitionException(key + ": effect is null.");
                    RequireId(effect.Kind, key + " effect kind");
                    RequireId(effect.TargetId, key + " effect target");
                    RequireId(effect.StateId, key + " effect state");
                    if (!effectIds.Add((effect.Kind, effect.TargetId))) throw new DefinitionException(key + ": duplicate effect target.");
                }
                upgrades.Add(upgrade.Id, upgrade with
                {
                    Prerequisites = Array.AsReadOnly(prerequisites),
                    Phases = Array.AsReadOnly(upgrade.Phases.ToArray()),
                    Effects = Array.AsReadOnly(upgrade.Effects.ToArray())
                });
            }
            foreach (var upgrade in upgrades.Values)
                foreach (string dependency in upgrade.Prerequisites)
                    if (!upgrades.ContainsKey(dependency)) throw new DefinitionException(source.Id + "/" + upgrade.Id + ": missing prerequisite " + dependency);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            void Visit(string id)
            {
                if (visited.Contains(id)) return;
                if (!visiting.Add(id)) throw new DefinitionException(source.Id + ": cyclic prerequisite at " + id);
                foreach (string dependency in upgrades[id].Prerequisites) Visit(dependency);
                visiting.Remove(id);
                visited.Add(id);
            }
            foreach (string id in upgrades.Keys) Visit(id);
            result.Add(source.Id, source with { Upgrades = Array.AsReadOnly(upgrades.Values.ToArray()) });
        }
        return result;
    }

    internal static void RequireId(string value, string context)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new DefinitionException(context + ": ID must not be empty.");
    }
}
