#if DEBUG
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Based.Content;
using Based.Content.Ming;
using UnityEngine;

namespace Based.Runtime;

/// <summary>
/// Dev tools for employees whose work spans both floors of a site: a position tracker that reports warps, stalls and
/// floor changes, a route between two placed objects picked by item and floor, and a list of what each object holds.
/// </summary>
internal static class StaffTrack
{
    // Above this height an employee counts as upstairs, below the lower one as downstairs; between the two they are on
    // the stairs and keep the floor they came from.
    private const float UpperFloor = 2.6f, LowerFloor = 1.0f, FloorSplit = 2.0f;
    private const float SampleSeconds = .25f, WarpMetres = 2.5f, StallSeconds = 4f, StallMetres = .3f, FallMetres = .6f;

    private static bool _running, _stop;

    private sealed class Trace
    {
        internal string Name = "";
        internal Vector3 Last, Anchor;
        internal float AnchorTime, MinY = float.MaxValue, MaxY = float.MinValue, LongestStall;
        internal bool Up, Stalled, Known, Under;
        internal int Samples, UpSamples, Changes, Warps, Stalls, Falls, UnderSamples;
        internal Vector3 LongestStallAt;
    }

    internal static Il2CppScheduleOne.Property.Property Property(SiteDefinition site) =>
        Il2CppScheduleOne.DevUtilities.Singleton<Il2CppScheduleOne.Property.PropertyManager>.Instance.GetProperty(site.Base.PropertyId)
        ?? throw new InvalidOperationException("The property is not loaded.");

    private static string Floor(Vector3 p) => p.y > FloorSplit ? "up" : "down";

    /// <summary>
    /// Where a save load stands: the load status, the queued load requests (path and loader type) and whether the
    /// local player finished loading. The vanilla load waits for both the queue and the player.
    /// </summary>
    internal static void LoadState()
    {
        var manager = Il2CppScheduleOne.DevUtilities.Singleton<Il2CppScheduleOne.Persistence.LoadManager>.Instance;
        if (manager == null)
        {
            Mod.Log("baseddiag loadstate: no LoadManager.");
            return;
        }
        var requests = manager.loadRequests;
        int count = requests?.Count ?? -1;
        var local = Il2CppScheduleOne.PlayerScripts.Player.Local;
        Mod.Log("baseddiag loadstate: status " + manager.LoadStatus + ", loading " + manager.IsLoading + ", game loaded " + manager.IsGameLoaded
            + ", requests " + count + ", local player " + (local == null ? "none" : "loaded " + local.PlayerLoaded) + ".");
        for (int i = 0; i < Math.Min(count, 10); i++)
        {
            var request = requests![i];
            Mod.Log("baseddiag loadstate: request " + i + " " + (request?.Loader?.GetIl2CppType().FullName ?? "no loader") + " at " + (request?.Path ?? "no path") + ".");
        }
    }

    private static string At(Vector3 p) => "(" + p.x.ToString("0.00", CultureInfo.InvariantCulture) + ", " + p.y.ToString("0.00", CultureInfo.InvariantCulture) + ", " + p.z.ToString("0.00", CultureInfo.InvariantCulture) + ")";

    /// <summary>"track [seconds]" starts the tracker, "track stop" ends it early and prints the summary.</summary>
    internal static void Track(SiteDefinition site, IReadOnlyList<string> args)
    {
        if (args.Count > 1 && args[1] == "stop")
        {
            if (!_running) { Mod.Warn("baseddiag track: no tracker is running"); return; }
            _stop = true;
            Mod.Log("baseddiag track: stop requested");
            return;
        }
        if (_running) { Mod.Warn("baseddiag track: a tracker is already running; 'baseddiag track stop' ends it"); return; }
        float seconds = args.Count > 1 ? float.Parse(args[1], CultureInfo.InvariantCulture) : 300f;
        MelonLoader.MelonCoroutines.Start(Run(Property(site), seconds));
    }

    private static System.Collections.IEnumerator Run(Il2CppScheduleOne.Property.Property property, float seconds)
    {
        _running = true;
        _stop = false;
        var traces = new Dictionary<IntPtr, Trace>();
        float end = Time.time + seconds;
        Mod.Log($"baseddiag track: started for {seconds:0} s on {property.PropertyCode}, {property.Employees.Count} employees");
        while (Time.time < end && !_stop)
        {
            Sample(property, traces);
            yield return new WaitForSeconds(SampleSeconds);
        }
        foreach (var t in traces.Values)
        {
            Mod.Log($"baseddiag track: summary {t.Name}: {t.Samples} samples, upstairs {(t.Samples == 0 ? 0 : 100 * t.UpSamples / t.Samples)}%, " +
                    $"floor changes {t.Changes}, warps {t.Warps}, falls {t.Falls}, under the wing floor {t.UnderSamples}, stalls {t.Stalls}, longest stall {t.LongestStall:0.0} s{(t.Stalls > 0 ? " at " + At(t.LongestStallAt) : "")}, " +
                    $"y {t.MinY:0.00} to {t.MaxY:0.00}");
        }
        if (traces.Count == 0) Mod.Warn("baseddiag track: no employee was sampled");
        Mod.Log("baseddiag track: done");
        _running = false;
    }

    private static void Sample(Il2CppScheduleOne.Property.Property property, Dictionary<IntPtr, Trace> traces)
    {
        try
        {
            float now = Time.time;
            bool sweatshop = string.Equals(property.PropertyCode, "sweatshop", StringComparison.OrdinalIgnoreCase);
            foreach (var employee in property.Employees)
            {
                if (employee == null) continue;
                var p = employee.transform.position;
                if (!traces.TryGetValue(employee.Pointer, out var t))
                {
                    // The interop wrapper reports the list's element type, so the role comes from a cast.
                    string role = employee.TryCast<Il2CppScheduleOne.Employees.Botanist>() != null ? "Botanist"
                        : employee.TryCast<Il2CppScheduleOne.Employees.Packager>() != null ? "Packager"
                        : employee.TryCast<Il2CppScheduleOne.Employees.Chemist>() != null ? "Chemist" : "Employee";
                    t = new Trace { Name = employee.FirstName + " (" + role + ")" };
                    traces[employee.Pointer] = t;
                }
                if (!t.Known)
                {
                    t.Known = true;
                    t.Last = t.Anchor = p;
                    t.AnchorTime = now;
                    t.Up = p.y > FloorSplit;
                    Mod.Log($"baseddiag track: {t.Name} starts {Floor(p)} at {At(p)}");
                }
                t.Samples++;
                t.MinY = Mathf.Min(t.MinY, p.y);
                t.MaxY = Mathf.Max(t.MaxY, p.y);
                float step = Vector3.Distance(p, t.Last);
                // Stairs lower a walker by about a quarter metre per sample; more than FallMetres is a fall.
                float drop = t.Last.y - p.y;
                if (drop > FallMetres)
                {
                    t.Falls++;
                    var agent = employee.GetComponentInChildren<UnityEngine.AI.NavMeshAgent>();
                    Mod.Warn($"baseddiag track: {t.Name} fell {drop:0.0} m in {SampleSeconds} s from {At(t.Last)} to {At(p)}, " +
                             $"on navmesh {(agent != null && agent.isOnNavMesh)}, agent enabled {(agent != null && agent.enabled)}");
                }
                if (step > WarpMetres)
                {
                    t.Warps++;
                    Mod.Warn($"baseddiag track: {t.Name} jumped {step:0.0} m in {SampleSeconds} s from {At(t.Last)} to {At(p)}");
                }
                if (!t.Up && p.y > UpperFloor) { t.Up = true; t.Changes++; Mod.Log($"baseddiag track: {t.Name} went up, now at {At(p)}"); }
                else if (t.Up && p.y < LowerFloor) { t.Up = false; t.Changes++; Mod.Log($"baseddiag track: {t.Name} went down, now at {At(p)}"); }
                if (t.Up) t.UpSamples++;
                // The Sweatshop's wing floor is a thin slab over lower ground; an employee inside its outline but well
                // below the slab has dropped through it.
                bool under = sweatshop && p.y < MingWorld.FloorY - FallMetres
                    && MingWorld.Wing.Contains(new Vector3(p.x, MingWorld.Wing.center.y, p.z));
                if (under)
                {
                    t.UnderSamples++;
                    if (!t.Under)
                    {
                        var agent = employee.GetComponentInChildren<UnityEngine.AI.NavMeshAgent>();
                        Mod.Warn($"baseddiag track: {t.Name} is under the wing floor at {At(p)}, " +
                                 $"on navmesh {(agent != null && agent.isOnNavMesh)}, agent enabled {(agent != null && agent.enabled)}");
                    }
                }
                else if (t.Under) Mod.Log($"baseddiag track: {t.Name} is back above the wing floor at {At(p)}");
                t.Under = under;
                var movement = employee.Movement;
                bool moving = movement != null && movement.IsMoving;
                if (!moving || Vector3.Distance(p, t.Anchor) > StallMetres)
                {
                    if (t.Stalled)
                    {
                        float held = now - t.AnchorTime;
                        Mod.Log($"baseddiag track: {t.Name} moves again after {held:0.0} s at {At(t.Anchor)}");
                    }
                    t.Stalled = false;
                    t.Anchor = p;
                    t.AnchorTime = now;
                }
                else
                {
                    float held = now - t.AnchorTime;
                    if (held > t.LongestStall && held >= StallSeconds) { t.LongestStall = held; t.LongestStallAt = p; }
                    if (!t.Stalled && held >= StallSeconds)
                    {
                        t.Stalled = true;
                        t.Stalls++;
                        var agent = employee.GetComponentInChildren<UnityEngine.AI.NavMeshAgent>();
                        string path = agent == null ? "no agent" :
                            $"on navmesh {agent.isOnNavMesh}, path {agent.hasPath} ({agent.pathStatus}), {agent.remainingDistance:0.0} m left to {At(agent.destination)}";
                        Mod.Warn($"baseddiag track: {t.Name} stalls {held:0.0} s at {At(p)} while moving, {path}");
                    }
                }
                t.Last = p;
            }
        }
        catch (Exception error) { Mod.Warn("baseddiag track: sample failed: " + error); }
    }

    private static List<Il2CppScheduleOne.EntityFramework.BuildableItem> Items(Il2CppScheduleOne.Property.Property property, string id, string floor) =>
        property.BuildableItems.ToArray()
            .Where(i => i?.ItemInstance?.ID != null && i.ItemInstance.ID == id && Floor(i.transform.position) == floor)
            .ToList();

    private static bool Holds(Il2CppScheduleOne.EntityFramework.BuildableItem item, string id)
    {
        var storage = item.TryCast<Il2CppScheduleOne.ObjectScripts.PlaceableStorageEntity>();
        if (storage == null) return false;
        foreach (var slot in storage.StorageEntity.ItemSlots)
            if (slot?.ItemInstance?.ID == id) return true;
        return false;
    }

    /// <summary>
    /// "route &lt;sourceId&gt; &lt;up|down&gt; &lt;destinationId&gt; &lt;up|down&gt; [filterId]": every handler at the site gets one route
    /// between the two objects, the clipboard setup a player makes. With a filter item the route moves only that item,
    /// and of several matching sources the one holding it is used.
    /// </summary>
    internal static void Route(SiteDefinition site, IReadOnlyList<string> args)
    {
        if (args.Count == 2 && args[1] == "unfilter")
        {
            Unfilter(Property(site));
            return;
        }
        if (args.Count < 5) throw new ArgumentException("route <sourceId> <up|down> <destinationId> <up|down> [filterId] | route unfilter");
        var property = Property(site);
        string? filterId = args.Count > 5 ? args[5] : null;
        var sources = Items(property, args[1], args[2]);
        var destinations = Items(property, args[3], args[4]);
        if (sources.Count == 0) { Mod.Warn($"baseddiag route: no {args[1]} {args[2]}stairs on {property.PropertyCode}"); return; }
        if (destinations.Count == 0) { Mod.Warn($"baseddiag route: no {args[3]} {args[4]}stairs on {property.PropertyCode}"); return; }
        var source = (filterId != null ? sources.FirstOrDefault(s => Holds(s, filterId)) : null) ?? sources[0];
        var destination = destinations[0];
        Il2CppScheduleOne.ItemFramework.ItemDefinition? filter = null;
        if (filterId != null)
        {
            filter = Il2CppScheduleOne.Registry.GetItem(filterId);
            if (filter == null) { Mod.Warn($"baseddiag route: no item '{filterId}'"); return; }
        }
        int set = 0;
        foreach (var employee in property.Employees)
        {
            var packager = employee?.TryCast<Il2CppScheduleOne.Employees.Packager>();
            var config = packager?.Configuration?.TryCast<Il2CppScheduleOne.Management.PackagerConfiguration>();
            if (config == null) continue;
            var route = new Il2CppScheduleOne.Management.AdvancedTransitRoute(
                new Il2CppScheduleOne.Management.ITransitEntity(source.Pointer),
                new Il2CppScheduleOne.Management.ITransitEntity(destination.Pointer));
            if (filter != null)
            {
                route.Filter.SetMode(Il2CppScheduleOne.Management.ManagementItemFilter.EMode.Whitelist);
                route.Filter.AddItem(filter);
            }
            config.Routes.AddItem(route);
            set++;
            Mod.Log($"baseddiag route: {packager!.FirstName} gets a route from {args[1]} {Floor(source.transform.position)} at {At(source.transform.position)} " +
                    $"to {args[3]} {Floor(destination.transform.position)} at {At(destination.transform.position)}, filter {filterId ?? "none"}");
        }
        if (set == 0) Mod.Warn("baseddiag route: no handler works at " + property.PropertyCode);
    }

    /// <summary>
    /// Empties the item filter of every handler route at the site (empty blacklist = every item). Vanilla 0.4.7f12 saves
    /// a route filter as item IDs and loads it back as GUIDs (RouteListField.Load), which throws inside the load's
    /// completion callback and leaves the game on "Loading data...", so a check that saves and reloads clears it first.
    /// </summary>
    private static void Unfilter(Il2CppScheduleOne.Property.Property property)
    {
        int cleared = 0;
        foreach (var employee in property.Employees)
        {
            var packager = employee?.TryCast<Il2CppScheduleOne.Employees.Packager>();
            var routes = packager?.Configuration?.TryCast<Il2CppScheduleOne.Management.PackagerConfiguration>()?.Routes?.Routes;
            if (routes == null) continue;
            foreach (var route in routes)
            {
                var items = route.Filter.Items;
                if (items.Count == 0) continue;
                var copy = new List<Il2CppScheduleOne.ItemFramework.ItemDefinition>();
                foreach (var item in items) copy.Add(item);
                foreach (var item in copy) route.Filter.RemoveItem(item);
                route.Filter.SetMode(Il2CppScheduleOne.Management.ManagementItemFilter.EMode.Blacklist);
                cleared++;
                Mod.Log($"baseddiag route: {packager!.FirstName}'s route lost its filter ({string.Join(", ", copy.Select(i => i.ID))}).");
            }
        }
        Mod.Log($"baseddiag route: unfiltered {cleared} route(s) on {property.PropertyCode}.");
    }

    private static string Slot(Il2CppScheduleOne.ItemFramework.ItemSlot? slot) =>
        slot?.ItemInstance == null ? "none" : slot.ItemInstance.ID + " x" + slot.Quantity;

    /// <summary>"contents [idPart]": what each placed object holds, with its floor; pots also report soil, water and plant.</summary>
    internal static void Contents(SiteDefinition site, IReadOnlyList<string> args)
    {
        var property = Property(site);
        string? part = args.Count > 1 ? args[1] : null;
        int listed = 0;
        foreach (var item in property.BuildableItems)
        {
            string? id = item?.ItemInstance?.ID;
            if (id == null || part != null && !id.Contains(part)) continue;
            listed++;
            var p = item!.transform.position;
            string what;
            var pot = item.TryCast<Il2CppScheduleOne.ObjectScripts.Pot>();
            var station = item.TryCast<Il2CppScheduleOne.ObjectScripts.PackagingStation>();
            var storage = item.TryCast<Il2CppScheduleOne.ObjectScripts.PlaceableStorageEntity>();
            if (pot != null)
                what = $"soil {pot.NormalizedSoilAmount.ToString("0.00", CultureInfo.InvariantCulture)}, moisture {pot.NormalizedMoistureAmount.ToString("0.00", CultureInfo.InvariantCulture)}, plant {(pot.Plant != null ? pot.Plant.name : "none")}";
            else if (station != null)
                what = $"packaging {Slot(station.PackagingSlot)}, product {Slot(station.ProductSlot)}";
            else if (storage != null)
            {
                var filled = new List<string>();
                foreach (var slot in storage.StorageEntity.ItemSlots)
                    if (slot?.ItemInstance != null) filled.Add(Slot(slot));
                what = filled.Count == 0 ? "empty" : string.Join(", ", filled);
            }
            else what = "no slots read";
            Mod.Log($"baseddiag contents: {id} at {At(p)} {Floor(p)}: {what}");
        }
        if (listed == 0) Mod.Warn($"baseddiag contents: no placed object{(part != null ? " matching '" + part + "'" : "")} on {property.PropertyCode}");
    }
}
#endif
