using Based.Content;
using Based.Core;
using HarmonyLib;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Employees;
using Il2CppScheduleOne.Property;
using UnityEngine;

namespace Based.Runtime;

// A finished room holds more employees. The game refuses to load or hire anyone above EmployeeCapacity, so the limit
// has to be up before the save restores them; Mod.Restore runs early enough for that.
internal static class StaffRoom
{
    private const string HolderName = "Based_StaffPlaces";
    private static readonly Dictionary<string, int> VanillaLimits = new(StringComparer.Ordinal);

    internal static void Reset() => VanillaLimits.Clear();

    internal static void Apply(SiteDefinition site)
    {
        UpgradeStage StageOf(string id) => Mod.StageOf(site, id);
        var property = Singleton<PropertyManager>.Instance?.GetProperty(site.Base.PropertyId);
        if (property == null)
        {
            // Any limit above the game's 1 means a finished room is waiting for its employees.
            if (site.StaffLimit(1, StageOf) > 1) throw new InvalidOperationException("Property " + site.Base.PropertyId + " is not loaded.");
            Mod.Log("Employee limit of " + site.Base.PropertyId + " not set: the property is not loaded yet.");
            return;
        }
        int vanilla = VanillaLimit(property);
        int wanted = site.StaffLimit(vanilla, StageOf);
        // Employee.AssignProperty indexes the idle points without a check, so the places come before the limit.
        if (wanted > vanilla) AddPlaces(site, property, wanted);
        if (property.EmployeeCapacity == wanted) return;
        property.EmployeeCapacity = wanted;
        Mod.Log("Employee limit of " + site.Base.PropertyId + " is now " + wanted + " (game value " + vanilla + ").");
    }

    // A joining client can receive an employee before the host's construction state, so its places exist regardless.
    internal static void EnsurePlaces(Property property)
    {
        var site = Mod.Sites.FirstOrDefault(s => s.Base.PropertyId == property.PropertyCode);
        if (site == null) return;
        int vanilla = VanillaLimit(property), most = site.MaxStaffLimit(vanilla);
        if (most > vanilla) AddPlaces(site, property, most);
    }

    private static int VanillaLimit(Property property)
    {
        if (!VanillaLimits.TryGetValue(property.PropertyCode, out int vanilla))
            VanillaLimits[property.PropertyCode] = vanilla = property.EmployeeCapacity;
        return vanilla;
    }

    private static void AddPlaces(SiteDefinition site, Property property, int wanted)
    {
        var known = property.EmployeeIdlePoints;
        int have = known == null ? 0 : known.Length;
        if (have >= wanted) return;
        if (site.StaffPlaces.Count == 0) throw new InvalidOperationException(site.Base.Id + " defines no places for employees.");
        if (wanted - have > site.StaffPlaces.Count)
            Mod.Warn(site.Base.Id + " has " + site.StaffPlaces.Count + " places for " + (wanted - have) + " employees; some share one.");

        var center = Vector3.zero;
        foreach (var spot in site.StaffPlaces) center += spot / site.StaffPlaces.Count;
        var holder = new GameObject(HolderName).transform;
        holder.SetParent(property.transform, false);
        var places = new Transform[wanted];
        for (int i = 0; i < have; i++) places[i] = known![i];
        for (int i = have; i < wanted; i++)
        {
            var spot = site.StaffPlaces[(i - have) % site.StaffPlaces.Count];
            var place = new GameObject("Place " + i).transform;
            place.SetParent(holder, false);
            place.position = spot;
            var inward = new Vector3(center.x - spot.x, 0f, center.z - spot.z);
            if (inward.sqrMagnitude > .01f) place.rotation = Quaternion.LookRotation(inward);
            places[i] = place;
        }
        property.EmployeeIdlePoints = places;
        Mod.Log(site.Base.PropertyId + " has " + wanted + " places for employees, " + (wanted - have) + " of them added.");
    }
}

[HarmonyPatch(typeof(Employee), nameof(Employee.AssignProperty))]
internal static class EmployeePlaceOnEveryPeer
{
    private static void Prefix(Property prop)
    {
        if (prop == null)
        {
            Mod.Warn("An employee was assigned without a property.");
            return;
        }
        try { StaffRoom.EnsurePlaces(prop); }
        catch (Exception error) { Mod.Warn("Places for employees of " + prop.PropertyCode + " not added: " + error); }
    }
}
