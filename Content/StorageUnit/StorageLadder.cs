using Based.Core;
using Il2CppScheduleOne.Map;
using MapExpansions;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace Based.Content.StorageUnit;

// The ladder from the hall up to the loft, from the south stage on. It is a clone of the game's own climbable ladder at
// the chemical plant: its trigger mounts the local player and climbing is the game's. A world state's copies lose their
// scripts, so the ladder is cloned at runtime on every machine from the stage the world state shows, like the effects.
// The clone has no networked part; each player climbs on their own machine.
internal static class StorageLadder
{
    private const string Template = "Map/Hyland Point/Region_Westville/ChemicalPlant/Functional Ladder";
    // The template's origin is the trigger's bottom corner on the climber's side; its ground lies there, its yellow
    // ladder reaches 3.42 m above it and its trigger 3.40 m. Placed on the hall floor (0.10) it reaches 0.25 m above
    // the loft floor (3.25), like the plant's roof. Turned like the template, the climber stands south of it in the
    // hall and faces north, and the ladder's back (0.24 m behind the origin) stands 2 cm in front of the loft's edge
    // (103.50). It stands between Unit 1's door frame (-4.25) and Unit 2's (-1.93), under the gallery railing's opening.
    internal static readonly Vector3 Origin = new(-2.95f, .1f, 103.24f);
    internal const float OpeningWest = -3.2f, Width = .6f;
    // The south stage's first night.
    internal const int FromLevel = 6;
    private const float CheckInterval = 1f;

    private static GameObject? _live;
    private static bool _missingReported;
    private static float _next;

    internal static GameObject? Live => _live;

    internal static void Tick()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + CheckInterval;
        string? state = MapExpansionsApi.IsWorldStateApplied(StorageDefinition.ExpansionId) ? MapExpansionsApi.GetWorldState(StorageDefinition.ExpansionId) : null;
        bool wanted = StorageWorld.LevelOf(state) >= FromLevel;
        if (wanted && _live == null) Place(state!);
        else if (!wanted && _live != null) Remove("stage " + (state ?? "none"));
    }

    internal static string Describe()
    {
        if (_live == null) return "no ladder";
        var ladder = _live.GetComponent<Ladder>();
        var box = _live.GetComponent<BoxCollider>();
        return "ladder at " + _live.transform.position + ", trigger " + (box == null ? "missing" : box.bounds.min + ".." + box.bounds.max)
            + ", climb height " + (ladder == null ? "no Ladder script" : ladder.LadderSize.y.ToString("F2")) + ".";
    }

    internal static void Refresh() => _next = 0;

    internal static void Reset()
    {
        if (_live != null) Remove("the game returned to the menu");
        _live = null;
        _missingReported = false;
        _next = 0;
    }

    private static void Place(string state)
    {
        var template = StorageVfx.FindInMap(Template);
        if (template == null)
        {
            if (!_missingReported) Mod.Warn("Storage ladder skipped: the template " + Template + " is not in the scene.");
            _missingReported = true;
            return;
        }
        _missingReported = false;
        var clone = Object.Instantiate(template);
        clone.name = "Based_StorageLadder";
        clone.transform.SetParent(null, false);
        clone.transform.SetPositionAndRotation(Origin, template.transform.rotation);
        clone.transform.localScale = template.transform.lossyScale;
        // The plant's links let NPCs climb; employees keep the outside stair the walking map was checked for.
        int links = 0;
        foreach (var link in clone.GetComponentsInChildren<OffMeshLink>(true))
        {
            Object.Destroy(link);
            links++;
        }
        var ladder = clone.GetComponent<Ladder>();
        if (ladder == null)
        {
            Object.Destroy(clone);
            Mod.Error("Storage ladder not placed: the clone of " + Template + " has no Ladder script.");
            return;
        }
        ladder.OffMeshLink = null;
        ladder.LinkedManholeCover = null;
        clone.SetActive(true);
        _live = clone;
        Mod.Log("Storage ladder placed at " + Origin + " for stage " + state + ": " + links + " off-mesh link(s) removed, "
                + clone.GetComponentsInChildren<Renderer>(true).Length + " renderer(s). " + Describe());
    }

    private static void Remove(string why)
    {
        if (_live != null) Object.Destroy(_live);
        _live = null;
        Mod.Log("Storage ladder removed: " + why + ".");
    }
}
