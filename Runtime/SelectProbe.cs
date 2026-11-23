#if DEBUG
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.UI.Management;
using UnityEngine;
using Property = Il2CppScheduleOne.Property.Property;

namespace Based.Runtime;

// Debug probes for the employee assign mode: which grid tiles of a property lie under something low, and what the game's
// own object selector picks when the player looks at a point.
internal static class SelectProbe
{
    // A station's body stands from the floor to about 1.5 m; the selector's look cast must reach it there.
    private const float BodyBottom = .1f, BodyTop = 1.5f, BodyHalf = .2f, Overhead = 4f;

    private static string F(float v) => v.ToString("F2", CultureInfo.InvariantCulture);
    private static string V(Vector3 v) => "(" + F(v.x) + ", " + F(v.y) + ", " + F(v.z) + ")";

    private static string Path(Transform t)
    {
        var parts = new List<string>();
        for (var at = t; at != null && parts.Count < 6; at = at.parent) parts.Add(at.name);
        parts.Reverse();
        return string.Join("/", parts);
    }

    // True for a collider under an object MapExpansions builds for an expansion (copies, boxes, shells).
    private static bool Expansion(Transform t)
    {
        for (var at = t; at != null; at = at.parent)
            if (at.name.StartsWith("MapExpansions.", StringComparison.Ordinal) || at.name.StartsWith("based_", StringComparison.Ordinal)) return true;
        return false;
    }

    private static ObjectSelector? Selector()
    {
        var found = Resources.FindObjectsOfTypeAll<ObjectSelector>();
        foreach (var s in found)
            if (s != null && s.gameObject.scene.IsValid()) return s;
        return null;
    }

    private static string MaskNames(int mask)
    {
        var names = new List<string>();
        for (int i = 0; i < 32; i++)
            if ((mask & (1 << i)) != 0) names.Add(i + ":" + LayerMask.LayerToName(i));
        return string.Join(", ", names);
    }

    // pickscan <code>: every tile of the property whose space above holds a collider the selector's look cast hits and
    // that is no buildable, and every tile with an expansion's collider (with all: any collider) less than 4 m overhead. Grouped per collider.
    internal static void Scan(string code, bool all)
    {
        Property? property = null;
        foreach (var p in Property.Properties)
            if (p != null && p.PropertyCode == code) property = p;
        if (property == null) { Mod.Warn("baseddiag pickscan: no property '" + code + "'."); return; }
        var selector = Selector();
        if (selector == null) { Mod.Warn("baseddiag pickscan: the game's object selector is not loaded."); return; }
        int mask = selector.DetectionMask.value;
        Mod.Log("baseddiag pickscan: '" + code + "', selector mask " + mask + " [" + MaskNames(mask) + "], " + property.Grids.Count + " grids.");
        var inside = new Dictionary<string, List<string>>();
        var under = new Dictionary<string, List<string>>();
        for (int g = 0; g < property.Grids.Count; g++)
        {
            var grid = property.Grids[g];
            if (grid == null) continue;
            foreach (var tile in grid.Tiles)
            {
                if (tile == null) continue;
                var at = tile.transform.position;
                string key = g + ":" + tile.x + "," + tile.y + "@" + V(at);
                var centre = at + Vector3.up * ((BodyBottom + BodyTop) * .5f);
                var half = new Vector3(BodyHalf, (BodyTop - BodyBottom) * .5f, BodyHalf);
                foreach (var c in Physics.OverlapBox(centre, half, grid.transform.rotation, mask, QueryTriggerInteraction.Ignore))
                {
                    if (c == null || c.GetComponentInParent<BuildableItem>() != null) continue;
                    string name = Path(c.transform) + " (layer " + c.gameObject.layer + ")";
                    if (!inside.TryGetValue(name, out var list)) inside[name] = list = new List<string>();
                    list.Add(key);
                }
                if (Physics.Raycast(at + Vector3.up * .05f, Vector3.up, out var hit, Overhead, ~0, QueryTriggerInteraction.Ignore)
                    && hit.collider.GetComponentInParent<BuildableItem>() == null && (all || Expansion(hit.collider.transform)))
                {
                    string name = Path(hit.collider.transform) + " (layer " + hit.collider.gameObject.layer + ", in mask "
                                  + ((mask & (1 << hit.collider.gameObject.layer)) != 0) + ")";
                    if (!under.TryGetValue(name, out var list)) under[name] = list = new List<string>();
                    list.Add(key + " h" + F(hit.distance + .05f));
                }
            }
        }
        foreach (var pair in inside)
            Mod.Log("baseddiag pickscan: inside " + pair.Key + ": " + pair.Value.Count + " tiles: " + string.Join(" ", pair.Value));
        foreach (var pair in under)
            Mod.Log("baseddiag pickscan: under " + pair.Key + ": " + pair.Value.Count + " tiles: " + string.Join(" ", pair.Value));
        Mod.Log("baseddiag pickscan: done, " + inside.Count + " colliders in station space, " + under.Count + " low overheads.");
    }

    // pickaround x y z: the buildable nearest the point, looked at from eight standpoints around it, 1.4 m beyond its
    // footprint, wherever a player fits and has floor underfoot. Each look runs the selector's own cast (5 m, radius 0.1,
    // its mask, no triggers) from a standing eye height toward the buildable's middle.
    internal static void Around(Vector3 point)
    {
        var selector = Selector();
        if (selector == null) { Mod.Warn("baseddiag pickaround: the game's object selector is not loaded."); return; }
        BuildableItem? item = null;
        float best = 1.5f;
        foreach (var c in Physics.OverlapSphere(point, 1.5f, ~0, QueryTriggerInteraction.Ignore))
        {
            var b = c?.GetComponentInParent<BuildableItem>();
            if (b == null) continue;
            float d = Vector3.Distance(b.transform.position, point);
            if (d < best) { best = d; item = b; }
        }
        if (item == null) { Mod.Warn("baseddiag pickaround: no buildable within 1.5 m of " + V(point) + "."); return; }
        var bounds = new Bounds(item.transform.position, Vector3.zero);
        foreach (var r in item.GetComponentsInChildren<Renderer>())
            if (r != null && r.enabled) bounds.Encapsulate(r.bounds);
        var aim = new Vector3(bounds.center.x, Mathf.Lerp(bounds.min.y, bounds.max.y, .6f), bounds.center.z);
        float floor = item.transform.position.y, radius = Mathf.Max(bounds.extents.x, bounds.extents.z) + 1.4f;
        int stands = 0, reached = 0;
        var blockers = new List<string>();
        for (int i = 0; i < 8; i++)
        {
            float a = i * 45f * Mathf.Deg2Rad;
            var feet = new Vector3(bounds.center.x + Mathf.Sin(a) * radius, floor, bounds.center.z + Mathf.Cos(a) * radius);
            if (!Physics.Raycast(feet + Vector3.up * .6f, Vector3.down, out var ground, 1.2f, ~0, QueryTriggerInteraction.Ignore)) continue;
            feet.y = ground.point.y;
            bool blocked = false;
            foreach (var c in Physics.OverlapCapsule(feet + Vector3.up * .4f, feet + Vector3.up * 1.6f, .3f, ~0, QueryTriggerInteraction.Ignore))
                if (c != null && c.GetComponentInParent<Il2CppScheduleOne.PlayerScripts.Player>() == null) { blocked = true; break; }
            if (blocked) continue;
            stands++;
            var eye = feet + Vector3.up * 1.6f;
            var dir = (aim - eye).normalized;
            string seen;
            if (Physics.SphereCast(eye, .1f, dir, out var hit, 5f, selector.DetectionMask, QueryTriggerInteraction.Ignore))
            {
                var owner = hit.collider.GetComponentInParent<BuildableItem>();
                if (owner != null && owner.Pointer == item.Pointer) { reached++; seen = "station"; }
                else seen = Path(hit.collider.transform) + " (layer " + hit.collider.gameObject.layer + ")";
            }
            else seen = "nothing within 5 m";
            if (seen != "station") blockers.Add((i * 45) + "deg: " + seen);
            Mod.Log("baseddiag pickaround: standpoint " + (i * 45) + " deg at " + V(feet) + " sees " + seen + ".");
        }
        Mod.Log("baseddiag pickaround: '" + item.ItemInstance?.Definition?.ID + "' at " + V(item.transform.position) + " reached from "
                + reached + " of " + stands + " standpoints" + (blockers.Count > 0 ? "; blocked " + string.Join("; ", blockers) : "") + ".");
    }

    // pickpoint x y z: a worker standing at the floor point, looked at from eight standpoints 1.6 m around it at chest
    // height. Runs the clipboard's look ray (ManagementWorldspaceCanvas: 6 m, its mask, triggers count) and the selector's
    // cast; a look is blocked when either stops on something more than 0.35 m short of the worker's middle.
    internal static void Point(Vector3 floor)
    {
        var selector = Selector();
        ManagementWorldspaceCanvas? canvas = null;
        foreach (var c in Resources.FindObjectsOfTypeAll<ManagementWorldspaceCanvas>())
            if (c != null && c.gameObject.scene.IsValid()) canvas = c;
        if (selector == null || canvas == null) { Mod.Warn("baseddiag pickpoint: selector " + (selector != null) + ", clipboard canvas " + (canvas != null) + "; both must be loaded."); return; }
        var body = floor + Vector3.up * 1.0f;
        int stands = 0, clear = 0;
        var blockers = new List<string>();
        for (int i = 0; i < 8; i++)
        {
            float a = i * 45f * Mathf.Deg2Rad;
            var feet = new Vector3(floor.x + Mathf.Sin(a) * 1.6f, floor.y, floor.z + Mathf.Cos(a) * 1.6f);
            if (!Physics.Raycast(feet + Vector3.up * .6f, Vector3.down, out var ground, 1.2f, ~0, QueryTriggerInteraction.Ignore)) continue;
            feet.y = ground.point.y;
            bool blocked = false;
            foreach (var c in Physics.OverlapCapsule(feet + Vector3.up * .4f, feet + Vector3.up * 1.6f, .3f, ~0, QueryTriggerInteraction.Ignore))
                if (c != null && c.GetComponentInParent<Il2CppScheduleOne.PlayerScripts.Player>() == null && c.GetComponentInParent<Il2CppScheduleOne.NPCs.NPC>() == null) { blocked = true; break; }
            if (blocked) continue;
            stands++;
            var eye = feet + Vector3.up * 1.6f;
            var dir = body - eye;
            float reach = dir.magnitude - .35f;
            dir.Normalize();
            string? stop = null;
            if (Physics.Raycast(eye, dir, out var ray, 6f, canvas.ObjectSelectionLayerMask, QueryTriggerInteraction.Collide) && ray.distance < reach
                && ray.collider.GetComponentInParent<Il2CppScheduleOne.NPCs.NPC>() == null)
                stop = "clipboard ray on " + Path(ray.collider.transform) + " (layer " + ray.collider.gameObject.layer + ") at " + F(ray.distance) + " m";
            else if (Physics.SphereCast(eye, .1f, dir, out var cast, 5f, selector.DetectionMask, QueryTriggerInteraction.Ignore) && cast.distance < reach
                     && cast.collider.GetComponentInParent<Il2CppScheduleOne.NPCs.NPC>() == null)
                stop = "selector cast on " + Path(cast.collider.transform) + " (layer " + cast.collider.gameObject.layer + ") at " + F(cast.distance) + " m";
            if (stop == null) clear++;
            else blockers.Add((i * 45) + "deg: " + stop);
        }
        Mod.Log("baseddiag pickpoint: worker at " + V(floor) + " clear from " + clear + " of " + stands + " standpoints"
                + (blockers.Count > 0 ? "; blocked " + string.Join("; ", blockers) : "") + ".");
    }

    // pick x y z: turns the player's view toward the point and asks the game's own object selector what it hovers, with
    // its own mask and cast. Also names the collider that cast hits first.
    internal static void Pick(Vector3 target)
    {
        var camera = Il2CppScheduleOne.DevUtilities.PlayerSingleton<Il2CppScheduleOne.PlayerScripts.PlayerCamera>.Instance;
        var player = Il2CppScheduleOne.PlayerScripts.Player.Local;
        if (camera == null) { Mod.Warn("baseddiag pick: no player camera."); return; }
        if (player == null) { Mod.Warn("baseddiag pick: no local player."); return; }
        var selector = Selector();
        if (selector == null) { Mod.Warn("baseddiag pick: the game's object selector is not loaded."); return; }
        var flat = target - camera.transform.position;
        flat.y = 0f;
        player.transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg, 0f);
        float down = Mathf.Atan2(camera.transform.position.y - target.y, flat.magnitude) * Mathf.Rad2Deg;
        camera.transform.localRotation = Quaternion.Euler(down, 0f, 0f);
        var hovered = selector.GetHoveredObject();
        var text = new StringBuilder("baseddiag pick: from " + V(camera.transform.position) + " at " + V(target) + ", selector hovers ");
        text.Append(hovered == null ? "nothing" : "'" + hovered.ItemInstance?.Definition?.ID + "' at " + V(hovered.transform.position));
        // The selector's own cast (PlayerCamera.LookRaycast: 5 m, radius 0.1, no triggers), to name what it hit.
        if (camera.LookRaycast(5f, out var hit, selector.DetectionMask, false, .1f))
            text.Append(", first hit " + Path(hit.collider.transform) + " (layer " + hit.collider.gameObject.layer + ") at " + F(hit.distance) + " m");
        else
            text.Append(", first hit none");
        Mod.Log(text.Append('.').ToString());
    }
}
#endif
