using Based.Content.StorageUnit;
using MapExpansions;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Based.Content.Bungalow;

// The storey site's effects, cloned from the same running map effects as the Storage Unit's: fire in the site's barrel
// at night while the site stands, and sparks at the day's weld while the fitter stands there. The Storage Unit's effect
// mode (off, normal, forced) applies here too.
internal static class BungalowVfx
{
    private const float CheckInterval = 1f;
    private const string FireId = "site-fire", SparksId = "weld-sparks";

    private static readonly Dictionary<string, GameObject> Live = new();
    private static float _next;
    private static string _shown = "";

    internal static void Tick(bool constructing)
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + CheckInterval;
        string? state = MapExpansionsApi.IsWorldStateApplied(BungalowDefinition.StoreyExpansionId)
            ? MapExpansionsApi.GetWorldState(BungalowDefinition.StoreyExpansionId) : null;
        int level = BungalowSiteProps.LevelOf(state);
        bool night = StorageVfx.IsNight();
        bool force = StorageVfx.Current == StorageVfx.Mode.Force, off = StorageVfx.Current == StorageVfx.Mode.Off;
        bool site = level is >= 1 and <= 4;
        var wanted = new Dictionary<string, (string Template, Vector3 At)>();
        if (!off && site && (night || force)) wanted[FireId] = (StorageVfx.Fire, BungalowSiteProps.FireSpot);
        var weld = BungalowSiteProps.Weld(level);
        bool welder = BungalowCrew.WelderAtSpot;
        if (!off && weld != null && ((constructing && welder) || force)) wanted[SparksId] = (StorageVfx.Sparks, weld.Value.Spark);

        foreach (var id in Live.Keys.ToList())
            if (!wanted.TryGetValue(id, out var keep) || Live[id] == null || (Live[id].transform.position - keep.At).sqrMagnitude > .01f) Remove(id);
        foreach (var (id, effect) in wanted)
            if (!Live.ContainsKey(id))
            {
                var clone = StorageVfx.Place("Based_BungalowVfx_" + id, effect.Template, effect.At, 0f, id == FireId);
                if (clone != null) Live[id] = clone;
                else Mod.Warn("Bungalow site effect " + id + " could not be placed; its source is missing.");
            }
        // Sparks that burnt out start a new burst, so the welding never pauses for longer than a check.
        if (Live.TryGetValue(SparksId, out var sparks) && sparks != null && StorageVfx.Particles(sparks) == 0)
            foreach (var system in sparks.GetComponentsInChildren<ParticleSystem>(true)) system.Play(true);

        string shown = string.Join(",", Live.Keys.OrderBy(k => k)) + "@" + level + (welder ? "+welder" : "");
        if (shown != _shown)
        {
            _shown = shown;
            Mod.Log("Bungalow site effects: " + (Live.Count == 0 ? "none" : string.Join(", ", Live.Keys)) + " at stage " + (state ?? "none")
                + (constructing ? ", constructing" : "") + (night ? ", night" : ", day") + (welder ? ", welder at the weld" : ", no welder")
                + ", mode " + StorageVfx.Current + ".");
        }
    }

    // Each live effect with its position and the particles alive right now.
    internal static string Describe() =>
        "live " + (Live.Count == 0 ? "none" : string.Join(", ", Live.Select(p => p.Key + " at "
            + (p.Value == null ? "destroyed" : p.Value.transform.position + " with " + StorageVfx.Particles(p.Value) + " particle(s)"))));

    internal static void Refresh() => _next = 0;

    internal static void Reset()
    {
        foreach (var id in Live.Keys.ToList()) Remove(id);
        _next = 0;
        _shown = "";
    }

    private static void Remove(string id)
    {
        if (Live.TryGetValue(id, out var clone) && clone != null) Object.Destroy(clone);
        Live.Remove(id);
        Mod.Log("Bungalow site effect " + id + " removed.");
    }
}
