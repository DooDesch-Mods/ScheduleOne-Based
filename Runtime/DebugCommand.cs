#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using Based.Core;
using Based.UI;
using Based.Content.Ming;
using S1API.Console;
using UnityEngine;

namespace Based.Runtime;

public sealed class BasedDebugCommand : BaseConsoleCommand
{
    public override string CommandWord => "baseddiag";
    public override string CommandDescription => "Based construction state and isolated runtime diagnostics.";
    public override string ExampleUsage => "baseddiag status | plan | dock | room | workers | hammer [seconds|set <time>] | network | packdoorway | phase <id> | hold <slot> | stayers | walkers | advance | lights | lightset <what> <value> | journal [off] | walk <x> <z> [<x> <z> ...]";

    public override void ExecuteCommand(List<string> args)
    {
        string action = args.Count == 0 ? "status" : args[0];
        try
        {
            switch (action)
            {
                case "status":
                    Mod.Log("baseddiag: " + BuildPlan.State(Mod.Sites[0]));
                    break;
                case "plan":
                    Mod.DebugPlan(Mod.Sites[0]);
                    Mod.Log("baseddiag: plan opened through the vendor conversation.");
                    break;
                case "blockers":
                    Mod.DebugBlockers(Mod.Sites[0]);
                    break;
                case "planfree":
                    Mod.DebugFreeConversation = args.Count > 1 && args[1] == "on";
                    Mod.Log("baseddiag: conversation hold under the plan " + (Mod.DebugFreeConversation ? "off" : "on") + ".");
                    break;
                case "folder":
                    Folder(args.Count > 1 ? args[1] : "state");
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
                    RoomProbe.Execute(args.Skip(1).ToArray());
                    break;
                case "workers":
                    MingWorkers.Diagnostics();
                    break;
                case "network":
                    NetworkProbe.Execute();
                    break;
                case "packdoorway":
                    if (args.Count > 1 && args[1] == "one") PackOneBlocker();
                    else PackTestDoorway();
                    break;
                case "phase":
                    JumpToPhase(args.Count > 1 ? args[1] : "");
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

    // Stands in for the clicks a test cannot make: read the folder, hang it where the aim is, or take one.
    private static void Folder(string action)
    {
        switch (action)
        {
            case "give": Mod.Log("baseddiag: folder given, answer '" + ProjectFolder.Give() + "'."); break;
            case "open": ProjectFolder.DebugOpen(); break;
            case "hang": ProjectFolder.DebugHang(); break;
            case "state": Mod.Log("baseddiag: folder " + ProjectFolder.DebugState()); break;
            default: throw new ArgumentException("baseddiag folder give|open|hang|state");
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
        string title = Text.T(Mod.Sites[0].Title);
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
        var site = new Vector3(-58f, 2f, 140f);
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
            MingWorkers.HammerContactNormalizedTime = float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
            Mod.Log("baseddiag: hammer contact time " + MingWorkers.HammerContactNormalizedTime + ".");
            return;
        }
        float seconds = args.Count > 1 ? float.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 10f;
        MingWorkers.ProbeSwing(seconds);
        Mod.Log("baseddiag: measuring the hammer swing for " + seconds + " s.");
    }

    // Moves the upgrade straight to a stage, so one run can look at every construction state.
    private static void JumpToPhase(string phase)
    {
        if (!NativeBus.IsHost) throw new InvalidOperationException("Only the host can change the construction state.");
        var state = Mod.Engine.ExportState();
        var upgrade = state.Upgrades.First(u => u.BaseId == MingDefinition.BaseId && u.UpgradeId == MingDefinition.UpgradeId);
        var (stage, cycles) = phase switch
        {
            "none" => (UpgradeStage.RightsMissing, 0),
            "rights" => (UpgradeStage.RightsOwned, 0),
            "ordered" => (UpgradeStage.Constructing, 0),
            "breakthrough" => (UpgradeStage.Constructing, 1),
            "frame" => (UpgradeStage.Constructing, 2),
            "roof" => (UpgradeStage.Constructing, 3),
            "complete" => (UpgradeStage.Complete, 4),
            _ => throw new ArgumentException("baseddiag phase none|rights|ordered|breakthrough|frame|roof|complete")
        };
        upgrade.Stage = stage;
        upgrade.CompletedCycles = cycles;
        if (stage == UpgradeStage.RightsMissing) upgrade.RightsPaid = 0;
        if (stage < UpgradeStage.Constructing) upgrade.ConstructionPaid = 0;
        state.Revision++;
        Mod.DebugClearFailure();
        Mod.Engine.RestoreState(state);
        Mod.Changed();
        Mod.Log("baseddiag: construction moved to " + phase + "; revision " + state.Revision + ".");
    }

    // Clears the walls item by item, as a player does, so the hold's list can be seen shrinking.
    private static void PackOneBlocker()
    {
        foreach (var item in Mod.BlockingItems(Mod.Sites[0]))
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
            foreach (var item in Mod.BlockingItems(Mod.Sites[0]))
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
        foreach (var item in Mod.BlockingItems(Mod.Sites[0]))
        {
            item.CanBePickedUp(out string reason);
            Mod.Warn("baseddiag: destroyed " + item.GetManagementName() + " in the test save, pickup refused: " + reason);
            item.Destroy_Server();
        }
        if (Mod.BlockingItems(Mod.Sites[0]).Length > 0) throw new InvalidOperationException("The marked walls are still blocked after packing.");
        Mod.Log("baseddiag: marked walls are clear.");
    }
}
#endif
