using Based.Core;
using Il2CppScheduleOne.Map;
using MapExpansions;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace Based.Content.StorageUnit;

// The ladder from the hall up to the walkway, from the south stage on. It is a clone of the game's own climbable ladder at
// the chemical plant: its trigger mounts the local player and climbing is the game's. A world state's copies lose their
// scripts, so the ladder is cloned at runtime on every machine from the stage the world state shows, like the effects.
// The clone has no networked part; each player climbs on their own machine.
internal static class StorageLadder
{
    private const string Template = "Map/Hyland Point/Region_Westville/ChemicalPlant/Functional Ladder";
    // The template's origin is the trigger's bottom corner on the climber's side; its ground lies there, its yellow
    // ladder reaches 3.42 m above it and its trigger 3.40 m. The ladder stands on the hall's east side against the
    // plain stretch of Unit 3's front (z 100.03 .. 101.5), between its roller door and the security camera, and climbs
    // to the walkway over Unit 3. Nobody passes there: the bays end 0.04 m west of it, the nook to Unit 2's door begins
    // north of the camera and Geraldine's door lies 3 m south. Turned 90 degrees from the template, the climber stands
    // west of it and faces east; the ladder's back (0.24 m behind the origin) lies on the walkway slab's edge (x -2.25)
    // and its rails run from z 100.18 to 100.74, centred between the posts of the walkway's railing above.
    internal static readonly Vector3 Origin = new(-2.49f, .1f, 100.74f);
    internal const float Yaw = 90f;
    // Where the rails stand, for the brackets to Unit 3's front and the opening in the walkway's railing.
    internal const float BackX = -2.25f, SouthRail = 100.18f, NorthRail = 100.74f;
    // The trigger keeps the template's height, 0.25 m over the walkway floor, so the climb ends over the edge as at the
    // plant; only the yellow ladder is lowered until its rails and top rung end level with the floor.
    private const string Visual = "ladder_a";
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
        clone.transform.SetPositionAndRotation(Origin, Quaternion.Euler(0f, Yaw, 0f) * template.transform.rotation);
        clone.transform.localScale = template.transform.lossyScale;
        string top = LowerToFloor(clone);
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
                + clone.GetComponentsInChildren<Renderer>(true).Length + " renderer(s), " + top + ". " + Describe());
    }

    // Moves the yellow ladder down until its highest point is the walkway floor; the part below the hall floor stays
    // hidden under it.
    private static string LowerToFloor(GameObject clone)
    {
        var visual = clone.transform.Find(Visual);
        var renderer = visual != null ? visual.GetComponentInChildren<Renderer>(true) : null;
        if (renderer == null)
        {
            Mod.Warn("Storage ladder: the clone has no " + Visual + " renderer; its top stays 0.27 m over the walkway floor.");
            return "top not lowered";
        }
        float drop = renderer.bounds.max.y - StorageWorld.UpperFloor;
        if (drop < 0f || drop > 1f)
        {
            Mod.Warn("Storage ladder: the yellow ladder's top is " + drop.ToString("F2") + " m from the walkway floor; it is left as cloned.");
            return "top not lowered";
        }
        visual!.position -= new Vector3(0f, drop, 0f);
        return "yellow ladder lowered " + drop.ToString("F2") + " m to top " + renderer.bounds.max.y.ToString("F2");
    }

    private static void Remove(string why)
    {
        if (_live != null) Object.Destroy(_live);
        _live = null;
        Mod.Log("Storage ladder removed: " + why + ".");
    }
}
