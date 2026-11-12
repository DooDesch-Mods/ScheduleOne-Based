#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using Based.Core;
using Based.UI;
using Based.Content;
using S1API.Console;
using UnityEngine;

namespace Based.Runtime;

public sealed class BasedDebugCommand : BaseConsoleCommand
{
    public override string CommandWord => "baseddiag";
    public override string CommandDescription => "Based construction state and isolated runtime diagnostics.";
    public override string ExampleUsage => "baseddiag [site=<id>] status | sites | plan | ray [office|street] | creator | dock | dockroute [build|route|routes|work|deliver] | room | workers | hammer [seconds|set <time>] | network | packdoorway | phase <id> [upgrade] | hold <slot> | stayers | walkers | staff | advance | lights | lightset <what> <value> | journal [off] | walk <x> <z> [<x> <z> ...]";

    // The site a command works on; "site=<id>" anywhere in the arguments picks it, without one it is the first site.
    private static SiteDefinition _site = null!;

    public override void ExecuteCommand(List<string> args)
    {
        args = new List<string>(args);
        int token = args.FindIndex(a => a.StartsWith("site=", StringComparison.OrdinalIgnoreCase));
        string? siteKey = null;
        if (token >= 0)
        {
            siteKey = args[token].Substring("site=".Length);
            args.RemoveAt(token);
        }
        string action = args.Count == 0 ? "status" : args[0];
        try
        {
            _site = Mod.FindSite(siteKey);
            switch (action)
            {
                case "sites":
                    Sites();
                    break;
                case "door":
                    Mod.Log("baseddiag: sunroom door " + Content.Bungalow.BungalowSunroomDoor.Debug(args.Count > 1 ? args[1] : "state"));
                    break;
                case "status":
                    Mod.Log("baseddiag: " + BuildPlan.State(_site));
                    break;
                case "plan":
                    Mod.DebugPlan(_site);
                    Mod.Log("baseddiag: plan opened through the vendor conversation.");
                    break;
                case "ray":
                    PlanCopies.DebugTalk(args.Count > 1 ? args[1] : "");
                    break;
                case "blockers":
                    Mod.DebugBlockers(_site);
                    break;
                case "planfree":
                    Mod.DebugFreeConversation = args.Count > 1 && args[1] == "on";
                    Mod.Log("baseddiag: conversation hold under the plan " + (Mod.DebugFreeConversation ? "off" : "on") + ".");
                    break;
                case "folder":
                    Folder(args.Count > 1 ? args[1] : "state");
                    break;
                case "creator":
                    // A joining test client without saved looks starts in the character creator; finish it with the defaults.
                    var creator = Il2CppScheduleOne.DevUtilities.Singleton<Il2CppScheduleOne.CharacterCreator.CharacterCreator>.Instance;
                    if (creator == null || !creator.IsOpen) Mod.Log("baseddiag: creator not open.");
                    else { creator.Done(); Mod.Log("baseddiag: creator done with the default looks."); }
                    break;
                case "morningsound":
                    ConstructionSounds.Play();
                    break;
                case "hammer":
                    Hammer(args);
                    break;
                case "advance":
                    Mod.DebugAdvance();
                    Mod.Log("baseddiag: one sleep cycle requested; revision " + Mod.Engine.Revision + ".");
                    break;
                case "dock":
                    DockProbe.Execute(args.Skip(1).ToArray());
                    break;
                case "room":
                    RoomProbe.Execute(_site, args.Skip(1).ToArray());
                    break;
                case "dockroute":
                    if (args.Count > 3 && args[1] == "deliver") DockDeliver(int.Parse(args[2]), args[3]);
                    else if (args.Count > 1 && args[1] == "route") SetDockRoute();
                    else if (args.Count > 1 && args[1] == "routes") DockRoutes();
                    else if (args.Count > 1 && args[1] == "work") DockWork();
                    else DockRoute(args.Count > 1 && args[1] == "build");
                    break;
                case "workers":
                    Crew().Diagnostics();
                    break;
                case "network":
                    NetworkProbe.Execute();
                    break;
                case "packdoorway":
                    if (args.Count > 1 && args[1] == "one") PackOneBlocker();
                    else PackTestDoorway();
                    break;
                case "phase":
                    JumpToPhase(args.Count > 1 ? args[1] : "", args.Count > 2 ? args[2] : "");
                    break;
                case "hold":
                    Hold(args.Count > 1 ? int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 0);
                    break;
                case "stayers":
                    Stayers();
                    break;
                case "walkers":
                    Walkers();
                    break;
                case "staff":
                    Staff();
                    break;
                case "walk":
                    MelonLoader.MelonCoroutines.Start(Walk(args));
                    break;
                case "lights":
                    Lights();
                    break;
                case "lightset":
                    LightSet(args[1], float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case "journal":
                    JournalDetails(args.Count < 2 || args[1] != "off");
                    break;
                default:
                    throw new ArgumentException(ExampleUsage);
            }
        }
        catch (Exception error) { Mod.Warn("baseddiag " + action + " failed: " + error); }
    }

    // Whether an employee can reach the site's loading docks: each access point, and the spot behind a parked van
    // where the game moves the first one, on the Employee NavMesh and on a route from the employees' spawn point.
    private static void DockRoute(bool build)
    {
        if (build) CompleteDock();
        var property = Il2CppScheduleOne.DevUtilities.Singleton<Il2CppScheduleOne.Property.PropertyManager>.Instance.GetProperty(_site.Base.PropertyId);
        if (property == null) throw new InvalidOperationException("The property is not loaded.");
        var filter = new UnityEngine.AI.NavMeshQueryFilter
        {
            agentTypeID = Il2CppScheduleOne.DevUtilities.NavMeshUtility.GetNavMeshAgentID("Employee"),
            areaMask = UnityEngine.AI.NavMesh.AllAreas
        };
        Vector3 from = property.NPCSpawnPoint.position;
        float vanLength = 0f;
        var shops = Il2CppScheduleOne.UI.Shop.ShopInterface.AllShops;
        for (int i = 0; shops != null && i < shops.Count && vanLength == 0f; i++)
            if (shops[i]?.DeliveryVehicle?.Vehicle != null) vanLength = shops[i].DeliveryVehicle.Vehicle.BoundingBoxDimensions.z;
        int docks = property.LoadingDocks?.Length ?? 0;
        Mod.Log($"baseddiag: dockroute {property.PropertyCode}: {docks} docks, spawn {from}, delivery van length {vanLength:0.00}");
        for (int d = 0; d < docks; d++)
        {
            var dock = property.LoadingDocks![d];
            if (dock == null) { Mod.Warn($"baseddiag: dockroute dock {d} is null"); continue; }
            var van = dock.DynamicOccupant;
            Mod.Log($"baseddiag: dockroute dock {d} guid {dock.GUID} at {dock.transform.position}, van {(van != null ? van.name : "none")}, output slots {dock.OutputSlots?.Count ?? 0}");
            var points = dock.accessPoints;
            for (int i = 0; points != null && i < points.Length; i++)
                if (points[i] != null) Reach($"access point {i}", points[i].position);
            var spots = dock.Parking?.ParkingSpots;
            for (int s = 0; spots != null && s < spots.Count; s++)
            {
                var align = spots[s]?.AlignmentPoint;
                if (align == null) continue;
                float back = vanLength / 2f + 0.6f;
                Reach($"parking {s} behind a van facing +forward", align.position - align.forward * back);
                Reach($"parking {s} behind a van facing -forward", align.position + align.forward * back);
            }
            if (van != null) Reach("behind the parked van", van.transform.position - van.transform.forward * (van.BoundingBoxDimensions.z / 2f + 0.6f));
        }

        void Reach(string what, Vector3 at)
        {
            if (!UnityEngine.AI.NavMesh.SamplePosition(at, out var hit, 2f, filter))
            {
                Mod.Warn($"baseddiag: dockroute   {what} {at}: no Employee NavMesh within 2 m");
                return;
            }
            var path = new UnityEngine.AI.NavMeshPath();
            bool found = UnityEngine.AI.NavMesh.CalculatePath(from, hit.position, filter, path);
            var end = found && path.corners.Length > 0 ? path.corners[path.corners.Length - 1] : from;
            bool reachable = found && path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete && Vector3.Distance(end, hit.position) <= 1f
                             && Vector3.Distance(hit.position, at) <= 1f;
            string line = $"baseddiag: dockroute   {what} {at}: mesh {Vector3.Distance(hit.position, at):0.00} m away at {hit.position}, route {(found ? path.status.ToString() : "none")}, employee can get there: {reachable}";
            if (reachable) Mod.Log(line); else Mod.Warn(line);
        }
    }

    // The employee limit, each waiting place with its route from the door, and where every employee stands.
    private static void Staff()
    {
        var property = Il2CppScheduleOne.DevUtilities.Singleton<Il2CppScheduleOne.Property.PropertyManager>.Instance.GetProperty(_site.Base.PropertyId);
        if (property == null) throw new InvalidOperationException("The property is not loaded.");
        var places = property.EmployeeIdlePoints;
        Mod.Log("baseddiag: staff " + property.Employees.Count + "/" + property.EmployeeCapacity + ", places " + places.Length + ", spawn " + property.NPCSpawnPoint.position);
        var filter = new UnityEngine.AI.NavMeshQueryFilter
        {
            agentTypeID = Il2CppScheduleOne.DevUtilities.NavMeshUtility.GetNavMeshAgentID("Employee"),
            areaMask = UnityEngine.AI.NavMesh.AllAreas
        };
        // A placed item can stand on a waiting place; the game then walks the employee to the nearest NavMesh, so the
        // route goes to the closest point within 2 m and the line names how far off it lies.
        for (int i = 0; i < places.Length; i++)
        {
            var at = places[i].position;
            if (!UnityEngine.AI.NavMesh.SamplePosition(at, out var hit, 2f, filter))
            {
                Mod.Warn("baseddiag: place " + i + " at " + at + " route none, no Employee NavMesh within 2 m");
                continue;
            }
            var path = new UnityEngine.AI.NavMeshPath();
            bool found = UnityEngine.AI.NavMesh.CalculatePath(property.NPCSpawnPoint.position, hit.position, filter, path);
            string line = "baseddiag: place " + i + " at " + at + " route " + (found ? path.status.ToString() : "none")
                + ", mesh " + Vector3.Distance(hit.position, at).ToString("0.00") + " m away";
            if (found && path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete) Mod.Log(line); else Mod.Warn(line);
        }
        for (int i = 0; i < property.Employees.Count; i++)
        {
            var employee = property.Employees[i];
            var agent = employee.GetComponentInChildren<UnityEngine.AI.NavMeshAgent>(true);
            // An employee whose NavMesh piece has no way down to the street cannot reach the dock or the stairs.
            var street = new UnityEngine.AI.NavMeshPath();
            bool found = UnityEngine.AI.NavMesh.CalculatePath(agent.transform.position, property.NPCSpawnPoint.position, filter, street);
            bool reaches = found && street.status == UnityEngine.AI.NavMeshPathStatus.PathComplete;
            string line = "baseddiag: employee " + employee.name + " index " + employee.EmployeeIndex + " at " + agent.transform.position
                + ", waits at " + places[employee.EmployeeIndex].position + ", agent type " + agent.agentTypeID + ", on mesh " + agent.isOnNavMesh
                + ", route to the street " + (found ? street.status.ToString() : "none") + ", reaches the street " + reaches;
            if (reaches) Mod.Log(line); else Mod.Warn(line);
        }
    }

    // Stands in for the clicks a test cannot make: read the folder, hang it where the aim is, or take one.
    private static void Folder(string action)
    {
        switch (action)
        {
            case "give": Mod.Log("baseddiag: folder given, answer '" + ProjectFolder.Give(_site) + "'."); break;
            case "open": ProjectFolder.DebugOpen(_site); break;
            case "hang": ProjectFolder.DebugHang(); break;
            case "wall": Mod.Log("baseddiag: folder hung on the " + ProjectFolder.DebugWall(_site) + "."); break;
            case "state": Mod.Log("baseddiag: folder " + ProjectFolder.DebugState(_site)); break;
            default: throw new ArgumentException("baseddiag folder give|open|hang|wall|state");
        }
    }

    // The journal only shows a quest's details while the pointer rests on it, so this stands in for the pointer.
    private static void JournalDetails(bool show)
    {
        if (!show)
        {
            Il2CppScheduleOne.Quests.Quest.HoveredQuest = null;
            Mod.Log("baseddiag: journal details closed.");
            return;
        }
        string title = Text.T(_site.Title);
        foreach (var quest in UnityEngine.Object.FindObjectsOfType<Il2CppScheduleOne.Quests.Quest>(true))
        {
            if (quest.Title != title) continue;
            Il2CppScheduleOne.Quests.Quest.HoveredQuest = quest;
            Mod.Log("baseddiag: journal details of '" + title + "' requested, " + quest.Entries.Count + " entries.");
            return;
        }
        Mod.Warn("baseddiag: no active quest named '" + title + "'.");
    }

    // Lists the lights and ceiling fan parts around the site, for checks of light reach, shadows and wall clearance.
    private static void Lights()
    {
        var site = PlanCentre(_site) + Vector3.up * 2f;
        foreach (var light in UnityEngine.Object.FindObjectsOfType<Light>())
        {
            Vector3 at = light.transform.position;
            if (Vector3.Distance(at, site) > 15f) continue;
            Mod.Log($"baseddiag lights: {Path(light.transform)} type {light.type} at ({at.x:F2},{at.y:F2},{at.z:F2}) range {light.range:F2} intensity {light.intensity:F2} shadows {light.shadows} enabled {light.enabled} mask {light.cullingMask} angle {light.spotAngle:F1} forward {light.transform.forward} bias {light.shadowBias:F3} normalBias {light.shadowNormalBias:F3} nearPlane {light.shadowNearPlane:F2} strength {light.shadowStrength:F2}");
        }
        Mod.Log($"baseddiag lights: quality {QualitySettings.names[QualitySettings.GetQualityLevel()]} shadowDistance {QualitySettings.shadowDistance:F1} shadows {QualitySettings.shadows} pixelLights {QualitySettings.pixelLightCount}");
        foreach (var part in UnityEngine.Object.FindObjectsOfType<Renderer>())
        {
            Vector3 at = part.transform.position;
            if (Vector3.Distance(at, site) > 15f || !Path(part.transform).Contains("CeilingFan")) continue;
            Bounds bounds = part.bounds;
            Mod.Log($"baseddiag lights: {Path(part.transform)} at ({at.x:F2},{at.y:F2},{at.z:F2}) bounds min ({bounds.min.x:F2},{bounds.min.y:F2},{bounds.min.z:F2}) max ({bounds.max.x:F2},{bounds.max.y:F2},{bounds.max.z:F2})");
        }
    }

    // Changes the two ceiling lights added by the upgrade live, so one run can compare reach and shadow settings.
    private static void LightSet(string what, float value)
    {
        if (what == "quality")
        {
            QualitySettings.SetQualityLevel((int)value, true);
            Mod.Log($"baseddiag lightset: quality {QualitySettings.names[QualitySettings.GetQualityLevel()]}, shadows {QualitySettings.shadows}.");
            return;
        }
        int changed = 0;
        foreach (var light in UnityEngine.Object.FindObjectsOfType<Light>())
        {
            if (!Path(light.transform).Contains("Copies/CeilingFan")) continue;
            if (what == "range") light.range = value;
            else if (what == "angle") light.spotAngle = value;
            else if (what == "intensity") light.intensity = value;
            else if (what == "normalbias") light.shadowNormalBias = value;
            else if (what == "bias") light.shadowBias = value;
            else if (what == "near") light.shadowNearPlane = value;
            else throw new ArgumentException("baseddiag lightset range|angle|intensity|normalbias|bias|near <value>");
            changed++;
        }
        Mod.Log($"baseddiag lightset: {what} {value} on {changed} ceiling lights.");
    }

    private static string Path(Transform transform)
    {
        string path = transform.name;
        for (Transform parent = transform.parent; parent != null; parent = parent.parent)
            path = parent.name + "/" + path;
        return path;
    }

    // Selects a hotbar slot like its number key, for checks of hand-held interactions.
    private static void Hold(int slot)
    {
        var inventory = Il2CppScheduleOne.PlayerScripts.PlayerInventory.Instance;
        inventory.Equip(inventory.IndexAllSlots(slot));
        Mod.Log("baseddiag: hotbar slot " + slot + " selected, holding " + (inventory.IndexAllSlots(slot).ItemInstance?.Name ?? "nothing") + ".");
    }

    // Pushes the player's own character controller along a route at walking speed, so walls, stairs and steps
    // answer as they do for a player, and names what stops it.
    private static System.Collections.IEnumerator Walk(List<string> args)
    {
        const float speed = 3f;
        var controller = Il2CppScheduleOne.DevUtilities.PlayerSingleton<Il2CppScheduleOne.PlayerScripts.PlayerMovement>.Instance.Controller;
        for (int i = 1; i + 1 < args.Count; i += 2)
        {
            var target = new Vector3(
                float.Parse(args[i], System.Globalization.CultureInfo.InvariantCulture), 0f,
                float.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture));
            Vector3 start = controller.transform.position, flat = target - start;
            flat.y = 0f;
            float deadline = Time.time + flat.magnitude / speed + 2f;
            while (true)
            {
                Vector3 here = controller.transform.position, delta = target - here;
                delta.y = 0f;
                if (delta.magnitude < .2f)
                {
                    Mod.Log($"baseddiag walk: reached ({target.x:F2},{target.z:F2}) from ({start.x:F2},{start.z:F2}), ground y {here.y:F2}.");
                    break;
                }
                if (Time.time > deadline)
                {
                    bool hit = Physics.SphereCast(here, .25f, delta.normalized, out var blocker, 1f, ~0, QueryTriggerInteraction.Ignore);
                    string name = hit ? blocker.collider.transform.parent?.name + "/" + blocker.collider.name : "nothing within 1 m";
                    Mod.Warn($"baseddiag walk: stopped at ({here.x:F2},{here.y:F2},{here.z:F2}) on the way to ({target.x:F2},{target.z:F2}); in front: {name}.");
                    yield break;
                }
                controller.Move(delta.normalized * (speed * Time.deltaTime));
                yield return null;
            }
        }
        Mod.Log("baseddiag walk: route finished.");
    }

    // Names the NPCs whose scheduled walk stands still short of its target: the game retries those every frame.
    private static void Walkers()
    {
        int running = 0, stalled = 0;
        foreach (var walk in UnityEngine.Object.FindObjectsOfType<Il2CppScheduleOne.NPCs.Schedules.NPCSignal_WalkToLocation>(true))
        {
            if (!walk.IsActive) continue;
            running++;
            var movement = walk.npc?.Movement;
            if (movement == null || walk.Destination == null) continue;
            float left = Vector3.Distance(movement.FootPosition, walk.Destination.position);
            if (movement.IsMoving || left < walk.DestinationThreshold) continue;
            stalled++;
            Mod.Warn("baseddiag: " + walk.npc!.name + " stalls on walk to '" + walk.Destination.name + "' at " + walk.Destination.position + ", feet at " + movement.FootPosition + ", " + left.ToString("0.0") + " m left, on navmesh " + walk.npc.gameObject.GetComponent<UnityEngine.AI.NavMeshAgent>()?.isOnNavMesh + ".");
        }
        Mod.Log("baseddiag: " + running + " scheduled walks running, " + stalled + " stalled.");
    }

    // Lists every running stay-in-building event whose building offers no door an NPC may use.
    private static void Stayers()
    {
        int running = 0, stuck = 0;
        foreach (var stay in UnityEngine.Object.FindObjectsOfType<Il2CppScheduleOne.NPCs.Schedules.NPCEvent_StayInBuilding>(true))
        {
            if (!stay.IsActive) continue;
            running++;
            var building = stay.Building;
            if (building == null) continue;
            var doors = building.Doors;
            bool usable = false;
            for (int i = 0; doors != null && i < doors.Length; i++) usable |= doors[i] != null && doors[i].Usable;
            if (usable) continue;
            stuck++;
            Mod.Warn("baseddiag: " + stay.npc?.name + " stays in '" + building.BuildingName + "' with " + (doors?.Length ?? 0) + " doors, none usable.");
        }
        Mod.Log("baseddiag: " + running + " stay-in-building events running, " + stuck + " without a usable door.");
    }

    private static void Hammer(List<string> args)
    {
        if (args.Count > 2 && args[1] == "set")
        {
            Crew().HammerContactNormalizedTime = float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
            Mod.Log("baseddiag: hammer contact time " + Crew().HammerContactNormalizedTime + ".");
            return;
        }
        float seconds = args.Count > 1 ? float.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 10f;
        Crew().ProbeSwing(seconds);
        Mod.Log("baseddiag: measuring the hammer swing for " + seconds + " s.");
    }

    // Finishes the expansion and every follow-up in Based's own state, so the dock is part of the save as after a purchase.
    private static void CompleteDock()
    {
        if (!NativeBus.IsHost) throw new InvalidOperationException("Only the host can change the construction state.");
        var state = Mod.Engine.ExportState();
        var finished = new HashSet<string>(_site.FollowUps.Select(f => f.UpgradeId)) { _site.UpgradeId };
        foreach (var upgrade in state.Upgrades.Where(u => u.BaseId == _site.Base.Id && finished.Contains(u.UpgradeId)))
        {
            upgrade.Stage = UpgradeStage.Complete;
            upgrade.CompletedCycles = _site.UpgradeOf(upgrade.UpgradeId).ConstructionCycles;
        }
        state.Revision++;
        Mod.DebugClearFailure();
        Mod.Engine.RestoreState(state);
        Mod.Changed();
        Mod.Log("baseddiag: dockroute: wing and loading dock complete (" + string.Join(", ", finished) + "); revision " + state.Revision + ".");
    }

    private static Il2CppScheduleOne.Property.Property SiteProperty() =>
        Il2CppScheduleOne.DevUtilities.Singleton<Il2CppScheduleOne.Property.PropertyManager>.Instance.GetProperty(_site.Base.PropertyId)
        ?? throw new InvalidOperationException("The property is not loaded.");

    private static IEnumerable<(Il2CppScheduleOne.Employees.Packager Packager, Il2CppScheduleOne.Management.PackagerConfiguration Config)> Packagers()
    {
        var property = SiteProperty();
        foreach (var employee in property.Employees)
        {
            var packager = employee?.TryCast<Il2CppScheduleOne.Employees.Packager>();
            var config = packager?.Configuration?.TryCast<Il2CppScheduleOne.Management.PackagerConfiguration>();
            if (config != null) yield return (packager!, config);
        }
    }

    // Gives every handler at the site one route from its loading dock, the setup a player makes in the clipboard.
    private static void SetDockRoute()
    {
        var property = SiteProperty();
        if (property.LoadingDocks == null || property.LoadingDocks.Length == 0) throw new InvalidOperationException("The " + _site.PropertyName + " has no loading dock.");
        var dock = property.LoadingDocks[0];
        var shelf = FirstShelf(property);
        if (shelf == null) Mod.Warn("baseddiag: dockroute route: no storage shelf on the property; the route ends at the dock and moves nothing");
        int set = 0;
        foreach (var (packager, config) in Packagers())
        {
            var source = new Il2CppScheduleOne.Management.ITransitEntity(dock.Pointer);
            var destination = new Il2CppScheduleOne.Management.ITransitEntity(shelf != null ? shelf.Pointer : dock.Pointer);
            config.Routes.AddItem(new Il2CppScheduleOne.Management.AdvancedTransitRoute(source, destination));
            Mod.Log($"baseddiag: dockroute route: {packager.FirstName} gets a route from dock {dock.GUID} to {(shelf != null ? "shelf " + shelf.GUID + " at " + shelf.transform.position : "the dock")}");
            set++;
        }
        if (set == 0) Mod.Warn("baseddiag: dockroute route: no handler works at the " + _site.PropertyName + "; add one with 'addemployee handler " + _site.Base.PropertyId + "'");
    }

    // The storage rack with the most empty slots, not the employee bed or locker, whose storage takes only their pay.
    // A full rack takes nothing, and a handler then has no item ready on the route.
    private static Il2CppScheduleOne.ObjectScripts.PlaceableStorageEntity? FirstShelf(Il2CppScheduleOne.Property.Property property)
    {
        Il2CppScheduleOne.ObjectScripts.PlaceableStorageEntity? best = null;
        int bestFree = -1;
        foreach (var item in property.BuildableItems)
        {
            if (item?.ItemInstance?.ID == null || !item.ItemInstance.ID.Contains("storagerack")) continue;
            var shelf = item.GetComponent<Il2CppScheduleOne.ObjectScripts.PlaceableStorageEntity>();
            if (shelf == null) continue;
            int free = 0;
            foreach (var slot in shelf.StorageEntity.ItemSlots)
                if (slot != null && slot.ItemInstance == null) free++;
            if (free > bestFree) { best = shelf; bestFree = free; }
        }
        return best;
    }

    // Walks the checks a handler makes before it moves anything on a route (Packager.GetTransitRouteReady) and names the
    // first one that fails, plus what the destination shelf holds now.
    private static void DockWork()
    {
        var property = SiteProperty();
        int handlers = 0;
        foreach (var (packager, config) in Packagers())
        {
            handlers++;
            var active = packager.Behaviour?.activeBehaviour;
            Mod.Log($"baseddiag: dockroute work: {packager.FirstName} at {packager.transform.position}, active {(active != null ? active.Name : "none")}, paid {packager.PaidForToday}");
            var routes = config.Routes.Routes;
            for (int i = 0; i < routes.Count; i++)
            {
                var route = routes[i];
                if (route.Source == null || route.Destination == null) { Mod.Warn($"baseddiag: dockroute work:   route {i} misses an end"); continue; }
                var item = route.GetItemReadyToMove();
                bool source = packager.Movement.CanGetTo(route.Source, 1f), destination = packager.Movement.CanGetTo(route.Destination, 1f);
                int capacity = item != null ? packager.Inventory.GetCapacityForItem(item) : -1;
                Mod.Log($"baseddiag: dockroute work:   route {i}: item ready {(item != null ? item.ID + " x" + item.Quantity : "none")}, reach source {source}, reach destination {destination}, carry capacity {capacity}, source output slots {route.Source.OutputSlots.Count}");
                if (item != null) continue;
                // Why nothing is ready: every filled source slot with the filter and the destination's room for it.
                int filled = 0;
                foreach (var slot in route.Source.OutputSlots)
                {
                    if (slot?.ItemInstance == null) continue;
                    filled++;
                    var ai = route.TryCast<Il2CppScheduleOne.Management.AdvancedTransitRoute>();
                    bool filter = ai == null || ai.Filter.DoesItemMeetFilter(slot.ItemInstance);
                    int room = route.Destination.GetInputCapacityForItem(slot.ItemInstance, null, true);
                    int roomNoFilter = route.Destination.GetInputCapacityForItem(slot.ItemInstance, null, false);
                    Mod.Log($"baseddiag: dockroute work:     source slot {slot.ItemInstance.ID} x{slot.Quantity}: route filter {(filter ? "passes" : "BLOCKS")}, destination room {room} ({roomNoFilter} without player filters), destination input slots {route.Destination.InputSlots.Count}");
                }
                if (filled == 0) Mod.Warn($"baseddiag: dockroute work:     the source has {route.Source.OutputSlots.Count} output slots and none holds an item");
            }
        }
        if (handlers == 0) Mod.Warn("baseddiag: dockroute work: no handler works at the " + _site.PropertyName);
        // Every storage rack together, so a delivery counts whichever rack the route ends at.
        int racks = 0, used = 0, free = 0, soil = 0;
        foreach (var item in property.BuildableItems)
        {
            if (item?.ItemInstance?.ID == null || !item.ItemInstance.ID.Contains("storagerack")) continue;
            var shelf = item.GetComponent<Il2CppScheduleOne.ObjectScripts.PlaceableStorageEntity>();
            if (shelf == null) continue;
            racks++;
            foreach (var slot in shelf.StorageEntity.ItemSlots)
            {
                if (slot == null) continue;
                if (slot.ItemInstance == null) { free++; continue; }
                used++;
                if (slot.ItemInstance.ID == "soil") soil += slot.Quantity;
            }
        }
        if (racks == 0) { Mod.Warn("baseddiag: dockroute work: no storage rack on the property"); return; }
        Mod.Log($"baseddiag: dockroute work: {racks} racks hold {used} filled and {free} free slots, soil {soil}");
    }

    // Sends one item from a shop to the site's dock through the game's own delivery, arriving in a minute.
    private static void DockDeliver(int shopIndex, string itemId)
    {
        var property = SiteProperty();
        if (property.LoadingDocks == null || property.LoadingDocks.Length == 0) throw new InvalidOperationException("The " + _site.PropertyName + " has no loading dock.");
        var shop = Il2CppScheduleOne.UI.Shop.ShopInterface.AllShops[shopIndex];
        if (shop?.DeliveryVehicle?.Vehicle == null) throw new InvalidOperationException("That shop has no delivery vehicle.");
        if (Il2CppScheduleOne.Registry.GetItem(itemId) == null) throw new ArgumentException("Unknown item: " + itemId);
        var items = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.DevUtilities.StringIntPair>(
            new[] { new Il2CppScheduleOne.DevUtilities.StringIntPair(itemId, 5) });
        var delivery = new Il2CppScheduleOne.Delivery.DeliveryInstance(Guid.NewGuid().ToString(), shop.ShopName, property.PropertyCode, 0, items,
            Il2CppScheduleOne.Delivery.EDeliveryStatus.InTransit, 1);
        Il2CppScheduleOne.Delivery.DeliveryManager.Instance.SendDelivery(delivery);
        Mod.Log($"baseddiag: dockroute deliver: 5 {itemId} from '{shop.ShopName}' to {property.PropertyCode} dock 0 in 1 minute");
    }

    private static void DockRoutes()
    {
        var property = SiteProperty();
        var dock = property.LoadingDocks != null && property.LoadingDocks.Length > 0 ? property.LoadingDocks[0] : null;
        int handlers = 0;
        foreach (var (packager, config) in Packagers())
        {
            handlers++;
            var routes = config.Routes.Routes;
            Mod.Log($"baseddiag: dockroute routes: {packager.FirstName} has {routes.Count} routes; dock {(dock == null ? "missing" : dock.GUID.ToString())}");
            for (int i = 0; i < routes.Count; i++)
            {
                var route = routes[i];
                string Side(Il2CppScheduleOne.Management.ITransitEntity? e) =>
                    e == null ? "none" : e.Pointer == dock?.Pointer ? "the loading dock" : e.Name;
                Mod.Log($"baseddiag: dockroute routes:   {i}: from {Side(route.Source)} to {Side(route.Destination)}");
            }
        }
        if (handlers == 0) Mod.Warn("baseddiag: dockroute routes: no handler works at the " + _site.PropertyName);
    }

    // Moves the upgrade straight to a stage, so one run can look at every construction state.
    // Without an upgrade argument the site's own upgrade moves; "carport" names "ray.bungalow-carport" by its last part.
    private static void JumpToPhase(string phase, string upgradeArg)
    {
        if (!NativeBus.IsHost) throw new InvalidOperationException("Only the host can change the construction state.");
        var definition = upgradeArg.Length == 0 ? _site.Upgrade
            : _site.Base.Upgrades.FirstOrDefault(u => u.Id == upgradeArg || u.Id.EndsWith("-" + upgradeArg, StringComparison.Ordinal))
              ?? throw new ArgumentException("baseddiag phase: no upgrade '" + upgradeArg + "' at " + _site.Base.Id + "; known: " +
                  string.Join(", ", _site.Base.Upgrades.Select(u => u.Id)));
        var state = Mod.Engine.ExportState();
        var upgrade = state.Upgrades.First(u => u.BaseId == _site.Base.Id && u.UpgradeId == definition.Id);
        // The named stages around the construction, and each phase at the night it starts.
        var stages = new List<(string Id, UpgradeStage Stage, int Cycles)>
        {
            ("none", UpgradeStage.RightsMissing, 0),
            ("rights", UpgradeStage.RightsOwned, 0),
            ("ordered", UpgradeStage.Constructing, 0)
        };
        stages.AddRange(definition.Phases.Select(p => (p.Id, UpgradeStage.Constructing, p.AfterSleepCycles)));
        stages.Add(("complete", UpgradeStage.Complete, definition.ConstructionCycles));
        var match = stages.FirstOrDefault(s => s.Id == phase);
        if (match.Id == null) throw new ArgumentException("baseddiag phase " + string.Join("|", stages.Select(s => s.Id)));
        var (stage, cycles) = (match.Stage, match.Cycles);
        upgrade.Stage = stage;
        upgrade.CompletedCycles = cycles;
        if (stage == UpgradeStage.RightsMissing) upgrade.RightsPaid = 0;
        if (stage < UpgradeStage.Constructing) upgrade.ConstructionPaid = 0;
        state.Revision++;
        Mod.DebugClearFailure();
        Mod.Engine.RestoreState(state);
        Mod.Changed();
        Mod.Log("baseddiag: construction of " + definition.Id + " moved to " + phase + " at " + _site.Base.Id + "; revision " + state.Revision + ".");
    }

    // Clears the walls item by item, as a player does, so the hold's list can be seen shrinking.
    private static void PackOneBlocker()
    {
        foreach (var item in Mod.BlockingItems(_site))
        {
            string name = item.GetManagementName();
            if (!item.CanBePickedUp(out string reason))
            {
                Mod.Log("baseddiag: " + name + " stays, pickup refused: " + reason);
                continue;
            }
            item.PickupItem();
            Mod.Log("baseddiag: picked up " + name + ".");
            return;
        }
        Mod.Warn("baseddiag: no blocking item could be picked up.");
    }

    private static void PackTestDoorway()
    {
        if (!NativeBus.IsHost) throw new InvalidOperationException("Doorway packing needs the host.");
        foreach (var peer in Il2CppFishNet.InstanceFinder.ServerManager.Clients)
            if (!peer.Value.IsHost) throw new InvalidOperationException("Doorway packing requires an isolated host without remote players.");
        // A rack refuses the pickup while it holds a light, so a second pass may free what the first refused.
        for (bool moved = true; moved;)
        {
            moved = false;
            foreach (var item in Mod.BlockingItems(_site))
            {
                if (!item.CanBePickedUp(out _)) continue;
                string name = item.GetManagementName();
                item.PickupItem();
                if (!item.IsDestroyed) throw new InvalidOperationException("Native pickup was not confirmed for " + name);
                Mod.Log("baseddiag: native pickup returned " + name + " to the test player's inventory.");
                moved = true;
            }
        }
        // What still refuses is loaded, like a planted pot. The test save is a fresh copy, so it is dropped with its contents.
        foreach (var item in Mod.BlockingItems(_site))
        {
            item.CanBePickedUp(out string reason);
            Mod.Warn("baseddiag: destroyed " + item.GetManagementName() + " in the test save, pickup refused: " + reason);
            item.Destroy_Server();
        }
        if (Mod.BlockingItems(_site).Length > 0) throw new InvalidOperationException("The marked walls are still blocked after packing.");
        Mod.Log("baseddiag: marked walls are clear.");
    }

    private static ISiteCrew Crew() =>
        _site.Crew ?? throw new InvalidOperationException(_site.Base.Id + " has no construction crew.");

    // The middle of the site's plan drawing, on the floor.
    private static Vector3 PlanCentre(SiteDefinition site)
    {
        if (site.Plan.Count == 0) return site.QuestMarker;
        float minX = site.Plan.Min(r => r.MinX), maxX = site.Plan.Max(r => r.MaxX);
        float minZ = site.Plan.Min(r => r.MinZ), maxZ = site.Plan.Max(r => r.MaxZ);
        return new Vector3((minX + maxX) / 2f, site.QuestMarker.y, (minZ + maxZ) / 2f);
    }

    // Every registered site with the key the other commands take, its stage and where its plan lies.
    private static void Sites()
    {
        for (int i = 0; i < Mod.Sites.Count; i++)
        {
            var site = Mod.Sites[i];
            var expansion = Mod.Engine.GetUpgrade(site.Base.Id, site.UpgradeId);
            string followUps = string.Join(", ", site.FollowUps.Select(f => f.UpgradeId + "=" + Mod.Engine.GetUpgrade(site.Base.Id, f.UpgradeId).Stage));
            Mod.Log($"baseddiag sites: {i} {site.Base.Id} property {site.Base.PropertyId}, owned {Mod.Owns(site)}, {site.UpgradeId} {expansion.Stage} "
                + $"({expansion.CompletedCycles}/{site.Upgrade.ConstructionCycles} nights), follow-ups [{followUps}], plan centre {PlanCentre(site)}, "
                + $"staff places {site.StaffPlaces.Count}, crew {(site.Crew == null ? "none" : site.Crew.ActiveCount + " visible")}, folder {site.FolderItemId}.");
        }
    }
}
#endif
