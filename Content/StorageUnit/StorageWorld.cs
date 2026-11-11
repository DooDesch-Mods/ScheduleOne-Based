using MapExpansions;
using UnityEngine;

namespace Based.Content.StorageUnit;

// The upper level of the Storage warehouse above Unit 1 and the units east and south of it. Every state holds all
// work of the states before it, because a finished build and the next one's phases change the same expansion.
public static class StorageWorld
{
    private const string Anchor = "@Properties/StorageUnit/InteriorSpawnPoint";
    private static readonly Vector3 AnchorPoint = new(-5.25f, .1f, 104.876f);
    // Templates from elsewhere on the map are placed through the Sweatshop's frame, whose pose is known.
    private const string Frame = "@Properties/Sweatshop";
    private static readonly Vector3 FramePivot = new(-62.5f, -.25f, 138f);

    // The steel stair of the docks shed rises 3.05 m over a 5.43 m run, its top edge centred on this point.
    private const string Stair = "@Properties/DocksWarehouse/IndustrialShed/dockshed/Stairs";
    private static readonly Vector3 StairTop = new(-79.95f, .83f, -48f);
    // Turned to climb north along the west wall and stretched to the 3.30 m of the ledge on top of the lower wall.
    private const float StairYaw = 30f, StairScale = 1.082f;
    private const string Metal = "metal_darkgrey_mat";

    // The upper floor lies at 3.25; the ledge along the west wall and the landing in front of it end at 3.30.
    private const float UpperY = 3.25f, LandingTop = 3.30f, LandingBottom = 3.20f;
    private const float LandingWest = -10.65f, LandingEast = -9.01f, LandingSouth = 103.6f, LandingNorth = 105.3f;
    private const float RailTop = LandingTop + 1.05f, GuardTop = LandingTop + 2.1f;

    // Order of the states; each level includes all lower ones.
    private static readonly string[] States = { "a-stair", "a-door", "a-complete", "b-open", "b-complete", "c-open", "c-complete" };

    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        if (!MapExpansionsApi.Register(Create()))
            throw new InvalidOperationException("Based could not register the Storage Unit world states; inspect the MapExpansions validation errors.");
        _registered = true;
    }

    public static ExpansionDefinition Create()
    {
        var definition = new ExpansionDefinition
        {
            Id = StorageDefinition.ExpansionId, DisplayName = "Storage Unit upper level", Version = "0.1.0", Source = null
        };
        for (int i = 0; i < States.Length; i++) definition.WorldStates.Add(BuildState(States[i], i + 1));
        return definition;
    }

    private static WorldStateDefinition BuildState(string id, int level)
    {
        var state = new WorldStateDefinition { Id = id };
        var geometry = new CopyDefinition
        {
            Id = "upper-level", Template = Anchor, Frame = Anchor,
            At = new LocalPoint(AnchorPoint, 0), OwnMesh = false, Obstacle = false
        };
        StairAndLanding(state, geometry);
        state.Copies.Add(geometry);
        return state;
    }

    // Night 1: the stair stands against the west wall and ends on a landing in front of the closed upper wall.
    private static void StairAndLanding(WorldStateDefinition state, CopyDefinition geometry)
    {
        // The stair is 1.62 m wide after scaling and runs from x -10.65 to -9.03, clear of the bus stop's pillar.
        var target = new Vector3(-9.84f, LandingTop, LandingSouth);
        state.Copies.Add(Copy("west-stair", Stair, StairTop, target, StairYaw, StairScale));
        Solid(geometry, "landing", Metal, LandingWest, LandingBottom, LandingSouth, LandingEast, LandingTop, LandingNorth);
        // Two posts carry the landing's open corners; the shipping container stands under its north half.
        Post(geometry, "landing-post-sw", LandingWest + .04f, LandingSouth + .04f, 0f, LandingBottom);
        Post(geometry, "landing-post-se", LandingEast - .06f, LandingSouth + .04f, 0f, LandingBottom);
        // A railing on the open west and north edges; the stair comes up on the south edge, the wall closes the east.
        Railing(geometry, "landing-west", false, LandingWest + .03f, LandingSouth, LandingNorth);
        Railing(geometry, "landing-north", true, LandingNorth - .03f, LandingWest, LandingEast);
    }

    private static void Railing(CopyDefinition geometry, string id, bool alongX, float cross, float start, float end)
    {
        const float half = .025f;
        int posts = Math.Max(2, (int)Math.Ceiling((end - start) / 1.2f) + 1);
        for (int i = 0; i < posts; i++)
        {
            float at = Mathf.Lerp(start + half, end - half, i / (float)(posts - 1));
            if (alongX) Face(geometry, id + "-post-" + i, Metal, at - half, LandingTop, cross - half, at + half, RailTop, cross + half);
            else Face(geometry, id + "-post-" + i, Metal, cross - half, LandingTop, at - half, cross + half, RailTop, at + half);
        }
        foreach (var (name, y) in new[] { ("top", RailTop - .04f), ("middle", LandingTop + .5f) })
            if (alongX) Face(geometry, id + "-rail-" + name, Metal, start, y, cross - .02f, end, y + .04f, cross + .02f);
            else Face(geometry, id + "-rail-" + name, Metal, cross - .02f, y, start, cross + .02f, y + .04f, end);
        // The railing is drawn without colliders; an invisible wall to head height keeps a jump from carrying over it.
        geometry.Boxes.Add(new BoxDefinition
        {
            Id = id + "-guard", Kind = BoxKind.Block,
            Box = alongX ? BoundsOf(start, LandingTop, cross - .05f, end, GuardTop, cross + .05f)
                : BoundsOf(cross - .05f, LandingTop, start, cross + .05f, GuardTop, end)
        });
    }

    private static CopyDefinition Copy(string id, string template, Vector3 sourcePivot, Vector3 targetPivot, float yaw = 0, float scale = 1f) => new()
    {
        Id = id, Template = template, Frame = Frame, Scale = scale,
        At = new LocalPoint(targetPivot - Quaternion.Euler(0, yaw, 0) * (scale * (sourcePivot - FramePivot)), yaw),
        IndependentMesh = true, VisualOnly = true, Obstacle = false
    };

    private static void Post(CopyDefinition copy, string id, float x, float z, float bottom, float top) =>
        Solid(copy, id, Metal, x - .06f, bottom, z - .06f, x + .06f, top, z + .06f);

    private static void Solid(CopyDefinition copy, string id, string material, float x1, float y1, float z1, float x2, float y2, float z2)
    {
        Face(copy, id, material, x1, y1, z1, x2, y2, z2);
        copy.Boxes.Add(new BoxDefinition { Id = id + "-collision", Kind = BoxKind.Block, Box = BoundsOf(x1, y1, z1, x2, y2, z2) });
    }

    private static void Face(CopyDefinition copy, string id, string material, float x1, float y1, float z1, float x2, float y2, float z2) =>
        copy.Boxes.Add(new BoxDefinition { Id = id, Kind = BoxKind.Panel, Material = material, TileMetres = 1f, Box = BoundsOf(x1, y1, z1, x2, y2, z2) });

    private static Bounds BoundsOf(float x1, float y1, float z1, float x2, float y2, float z2)
    {
        var bounds = new Bounds();
        bounds.SetMinMax(new Vector3(x1, y1, z1), new Vector3(x2, y2, z2));
        return bounds;
    }
}
