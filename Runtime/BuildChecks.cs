#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using Based.Core;
using UnityEngine;

namespace Based.Runtime;

// Dev checks of a finished build's geometry, for the stage scripts: where a property's grids end against walls, and
// which faces of two parts lie in one plane and flicker.
internal static class BuildChecks
{
    private const float Tile = .5f;

    // Every outer edge of every grid of a property: a wall or railing must stand on the grid line (its face from 2 cm
    // inside the tile to 1 cm outside), and an edge without one must be where the floor ends or another grid goes on.
    // Prints one line per edge that fails, with its offset, and a PASS or FAIL line per grid.
    internal static void GridEdges(string code)
    {
        Il2CppScheduleOne.Property.Property? property = null;
        foreach (var p in Il2CppScheduleOne.Property.Property.Properties)
            if (p != null && string.Equals(p.PropertyCode, code, StringComparison.OrdinalIgnoreCase)) property = p;
        if (property == null) { Mod.Warn("baseddiag gridedges: no property has the code '" + code + "'."); return; }
        // The game's walls are often one-sided mesh colliders; the probe looks at them from inside, against their faces.
        bool backfaces = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        try { GridEdgesOf(property, code); }
        finally { Physics.queriesHitBackfaces = backfaces; }
    }

    private static void GridEdgesOf(Il2CppScheduleOne.Property.Property property, string code)
    {
        const float inside = .6f, reach = .85f, wallHeight = .3f;
        int failedGrids = 0;
        for (int gi = 0; gi < property.Grids.Count; gi++)
        {
            var grid = property.Grids[gi];
            var t = grid.transform;
            var set = new HashSet<(int, int)>();
            foreach (var tile in grid.Tiles) if (tile != null) set.Add((tile.x, tile.y));
            if (set.Count == 0) { Mod.Log($"baseddiag gridedges grid {gi} '{grid.name}': no tiles."); continue; }
            var extent = new Bounds(t.TransformPoint(new Vector3(set.First().Item1 * Tile, 0, set.First().Item2 * Tile)), Vector3.zero);
            foreach (var (x, y) in set)
            {
                extent.Encapsulate(t.TransformPoint(new Vector3((x - .5f) * Tile, 0, (y - .5f) * Tile)));
                extent.Encapsulate(t.TransformPoint(new Vector3((x + .5f) * Tile, 0, (y + .5f) * Tile)));
            }
            Mod.Log($"baseddiag gridedges grid {gi} '{grid.name}': {set.Count} tiles, origin {F(t.position)}, yaw {t.eulerAngles.y:0}, " +
                    $"tiles x {extent.min.x:0.000}..{extent.max.x:0.000} z {extent.min.z:0.000}..{extent.max.z:0.000} y {extent.center.y:0.000}");
            int edges = 0, failed = 0, walls = 0, shared = 0, open = 0;
            foreach (var (x, y) in set)
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    if (set.Contains((x + dx, y + dy))) continue;
                    edges++;
                    var mid = t.TransformPoint(new Vector3((x + dx * .5f) * Tile, 0, (y + dy * .5f) * Tile));
                    var outward = t.TransformDirection(new Vector3(dx, 0, dy)).normalized;
                    string where = $"tile {x},{y} edge at {F(mid)} out {F(outward)}";
                    if (CoveredByOtherGrid(property, gi, mid + outward * (Tile / 2f))) { shared++; continue; }
                    var from = mid - outward * inside + Vector3.up * wallHeight;
                    // A ray that starts inside a collider does not see it, so a wall reaching deeper than the probe's
                    // start would pass as an open edge.
                    if (Physics.CheckSphere(from, .005f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        failed++;
                        var inner = Physics.OverlapSphere(from, .005f, ~0, QueryTriggerInteraction.Ignore);
                        Mod.Log($"baseddiag gridedges grid {gi} {where}: wall '{(inner.Length > 0 ? Path(inner[0].transform, 6) : "?")}' reaches more than {inside * 100f:0} cm into the tile");
                        continue;
                    }
                    if (Physics.Raycast(from, outward, out var hit, reach, ~0, QueryTriggerInteraction.Ignore))
                    {
                        walls++;
                        float off = hit.distance - inside;
                        if (off > .01f || off < -.02f)
                        {
                            failed++;
                            Mod.Log($"baseddiag gridedges grid {gi} {where}: wall '{Path(hit.collider.transform, 6)}' off {off * 100f:0.0} cm");
                        }
                        continue;
                    }
                    open++;
                    var probe = mid + outward * .05f + Vector3.up * .5f;
                    if (Physics.Raycast(probe, Vector3.down, out var floor, .7f, ~0, QueryTriggerInteraction.Ignore) && Mathf.Abs(floor.point.y - mid.y) < .08f)
                    {
                        failed++;
                        Mod.Log($"baseddiag gridedges grid {gi} {where}: floor '{Path(floor.collider.transform, 6)}' goes on past the line");
                    }
                }
            if (failed > 0) failedGrids++;
            Mod.Log($"baseddiag gridedges grid {gi} '{grid.name}' {(failed == 0 ? "PASS" : "FAIL")}: {edges} outer edges, {walls} at a wall, {shared} shared, {open} open, {failed} off the line");
        }
        Mod.Log($"baseddiag gridedges {code} done: {property.Grids.Count} grids, {failedGrids} failed");
    }

    private static bool CoveredByOtherGrid(Il2CppScheduleOne.Property.Property p, int own, Vector3 point)
    {
        for (int i = 0; i < p.Grids.Count; i++)
        {
            if (i == own) continue;
            var local = p.Grids[i].transform.InverseTransformPoint(point);
            if (Mathf.Abs(local.y) > .1f) continue;
            int x = Mathf.RoundToInt(local.x / Tile), y = Mathf.RoundToInt(local.z / Tile);
            if (p.Grids[i].GetTile(new Il2CppScheduleOne.Tiles.Coordinate(x, y)) != null) return true;
        }
        return false;
    }

    private readonly struct Face
    {
        public readonly int Owner;
        public readonly Vector3 Normal;
        public readonly float Distance;
        public readonly Vector3[] Points;
        public Face(int owner, Vector3 normal, float distance, Vector3[] points) { Owner = owner; Normal = normal; Distance = distance; Points = points; }
    }

    // Faces that flicker: two renderers whose faces look the same way, lie in one plane within 2 mm and overlap by more
    // than 4 cm². At least one of the two must have the filter in its path. Readable meshes give their triangles, others
    // the six faces of their mesh bounds. A LOD group counts with its first level only, and two levels of one group or
    // two faces of one renderer are never a pair. Prints one line per pair, at most 60, and a summary.
    internal static void ZFight(string filter, Vector3 centre, float radius)
    {
        const float planeTolerance = .002f, minArea = .0004f;
        var renderers = new List<Renderer>();
        var group = new List<int>();
        var lodGroups = new Dictionary<int, int>();
        var skipped = new HashSet<int>();
        foreach (var lod in UnityEngine.Object.FindObjectsOfType<LODGroup>())
        {
            var levels = lod.GetLODs();
            for (int l = 0; l < levels.Length; l++)
                foreach (var r in levels[l].renderers)
                {
                    if (r == null) continue;
                    lodGroups[r.GetInstanceID()] = lod.GetInstanceID();
                    if (l > 0) skipped.Add(r.GetInstanceID());
                }
        }
        foreach (var r in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy || skipped.Contains(r.GetInstanceID())) continue;
            var b = r.bounds;
            if (b.SqrDistance(centre) > radius * radius) continue;
            renderers.Add(r);
            group.Add(lodGroups.TryGetValue(r.GetInstanceID(), out var g) ? g : r.GetInstanceID());
        }
        var paths = renderers.Select(r => Path(r.transform, 8)).ToList();
        var faces = new List<Face>();
        var isBox = new bool[renderers.Count];
        int readable = 0, boxes = 0;
        for (int i = 0; i < renderers.Count; i++)
        {
            var filterComponent = renderers[i].GetComponent<MeshFilter>();
            var mesh = filterComponent != null ? filterComponent.sharedMesh : null;
            if (mesh == null) continue;
            var m = renderers[i].transform.localToWorldMatrix;
            if (mesh.isReadable)
            {
                readable++;
                var v = mesh.vertices;
                var tris = mesh.triangles;
                for (int k = 0; k + 2 < tris.Length; k += 3)
                    AddFace(faces, i, m.MultiplyPoint3x4(v[tris[k]]), m.MultiplyPoint3x4(v[tris[k + 1]]), m.MultiplyPoint3x4(v[tris[k + 2]]));
            }
            else
            {
                boxes++;
                isBox[i] = true;
                var lb = mesh.bounds;
                Vector3 P(float x, float y, float z) => m.MultiplyPoint3x4(new Vector3(x, y, z));
                Vector3 a = lb.min, c = lb.max;
                // Each face wound so its normal points out of the box.
                AddQuad(faces, i, P(a.x, a.y, a.z), P(a.x, a.y, c.z), P(a.x, c.y, c.z), P(a.x, c.y, a.z));
                AddQuad(faces, i, P(c.x, a.y, a.z), P(c.x, c.y, a.z), P(c.x, c.y, c.z), P(c.x, a.y, c.z));
                AddQuad(faces, i, P(a.x, a.y, a.z), P(c.x, a.y, a.z), P(c.x, a.y, c.z), P(a.x, a.y, c.z));
                AddQuad(faces, i, P(a.x, c.y, a.z), P(a.x, c.y, c.z), P(c.x, c.y, c.z), P(c.x, c.y, a.z));
                AddQuad(faces, i, P(a.x, a.y, a.z), P(a.x, c.y, a.z), P(c.x, c.y, a.z), P(c.x, a.y, a.z));
                AddQuad(faces, i, P(a.x, a.y, c.z), P(c.x, a.y, c.z), P(c.x, c.y, c.z), P(a.x, c.y, c.z));
            }
        }
        // Buckets by direction and plane distance; a neighbour bucket catches a plane that straddles a boundary.
        var buckets = new Dictionary<(int, int, int, int), List<int>>();
        (int, int, int, int) Key(Face f, int dd) => (Mathf.RoundToInt(f.Normal.x * 20f), Mathf.RoundToInt(f.Normal.y * 20f), Mathf.RoundToInt(f.Normal.z * 20f),
            Mathf.FloorToInt(f.Distance / .01f) + dd);
        for (int i = 0; i < faces.Count; i++)
        {
            var key = Key(faces[i], 0);
            if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new List<int>();
            list.Add(i);
        }
        var pairs = new Dictionary<(int, int), (float Area, Vector3 At, Vector3 Normal)>();
        foreach (var list in buckets)
            foreach (int dd in new[] { 0, 1 })
            {
                if (!buckets.TryGetValue((list.Key.Item1, list.Key.Item2, list.Key.Item3, list.Key.Item4 + dd), out var other)) continue;
                foreach (int ia in list.Value)
                    foreach (int ib in other)
                    {
                        if (dd == 0 && ib <= ia) continue;
                        var fa = faces[ia];
                        var fb = faces[ib];
                        if (fa.Owner == fb.Owner || group[fa.Owner] == group[fb.Owner]) continue;
                        if (!paths[fa.Owner].Contains(filter) && !paths[fb.Owner].Contains(filter)) continue;
                        if (Vector3.Dot(fa.Normal, fb.Normal) < .999f || Mathf.Abs(fa.Distance - fb.Distance) > planeTolerance) continue;
                        float area = Overlap(fa, fb, out var at);
                        if (area < minArea) continue;
                        var key = fa.Owner < fb.Owner ? (fa.Owner, fb.Owner) : (fb.Owner, fa.Owner);
                        pairs.TryGetValue(key, out var sum);
                        pairs[key] = (sum.Area + area, sum.Area > 0 ? sum.At : at, fa.Normal);
                    }
            }
        int shown = 0;
        foreach (var pair in pairs.OrderByDescending(p => p.Value.Area))
        {
            if (shown++ >= 60) break;
            // [box] marks a side whose mesh could not be read and stands in as its bounds; such a pair may be no real overlap.
            string Kind(int owner) => isBox[owner] ? "[box]" : "[mesh]";
            Mod.Log($"baseddiag zfight: FAIL {paths[pair.Key.Item1]} {Kind(pair.Key.Item1)} | {paths[pair.Key.Item2]} {Kind(pair.Key.Item2)}: {pair.Value.Area * 1e4f:0} cm² in one plane, facing {F(pair.Value.Normal)}, at {F(pair.Value.At)}");
        }
        Mod.Log($"baseddiag zfight done: {pairs.Count} pairs over {renderers.Count} renderers ({readable} meshes, {boxes} boxes), {faces.Count} faces, filter '{filter}', within {radius:0} m of {F(centre)}");
    }

    private static void AddQuad(List<Face> faces, int owner, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        AddFace(faces, owner, a, b, c);
        AddFace(faces, owner, a, c, d);
    }

    private static void AddFace(List<Face> faces, int owner, Vector3 a, Vector3 b, Vector3 c)
    {
        var n = Vector3.Cross(b - a, c - a);
        float doubled = n.magnitude;
        if (doubled < 1e-6f) return;
        n /= doubled;
        faces.Add(new Face(owner, n, Vector3.Dot(n, a), new[] { a, b, c }));
    }

    // Area shared by two triangles in one plane: the first clipped by the second in plane coordinates.
    private static float Overlap(Face a, Face b, out Vector3 at)
    {
        var n = a.Normal;
        var u = Vector3.Cross(n, Mathf.Abs(n.y) < .9f ? Vector3.up : Vector3.right).normalized;
        var v = Vector3.Cross(n, u);
        List<Vector2> To2(Vector3[] p) => p.Select(q => new Vector2(Vector3.Dot(q, u), Vector3.Dot(q, v))).ToList();
        var subject = To2(a.Points);
        var clip = To2(b.Points);
        if (Signed(clip) < 0) clip.Reverse();
        for (int i = 0; i < clip.Count && subject.Count > 0; i++)
        {
            Vector2 e0 = clip[i], e1 = clip[(i + 1) % clip.Count];
            var input = subject;
            subject = new List<Vector2>();
            for (int k = 0; k < input.Count; k++)
            {
                Vector2 p = input[k], q = input[(k + 1) % input.Count];
                bool pin = Cross(e1 - e0, p - e0) >= 0, qin = Cross(e1 - e0, q - e0) >= 0;
                if (pin) subject.Add(p);
                if (pin != qin)
                {
                    float t = Cross(e1 - e0, p - e0) / (Cross(e1 - e0, p - e0) - Cross(e1 - e0, q - e0));
                    subject.Add(p + (q - p) * t);
                }
            }
        }
        at = a.Points[0];
        if (subject.Count < 3) return 0f;
        var c = subject.Aggregate(Vector2.zero, (s, p) => s + p) / subject.Count;
        at = u * c.x + v * c.y + n * a.Distance;
        return Mathf.Abs(Signed(subject));
    }

    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    private static float Signed(List<Vector2> p)
    {
        float s = 0;
        for (int i = 0; i < p.Count; i++) s += Cross(p[i], p[(i + 1) % p.Count]);
        return s * .5f;
    }

    private static string F(Vector3 v) => $"({v.x:0.000},{v.y:0.000},{v.z:0.000})";

    private static string Path(Transform transform, int depth)
    {
        string path = transform.name;
        var parent = transform.parent;
        for (int i = 1; i < depth && parent != null; i++, parent = parent.parent) path = parent.name + "/" + path;
        return path;
    }
}
#endif
