using Based.Content;
using Based.Core;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.Trash;
using UnityEngine;
using UnityEngine.AI;

namespace Based.Runtime;

// The crew's trash in a room that was just finished: cigarette butts, packs, cans, bottles and one bag, as the game's
// own trash items. The host creates them once, in the sleep that completes the build; the game saves them, sends them
// to every client and to late joiners, and the trash grabber and cleaners pick them up like any other trash. A load or
// a re-entry never runs this, because only a sleep that changes a build to complete calls it.
internal static class ConstructionLitter
{
    // Count per finished room: 8, one more for every 12 m² of new floor, at most 15.
    internal const int MinItems = 8, MaxItems = 15;
    private const float SquareMetresPerItem = 12f;
    // Rooms of the plan narrower than this are passages (a stair, a door strip) and get no trash.
    private const float MinRoomSide = 2f;
    // Distance kept from the room's outline, so nothing lands inside a wall or on the far side of it.
    private const float Margin = .35f;
    // The ray starts this far above the floor and accepts ground this far around it; a table top or a pallet is no floor.
    private const float RayHeight = 1.6f, FloorTolerance = .3f;
    private const float NavReach = .35f, DropHeight = .15f;
    private const int TriesPerItem = 8;

    // Weights of the loose pieces, a cigarette butt the most common.
    private static readonly (string Id, int Weight)[] Pieces =
    {
        ("usedcigarette", 5), ("cigarettebox", 3), ("energydrink", 3), ("glassbottle", 2), ("waterbottle", 2), ("cigarette", 1)
    };
    private const string Bag = "trashbag";

    internal static void Drop(SiteDefinition site, string upgradeId)
    {
        string label = site.Base.Id + "/" + upgradeId;
        if (!NativeBus.IsHost) { Mod.Log("Construction litter for " + label + " left to the host."); return; }
        if (site.UpgradeOf(upgradeId).IsPermit) { Mod.Log("Construction litter for " + label + " skipped: a permit has no work."); return; }
        if (!NetworkSingleton<TrashManager>.InstanceExists) { Mod.Warn("Construction litter for " + label + " not dropped: the trash manager is missing."); return; }
        var rooms = RoomsOf(site, upgradeId);
        if (rooms.Count == 0) { Mod.Log("Construction litter for " + label + " skipped: the plan has no new room of at least " + MinRoomSide + " m."); return; }
        float floor = FloorOf(site, upgradeId);
        if (float.IsNaN(floor)) { Mod.Warn("Construction litter for " + label + " not dropped: no floor height (follow-up Floor or a staff place)."); return; }

        int count = CountFor(rooms);
        var random = new System.Random(Seed(label));
        var manager = NetworkSingleton<TrashManager>.Instance;
        int placed = 0, missed = 0;
        var made = new Dictionary<string, int>();
        for (int i = 0; i < count; i++)
        {
            string id = i == 0 ? Bag : Pick(random);
            if (!Spot(rooms, floor, random, out var at))
            {
                missed++;
                Mod.Warn("Construction litter for " + label + ": no free floor found for piece " + (i + 1) + " (" + id + ") after " + TriesPerItem + " tries.");
                continue;
            }
            var rotation = Quaternion.Euler((float)random.NextDouble() * 360f, (float)random.NextDouble() * 360f, (float)random.NextDouble() * 360f);
            var item = id == Bag
                ? manager.CreateTrashBag(id, at, rotation, new TrashContentData(), Vector3.zero, "")
                : manager.CreateTrashItem(id, at, rotation, Vector3.zero, "");
            if (item == null)
            {
                missed++;
                Mod.Warn("Construction litter for " + label + ": the game created no item for '" + id + "' at " + at + ".");
                continue;
            }
            placed++;
            made[id] = made.TryGetValue(id, out var n) ? n + 1 : 1;
        }
        Mod.Log("Construction litter for " + label + ": " + placed + " of " + count + " piece(s) dropped over "
                + rooms.Count + " room(s) at floor " + floor.ToString("F2") + " (" + string.Join(", ", made.Select(p => p.Value + " " + p.Key)) + ")"
                + (missed > 0 ? ", " + missed + " missed" : "") + ".");
    }

    // The rooms the trash goes into: the plan's new rooms of this build, without passages.
    internal static List<PlanRoom> RoomsOf(SiteDefinition site, string upgradeId) =>
        site.PlanOf(upgradeId)
            .Where(r => r.Kind == PlanRoomKind.New && r.MaxX - r.MinX >= MinRoomSide && r.MaxZ - r.MinZ >= MinRoomSide)
            .ToList();

    internal static float FloorOf(SiteDefinition site, string upgradeId)
    {
        var followUp = site.FollowUps.FirstOrDefault(f => f.UpgradeId == upgradeId);
        if (followUp != null && !float.IsNaN(followUp.Floor)) return followUp.Floor;
        return site.StaffPlaces.Count > 0 ? site.StaffPlaces[0].y : float.NaN;
    }

    internal static int CountFor(IReadOnlyList<PlanRoom> rooms)
    {
        float area = rooms.Sum(r => (r.MaxX - r.MinX) * (r.MaxZ - r.MinZ));
        return Mathf.Clamp(MinItems + Mathf.FloorToInt(area / SquareMetresPerItem), MinItems, MaxItems);
    }

    // Trash items standing in the build's rooms right now, for checks.
    internal static int CountIn(SiteDefinition site, string upgradeId)
    {
        if (!NetworkSingleton<TrashManager>.InstanceExists) return -1;
        var rooms = RoomsOf(site, upgradeId);
        float floor = FloorOf(site, upgradeId);
        var items = NetworkSingleton<TrashManager>.Instance._trashItems;
        if (items == null) return -1;
        int count = 0;
        foreach (var item in items)
        {
            if (item == null) continue;
            var p = item.transform.position;
            if (Mathf.Abs(p.y - floor) <= 1f && rooms.Any(r => p.x >= r.MinX && p.x <= r.MaxX && p.z >= r.MinZ && p.z <= r.MaxZ)) count++;
        }
        return count;
    }

    private static string Pick(System.Random random)
    {
        int roll = random.Next(Pieces.Sum(p => p.Weight));
        foreach (var (id, weight) in Pieces)
        {
            if (roll < weight) return id;
            roll -= weight;
        }
        return Pieces[0].Id;
    }

    // A point on the walkable floor of one of the rooms, a room picked by its area.
    private static bool Spot(IReadOnlyList<PlanRoom> rooms, float floor, System.Random random, out Vector3 at)
    {
        float total = rooms.Sum(r => (r.MaxX - r.MinX) * (r.MaxZ - r.MinZ));
        for (int attempt = 0; attempt < TriesPerItem; attempt++)
        {
            float roll = (float)random.NextDouble() * total;
            var room = rooms[rooms.Count - 1];
            foreach (var r in rooms)
            {
                float a = (r.MaxX - r.MinX) * (r.MaxZ - r.MinZ);
                if (roll < a) { room = r; break; }
                roll -= a;
            }
            float x = Mathf.Lerp(room.MinX + Margin, room.MaxX - Margin, (float)random.NextDouble());
            float z = Mathf.Lerp(room.MinZ + Margin, room.MaxZ - Margin, (float)random.NextDouble());
            var from = new Vector3(x, floor + RayHeight, z);
            if (!Physics.Raycast(from, Vector3.down, out var hit, RayHeight + FloorTolerance, ~0, QueryTriggerInteraction.Ignore)) continue;
            if (Mathf.Abs(hit.point.y - floor) > FloorTolerance || hit.normal.y < .7f) continue;
            // Off the walking map means inside a prop's block, against a wall or behind a railing.
            if (!NavMesh.SamplePosition(hit.point, out var nav, NavReach, NavMesh.AllAreas)) continue;
            at = new Vector3(hit.point.x, hit.point.y + DropHeight, hit.point.z);
            return true;
        }
        at = default;
        return false;
    }

    // A stable seed per base and build, the same in every session and on every machine.
    private static int Seed(string text)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char c in text) hash = (hash ^ c) * 16777619;
            return (int)hash;
        }
    }
}
