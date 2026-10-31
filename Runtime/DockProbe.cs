#if DEBUG
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne.Delivery;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.UI.Shop;
using Il2CppScheduleOne.Vehicles;
using MapExpansions;
using UnityEngine;
using Property = Il2CppScheduleOne.Property.Property;

namespace Based.Runtime;

public static class DockProbe
{
    public const string ExpansionId = "based_debug_delivery_dock";
    private const string StateId = "enabled";
    private const string ExtensionId = "isolated-dock-upgrade";
    private const string DockId = "extra-delivery-dock";
    private static PropertyExtensionDefinition _extension = null!;
    private static PropertyDockDefinition _dock = null!;
    private static bool _registered;
    private static (IntPtr Pointer, string Guid)[] _original = Array.Empty<(IntPtr, string)>();
    private static string _deliveryId = "";
    private static string _deliveryItem = "";
    private static DeliveryInstance? _observed;
    public static bool Active => MapExpansionsApi.GetWorldState(ExpansionId) == StateId;

    public static void Register()
    {
        if (_registered) return;
        _extension = new PropertyExtensionDefinition { Id = ExtensionId, PropertyCode = "bungalow" };
        _dock = new PropertyDockDefinition { Id = DockId, Index = 0, TemplatePropertyCode = "bungalow", TemplateDockIndex = 0 };
        _extension.Docks.Add(_dock);
        var state = new WorldStateDefinition { Id = StateId };
        state.PropertyExtensions.Add(_extension);
        var definition = new ExpansionDefinition { Id = ExpansionId, DisplayName = "Based isolated delivery-dock diagnostic", Version = "0.1.0", Source = null };
        definition.WorldStates.Add(state);
        if (!MapExpansionsApi.Register(definition)) throw new InvalidOperationException("The Based dock diagnostic definition could not be registered.");
        _registered = true;
    }

    public static void Execute(IReadOnlyList<string> args)
    {
        string action = args.Count == 0 ? "inspect" : args[0].ToLowerInvariant();
        if (action == "inspect") { Inspect(args.Count > 1 ? args[1] : null); return; }
        if (action == "status") { Status(); return; }
        if (action == "candidate") { Candidate(args); return; }
        RequireHost();
        switch (action)
        {
            case "enable": Enable(args); break;
            case "disable": Disable(); break;
            case "deliver": Deliver(args); break;
            default: throw new ArgumentException("baseddiag dock inspect [property] | candidate/enable <property> <sourceDockIndex> <localDx> <dy> <localDz> | status | disable | deliver <shopIndex> <itemId>");
        }
    }

    public static void ResetForMenu()
    {
        _original = Array.Empty<(IntPtr, string)>();
        _deliveryId = _deliveryItem = "";
        _observed = null;
    }

    private static void RequireHost()
    {
        if (!NativeBus.IsHost || LoadManager.Instance == null || !LoadManager.Instance.IsGameLoaded)
            throw new InvalidOperationException("Dock mutations require the loaded isolated host world.");
        foreach (var pair in Il2CppFishNet.InstanceFinder.NetworkManager.ServerManager.Clients)
            if (pair.Value != null && pair.Value.Authenticated && !pair.Value.IsLocalClient)
                throw new InvalidOperationException("This temporary fixture needs an isolated host without remote peers; it is outside the production world-state snapshot.");
    }

    private static Property Find(string code)
    {
        foreach (var property in Property.Properties)
            if (property != null && property.PropertyCode == code) return property;
        throw new InvalidOperationException("Property is not loaded: " + code);
    }

    private static string V(Vector3 value) => string.Create(CultureInfo.InvariantCulture, $"({value.x:F3},{value.y:F3},{value.z:F3})");
    private static void Inspect(string? code)
    {
        foreach (var property in Property.Properties)
        {
            if (property == null || code != null && property.PropertyCode != code) continue;
            Mod.Log($"dock inspect property={property.PropertyCode} owned={property.IsOwned} count={property.LoadingDocks?.Length ?? 0}");
            for (int i = 0; i < (property.LoadingDocks?.Length ?? 0); i++)
            {
                var dock = property.LoadingDocks![i];
                if (dock == null) { Mod.Warn($"dock inspect {property.PropertyCode}[{i}] is null"); continue; }
                var lot = dock.Parking;
                Mod.Log($"dock inspect {property.PropertyCode}[{i}] guid={dock.GUID} pos={V(dock.transform.position)} yaw={dock.transform.eulerAngles.y:F3} occupied={dock.IsInUse} parking={lot != null} spots={lot?.ParkingSpots?.Count ?? 0}");
                if (lot?.EntryPoint != null) Mod.Log("dock road entry=" + V(lot.EntryPoint.position));
                if (lot?.HiddenVehicleAccessPoint != null) Mod.Log("dock hidden access=" + V(lot.HiddenVehicleAccessPoint.position));
                if (lot?.ParkingSpots != null)
                    for (int j = 0; j < lot.ParkingSpots.Count; j++)
                    {
                        var spot = lot.ParkingSpots[j];
                        if (spot != null) Mod.Log($"dock parking {j} pos={V(spot.transform.position)} alignment={V(spot.AlignmentPoint.position)} occupant={spot.OccupantVehicle != null}");
                    }
            }
        }
        var shops = ShopInterface.AllShops;
        for (int i = 0; shops != null && i < shops.Count; i++)
            if (shops[i] != null && shops[i].DeliveryVehicle != null)
                Mod.Log($"dock shop index={i} name='{shops[i].ShopName}' vehicle={shops[i].DeliveryVehicle.Vehicle != null}");
    }

    private static (Property Property, LoadingDock Source, Vector3 Offset, Vector3 Position, float Yaw) Candidate(IReadOnlyList<string> args)
    {
        if (args.Count != 6) throw new ArgumentException("dock candidate/enable <property> <sourceDockIndex> <localDx> <dy> <localDz>");
        var property = Find(args[1]);
        int index = int.Parse(args[2], CultureInfo.InvariantCulture);
        if (index < 0 || index >= (property.LoadingDocks?.Length ?? 0)) throw new ArgumentException("Template dock index is outside the existing property array.");
        var source = property.LoadingDocks![index] ?? throw new InvalidOperationException("Template dock is null.");
        var offset = new Vector3(float.Parse(args[3], CultureInfo.InvariantCulture), float.Parse(args[4], CultureInfo.InvariantCulture), float.Parse(args[5], CultureInfo.InvariantCulture));
        if (!float.IsFinite(offset.x) || !float.IsFinite(offset.y) || !float.IsFinite(offset.z) || offset.sqrMagnitude < 9f)
            throw new ArgumentException("Probe offset must be finite and at least 3 m from the template root.");
        Vector3 position = source.transform.position + source.transform.rotation * offset;
        float yaw = source.transform.eulerAngles.y;
        Mod.Log($"dock candidate property={property.PropertyCode} nextIndex={property.LoadingDocks.Length} sourceIndex={index} localOffset={V(offset)} root={V(position)} yaw={yaw:F3}");
        if (source.Parking?.ParkingSpots != null && source.Parking.ParkingSpots.Count > 0)
            Mod.Log("dock candidate first parking alignment=" + V(source.Parking.ParkingSpots[0].AlignmentPoint.position + source.transform.rotation * offset));
        Mod.Log("dock candidate only: confirm street clearance before enable; no world state changed.");
        return (property, source, offset, position, yaw);
    }

    private static void Enable(IReadOnlyList<string> args)
    {
        if (Active) throw new InvalidOperationException("The probe dock is already enabled; inspect it or disable it before reconfiguration.");
        var candidate = Candidate(args);
        if (candidate.Source.IsInUse) throw new InvalidOperationException("The source dock is occupied.");
        var manager = DeliveryManager.Instance ?? throw new InvalidOperationException("The delivery manager is not ready.");
        foreach (var delivery in manager.Deliveries)
            if (delivery != null && delivery.DestinationCode == candidate.Property.PropertyCode && delivery.Status != EDeliveryStatus.Completed)
                throw new InvalidOperationException("An existing delivery already targets this property: " + delivery.DeliveryID);
        var targetParking = candidate.Source.Parking?.ParkingSpots;
        if (targetParking == null || targetParking.Count == 0) throw new InvalidOperationException("The template dock has no parking spot.");
        Vector3 parkingCenter = targetParking[0].AlignmentPoint.position + candidate.Source.transform.rotation * candidate.Offset;
        foreach (var player in Player.PlayerList)
            if (player != null && Vector3.Distance(player.transform.position, parkingCenter) < 5f)
                throw new InvalidOperationException("A player is within 5 m of the candidate parking point.");
        foreach (var vehicle in UnityEngine.Object.FindObjectsOfType<LandVehicle>())
            if (vehicle != null && vehicle.IsVisible && Vector3.Distance(vehicle.transform.position, parkingCenter) < 6f)
                throw new InvalidOperationException("A visible vehicle is within 6 m of the candidate parking point.");
        _original = candidate.Property.LoadingDocks.Select(d => (d.Pointer, d.GUID.ToString())).ToArray();
        _extension.PropertyCode = candidate.Property.PropertyCode;
        _dock.Index = _original.Length;
        _dock.TemplatePropertyCode = candidate.Property.PropertyCode;
        _dock.TemplateDockIndex = int.Parse(args[2], CultureInfo.InvariantCulture);
        _dock.At = new LocalPoint(candidate.Position, candidate.Yaw);
        if (!MapExpansionsApi.TrySetWorldState(ExpansionId, StateId, out string error)) throw new InvalidOperationException(error);
        if (!MapExpansionsApi.IsWorldStateApplied(ExpansionId)) throw new InvalidOperationException("Probe world state has not been applied.");
        AssertDock();
        Mod.Log("dock enabled through shared MapExpansions world state; native dock readback confirmed. Saving is blocked while this temporary probe is enabled.");
    }

    private static LoadingDock AssertDock()
    {
        if (!Active || _extension == null) throw new InvalidOperationException("The dock probe is dormant.");
        var property = Find(_extension.PropertyCode);
        if (property.LoadingDocks == null || property.LoadingDocks.Length != _original.Length + 1 || _dock.Index != _original.Length)
            throw new InvalidOperationException("Probe changed the expected dock array length or index.");
        for (int i = 0; i < _original.Length; i++)
            if (property.LoadingDocks[i]?.Pointer != _original[i].Pointer || property.LoadingDocks[i].GUID.ToString() != _original[i].Guid)
                throw new InvalidOperationException("An original dock identity or index changed: " + i);
        var dock = property.LoadingDocks[_dock.Index] ?? throw new InvalidOperationException("The added dock is null.");
        var expectedDock = StableGuid("property-dock");
        var expectedLot = StableGuid("property-dock-lot");
        if (dock.GUID.ToString() != expectedDock || dock.BakedGUID != expectedDock || dock.ParentProperty?.Pointer != property.Pointer ||
            Il2Cpp.GUIDManager.GetObject<LoadingDock>(dock.GUID)?.Pointer != dock.Pointer || dock.Parking == null ||
            dock.Parking.GUID.ToString() != expectedLot || Il2Cpp.GUIDManager.GetObject<Il2CppScheduleOne.Map.ParkingLot>(dock.Parking.GUID)?.Pointer != dock.Parking.Pointer)
            throw new InvalidOperationException("Probe dock GUID, parking GUID, parent or native registry readback failed.");
        Mod.Log($"dock assert property={property.PropertyCode} index={_dock.Index} count={property.LoadingDocks.Length} guid={dock.GUID} parkingGuid={dock.Parking.GUID} originalIndices=preserved parent=confirmed registry=confirmed");
        return dock;
    }

    private static string StableGuid(string kind) => new Guid(MD5.HashData(Encoding.UTF8.GetBytes($"mapexpansions/{ExpansionId}/{kind}/{ExtensionId}/{DockId}"))).ToString();

    private static void Deliver(IReadOnlyList<string> args)
    {
        if (args.Count != 3) throw new ArgumentException("dock deliver <shopIndex> <itemId> sends exactly one test item through vanilla delivery, without a shop purchase.");
        var dock = AssertDock();
        if (!dock.ParentProperty.IsOwned) throw new InvalidOperationException("Choose an owned property for the actual delivery probe.");
        if (dock.IsInUse || !string.IsNullOrEmpty(_deliveryId)) throw new InvalidOperationException("The probe dock is occupied or already has an issued probe delivery.");
        int shopIndex = int.Parse(args[1], CultureInfo.InvariantCulture);
        if (shopIndex < 0 || shopIndex >= ShopInterface.AllShops.Count) throw new ArgumentException("Shop index is outside the inspected list.");
        var shop = ShopInterface.AllShops[shopIndex];
        if (shop?.DeliveryVehicle?.Vehicle == null) throw new InvalidOperationException("The selected shop has no initialized delivery vehicle.");
        if (Il2CppScheduleOne.Registry.GetItem(args[2]) == null) throw new ArgumentException("Unknown test item: " + args[2]);
        var manager = DeliveryManager.Instance ?? throw new InvalidOperationException("The delivery manager is unavailable.");
        foreach (var existing in manager.Deliveries)
            if (existing != null && (existing.StoreName == shop.ShopName || existing.DestinationCode == dock.ParentProperty.PropertyCode) && existing.Status != EDeliveryStatus.Completed)
                throw new InvalidOperationException("An active delivery already uses this shop or destination.");
        _deliveryId = Guid.NewGuid().ToString();
        _deliveryItem = args[2];
        var items = new Il2CppReferenceArray<StringIntPair>(new[] { new StringIntPair(_deliveryItem, 1) });
        var delivery = new DeliveryInstance(_deliveryId, shop.ShopName, dock.ParentProperty.PropertyCode, _dock.Index, items, EDeliveryStatus.InTransit, 1);
        manager.SendDelivery(delivery);
        foreach (var entry in manager.Deliveries) if (entry != null && entry.DeliveryID == _deliveryId) _observed = entry;
        if (_observed == null || _observed.LoadingDock?.Pointer != dock.Pointer) throw new InvalidOperationException("Vanilla did not acknowledge the issued probe delivery at the added dock. Inspect current delivery state before retrying.");
        Mod.Log($"dock delivery issued-and-read-back id={_deliveryId} store='{shop.ShopName}' item={_deliveryItem} quantity=1 destination={dock.ParentProperty.PropertyCode} dockIndex={_dock.Index} status={_observed.Status}; arrival still requires native time progression");
    }

    private static void Status()
    {
        Mod.Log($"dock status active={Active} expansionState={MapExpansionsApi.GetState(ExpansionId)} desired='{MapExpansionsApi.GetWorldState(ExpansionId)}' deliveryId='{_deliveryId}'");
        if (!Active) return;
        var dock = AssertDock();
        var delivery = _observed;
        Mod.Log($"dock status occupied={dock.IsInUse} dynamic={dock.DynamicOccupant != null} static={dock.StaticOccupant != null} outputSlots={dock.OutputSlots?.Count ?? 0}");
        if (delivery == null) return;
        int quantity = 0;
        var vehicle = delivery.ActiveVehicle?.Vehicle;
        if (vehicle?.Storage?.ItemSlots != null)
            foreach (var slot in vehicle.Storage.ItemSlots)
                if (slot?.ItemInstance != null && slot.ItemInstance.ID == _deliveryItem) quantity += slot.ItemInstance.Quantity;
        Mod.Log($"dock delivery readback id={delivery.DeliveryID} status={delivery.Status} remainingMinutes={delivery.TimeUntilArrival} activeVehicle={vehicle != null} testItemQuantity={quantity}" +
            (vehicle == null ? "" : $" vehiclePos={V(vehicle.transform.position)}"));
    }

    private static void Disable()
    {
        if (!MapExpansionsApi.TrySetWorldState(ExpansionId, null, out string error)) throw new InvalidOperationException(error);
        var property = _extension == null ? null : Find(_extension.PropertyCode);
        if (_original.Length > 0 && property?.LoadingDocks?.Length != _original.Length) throw new InvalidOperationException("Dock probe disable did not restore the original dock count.");
        ResetForMenu();
        Mod.Log("dock probe disabled; original dock array retained and saving released.");
    }
}

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.Save), new[] { typeof(string) })]
internal static class DockProbeSaveGuard
{
    private static bool _reported;
    private static bool Prefix()
    {
        if (!DockProbe.Active) { _reported = false; return true; }
        if (!_reported) Mod.Warn("Save blocked while the temporary delivery-dock diagnostic is active. Complete the delivery and disable the probe first.");
        _reported = true;
        return false;
    }
}
#endif
