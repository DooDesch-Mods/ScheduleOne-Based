#if DEBUG
using System;
using Based.Core;
using Il2CppScheduleOne;
using Il2CppScheduleOne.Building;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.PlayerScripts;
using UnityEngine;

namespace Based.Runtime;

// The game's own placement ghost, read from outside: an item is put in the hand as a player would, the camera is aimed
// by the test script, and "read" reports what the build handler decided in its last frame. The ghost is red exactly
// when that handler's valid flag is false, so a test of "can a player place here" reads that flag.
internal static class GhostProbe
{
    internal static void Handle(System.Collections.Generic.List<string> args)
    {
        string what = args.Count > 1 ? args[1] : "read";
        switch (what)
        {
            case "read": Read(); break;
            case "drop": Drop(); break;
            case "hush": Hush(); break;
            default: Hold(what); break;
        }
    }

    // Gives one of the item if none is carried and selects its hotbar slot; equipping a buildable starts the ghost.
    private static void Hold(string itemId)
    {
        var definition = Registry.GetItem(itemId) ?? throw new ArgumentException("Unknown item: " + itemId);
        var inventory = PlayerSingleton<PlayerInventory>.Instance ?? throw new InvalidOperationException("No player inventory.");
        if (inventory.GetAmountOfItem(itemId) == 0)
        {
            inventory.AddItemToInventory(definition.GetDefaultInstance(1));
            Mod.Log("baseddiag ghost: gave one " + itemId + ".");
        }
        for (int i = 0; i < inventory.hotbarSlots.Count; i++)
        {
            var slot = inventory.hotbarSlots[i];
            if (slot?.ItemInstance == null || !string.Equals(slot.ItemInstance.ID, itemId, StringComparison.OrdinalIgnoreCase)) continue;
            ProjectFolder.EquipOnly(inventory, slot);
            Mod.Log("baseddiag ghost: holding " + itemId + " from hotbar " + (i + 1) + ".");
            return;
        }
        throw new InvalidOperationException(itemId + " is not on the hotbar; free a slot first.");
    }

    private static void Drop()
    {
        Mod.Log("baseddiag ghost: hands free on hotbar " + ProjectFolder.DebugHold(Mod.Sites[0], empty: true) + ".");
    }

    // A customer who walks up opens a dialogue over the view; shots from the player's eye need it closed.
    private static void Hush()
    {
        var canvas = Singleton<Il2CppScheduleOne.UI.DialogueCanvas>.Instance;
        var handler = canvas != null ? canvas.currentHandler : null;
        if (handler == null) { Mod.Log("baseddiag ghost: no dialogue open."); return; }
        handler.EndDialogue();
        Mod.Log("baseddiag ghost: dialogue with " + handler.name + " ended.");
    }

    private static void Read()
    {
        var manager = NetworkSingleton<BuildManager>.Instance;
        var handler = manager != null ? manager.currentBuildHandler : null;
        if (handler == null) { Mod.Warn("baseddiag ghost: no build handler; nothing buildable in hand."); return; }
        var update = handler.GetComponent<BuildUpdate_Grid>();
        if (update == null) { Mod.Warn("baseddiag ghost: the build handler '" + handler.name + "' is not a grid placement."); return; }
        var camera = PlayerSingleton<PlayerCamera>.Instance.transform;
        var ghost = update.GhostModel != null ? update.GhostModel.transform.position : Vector3.zero;
        var item = update.BuildableItemClass;
        var hit = update.closestIntersection;
        string tile = "none";
        if (hit != null && hit.tile != null)
            tile = $"grid '{hit.tile.OwnerGrid?.name}' tile {hit.tile.x},{hit.tile.y}";
        Mod.Log($"baseddiag ghost: valid {update._validPosition}, ghost at {F(ghost)}, camera {F(camera.position)} looking {F(camera.forward)}, nearest {tile}.");
        Surfaces(update, camera);
        if (item == null || hit == null || hit.tile == null) return;
        var grid = hit.tile.OwnerGrid;
        for (int k = 0; k < item.CoordinateFootprintTilePairs.Count; k++)
        {
            var footprint = item.CoordinateFootprintTilePairs[k].footprintTile;
            var coordinate = grid.GetMatchedCoordinate(footprint);
            var target = grid.GetTile(coordinate);
            string why = target == null ? "no tile"
                : footprint.AreCornerObstaclesBlocked(target) ? "corner obstacle"
                : grid.IsTileValidAtCoordinate(coordinate, footprint, item) ? "free"
                : target.BuildableOccupants.Count > 0 ? "occupied by " + target.BuildableOccupants[0].name
                : "tile refuses (offset " + target.AvailableOffset.ToString("0.00") + ")";
            Mod.Log($"baseddiag ghost: footprint {k} on {coordinate.x},{coordinate.y}: {why}.");
        }
    }

    // The handler lands the ghost on what its own two rays hit (look ray, then straight down from the ghost) with its
    // detection mask, skipping anything under a BuildableItem. Listing those hits shows why a floor is not taken.
    private static void Surfaces(BuildUpdate_Grid update, Transform camera)
    {
        int mask = update.detectionMask.value;
        var layers = new System.Collections.Generic.List<string>();
        for (int i = 0; i < 32; i++)
            if ((mask & (1 << i)) != 0) layers.Add(LayerMask.LayerToName(i));
        Mod.Log("baseddiag ghost: detection layers " + string.Join(",", layers) + ".");
        var item = update.BuildableItemClass;
        Cast("look", camera.position, camera.forward, 10f, mask);
        if (item != null) Cast("down", item.transform.position + Vector3.up * .1f, Vector3.down, 3f, mask);
    }

    private static void Cast(string label, Vector3 origin, Vector3 direction, float range, int mask)
    {
        var hits = Physics.RaycastAll(origin, direction, range, mask, QueryTriggerInteraction.Ignore);
        if (hits.Length == 0) { Mod.Log($"baseddiag ghost: {label} ray from {F(origin)} hits nothing within {range} m."); return; }
        foreach (var h in hits)
        {
            var c = h.collider;
            bool buildable = c.GetComponentInParent<Il2CppScheduleOne.EntityFramework.BuildableItem>() != null;
            Mod.Log($"baseddiag ghost: {label} hit '{c.name}' layer {LayerMask.LayerToName(c.gameObject.layer)} at {F(h.point)} after {h.distance:0.00} m, normal {F(h.normal)}{(buildable ? ", skipped as buildable" : "")}.");
        }
    }

    private static string F(Vector3 v) => $"({v.x:0.00},{v.y:0.00},{v.z:0.00})";
}
#endif
