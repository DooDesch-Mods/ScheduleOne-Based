#if DEBUG
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Based.Content.Ming;
using HarmonyLib;
using Il2CppScheduleOne.Building;
using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.Tiles;
using MapExpansions;
using Newtonsoft.Json;
using S1API.Internal.Abstraction;
using S1API.Saveables;
using UnityEngine;
using Grid = Il2CppScheduleOne.Tiles.Grid;
using Property = Il2CppScheduleOne.Property.Property;

namespace Based.Runtime;

/// <summary>Creates native acceptance fixtures in the isolated Ming host world and records their saved identity.</summary>
public static class RoomProbe
{
    private const string PropertyCode = "sweatshop";
    private const string ExtensionId = "ming-wing-floor";
    private static string TilePrefix => $"MapExpansions.{MingDefinition.ExpansionId}.{ExtensionId}.Tile.";

    public static void Execute(IReadOnlyList<string> args)
    {
        string action = args.Count == 0 ? "inspect" : args[0].ToLowerInvariant();
        switch (action)
        {
            case "inspect": Inspect(); break;
            case "verify": Verify(); break;
            case "place": RequireHost(); Place(args); break;
            case "surface": RequireHost(); PlaceSurface(args); break;
            default: throw new ArgumentException("baseddiag room inspect | place <vanillaItemId> [tileX tileY rotation] | surface <vanillaItemId> [surfaceId] | verify");
        }
    }

    public static void ResetForMenu() => RoomProbeSave.BeginLoad();

    private static void RequireHost()
    {
        string location = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
        string[] allowed = {
            @"F:\Projects\Mods\Schedule1\_MPTest\Schedule I\Schedule I.exe",
            @"F:\Projects\Mods\Schedule1\_MPTest4\Schedule I\Schedule I.exe"
        };
        if (!NativeBus.IsHost || !allowed.Contains(location, StringComparer.OrdinalIgnoreCase) || LoadManager.Instance == null || !LoadManager.Instance.IsGameLoaded)
            throw new InvalidOperationException("Room fixture mutations require the loaded isolated _MPTest or _MPTest4 host.");
        foreach (var peer in Il2CppFishNet.InstanceFinder.ServerManager.Clients)
            if (peer.Value != null && !peer.Value.IsHost)
                throw new InvalidOperationException("Room fixture mutations require an isolated host without remote peers.");
        if (!MapExpansionsApi.IsWorldStateApplied(MingDefinition.ExpansionId) || MapExpansionsApi.GetWorldState(MingDefinition.ExpansionId) != "complete")
            throw new InvalidOperationException("Ming's completed world state must be applied before placing acceptance objects.");
        if (!string.IsNullOrEmpty(Mod.Failure)) throw new InvalidOperationException("Based has a retained runtime failure: " + Mod.Failure);
        if (RoomProbeSave.Instance == null) throw new InvalidOperationException("S1API has not registered RoomProbeSave.");
    }

    private static Property Property()
    {
        foreach (var property in Il2CppScheduleOne.Property.Property.Properties)
            if (property != null && property.PropertyCode == PropertyCode) return property;
        throw new InvalidOperationException("The existing Ming property is not loaded.");
    }

    private static Grid Grid()
    {
        var property = Property();
        if (property.Grids == null || property.Grids.Count == 0 || property.Grids[0] == null)
            throw new InvalidOperationException("Ming's original floor grid 0 is unavailable.");
        var grid = property.Grids[0];
        if (grid.GUID.ToString() != grid._guid || Il2Cpp.GUIDManager.GetObject<Grid>(grid.GUID)?.Pointer != grid.Pointer)
            throw new InvalidOperationException("Ming's original baked grid GUID or registry readback does not match.");
        return grid;
    }

    private static List<Tile> AddedTiles(Grid grid)
    {
        var result = new List<Tile>();
        foreach (var tile in grid.Tiles)
            if (tile != null && tile.name.StartsWith(TilePrefix, StringComparison.Ordinal)) result.Add(tile);
        return result.OrderBy(x => x.x).ThenBy(x => x.y).ToList();
    }

    private static void Inspect()
    {
        var property = Property();
        var grid = Grid();
        var added = AddedTiles(grid);
        var free = added.Where(t => t.BuildableOccupants.Count == 0 && t.OccupantTiles.Count == 0 && t.CanBeBuiltOn()).ToArray();
        Mod.Log($"room inspect property={property.PropertyCode} owned={property.IsOwned} gridIndex=0 originalBakedGUID={grid._guid} currentGUID={grid.GUID} totalTiles={grid.Tiles.Count} newTiles={added.Count} newFreeTiles={free.Length}");
        for (int start = 0; start < added.Count; start += 30)
            Mod.Log("room new tile coordinates=" + string.Join(" ", added.Skip(start).Take(30).Select(t => $"[{t.x},{t.y}]={(t.BuildableOccupants.Count == 0 && t.OccupantTiles.Count == 0 ? "free" : "occupied")}")));
        foreach (var pair in Surfaces())
            Mod.Log($"room surface id={pair.Id} guid={pair.Surface.GUID} type={pair.Surface.SurfaceType} parent={pair.Surface.ParentProperty.PropertyCode} collider={pair.Surface.GetComponent<Collider>() != null}");
        Mod.Log($"room evidence records={RoomProbeSave.Instance?.Data.Targets.Count ?? 0} loadedFromSave={RoomProbeSave.LoadedFromSave}; inspect does not place or save objects");
    }

    private static BuildableItemDefinition Definition(string id)
    {
        var definition = Il2CppScheduleOne.Registry.GetItem(id)?.TryCast<BuildableItemDefinition>();
        if (definition?.BuiltItem == null) throw new ArgumentException("No native buildable item definition exists for exact id '" + id + "'.");
        return definition;
    }

    private static BuildManager Begin(BuildableItemDefinition definition)
    {
        var manager = BuildManager.Instance ?? throw new InvalidOperationException("Native BuildManager is unavailable.");
        if (manager.isBuilding) throw new InvalidOperationException("Finish the player's current build action before running a room fixture.");
        manager.StartBuilding(definition.GetDefaultInstance(1));
        if (!manager.isBuilding || manager.currentBuildHandler == null) throw new InvalidOperationException("Native StartBuilding did not create a build handler.");
        return manager;
    }

    private static void Stop(BuildManager manager)
    {
        if (manager != null && manager.isBuilding) manager.StopBuilding();
    }

    private static bool ValidateGrid(BuildUpdate_Grid handler, Grid grid, int x, int y, int rotation, out string reason)
    {
        var ghost = handler.BuildableItemClass;
        var origin = grid.GetTile(new Coordinate(x, y));
        reason = "";
        if (ghost == null || origin == null) { reason = "ghost or origin tile is missing"; return false; }
        ghost.transform.rotation = grid.transform.rotation * (Quaternion.Inverse(ghost.BuildPoint.rotation) * ghost.transform.rotation);
        ghost.transform.Rotate(ghost.BuildPoint.up, rotation);
        ghost.transform.position = origin.transform.position - (ghost.OriginFootprint.transform.position - ghost.transform.position);
        Physics.SyncTransforms();
        var pairs = Coordinate.BuildCoordinateMatches(new Coordinate(x, y), ghost.FootprintX, ghost.FootprintY, rotation);
        foreach (var pair in pairs)
        {
            var tile = grid.GetTile(pair.coord2);
            var footprint = ghost.GetFootprintTile(pair.coord1);
            if (tile == null || !tile.name.StartsWith(TilePrefix, StringComparison.Ordinal))
            { reason = $"footprint [{pair.coord2.x},{pair.coord2.y}] is outside the new tiles"; return false; }
            if (tile.BuildableOccupants.Count > 0 || tile.OccupantTiles.Count > 0)
            { reason = $"footprint [{tile.x},{tile.y}] is occupied"; return false; }
            if (footprint == null || footprint.AreCornerObstaclesBlocked(tile) || !grid.IsTileValidAtCoordinate(pair.coord2, footprint, ghost))
            { reason = $"native footprint validation rejected [{tile.x},{tile.y}]"; return false; }
        }
        if (ghost.GetPenetration(out float px, out float pz, out float py) && Mathf.Max(Mathf.Abs(px), Mathf.Max(Mathf.Abs(pz), Mathf.Abs(py))) > .01f)
        { reason = $"native wall penetration x={px},z={pz},y={py}"; return false; }
        ghost.CalculateFootprintTileIntersections();
        handler.CheckIntersections();
        if (!handler._validPosition || handler.closestIntersection?.tile?.OwnerGrid?.Pointer != grid.Pointer)
        { reason = "actual native build handler did not validate this footprint on the original grid"; return false; }
        foreach (var pair in pairs)
        {
            var footprint = ghost.GetFootprintTile(pair.coord1);
            var actual = grid.GetMatchedCoordinate(footprint);
            if (actual.x != pair.coord2.x || actual.y != pair.coord2.y)
            { reason = "native ghost snapping changed the requested footprint coordinates"; return false; }
        }
        return true;
    }

    private static void Place(IReadOnlyList<string> args)
    {
        if (args.Count != 2 && args.Count != 5) throw new ArgumentException("room place <vanillaItemId> [tileX tileY rotation]");
        var definition = Definition(args[1]);
        var prefab = definition.BuiltItem.TryCast<GridItem>() ?? throw new ArgumentException("The requested item is not a native GridItem.");
        if (prefab.FootprintX * prefab.FootprintY < 2) throw new ArgumentException("This acceptance probe requires a multi-cell native machine footprint.");
        var grid = Grid();
        var evidence = RoomProbeSave.Instance!.Data;
        if (evidence.Targets.Any(t => t.Kind == "grid")) throw new InvalidOperationException("A machine probe record already exists; run room verify before taking another action.");
        var manager = Begin(definition);
        try
        {
            var handler = manager.currentBuildHandler.GetComponent<BuildUpdate_Grid>() ?? throw new InvalidOperationException("Native StartBuilding created no grid build handler.");
            var candidates = args.Count == 5
                ? new[] { (X: int.Parse(args[2], CultureInfo.InvariantCulture), Y: int.Parse(args[3], CultureInfo.InvariantCulture), Rotation: int.Parse(args[4], CultureInfo.InvariantCulture)) }
                : AddedTiles(grid).SelectMany(t => new[] { 0, 90, 180, 270 }.Select(r => (X: t.x, Y: t.y, Rotation: r))).ToArray();
            string reason = "no added floor tiles exist";
            foreach (var candidate in candidates)
            {
                if (candidate.Rotation is not (0 or 90 or 180 or 270)) throw new ArgumentException("Rotation must be 0, 90, 180 or 270.");
                if (!ValidateGrid(handler, grid, candidate.X, candidate.Y, candidate.Rotation, out reason)) continue;
                var record = new RoomProbeTarget { Kind = "grid", ItemId = args[1], TargetGuid = Il2Cpp.GUIDManager.GenerateUniqueGUID().ToString(), GridGuid = grid.GUID.ToString(), X = candidate.X, Y = candidate.Y, Rotation = candidate.Rotation };
                evidence.Targets.Add(record);
                var item = manager.CreateGridItem(definition.GetDefaultInstance(1), grid, new Vector2(candidate.X, candidate.Y), candidate.Rotation, record.TargetGuid);
                ConfirmNative(item, record);
                Capture(record, item);
                Mod.Log($"room placed native machine item={record.ItemId} guid={record.TargetGuid} grid={record.GridGuid} origin=[{record.X},{record.Y}] rotation={record.Rotation} footprint={prefab.FootprintX}x{prefab.FootprintY}; production interaction and restart verification remain pending");
                return;
            }
            throw new InvalidOperationException("No fully free new-region footprint passed the native build handler: " + reason);
        }
        finally { Stop(manager); }
    }

    private static IEnumerable<(string Id, Surface Surface)> Surfaces()
    {
        var definition = MingWorld.Create().WorldStates.Single(x => x.Id == "complete");
        foreach (var extension in definition.PropertyExtensions)
        foreach (var description in extension.Surfaces)
        {
            var guid = new Il2CppSystem.Guid(new Guid(MD5.HashData(Encoding.UTF8.GetBytes($"mapexpansions/{MingDefinition.ExpansionId}/surface/{extension.Id}/{description.Id}"))).ToString());
            var surface = Il2Cpp.GUIDManager.GetObject<Surface>(guid);
            if (surface != null && surface.ParentProperty?.PropertyCode == PropertyCode) yield return (description.Id, surface);
        }
    }

    private static Vector3 Face(Surface.EFace face) => face switch
    {
        Surface.EFace.Front => Vector3.forward, Surface.EFace.Back => Vector3.back,
        Surface.EFace.Top => Vector3.up, Surface.EFace.Bottom => Vector3.down,
        Surface.EFace.Left => Vector3.left, Surface.EFace.Right => Vector3.right,
        _ => throw new ArgumentOutOfRangeException(nameof(face))
    };

    private static bool OverlapsPlacedItem(BuildableItem ghost)
    {
        var box = ghost.BoundingCollider;
        var extents = Vector3.Scale(box.size * .48f, box.transform.lossyScale);
        foreach (var hit in Physics.OverlapBox(box.transform.TransformPoint(box.center), extents, box.transform.rotation, ~0, QueryTriggerInteraction.Ignore))
        {
            var item = hit.GetComponentInParent<BuildableItem>();
            if (item != null && item.Pointer != ghost.Pointer && !item.isGhost && !item.IsDestroyed) return true;
        }
        return false;
    }

    private static void PlaceSurface(IReadOnlyList<string> args)
    {
        if (args.Count is < 2 or > 3) throw new ArgumentException("room surface <vanillaItemId> [surfaceId]");
        var definition = Definition(args[1]);
        if (definition.BuiltItem.TryCast<SurfaceItem>() == null) throw new ArgumentException("The requested item is not a native SurfaceItem.");
        var evidence = RoomProbeSave.Instance!.Data;
        if (evidence.Targets.Any(t => t.Kind == "surface")) throw new InvalidOperationException("A wall probe record already exists; run room verify before taking another action.");
        var manager = Begin(definition);
        try
        {
            var handler = manager.currentBuildHandler.GetComponent<BuildUpdate_Surface>() ?? throw new InvalidOperationException("Native StartBuilding created no surface build handler.");
            var ghost = handler.BuildableItemClass;
            foreach (var pair in Surfaces())
            {
                if (args.Count == 3 && pair.Id != args[2]) continue;
                var surface = pair.Surface;
                var collider = surface.GetComponent<BoxCollider>();
                if (collider == null || surface.SurfaceType != Surface.ESurfaceType.Wall) continue;
                var faces = new List<Vector3>();
                foreach (var face in surface.ValidFaces) faces.Add(Face(face));
                foreach (var face in faces.OrderByDescending(f => Vector3.Dot(surface.transform.TransformDirection(f), MingWorld.Wing.center - collider.bounds.center)))
                {
                    Vector3 point = collider.transform.TransformPoint(collider.center + Vector3.Scale(face, collider.size * .5f));
                    if (!handler.IsSurfaceValidForItem(surface, collider, point)) continue;
                    Vector3 normal = collider.transform.TransformDirection(face).normalized;
                    ghost.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    ghost.transform.rotation = Quaternion.LookRotation(normal, Vector3.up) * Quaternion.Inverse(ghost.BuildPoint.rotation) * ghost.transform.rotation;
                    ghost.transform.position += point - ghost.BuildPoint.position;
                    Physics.SyncTransforms();
                    if (ghost.GetPenetration(out float px, out float pz, out float py))
                    {
                        ghost.transform.position -= ghost.transform.right * px + ghost.transform.forward * pz + ghost.transform.up * py;
                        Physics.SyncTransforms();
                    }
                    if (ghost.GetPenetration(out px, out pz, out py) && Mathf.Max(Mathf.Abs(px), Mathf.Max(Mathf.Abs(pz), Mathf.Abs(py))) > .01f) continue;
                    if (!surface.ParentProperty.DoBoundsContainPoint(ghost.BoundingCollider.bounds.center) || OverlapsPlacedItem(ghost)) continue;
                    var record = new RoomProbeTarget { Kind = "surface", ItemId = args[1], TargetGuid = Il2Cpp.GUIDManager.GenerateUniqueGUID().ToString(), SurfaceGuid = surface.GUID.ToString(), SurfaceId = pair.Id };
                    evidence.Targets.Add(record);
                    var item = manager.CreateSurfaceItem(definition.GetDefaultInstance(1), surface, surface.GetRelativePosition(ghost.transform.position), surface.GetRelativeRotation(ghost.transform.rotation), record.TargetGuid);
                    ConfirmNative(item, record);
                    Capture(record, item);
                    Mod.Log($"room placed native wall item={record.ItemId} guid={record.TargetGuid} surface={record.SurfaceId} surfaceGUID={record.SurfaceGuid}; restart verification remains pending");
                    return;
                }
            }
            throw new InvalidOperationException("No added wall surface passed native type/face/ownership and free-space checks.");
        }
        finally { Stop(manager); }
    }

    private static void ConfirmNative(BuildableItem? item, RoomProbeTarget record)
    {
        if (item == null || item.IsDestroyed || !item.Initialized || item.GUID.ToString() != record.TargetGuid || item.ItemInstance?.ID != record.ItemId ||
            item.ParentProperty?.PropertyCode != PropertyCode || item.NetworkObject == null || !item.NetworkObject.IsSpawned ||
            Il2Cpp.GUIDManager.GetObject<BuildableItem>(item.GUID)?.Pointer != item.Pointer)
            throw new InvalidOperationException($"Native item creation/readback failed for {record.TargetGuid}; evidence retained, do not replay placement.");
        if (record.Kind == "grid")
        {
            var placed = item.TryCast<GridItem>();
            if (placed == null || placed.OwnerGrid?.GUID.ToString() != record.GridGuid || placed._originCoordinate.x != record.X || placed._originCoordinate.y != record.Y || placed._rotation != record.Rotation)
                throw new InvalidOperationException("Native machine changed its original grid, origin coordinate or rotation.");
            foreach (var coordinate in placed.CoordinatePairs)
            {
                var tile = placed.OwnerGrid.GetTile(coordinate.coord2);
                bool occupied = false;
                if (tile != null) foreach (var occupant in tile.BuildableOccupants) if (occupant?.Pointer == item.Pointer) occupied = true;
                if (!occupied) throw new InvalidOperationException("Native machine footprint is not registered on every saved grid tile.");
            }
        }
        else if (item.TryCast<SurfaceItem>()?.ParentSurface?.GUID.ToString() != record.SurfaceGuid)
            throw new InvalidOperationException("Native wall item changed its saved parent surface.");
        record.CreationConfirmed = true;
    }

    private static (string Json, int Quantity) Inventory(BuildableItem item)
    {
        var slots = new List<object>();
        int quantity = 0;
        foreach (var component in item.GetComponentsInChildren<Component>(true))
        {
            var owner = component.TryCast<IItemSlotOwner>();
            if (owner?.ItemSlots == null) continue;
            for (int i = 0; i < owner.ItemSlots.Count; i++)
            {
                var stored = owner.ItemSlots[i]?.ItemInstance;
                quantity += stored?.Quantity ?? 0;
                slots.Add(new { Component = component.GetIl2CppType().FullName, Object = component.name, Slot = i, Item = stored?.GetItemData().GetJson(false) ?? "" });
            }
        }
        return (JsonConvert.SerializeObject(slots), quantity);
    }

    private static BuildableItem Find(RoomProbeTarget record)
    {
        var item = Il2Cpp.GUIDManager.GetObject<BuildableItem>(new Il2CppSystem.Guid(record.TargetGuid));
        ConfirmNative(item, record);
        return item;
    }

    private static void Capture(RoomProbeTarget record, BuildableItem item)
    {
        var inventory = Inventory(item);
        record.InventoryJson = inventory.Json;
        record.InventoryQuantity = inventory.Quantity;
        record.NativeDataSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.GetSaveData().GetJson(false))));
        record.Position = new[] { item.transform.position.x, item.transform.position.y, item.transform.position.z };
    }

    internal static void BeforeSave()
    {
        var save = RoomProbeSave.Instance;
        if (save == null || save.Data.Targets.Count == 0) return;
        RequireHost();
        foreach (var record in save.Data.Targets) Capture(record, Find(record));
        save.Data.CaptureProcessId = Environment.ProcessId;
        save.Data.SaveCaptures++;
        Mod.Log($"room evidence captured before native save targets={save.Data.Targets.Count} capture={save.Data.SaveCaptures}; native save completion remains separate");
    }

    private static void Verify()
    {
        var save = RoomProbeSave.Instance ?? throw new InvalidOperationException("RoomProbeSave is not registered.");
        var data = save.Data;
        if (data.Targets.Count == 0) throw new InvalidOperationException("No room acceptance objects have been recorded.");
        bool inventoryPresent = false;
        foreach (var record in data.Targets)
        {
            var item = Find(record);
            var inventory = Inventory(item);
            if (record.Position.Length != 3 || Vector3.Distance(item.transform.position, new Vector3(record.Position[0], record.Position[1], record.Position[2])) > .03f)
                throw new InvalidOperationException("Native object position differs from recorded evidence: " + record.TargetGuid);
            bool matches = inventory.Json == record.InventoryJson;
            if (RoomProbeSave.LoadedFromSave && !matches) throw new InvalidOperationException("Native slot inventory differs from the saved probe baseline for " + record.TargetGuid);
            inventoryPresent |= record.InventoryQuantity > 0;
            Mod.Log($"room verify kind={record.Kind} item={record.ItemId} guid={record.TargetGuid} originalGrid={record.GridGuid} surface={record.SurfaceGuid} nativeInitialized=true nativeSpawned=true inventoryQuantity={inventory.Quantity} savedInventoryQuantity={record.InventoryQuantity} inventoryMatch={matches}");
        }
        bool restarted = RoomProbeSave.LoadedFromSave && data.CaptureProcessId != Environment.ProcessId;
        Mod.Log($"room verify targets={data.Targets.Count} loadedFromSave={RoomProbeSave.LoadedFromSave} nativeSaveCaptures={data.SaveCaptures} differentProcess={restarted} nonEmptySavedInventory={inventoryPresent}");
        if (!RoomProbeSave.LoadedFromSave || data.SaveCaptures == 0) Mod.Warn("Room readback is current-session evidence; save/reload has not been proved.");
        if (!inventoryPresent) Mod.Warn("Saved inventory is empty; non-empty inventory persistence has not been proved.");
        if (!data.Targets.Any(x => x.Kind == "surface")) Mod.Warn("No wall object has been recorded; wall-object persistence has not been proved.");
    }
}

public sealed class RoomProbeSave : Saveable
{
    internal static RoomProbeSave? Instance { get; private set; }
    internal static bool LoadedFromSave { get; private set; }
    [SaveableField("room_probe")] private RoomProbeState? _state = new();
    internal RoomProbeState Data => _state ?? throw new InvalidOperationException("DEBUG room-probe save data is null.");
    public override SaveableLoadOrder LoadOrder => SaveableLoadOrder.BeforeBaseGame;
    public RoomProbeSave() { Instance = this; }
    internal static void BeginLoad()
    {
        LoadedFromSave = false;
        if (Instance != null) Instance._state = new RoomProbeState();
    }
    protected override void OnLoaded()
    {
        Instance = this;
        LoadedFromSave = true;
        if (_state == null || _state.Schema != 1) Mod.Warn("DEBUG room-probe evidence is null or has an unsupported schema; verification will fail.");
        else Mod.Log($"DEBUG room-probe evidence loaded before vanilla objects: {_state.Targets.Count} targets; actual native restoration remains unverified until room verify.");
    }
    protected override void OnCreated() { Instance = this; _state = new RoomProbeState(); LoadedFromSave = false; }
    protected override void OnSaved() => Mod.Log($"DEBUG room-probe metadata saved through S1API: {Data.Targets.Count} targets.");
}

public sealed class RoomProbeState
{
    public int Schema { get; set; } = 1;
    public int SaveCaptures { get; set; }
    public int CaptureProcessId { get; set; }
    public List<RoomProbeTarget> Targets { get; set; } = new();
}

public sealed class RoomProbeTarget
{
    public string Kind { get; set; } = "";
    public string ItemId { get; set; } = "";
    public string TargetGuid { get; set; } = "";
    public string GridGuid { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Rotation { get; set; }
    public string SurfaceGuid { get; set; } = "";
    public string SurfaceId { get; set; } = "";
    public bool CreationConfirmed { get; set; }
    public string InventoryJson { get; set; } = "";
    public int InventoryQuantity { get; set; }
    public string NativeDataSha256 { get; set; } = "";
    public float[] Position { get; set; } = Array.Empty<float>();
}

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.Save), new[] { typeof(string) })]
internal static class RoomProbeSaveCapture
{
    private static bool Prefix()
    {
        try { RoomProbe.BeforeSave(); return true; }
        catch (Exception error)
        {
            Mod.Warn("Room acceptance save blocked because native fixture readback failed: " + error);
            return false;
        }
    }
}
#endif
