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
    public override string ExampleUsage => "baseddiag [site=<id>] status | sites | plan | ray [office|street] | creator | dock | dockroute [build|route|routes|work|deliver] | room | workers | hammer [seconds|set <time>] | network | packdoorway | phase <id> [upgrade] | hold <slot> | stayers | walkers | staff | advance | rain [on|dry|off|state] | lights | lightset <what> <value> | journal [off] | vfx [on|off|force] | walk <x> <z> [<x> <z> ...] | litter [<upgrade>|drop <upgrade>|pickup [n]] | ladder | climb [seconds] | fixtures | tip [state|cash <n>|product [i]|set <n>|wallet <n>] | folder slot <n> | equip | track [seconds|stop] | route <sourceId> <up|down> <destinationId> <up|down> [filterId] | route unfilter | contents [idPart] | loadstate | aim <x> <y> <z> [pitch|-] [yaw] | ghost [<itemId>|read|drop|hush] | gridedges [code] | zfight [filter] [x y z radius]";

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
                    Mod.Log("baseddiag: " + Content.Bungalow.BungalowDoors.Debug(args));
                    break;
                case "plaque":
                    // plaque x y z lean = moves the live sunroom plaque until the next scene; plaque alone only reports.
                    if (args.Count > 4)
                    {
                        float P(int i) => float.Parse(args[i], System.Globalization.CultureInfo.InvariantCulture);
                        Content.Bungalow.BungalowPlaque.DebugMove(new UnityEngine.Vector3(P(1), P(2), P(3)), P(4));
                    }
                    Mod.Log("baseddiag: " + Content.Bungalow.BungalowPlaque.Describe());
                    break;
                case "cake":
                    if (args.Count > 2 && args[1] == "date") Mod.Log("baseddiag: " + Content.Bungalow.BungalowCake.ForceDate(args[2]) + ".");
                    else Mod.Log("baseddiag: " + Content.Bungalow.BungalowCake.Describe());
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
                case "raythief":
                    if (args.Count > 1 && args[1] == "status") RayMenuGuard.DebugThiefStatus();
                    else RayMenuGuard.DebugThief();
                    break;
                case "blockers":
                    Mod.DebugBlockers(_site);
                    break;
                case "planfree":
                    Mod.DebugFreeConversation = args.Count > 1 && args[1] == "on";
                    Mod.Log("baseddiag: conversation hold under the plan " + (Mod.DebugFreeConversation ? "off" : "on") + ".");
                    break;
                case "folder":
                    if (args.Count > 2 && args[1] == "slot")
                        Mod.Log("baseddiag: folder in hotbar slot " + args[2] + ": " + ProjectFolder.DebugSlot(_site, int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture)) + ".");
                    else Folder(args.Count > 1 ? args[1] : "state");
                    break;
                case "tip":
                    // tip [state] = tips, limit and trash per build; tip cash <n> and tip product [i] pay like the
                    // foreman's choices; tip set <n> = the running build's total (host), so each check starts known.
                    string tipAction = args.Count > 1 ? args[1] : "state";
                    int tipValue = args.Count > 2 ? int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 0;
                    // tip wallet <n> = cash for this peer's player, also on a client.
                    if (tipAction == "wallet") Tips.DebugWallet(tipValue);
                    else if (tipAction == "cash") Tips.DebugCash(_site.Base.Id, tipValue);
                    else if (tipAction == "product") Tips.DebugProduct(_site.Base.Id, tipValue);
                    else if (tipAction == "set") Tips.DebugSet(_site.Base.Id, tipValue);
                    else if (tipAction != "state") throw new ArgumentException("baseddiag tip [state|cash <n>|product [i]|set <n>|wallet <n>]");
                    Mod.Log("baseddiag: tips " + Tips.Describe());
                    break;
                case "heldby":
                    // heldby Small Storage Rack|Wall Clock = the hold names these items (host), heldby off clears it,
                    // heldby alone only reports. Logs the hold line and the order line in this peer's language.
                    if (args.Count > 1) Mod.DebugHeld(args[1] == "off" ? "" : string.Join(" ", args.Skip(1)));
                    Mod.Log("baseddiag held: language " + DooDesch.Localization.L10n.Language + "; names '" + Mod.HeldBy
                        + "'; hold line '" + Text.T("On hold: move {0} off the red walls.", Text.Items(Mod.HeldBy))
                        + "'; order line '" + Text.T("Move {0} off the red walls.", Text.Items(Mod.HeldBy)) + "'.");
                    break;
                case "lang":
                    SetLanguage(args.Count > 1 ? args[1] : throw new ArgumentException("baseddiag lang <en|de>"));
                    break;
                case "vfx":
                    // on = by stage and hour, off = none, force = the yard fire and the current stage's sparks at any hour.
                    if (args.Count > 1)
                    {
                        Content.StorageUnit.StorageVfx.Current = args[1] switch
                        {
                            "on" => Content.StorageUnit.StorageVfx.Mode.Normal,
                            "off" => Content.StorageUnit.StorageVfx.Mode.Off,
                            "force" => Content.StorageUnit.StorageVfx.Mode.Force,
                            _ => throw new ArgumentException("baseddiag vfx [on|off|force]")
                        };
                        Content.StorageUnit.StorageVfx.Refresh();
                        Content.Bungalow.BungalowVfx.Refresh();
                    }
                    Mod.Log("baseddiag: storage effects " + Content.StorageUnit.StorageVfx.Describe());
                    Mod.Log("baseddiag: bungalow effects " + Content.Bungalow.BungalowVfx.Describe());
                    Mod.Log("baseddiag: bungalow effects " + Content.Bungalow.BungalowVfx.Describe());
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
                case "rain":
                    Rain(args);
                    break;
                case "dock":
                    DockProbe.Execute(args.Skip(1).ToArray());
                    break;
                case "room":
                    RoomProbe.Execute(_site, args.Skip(1).ToArray());
                    break;
                case "dockroute":
                    if (args.Count > 3 && args[1] == "deliver") DockDeliver(int.Parse(args[2]), args[3], args.Count > 4 ? int.Parse(args[4]) : 0);
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
                case "litter":
                    // litter [<upgrade>] = trash in the build's rooms; litter drop <upgrade> = the drop of a finished
                    // build again; litter pickup [n] = the equipped trash grabber takes the n nearest pieces.
                    if (args.Count > 2 && args[1] == "drop") ConstructionLitter.Drop(_site, args[2]);
                    else if (args.Count > 1 && args[1] == "pickup") Pickup(args.Count > 2 ? int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 1);
                    else LitterCounts(args.Count > 1 ? args[1] : null);
                    break;
                case "ladder":
                    Mod.Log("baseddiag: storage " + Content.StorageUnit.StorageLadder.Describe());
                    break;
                case "climb":
                    MelonLoader.MelonCoroutines.Start(Climb(args.Count > 1 ? float.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 3f));
                    break;
                case "fixtures":
                    Fixtures();
                    break;
                case "equip":
                    EquippedHandlers();
                    break;
                case "track":
                    StaffTrack.Track(_site, args);
                    break;
                case "route":
                    StaffTrack.Route(_site, args);
                    break;
                case "contents":
                    StaffTrack.Contents(_site, args);
                    break;
                case "loadstate":
                    StaffTrack.LoadState();
                    break;
                case "aim" when args.Count > 3:
                    Aim(F(args[1]), F(args[2]), F(args[3]), args.Count > 4 && args[4] != "-" ? F(args[4]) : null, args.Count > 5 && args[5] != "-" ? F(args[5]) : null, args.Count > 6 ? args[6] : null);
                    break;
                case "ghost" when args.Count == 1:
                    // ghost = the active placement ghost's grid, footprint tiles and verdict
                    Ghost();
                    break;
                case "ghost":
                    // ghost <itemId> = hold it, ghost read = the placement ghost's verdict, ghost drop = hands free
                    GhostProbe.Handle(args);
                    break;
                case "gridedges":
                    // gridedges <property code> = every outer grid edge against the walls and floor ends there
                    BuildChecks.GridEdges(args.Count > 1 ? args[1] : _site.PropertyName.ToLowerInvariant());
                    break;
                case "zfight":
                    // zfight <path filter> [x y z radius] = faces of two parts in one plane
                    var at = args.Count > 5
                        ? new Vector3(float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture), float.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture), float.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture))
                        : new Vector3(-176f, -2f, 113f);
                    BuildChecks.ZFight(args.Count > 1 ? args[1] : "based_", at, args.Count > 5 ? float.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture) : 24f);
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
            if (van != null)
                Mod.Log($"baseddiag: dockroute dock {d} van at {van.transform.position} forward {van.transform.forward} size {van.BoundingBoxDimensions}");
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
                + ", mesh " + Vector3.Distance(hit.position, at).ToString("0.00") + " m away, faces " + places[i].forward.ToString("F2");
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
            // A worker that is there to the game but not to the player: hidden avatar, inside a building, or no behaviour.
            var behaviour = employee.Behaviour != null ? employee.Behaviour.activeBehaviour : null;
            string line = "baseddiag: employee " + employee.name + " index " + employee.EmployeeIndex + " at " + agent.transform.position
                + ", waits at " + (employee.WaitOutside?.IdlePoint != null ? employee.WaitOutside.IdlePoint.position.ToString() : "none") + ", agent type " + agent.agentTypeID + ", on mesh " + agent.isOnNavMesh
                + ", route to the street " + (found ? street.status.ToString() : "none") + ", reaches the street " + reaches
                + ", visible " + employee.isVisible + ", active " + employee.gameObject.activeInHierarchy + ", agent enabled " + agent.enabled
                + ", building " + (employee.CurrentBuilding != null ? employee.CurrentBuilding.name : "none")
                + ", behaviour " + (behaviour != null ? behaviour.Name : "none")
                // The game's own reasons for "Wait outside": no locker, not paid for today, or the end of the day.
                + ", home " + (employee.GetHome() != null) + ", paid " + employee.PaidForToday
                + ", end of day " + Il2CppScheduleOne.DevUtilities.NetworkSingleton<Il2CppScheduleOne.GameTime.TimeManager>.Instance.IsEndOfDay;
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
            case "hold": Mod.Log("baseddiag: folder in hand from hotbar " + ProjectFolder.DebugHold(_site) + "."); break;
            case "drop": Mod.Log("baseddiag: hands free on hotbar " + ProjectFolder.DebugHold(_site, empty: true) + "."); break;
            case "close":
                Based.UI.BuildPlan.Close("console");
                Mod.Log("baseddiag: folder plan closed.");
                break;
            default: throw new ArgumentException("baseddiag folder give|open|close|hang|wall|state|hold|drop");
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

    // Every visible tube lamp the expansion adds against the ceiling above it: a ray up from both ends and the middle of
    // the lamp's back. The ceiling is the lower of the first collider above and the lowest flat panel drawn over that
    // point, since the expansion's ceiling panels have no collider. The gap is the distance from the back to the ceiling;
    // a negative gap means that end lies inside it. PASS when every gap lies between -0.5 and 2 cm.
    private static void Fixtures()
    {
        int pass = 0, fail = 0, vanilla = 0, hidden = 0;
        var all = UnityEngine.Object.FindObjectsOfType<MeshRenderer>();
        var panels = new List<MeshRenderer>();
        foreach (var part in all)
        {
            Bounds b = part.bounds;
            if (b.max.x < -12f || b.min.x > 4f || b.max.z < 92f || b.min.z > 115f || b.max.y < 1f) continue;
            if (!part.enabled || !part.gameObject.activeInHierarchy) continue;
            if (b.size.y > .2f || Mathf.Max(b.size.x, b.size.z) < 1f || Path(part.transform).Contains("FluroBar")) continue;
            panels.Add(part);
        }
        foreach (var part in all)
        {
            Vector3 at = part.transform.position;
            if (at.x < -12f || at.x > 4f || at.z < 92f || at.z > 115f || at.y < 1f) continue;
            string path = Path(part.transform);
            if (!path.Contains("FluroBar") || !path.Contains("/Body")) continue;
            if (!path.Contains("based_storage"))
            {
                vanilla++;
                continue;
            }
            if (!part.enabled || !part.gameObject.activeInHierarchy)
            {
                hidden++;
                continue;
            }
            var filter = part.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                Mod.Warn("baseddiag fixtures: " + path + " has no mesh, skipped.");
                continue;
            }
            Bounds local = filter.sharedMesh.bounds;
            bool alongX = local.extents.x >= local.extents.z;
            Vector3 half = alongX ? new Vector3(local.extents.x - .02f, 0, 0) : new Vector3(0, 0, local.extents.z - .02f);
            Vector3 top = new(local.center.x, local.max.y, local.center.z);
            var gaps = new List<string>();
            bool ok = true;
            foreach (var (name, offset) in new[] { ("end-a", -half), ("middle", Vector3.zero), ("end-b", half) })
            {
                Vector3 back = part.transform.TransformPoint(top + offset);
                // From well below the back, so a ceiling the end reaches into is still hit from its open side.
                Vector3 from = back + Vector3.down * .3f;
                float ceiling = float.MaxValue;
                string by = null;
                if (Physics.Raycast(from, Vector3.up, out RaycastHit hit, 3.5f, ~0, QueryTriggerInteraction.Ignore))
                {
                    ceiling = hit.point.y;
                    by = "collider " + hit.collider.name;
                }
                foreach (var panel in panels)
                {
                    Bounds b = panel.bounds;
                    if (back.x < b.min.x || back.x > b.max.x || back.z < b.min.z || back.z > b.max.z) continue;
                    if (b.min.y < from.y || b.min.y >= ceiling) continue;
                    ceiling = b.min.y;
                    by = "panel " + Path(panel.transform);
                }
                if (by == null)
                {
                    gaps.Add($"{name} ({back.x:F2},{back.y:F2},{back.z:F2}) no ceiling");
                    ok = false;
                    continue;
                }
                float gap = ceiling - back.y;
                ok &= gap >= -.005f && gap < .02f;
                gaps.Add($"{name} ({back.x:F2},{back.y:F2},{back.z:F2}) gap {gap * 100f:F1} cm to {by}");
            }
            if (ok) pass++; else fail++;
            Mod.Log($"baseddiag fixtures: {(ok ? "PASS" : "FAIL")} {path} " + string.Join("; ", gaps));
        }
        Mod.Log($"baseddiag fixtures: {pass} pass, {fail} fail; {panels.Count} drawn panels checked; {vanilla} vanilla and {hidden} hidden lamp bodies not counted.");
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
    // L10n resolves the language once per session; a test resets its cached choice to read both languages in one game.
    private static void SetLanguage(string code)
    {
        const System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
        var type = typeof(DooDesch.Localization.L10n);
        var lang = type.GetField("_lang", Flags) ?? throw new MissingFieldException(type.FullName, "_lang");
        var active = type.GetField("_active", Flags) ?? throw new MissingFieldException(type.FullName, "_active");
        lang.SetValue(null, code);
        active.SetValue(null, null);
        Mod.Log("baseddiag: language set to " + DooDesch.Localization.L10n.Language + "; reopen the plan to see it.");
    }

    private static void Hold(int slot)
    {
        var inventory = Il2CppScheduleOne.PlayerScripts.PlayerInventory.Instance;
        ProjectFolder.EquipOnly(inventory, inventory.IndexAllSlots(slot));
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

    // Every equipped item handler in the scene with its user, data and network object. A handler whose synced user is
    // null throws a NullReferenceException in its Update every frame on this peer.
    private static void EquippedHandlers()
    {
        var handlers = UnityEngine.Object.FindObjectsOfType<Il2CppScheduleOne.Equipping.EquippedItemHandler>(true);
        int missing = 0;
        foreach (var handler in handlers)
        {
            string user, data, net;
            try
            {
                var raw = handler._user;
                var component = raw == null ? null : ((Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)(object)raw).TryCast<Component>();
                user = raw == null ? "null" : component != null ? PathOf(component.transform) : "not a component";
                if (raw == null) missing++;
            }
            catch (Exception error) { user = "unreadable (" + error.GetType().Name + ")"; }
            try { data = handler._equippableData == null ? "null" : handler._equippableData.name; }
            catch (Exception error) { data = "unreadable (" + error.GetType().Name + ")"; }
            try { net = "object " + handler.ObjectId + ", spawned " + handler.IsSpawned + ", server " + handler.IsServer; }
            catch (Exception error) { net = "unreadable (" + error.GetType().Name + ")"; }
            Mod.Log("baseddiag equip: '" + PathOf(handler.transform) + "' active " + handler.gameObject.activeInHierarchy
                    + ", equipped " + handler.IsEquipped + ", user " + user + ", data " + data + ", " + net + ".");
        }
        Mod.Log("baseddiag equip: " + handlers.Length + " handler(s), " + missing + " without a user.");
    }

    private static string PathOf(Transform transform)
    {
        var parts = new List<string>();
        for (var t = transform; t != null; t = t.parent) parts.Add(t.name);
        parts.Reverse();
        return string.Join("/", parts);
    }

    private static void LitterCounts(string? upgradeId)
    {
        foreach (var id in _site.BuildIds.Distinct().Where(i => upgradeId == null || i == upgradeId))
        {
            var rooms = ConstructionLitter.RoomsOf(_site, id);
            Mod.Log("baseddiag litter: " + id + " " + Mod.StageOf(_site, id) + ", " + rooms.Count + " room(s), planned "
                    + (rooms.Count == 0 ? 0 : ConstructionLitter.CountFor(rooms)) + ", in the rooms now " + ConstructionLitter.CountIn(_site, id) + ".");
        }
        var manager = Il2CppScheduleOne.DevUtilities.NetworkSingleton<Il2CppScheduleOne.Trash.TrashManager>.Instance;
        Mod.Log("baseddiag litter: trash items in the world " + (manager == null || manager._trashItems == null ? -1 : manager._trashItems.Count) + ".");
    }

    // The game's own pickup of the equipped trash grabber, on the pieces nearest the player.
    private static float F(string text) => float.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

    // aim x y z [pitch] [yaw] [item]: turns the player toward a world point and equips the first buildable in the hotbar
    // (or the one with that item id), so the game's own placement ghost asks the grids or walls there every frame. The
    // ghost stands holdDistance in front of the camera, closer the steeper the look; pitch is degrees below the horizon
    // ("-" aims at the point), yaw the ghost's own turn ("-" leaves it).
    private static void Aim(float x, float y, float z, float? pitch, float? yaw, string? item = null)
    {
        var camera = Il2CppScheduleOne.DevUtilities.PlayerSingleton<Il2CppScheduleOne.PlayerScripts.PlayerCamera>.Instance;
        var inventory = Il2CppScheduleOne.DevUtilities.PlayerSingleton<Il2CppScheduleOne.PlayerScripts.PlayerInventory>.Instance;
        var player = Il2CppScheduleOne.PlayerScripts.Player.Local;
        if (camera == null) { Mod.Warn("baseddiag aim: no player camera."); return; }
        if (inventory == null) { Mod.Warn("baseddiag aim: no player inventory."); return; }
        if (player == null) { Mod.Warn("baseddiag aim: no local player."); return; }
        var target = new Vector3(x, y, z);
        var flat = target - camera.transform.position;
        flat.y = 0f;
        player.transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg, 0f);
        float down = pitch ?? Mathf.Atan2(camera.transform.position.y - y, flat.magnitude) * Mathf.Rad2Deg;
        camera.transform.localRotation = Quaternion.Euler(down, 0f, 0f);
        // An open phone menu (a phone app that opened itself on load, for one) turns equipping off, and no ghost would
        // appear; it closes the way Tab closes it.
        var menu = Il2CppScheduleOne.DevUtilities.Singleton<Il2CppScheduleOne.UI.GameplayMenu>.Instance;
        if (menu != null && menu.IsOpen)
        {
            menu.Close();
            Mod.Log("baseddiag aim: closed the open phone menu so the item can be equipped.");
        }
        if (!inventory.EquippingEnabled) Mod.Warn("baseddiag aim: equipping is turned off (a menu or screen is open); no placement ghost will appear.");
        string? equipped = null;
        for (int i = 0; i < inventory.hotbarSlots.Count && equipped == null; i++)
        {
            var slot = inventory.hotbarSlots[i];
            var def = slot?.ItemInstance?.Definition;
            if (def == null || def.TryCast<Il2CppScheduleOne.ItemFramework.BuildableItemDefinition>() == null) continue;
            if (item != null && !string.Equals(def.ID, item, StringComparison.OrdinalIgnoreCase)) continue;
            // A slot that was already selected when the item arrived holds no live equippable, so the build never
            // starts; deselecting first makes Equip build a fresh one. A selected slot with a running build keeps it, so
            // its ghost (and the turn below) stays the same object.
            var building = Il2CppScheduleOne.DevUtilities.NetworkSingleton<Il2CppScheduleOne.Building.BuildManager>.Instance?.currentBuildHandler != null;
            if (slot!.IsSelected && building) { equipped = "'" + def.ID + "' in hotbar slot " + i + " (already held)"; break; }
            if (slot.IsSelected) slot.Deselect();
            inventory.Equip(slot);
            equipped = "'" + def.ID + "' in hotbar slot " + i;
        }
        if (equipped == null) { Mod.Warn("baseddiag aim: the hotbar holds no buildable item" + (item != null ? " '" + item + "'" : "") + "; give one first."); return; }
        if (yaw is float turn) MelonLoader.MelonCoroutines.Start(TurnGhost(turn));
        Mod.Log($"baseddiag aim: looking at {target.ToString("F2")} from {camera.transform.position.ToString("F2")} (pitch {down:F1}), equipped {equipped}.");
    }

    // The ghost exists a frame after the item is equipped; its rotation is the build's own, set the way R turns it.
    private static System.Collections.IEnumerator TurnGhost(float yaw)
    {
        // The ghost of the build handler the BuildManager holds now: aim re-equips, and the previous ghost lives until the
        // frame ends, so a scene search could turn the one about to be destroyed.
        for (int i = 0; i < 30; i++)
        {
            var handler = Il2CppScheduleOne.DevUtilities.NetworkSingleton<Il2CppScheduleOne.Building.BuildManager>.Instance?.currentBuildHandler;
            var build = handler != null ? handler.GetComponent<Il2CppScheduleOne.Building.BuildUpdate_Grid>() : null;
            if (build != null)
            {
                build._rotation = yaw;
                Mod.Log($"baseddiag aim: ghost turned to {yaw:F0} degrees.");
                yield break;
            }
            yield return null;
        }
        Mod.Warn("baseddiag aim: no placement ghost appeared to turn.");
    }

    // Where the game's placement ghost stands, which tile of which grid it snapped to, every footprint tile with the grid
    // it lies on, and whether the game accepts the spot (BuildUpdate_Grid._validPosition, the red or white box).
    private static void Ghost()
    {
        // The ghost's logic sits on the build handler the BuildManager made for the equipped item.
        var manager = Il2CppScheduleOne.DevUtilities.NetworkSingleton<Il2CppScheduleOne.Building.BuildManager>.Instance;
        var handler = manager != null ? manager.currentBuildHandler : null;
        if (handler == null)
        {
            var inv = Il2CppScheduleOne.DevUtilities.PlayerSingleton<Il2CppScheduleOne.PlayerScripts.PlayerInventory>.Instance;
            string equip = "no inventory";
            if (inv != null)
            {
                var held = new List<string>();
                var container = inv.EquipContainer;
                if (container != null) for (int i = 0; i < container.childCount; i++) held.Add(container.GetChild(i).name);
                var selected = inv.equippedSlot?.ItemInstance?.Definition?.ID ?? "nothing";
                equip = $"equipping enabled {inv.EquippingEnabled}, slot {inv.EquippedSlotIndex} holds {selected}, container " +
                        (container == null ? "missing" : (container.gameObject.activeInHierarchy ? "active" : "inactive") + " [" + string.Join(", ", held) + "]");
            }
            Mod.Log("baseddiag ghost: no grid or wall placement ghost is active (build manager " + (manager == null ? "missing" : "building " + manager.isBuilding) + "; " + equip + ").");
            return;
        }
        var build = handler.GetComponent<Il2CppScheduleOne.Building.BuildUpdate_Grid>();
        if (build == null)
        {
            var wall = handler.GetComponent<Il2CppScheduleOne.Building.BuildUpdate_Surface>();
            if (wall == null)
            {
                var kinds = new List<string>();
                foreach (var c in handler.GetComponents<Component>()) if (c != null) kinds.Add(c.GetIl2CppType().Name);
                Mod.Log("baseddiag ghost: build handler '" + handler.name + "' has neither grid nor wall placement [" + string.Join(", ", kinds) + "].");
                return;
            }
            WallGhost(wall);
            return;
        }
        var tile = build.closestIntersection?.tile;
        if (tile == null || tile.OwnerGrid == null)
        {
            Mod.Log($"baseddiag ghost: at {build.GhostModel?.transform.position.ToString("F2") ?? "none"}, no tile under the ghost, valid {build._validPosition}.");
            return;
        }
        var grid = tile.OwnerGrid;
        var feet = new List<string>();
        var pairs = build.BuildableItemClass.CoordinateFootprintTilePairs;
        for (int i = 0; i < pairs.Count; i++)
        {
            var foot = pairs[i].footprintTile;
            var on = grid.GetTile(grid.GetMatchedCoordinate(foot));
            string other = "";
            if (on == null)
                foreach (var g in grid._parentProperty.Grids)
                {
                    if (g == null || g.Pointer == grid.Pointer || Mathf.Abs(g.transform.position.y - grid.transform.position.y) > .5f) continue;
                    if (g.GetTile(g.GetMatchedCoordinate(foot)) != null) { other = " on grid '" + g.name + "'"; break; }
                }
            var p = foot.transform.position;
            feet.Add($"({p.x:F2},{p.z:F2}) {(on != null ? "tile" : "missing" + other)}");
        }
        Mod.Log($"baseddiag ghost: at {build.GhostModel.transform.position.ToString("F2")}, rotation {build._rotation:F0}, grid '{grid.name}', " +
                $"{pairs.Count} footprint tiles [{string.Join("; ", feet)}], valid {build._validPosition}.");
    }

    // The wall placement's own answer (BuildUpdate_Surface.validPosition, the red or white ghost), with the collider the
    // look ray hits, the Surface the game finds above it, and each of the game's conditions on that Surface.
    private static void WallGhost(Il2CppScheduleOne.Building.BuildUpdate_Surface wall)
    {
        var camera = Il2CppScheduleOne.DevUtilities.PlayerSingleton<Il2CppScheduleOne.PlayerScripts.PlayerCamera>.Instance;
        string hit = "nothing", surface = "none";
        if (camera != null && camera.LookRaycast_ExcludeBuildables(wall.detectionRange, out var ray, wall.DetectionMask))
        {
            hit = PathOf(ray.collider.transform) + " at " + ray.point.ToString("F2") + " normal " + ray.normal.ToString("F2");
            var found = ray.collider.GetComponentInParent<Il2CppScheduleOne.Building.Surface>();
            if (found != null)
            {
                bool typeOk = wall.BuildableItemClass.ValidSurfaceTypes.Contains(found.SurfaceType);
                bool owned = found.ParentProperty != null && found.ParentProperty.IsOwned;
                bool point = found.IsPointValid(ray.point, ray.collider);
                surface = $"'{PathOf(found.transform)}' type {found.SurfaceType} ok {typeOk}, property " +
                          $"{found.ParentProperty?.PropertyCode ?? "none"} owned {owned}, face ok {point}";
            }
        }
        else if (camera == null) Mod.Warn("baseddiag ghost: no player camera; the look ray is not cast.");
        Mod.Log($"baseddiag ghost: wall item '{wall.ItemInstance?.ID}', valid {wall.validPosition}, hit {hit}, surface {surface}.");
    }

    private static void Pickup(int count)
    {
        var grabber = UnityEngine.Object.FindObjectOfType<Il2CppScheduleOne.Equipping.Equippable_TrashGrabber>();
        if (grabber == null) { Mod.Warn("baseddiag litter pickup: no trash grabber equipped."); return; }
        var manager = Il2CppScheduleOne.DevUtilities.NetworkSingleton<Il2CppScheduleOne.Trash.TrashManager>.Instance;
        var here = Il2CppScheduleOne.PlayerScripts.Player.Local.transform.position;
        var items = new List<Il2CppScheduleOne.Trash.TrashItem>();
        foreach (var item in manager._trashItems) if (item != null) items.Add(item);
        int before = items.Count, taken = 0;
        foreach (var item in items.OrderBy(i => (i.transform.position - here).sqrMagnitude).Take(count))
        {
            if (grabber.GetCapacity() <= 0) { Mod.Warn("baseddiag litter pickup: the grabber is full."); break; }
            Mod.Log("baseddiag litter pickup: " + item.ID + " at " + item.transform.position + ", " + Vector3.Distance(here, item.transform.position).ToString("F2") + " m away.");
            grabber.PickupTrash(item);
            taken++;
        }
        MelonLoader.MelonCoroutines.Start(AfterPickup(before, taken));
    }

    // The game destroys picked trash at the end of the frame, so the count is read a moment later.
    private static System.Collections.IEnumerator AfterPickup(int before, int taken)
    {
        yield return new WaitForSeconds(.5f);
        var manager = Il2CppScheduleOne.DevUtilities.NetworkSingleton<Il2CppScheduleOne.Trash.TrashManager>.Instance;
        Mod.Log("baseddiag litter pickup: took " + taken + "; trash items in the world " + before + " before, " + manager._trashItems.Count + " after.");
        LitterCounts(null);
    }

    // Holds forward on the game's own move input for the given seconds, so the ladder climbs the way a player's key
    // does, and reports where the player started, whether a ladder took them, and where they ended.
    private static System.Collections.IEnumerator Climb(float seconds)
    {
        var movement = Il2CppScheduleOne.DevUtilities.PlayerSingleton<Il2CppScheduleOne.PlayerScripts.PlayerMovement>.Instance;
        var start = movement.transform.position;
        string mounted = "";
        float end = Time.time + seconds, top = start.y;
        Mod.Log($"baseddiag climb: start at ({start.x:F2},{start.y:F2},{start.z:F2}), on a ladder {movement.IsOnLadder}.");
        while (Time.time < end)
        {
            Il2CppScheduleOne.GameInput.MotionAxis = new Vector2(0f, 1f);
            if (movement.IsOnLadder && mounted.Length == 0) mounted = movement.CurrentLadder.name;
            top = Mathf.Max(top, movement.transform.position.y);
            yield return null;
        }
        Il2CppScheduleOne.GameInput.MotionAxis = Vector2.zero;
        yield return new WaitForSeconds(.5f);
        var stop = movement.transform.position;
        Mod.Log($"baseddiag climb: end at ({stop.x:F2},{stop.y:F2},{stop.z:F2}), highest {top:F2}, ladder {(mounted.Length == 0 ? "none" : mounted)}, on a ladder now {movement.IsOnLadder}.");
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

    // rain on: every night counts as rainy; dry: every night counts as dry; off: the game's weather decides again.
    private static void Rain(List<string> args)
    {
        string mode = args.Count > 1 ? args[1].ToLowerInvariant() : "state";
        switch (mode)
        {
            case "on": NightRain.Forced = true; break;
            case "dry": NightRain.Forced = false; break;
            case "off": NightRain.Forced = null; break;
            case "state": break;
            default: throw new ArgumentException("baseddiag rain [on|dry|off|state]");
        }
        Mod.Log("baseddiag: rain " + (mode == "state" ? "state" : mode + " set") + "; " + NightRain.Describe() + ".");
        foreach (var site in Mod.Sites)
            foreach (var upgrade in site.Base.Upgrades)
            {
                var state = Mod.Engine.GetUpgrade(site.Base.Id, upgrade.Id);
                if (state.Stage != UpgradeStage.Constructing && !state.RainDelayed) continue;
                Mod.Log("baseddiag: rain " + site.Base.Id + "/" + upgrade.Id + ": stage=" + state.Stage + ", cycles=" + state.CompletedCycles + "/"
                    + upgrade.ConstructionCycles + ", openToRain=" + Mod.Engine.IsOpenToRain(site.Base.Id, upgrade.Id) + ", delayed=" + state.RainDelayed
                    + (state.RainDelayed ? " (cycle " + state.RainDelayedCycle + ")" : "") + ".");
            }
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

    // Gives every handler at the site one route from each loading dock, the setup a player makes in the clipboard.
    private static void SetDockRoute()
    {
        var property = SiteProperty();
        if (property.LoadingDocks == null || property.LoadingDocks.Length == 0) throw new InvalidOperationException("The " + _site.PropertyName + " has no loading dock.");
        var shelf = FirstShelf(property);
        if (shelf == null) Mod.Warn("baseddiag: dockroute route: no storage shelf on the property; the route ends at the dock and moves nothing");
        int set = 0;
        foreach (var (packager, config) in Packagers())
        {
            for (int d = 0; d < property.LoadingDocks.Length; d++)
            {
                var dock = property.LoadingDocks[d];
                var source = new Il2CppScheduleOne.Management.ITransitEntity(dock.Pointer);
                var destination = new Il2CppScheduleOne.Management.ITransitEntity(shelf != null ? shelf.Pointer : dock.Pointer);
                config.Routes.AddItem(new Il2CppScheduleOne.Management.AdvancedTransitRoute(source, destination));
                Mod.Log($"baseddiag: dockroute route: {packager.FirstName} gets a route from dock {d} {dock.GUID} to {(shelf != null ? "shelf " + shelf.GUID + " at " + shelf.transform.position : "the dock")}");
            }
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
    private static void DockDeliver(int shopIndex, string itemId, int dockIndex)
    {
        var property = SiteProperty();
        int docks = property.LoadingDocks?.Length ?? 0;
        if (dockIndex < 0 || dockIndex >= docks) throw new InvalidOperationException($"The {_site.PropertyName} has {docks} loading docks; dock {dockIndex} does not exist.");
        var shop = Il2CppScheduleOne.UI.Shop.ShopInterface.AllShops[shopIndex];
        if (shop?.DeliveryVehicle?.Vehicle == null) throw new InvalidOperationException("That shop has no delivery vehicle.");
        if (Il2CppScheduleOne.Registry.GetItem(itemId) == null) throw new ArgumentException("Unknown item: " + itemId);
        var items = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.DevUtilities.StringIntPair>(
            new[] { new Il2CppScheduleOne.DevUtilities.StringIntPair(itemId, 5) });
        var delivery = new Il2CppScheduleOne.Delivery.DeliveryInstance(Guid.NewGuid().ToString(), shop.ShopName, property.PropertyCode, dockIndex, items,
            Il2CppScheduleOne.Delivery.EDeliveryStatus.InTransit, 1);
        Il2CppScheduleOne.Delivery.DeliveryManager.Instance.SendDelivery(delivery);
        Mod.Log($"baseddiag: dockroute deliver: 5 {itemId} from '{shop.ShopName}' to {property.PropertyCode} dock {dockIndex} in 1 minute");
    }

    private static void DockRoutes()
    {
        var property = SiteProperty();
        var docks = property.LoadingDocks?.ToArray() ?? Array.Empty<Il2CppScheduleOne.Delivery.LoadingDock>();
        int handlers = 0;
        foreach (var (packager, config) in Packagers())
        {
            handlers++;
            var routes = config.Routes.Routes;
            Mod.Log($"baseddiag: dockroute routes: {packager.FirstName} has {routes.Count} routes; docks {(docks.Length == 0 ? "missing" : string.Join(", ", docks.Select(d => d.GUID.ToString())))}");
            for (int i = 0; i < routes.Count; i++)
            {
                var route = routes[i];
                // Dock 0 keeps the name the Sweatshop test looks for; a further dock carries its index.
                string Side(Il2CppScheduleOne.Management.ITransitEntity? e)
                {
                    if (e == null) return "none";
                    int d = Array.FindIndex(docks, k => k.Pointer == e.Pointer);
                    return d < 0 ? e.Name : d == 0 ? "the loading dock" : "the loading dock " + d;
                }
                Mod.Log($"baseddiag: dockroute routes:   {i}: from {Side(route.Source)} to {Side(route.Destination)}");
            }
        }
        if (handlers == 0) Mod.Warn("baseddiag: dockroute routes: no handler works at the " + _site.PropertyName);
    }

    // Moves the upgrade straight to a stage, so one run can look at every construction state.
    // "phase <phase>" moves the site's first upgrade; "phase <upgradeId>:<phase>" or "phase <phase> <upgrade>" moves a
    // follow-up, named in full or by its last part ("carport" for "ray.bungalow-carport"). A follow-up finishes
    // everything it needs first and resets a build still under construction, since the engine allows one per base.
    private static void JumpToPhase(string argument, string upgradeArg)
    {
        if (!NativeBus.IsHost) throw new InvalidOperationException("Only the host can change the construction state.");
        int colon = argument.LastIndexOf(':');
        if (colon > 0) upgradeArg = argument.Substring(0, colon);
        string phase = colon > 0 ? argument.Substring(colon + 1) : argument;
        string upgradeId = upgradeArg.Length == 0 ? _site.UpgradeId
            : _site.BuildIds.FirstOrDefault(id => id == upgradeArg || id.EndsWith("-" + upgradeArg, StringComparison.Ordinal))
              ?? throw new ArgumentException("baseddiag phase [upgradeId:]phase; " + _site.Base.Id + " builds " + string.Join(", ", _site.BuildIds));
        var state = Mod.Engine.ExportState();
        var upgrade = state.Upgrades.First(u => u.BaseId == _site.Base.Id && u.UpgradeId == upgradeId);
        var definition = _site.UpgradeOf(upgradeId);
        if (upgradeId != _site.UpgradeId)
        {
            var needed = new HashSet<string>();
            void Need(string id)
            {
                foreach (var p in _site.UpgradeOf(id).Prerequisites)
                    if (needed.Add(p)) Need(p);
            }
            Need(upgradeId);
            foreach (var other in state.Upgrades.Where(u => u.BaseId == _site.Base.Id && u.UpgradeId != upgradeId))
            {
                if (needed.Contains(other.UpgradeId))
                {
                    other.Stage = UpgradeStage.Complete;
                    other.CompletedCycles = _site.UpgradeOf(other.UpgradeId).ConstructionCycles;
                }
                else if (other.Stage == UpgradeStage.Constructing)
                {
                    other.Stage = UpgradeStage.RightsMissing;
                    other.CompletedCycles = 0;
                    other.RightsPaid = 0;
                    other.ConstructionPaid = 0;
                }
            }
            Mod.Log("baseddiag: phase: " + upgradeId + " needs " + (needed.Count == 0 ? "nothing" : string.Join(", ", needed)) + "; those are complete, builds under construction reset, finished ones stay.");
        }
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
        // A jump starts the build fresh at that night, so no earlier night was lost to rain.
        upgrade.RainDelayed = false;
        upgrade.RainDelayedCycle = 0;
        state.Revision++;
        Mod.DebugClearFailure();
        Mod.Engine.RestoreState(state);
        Mod.Changed();
        Mod.Log("baseddiag: construction moved to " + phase + " at " + _site.Base.Id + "/" + upgradeId + "; revision " + state.Revision + ".");
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
