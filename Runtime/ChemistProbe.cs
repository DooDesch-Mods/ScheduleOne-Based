#if DEBUG
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Based.Content;
using Based.Content.Ming;
using Il2CppScheduleOne.Employees;
using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.Management;
using Il2CppScheduleOne.ObjectScripts;
using UnityEngine;

namespace Based.Runtime;

/// <summary>
/// "chemist setup [wing] [product] [mixer] [qty]" gives the site's chemists the mixing stations (with wing only those in
/// the Sweatshop's new wing), fills each with product and mixer, and sends its output to a storage rack outside the wing,
/// so a chemist walks between the old room and the wing the way a player's mixing setup makes it.
/// "chemist" alone reports each chemist's position, behaviour and stations.
/// </summary>
internal static class ChemistProbe
{
    private static string At(Vector3 p) => "(" + p.x.ToString("0.00", CultureInfo.InvariantCulture) + ", " + p.y.ToString("0.00", CultureInfo.InvariantCulture) + ", " + p.z.ToString("0.00", CultureInfo.InvariantCulture) + ")";

    private static bool InWing(Component item) => MingWorld.Wing.Contains(item.transform.position);

    internal static void Handle(SiteDefinition site, IReadOnlyList<string> args)
    {
        var property = StaffTrack.Property(site);
        var chemists = property.Employees.ToArray().Select(e => e?.TryCast<Chemist>()).Where(c => c != null).ToList();
        if (chemists.Count == 0) { Mod.Warn($"baseddiag chemist: {property.PropertyCode} has no chemist"); return; }
        var stations = property.BuildableItems.ToArray().Select(i => i?.TryCast<MixingStation>()).Where(s => s != null).ToList();
        if (args.Count > 1 && args[1] == "setup") Setup(property, chemists!, stations!, args);
        else Report(chemists!);
    }

    private static void Setup(Il2CppScheduleOne.Property.Property property, List<Chemist> chemists, List<MixingStation> all, IReadOnlyList<string> args)
    {
        bool wing = args.Count > 2 && args[2] == "wing";
        string productId = args.Count > 3 ? args[3] : "ogkush", mixerId = args.Count > 4 ? args[4] : "cuke";
        int qty = args.Count > 5 ? int.Parse(args[5], CultureInfo.InvariantCulture) : 20;
        var stations = wing ? all.Where(InWing).ToList() : all;
        if (stations.Count == 0) { Mod.Warn($"baseddiag chemist setup: no mixing station{(wing ? " in the wing" : "")} on {property.PropertyCode}; place one first"); return; }
        var product = Il2CppScheduleOne.Registry.GetItem(productId);
        var mixer = Il2CppScheduleOne.Registry.GetItem(mixerId);
        if (product == null || mixer == null) { Mod.Warn($"baseddiag chemist setup: unknown item '{(product == null ? productId : mixerId)}'"); return; }
        // The output goes to a rack in the old room, so each finished mix sends the chemist out of the wing and back.
        var storages = property.BuildableItems.ToArray().Where(i => i?.TryCast<PlaceableStorageEntity>() != null).ToList();
        var destination = storages.FirstOrDefault(i => !InWing(i) && (i.ItemInstance?.ID?.Contains("storagerack") ?? false)) ?? storages.FirstOrDefault(i => !InWing(i));
        if (destination == null) Mod.Warn("baseddiag chemist setup: no storage outside the wing; the stations keep their output");
        var locker = property.BuildableItems.ToArray().FirstOrDefault(i => i != null && i.GetComponent<EmployeeHome>() != null);
        int next = 0;
        foreach (var chemist in chemists)
        {
            var config = chemist.configuration;
            if (config == null) { Mod.Warn($"baseddiag chemist setup: {chemist.FirstName} has no configuration yet"); continue; }
            foreach (var item in config.Stations.SelectedObjects.ToArray()) config.Stations.RemoveItem(item);
            // Each station goes to one chemist only; a station already taken by another chemist is refused by the filter.
            while (next < stations.Count && config.Stations.SelectedObjects.Count < config.Stations.MaxItems)
            {
                var station = stations[next++];
                config.Stations.AddItem(station.Cast<BuildableItem>());
                var stationConfig = station.Configuration?.TryCast<MixingStationConfiguration>();
                if (stationConfig != null && destination != null) stationConfig.Destination.SetObject(destination, true);
                if (station.OutputSlot.Quantity > 0) station.OutputSlot.ClearStoredInstance();
                station.ProductSlot.InsertItem(product.GetDefaultInstance(qty));
                station.MixerSlot.InsertItem(mixer.GetDefaultInstance(qty));
                Mod.Log($"baseddiag chemist setup: {chemist.FirstName} works {station.name} at {At(station.transform.position)}{(InWing(station) ? " in the wing" : "")}, " +
                        $"product {station.ProductSlot.Quantity}, mixer {station.MixerSlot.Quantity}, can start {station.CanStartMix()}, " +
                        $"destination {(destination != null ? destination.name + " at " + At(destination.transform.position) : "none")}");
            }
            if (locker != null) config.Home.SetObject(locker, true);
            chemist.SetIsPaid();
            Mod.Log($"baseddiag chemist setup: {chemist.FirstName} has {config.Stations.SelectedObjects.Count} stations ({config.MixStations.Count} mixing), " +
                    $"locker {(locker != null ? locker.name : "none on the property")}, paid for today");
        }
    }

    private static void Report(List<Chemist> chemists)
    {
        foreach (var chemist in chemists)
        {
            var agent = chemist.GetComponentInChildren<UnityEngine.AI.NavMeshAgent>();
            string behaviour = chemist.Behaviour?.activeBehaviour?.Name ?? "none";
            Mod.Log($"baseddiag chemist: {chemist.FirstName} at {At(chemist.transform.position)}, behaviour {behaviour}, " +
                    $"on navmesh {(agent != null && agent.isOnNavMesh)}, moving {chemist.Movement?.IsMoving ?? false}");
            var config = chemist.configuration;
            if (config == null) continue;
            foreach (var station in config.MixStations)
                Mod.Log($"baseddiag chemist:   {station.name} at {At(station.transform.position)}{(InWing(station) ? " in the wing" : "")}: product {station.ProductSlot.Quantity}, " +
                        $"mixer {station.MixerSlot.Quantity}, output {station.OutputSlot.Quantity}, in use {station.NPCUserObject != null},mixing {station.CurrentMixOperation != null}");
        }
    }
}
#endif
