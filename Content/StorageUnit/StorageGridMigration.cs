using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using Il2CppScheduleOne.Persistence.Datas;
using Il2CppScheduleOne.Persistence.Loaders;
using MapExpansions;
using UnityEngine;
using Property = Il2CppScheduleOne.Property.Property;

namespace Based.Content.StorageUnit;

// Up to Based 1.2.2 the loft, the east room and the south room had a grid each, and vanilla cannot place an item across
// two grids. From 1.3 they are one grid (Based#88). The east room's grid became that grid with its GUID and origin, so
// what stands there loads as it is. What a save holds on the loft's or the south room's old grid is moved onto it here,
// before vanilla looks the grid up, with its origin tile shifted by the offset between the grids' origins. Once saved,
// the item is on the upper grid for good.
//
// S1API has no hook for grid items while they load (requested in ifBars/S1API#422); this patch stands in for one. It
// touches only items whose saved grid is one of the two old grids.
[HarmonyPatch(typeof(GridItemLoader), nameof(GridItemLoader.LoadAndCreate), new[] { typeof(GridItemData) })]
internal static class StorageGridMigration
{
    internal static readonly Guid UpperGrid = StableGridGuid(StorageWorld.UpperGridExtensionId);
    private static readonly Dictionary<Guid, (string name, Vector2 shift)> OldGrids = new()
    {
        [StableGridGuid(StorageWorld.OldLoftGridId)] = ("loft", new Vector2(StorageWorld.LoftShiftX, 0f)),
        [StableGridGuid(StorageWorld.OldSouthGridId)] = ("south room", new Vector2(0f, StorageWorld.SouthShiftY)),
    };

    // MapExpansions gives an extension's own grid this GUID (Places.StableGuid); saves hold it, so it never changes.
    private static Guid StableGridGuid(string extensionId)
    {
        using var md5 = MD5.Create();
        return new Guid(md5.ComputeHash(Encoding.UTF8.GetBytes($"mapexpansions/{StorageDefinition.ExpansionId}/grid/{extensionId}")));
    }

    private static void Prefix(GridItemData data)
    {
        // Vanilla returns null for a missing record itself, and every other item's grid is not ours to move.
        if (data == null || !Guid.TryParse(data.GridGUID, out var grid) || !OldGrids.TryGetValue(grid, out var old)) return;
        try
        {
            var from = data.OriginCoordinate;
            var to = from + old.shift;
            data.GridGUID = UpperGrid.ToString();
            data.OriginCoordinate = to;
            Mod.Log($"Storage Unit: grid item {data.GUID} moved from the {old.name}'s old grid tile {from} onto the upper floor's grid tile {to}.");
        }
        catch (Exception e)
        {
            Mod.Error($"Storage Unit: grid item {data.GUID} on the {old.name}'s old grid could not be moved onto the upper floor's grid, so it does not load: {e}");
        }
    }

    // The east and south rooms put their tiles on the property's grid StorageWorld.UpperGridIndex; this reports when that
    // is not the upper grid, for instance because another mod built a grid on the Storage Unit first.
    internal static void CheckUpperGridIndex(string expansion, string state)
    {
        if (expansion != StorageDefinition.ExpansionId) return;
        int level = Array.IndexOf(StorageWorld.States, state) + 1;
        if (level < 3) return;
        Property? property = null;
        foreach (var candidate in Property.Properties)
            if (candidate != null && candidate.PropertyCode == "storageunit") { property = candidate; break; }
        if (property == null)
        {
            Mod.Error($"Storage Unit: state '{state}' applied, but the property is not in the scene; the upper grid is not checked.");
            return;
        }
        var grids = property.Grids;
        var at = grids != null && grids.Count > StorageWorld.UpperGridIndex ? grids[StorageWorld.UpperGridIndex] : null;
        string found = at == null ? "none" : at.GUID.ToString();
        if (Guid.TryParse(found, out var guid) && guid == UpperGrid)
            Mod.Log($"Storage Unit: state '{state}': the upper floor's grid is grid {StorageWorld.UpperGridIndex} of the property, GUID {UpperGrid}.");
        else
            Mod.Error($"Storage Unit: state '{state}': grid {StorageWorld.UpperGridIndex} of the property is {found}, not the upper floor's grid {UpperGrid}; the rooms' tiles lie on the wrong grid.");
    }
}
