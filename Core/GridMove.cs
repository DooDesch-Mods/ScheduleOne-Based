using System.Globalization;
using System.Text.Json.Nodes;

namespace Based.Core;

// Moves a saved grid item from a grid that no longer exists onto the grid that took over its tiles, in the item's saved
// JSON (the BaseData of a DynamicSaveData). Every vanilla loader reads its item from that string, often more than once
// (MushroomBedLoader and SpawnStationLoader parse it as GridItemData and again as their own data type), so the move is
// made there and holds for each of them.
public static class GridMove
{
    public readonly record struct OldGrid(string Name, float ShiftX, float ShiftY);

    public sealed record Moved(string Json, string ItemGuid, string OldGridName, float FromX, float FromY, float ToX, float ToY);

    // Null when the JSON holds no grid item on one of the old grids. Throws when it does, but its tile cannot be read.
    public static Moved? Rewrite(string baseData, Guid newGrid, IReadOnlyDictionary<Guid, OldGrid> oldGrids)
    {
        if (string.IsNullOrEmpty(baseData)) return null;
        if (JsonNode.Parse(baseData) is not JsonObject item) return null;
        if (item["GridGUID"] is not JsonValue gridValue || !gridValue.TryGetValue(out string? gridText)) return null;
        if (!Guid.TryParse(gridText, out var grid) || !oldGrids.TryGetValue(grid, out var old)) return null;

        if (item["OriginCoordinate"] is not JsonObject origin)
            throw new FormatException("the item on the " + old.Name + "'s old grid has no OriginCoordinate");
        float fromX = Number(origin, "x", old.Name), fromY = Number(origin, "y", old.Name);
        float toX = fromX + old.ShiftX, toY = fromY + old.ShiftY;
        item["GridGUID"] = newGrid.ToString();
        origin["x"] = toX;
        origin["y"] = toY;
        string itemGuid = item["GUID"] is JsonValue g && g.TryGetValue(out string? text) ? text ?? "?" : "?";
        return new Moved(item.ToJsonString(), itemGuid, old.Name, fromX, fromY, toX, toY);
    }

    private static float Number(JsonObject origin, string axis, string grid)
    {
        if (origin[axis] is JsonValue value)
        {
            if (value.TryGetValue(out double d)) return (float)d;
            if (value.TryGetValue(out string? s) && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) return f;
        }
        throw new FormatException($"the item on the {grid}'s old grid has no readable OriginCoordinate.{axis}");
    }
}
