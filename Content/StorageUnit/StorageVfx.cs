using Based.Core;
using Il2CppScheduleOne.Audio;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Tools;
using Il2CppScheduleOne.GameTime;
using MapExpansions;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Based.Content.StorageUnit;

// Particle effects at the Storage Unit site. A world state's copies are stripped to meshes and lights, so the effects
// are clones of particle objects the game keeps running in the map: the fire from a recycler's exhaust in the yard's
// fire barrel at night, and an ATM's sparks where the crew cuts on the night a stage is built.
internal static class StorageVfx
{
    private const string FireTemplate = "Map/Hyland Point/Recyclers/Recycler (10)/Recycler/recycler/Exhaust.001/FX_Fire_01";
    private const string SparksTemplate = "Map/Hyland Point/ATMs/ATM (3)/FX_Sparks_01";
    private const float CheckInterval = 1f;
    private const string FireId = "yard-fire";

    private static readonly Dictionary<string, GameObject> Live = new();
    private static readonly Dictionary<string, GameObject> Templates = new();
    private static readonly HashSet<string> Missing = new();
    private static float _next;
    private static string _shown = "";

    // Off hides every effect; Force shows the fire and the sparks of the current stage at any hour, for shots and the
    // frame-cost measurement.
    internal enum Mode { Normal, Off, Force }
    internal static Mode Current = Mode.Normal;

    internal static void Tick(bool constructing)
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + CheckInterval;
        string? state = MapExpansionsApi.IsWorldStateApplied(StorageDefinition.ExpansionId) ? MapExpansionsApi.GetWorldState(StorageDefinition.ExpansionId) : null;
        int level = StorageWorld.LevelOf(state);
        bool night = IsNight();
        bool force = Current == Mode.Force, off = Current == Mode.Off;
        var wanted = new Dictionary<string, (string Template, Vector3 At, float Yaw)>();
        if (!off && level >= 1 && (night || force)) wanted[FireId] =(FireTemplate, StorageWorld.FireSpot, 0f);
        var spark = StorageWorld.SparkSpot(level);
        if (!off && spark != null && ((constructing && night) || force)) wanted["work-sparks"] = (SparksTemplate, spark.Value.At, spark.Value.Yaw);

        // An effect whose spot moved with the stage goes and comes back at the new spot.
        foreach (var id in Live.Keys.ToList())
            if (!wanted.TryGetValue(id, out var keep) || Live[id] == null || (Live[id].transform.position - keep.At).sqrMagnitude > .01f) Remove(id);
        foreach (var (id, effect) in wanted)
            if (!Live.ContainsKey(id)) Spawn(id, effect.Template, effect.At, effect.Yaw);
        // Sparks that burnt out start a new burst, besides the interval script's own, so the cutting never pauses
        // for longer than a check.
        if (Live.TryGetValue("work-sparks", out var sparks) && sparks != null && Particles(sparks) == 0)
            foreach (var system in sparks.GetComponentsInChildren<ParticleSystem>(true)) system.Play(true);

        string shown = string.Join(",", Live.Keys.OrderBy(k => k)) + "@" + level;
        if (shown != _shown)
        {
            _shown = shown;
            Mod.Log("Storage site effects: " + (Live.Count == 0 ? "none" : string.Join(", ", Live.Keys)) + " at stage " + (state ?? "none")
                + (constructing ? ", constructing" : "") + (night ? ", night" : ", day") + ", mode " + Current + ".");
        }
    }

    // Each live effect with its position and the particles alive right now, so a check can tell a placed effect
    // that never emits from one that does.
    internal static string Describe() =>
        "mode " + Current + ", live " + (Live.Count == 0 ? "none" : string.Join(", ", Live.Select(p => p.Key + " at "
            + (p.Value == null ? "destroyed" : p.Value.transform.position + " with " + Particles(p.Value) + " particle(s)"))));

    private static int Particles(GameObject effect)
    {
        int count = 0;
        foreach (var system in effect.GetComponentsInChildren<ParticleSystem>(true)) count += system.particleCount;
        return count;
    }

    // Forces the next tick to re-check at once.
    internal static void Refresh() => _next = 0;

    internal static void Reset()
    {
        foreach (var id in Live.Keys.ToList()) Remove(id);
        Templates.Clear();
        Missing.Clear();
        _next = 0;
        _shown = "";
    }

    private static bool IsNight()
    {
        if (!NetworkSingleton<TimeManager>.InstanceExists) return false;
        int time = NetworkSingleton<TimeManager>.Instance.CurrentTime;
        return time >= 1800 || time < 600;
    }

    private static void Spawn(string id, string path, Vector3 at, float yaw)
    {
        var template = Template(path);
        if (template == null) return;
        var clone = Object.Instantiate(template);
        clone.name = "Based_StorageVfx_" + id;
        clone.transform.SetParent(null, false);
        clone.transform.position = at;
        clone.transform.rotation = Quaternion.Euler(0, yaw, 0) * template.transform.rotation;
        clone.transform.localScale = template.transform.lossyScale;
        // The ATM's sparks fire from the game's random-interval script, which also plays their crackle; it stays and
        // fires more often, like welding. Every other script belongs to the recycler or the ATM and goes.
        int removed = 0, kept = 0;
        foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
        {
            var interval = behaviour.TryCast<RandomIntervalEvent>();
            if (interval != null)
            {
                interval.MinInterval = .4f;
                interval.MaxInterval = 1.6f;
                interval.ExecuteOnEnable = true;
                kept++;
                continue;
            }
            if (behaviour.TryCast<RandomizedAudioSourceController>() != null)
            {
                kept++;
                continue;
            }
            Object.Destroy(behaviour);
            removed++;
        }
        // The fire lights the yard around the barrel; the recycler's fire has no light of its own.
        if (id == FireId)
        {
            var glow = new GameObject("Based_StorageVfx_fire-light");
            glow.transform.SetParent(clone.transform, false);
            glow.transform.position = at + new Vector3(0, .5f, 0);
            var light = glow.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, .55f, .22f);
            light.range = 5f;
            light.intensity = 2.2f;
            light.shadows = LightShadows.None;
        }
        // The clone is active from the moment it exists, so play on awake and the interval script's first burst may
        // already have passed before the position was set; every particle system starts again at the new spot.
        clone.SetActive(true);
        foreach (var system in clone.GetComponentsInChildren<ParticleSystem>(true)) system.Play(true);
        int renderers = clone.GetComponentsInChildren<Renderer>(true).Length;
        Live[id] = clone;
        Mod.Log("Storage site effect " + id + " placed at " + at + " from " + path + ": " + renderers + " renderer(s), " + kept + " script(s) kept, " + removed + " removed.");
    }

    private static void Remove(string id)
    {
        if (Live.TryGetValue(id, out var clone) && clone != null) Object.Destroy(clone);
        Live.Remove(id);
        Mod.Log("Storage site effect " + id + " removed.");
    }

    // A lookup that fails is reported once and tried again on the next check.
    private static GameObject? Template(string path)
    {
        if (Templates.TryGetValue(path, out var cached) && cached != null) return cached;
        var found = FindInMap(path);
        if (found == null)
        {
            if (Missing.Add(path)) Mod.Warn("Storage site effect skipped: the source " + path + " is not in the scene or not active.");
            return null;
        }
        Missing.Remove(path);
        Templates[path] = found;
        return found;
    }

    // GameObject.Find sees active objects only, and a joined client holds parts of the map inactive that the host keeps
    // active. Transform.Find walks inactive children too, so only the root has to be active.
    internal static GameObject? FindInMap(string path)
    {
        int slash = path.IndexOf('/');
        if (slash < 0) return GameObject.Find(path);
        // A bare name matches any object of that name; the leading slash keeps the search to scene roots.
        var root = GameObject.Find("/" + path.Substring(0, slash));
        var child = root == null ? null : root.transform.Find(path.Substring(slash + 1));
        return child == null ? null : child.gameObject;
    }
}
