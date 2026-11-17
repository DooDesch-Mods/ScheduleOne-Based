using System.Collections.Generic;
using System.Linq;
using MapExpansions;
using UnityEngine;

namespace Based.Content.Bungalow;

// The crew's site for the second storey, on the grass east of the house: the foreman's table against the house, the
// timber stock north of it, the fire barrel and the generator south of the skip bin, cones and the site sign toward the
// street. The stock shrinks night by night and offcuts lie where that night's work was: on the open floor while the
// floor and frame stand, in the rooms once the walls close. On the last night the site goes and its trash stays by the
// house. Every prop keeps clear of the skip bin (x -162.14 .. -160.3, z 117.39 .. 121.18), the two trees' trunks
// (x -161.63 .. -161.08, z 112.21 .. 112.76 and x -160.93 .. -160.36, z 122.3 .. 122.87), the loading dock
// (z to 110.85), the carport's posts and the gravel strip at the stair's foot.
internal static class BungalowSiteProps
{
    private const string Property = "@Properties/Bungalow";
    private const string Anchor = Property + "/bungalow/PorchBeam";
    private static readonly Vector3 PropertyPivot = new(-170f, -4f, 115.5f);
    private const float PropertyYaw = 90f;

    private const string Pallet = "Map/Hyland Point/Region_Northtown/North Waterfront/Pallet Prop (2)";
    private static readonly Vector3 PalletPivot = new(-57.9526f, -.348f, 143.1308f);
    private const string Plank = "Map/Hyland Point/Region_Westville/Tattoo Parlour New/Interior/GameObject/SM_Prop_Plank_01";
    private static readonly Vector3 PlankPivot = new(-131.62f, -3f, 69.53f);
    private const float PalletDeck = .2f, PlankThickness = .04f;
    private const string ManorSite = "@Properties/Manor/Construction/";
    private const string BuildSite = "Map/Hyland Point/Region_Westville/Construction Site/";
    private static readonly Vector3 BarrelsPivot = new(171.41f, 10.15f, -42.39f);
    private static readonly Vector3 GeneratorPivot = new(-127.05f, -4f, 94.47f);
    private static readonly Vector3 SignPivot = new(-135.09f, -4.26f, 93.17f);
    private static readonly Vector3 ConePivot = new(-132.98f, -3.77f, 99.88f);
    private const float ConeCentre = .23f;
    private const string FoldingTable = "Map/SpecialCustomerCamps/BikerCamp/Container/Props/Table_Foldout/Table_Foldout_LOD0";
    private static readonly Vector3 FoldingTablePivot = new(-142.02f, -3.9f, 50.01f);
    private const float TableScale = .9f;
    private const string Camp = "Map/Hyland Point/Region_Westville/Encampment/";
    private static readonly Vector3 FireDrumPivot = new(-96.49f, -4.06f, 59.52f);
    private static readonly Vector3 TrashCanPivot = new(-94.74f, -4.03f, 59.14f);
    private static readonly Vector3 TrashDrumPivot = new(-91.39f, -3.89f, 62.26f);
    private static readonly Vector3 CratePivot = new(-91.51f, -4.00f, 57.01f);
    private static readonly Vector3 JerryCanPivot = new(-95.21f, -4.05f, 58.92f);
    private static readonly Vector3 BucketPivot = new(-95.43f, -3.28f, 58.37f);
    private const float BucketUprightDrop = .20f;
    private static readonly Vector3 CinderblockPivot = new(-96.74f, -4.08f, 59.92f);

    private const string Timber = "wood brown", Boards = "wood planks medium brown mat";
    // The boards of the upper floor; offcuts lie on them.
    private const float UpperBoards = -.645f;

    // The table stands long side to the house with the plan on it; the foreman reads it from the grass side and walks to
    // its north end. Ground from -4.0 at the south end to -3.92 at the north end.
    private static readonly Vector3 TableAt = new(-162.75f, -3.97f, 115.5f);
    // The timber stock north of the house, between the house's corner and the tree.
    private static readonly Vector3 StockAt = new(-162.75f, -3.95f, 122.75f);
    private static readonly Vector3 FireDrumAt = new(-160.4f, -3.98f, 114.6f);
    internal static readonly Vector3 FireSpot = FireDrumAt + new Vector3(0, .85f, 0);

    internal static readonly (Vector3 At, Vector3 Face) TableFront = (new(-161.75f, -3.98f, 115.5f), new(-162.75f, -3.25f, 115.5f));
    internal static readonly (Vector3 At, Vector3 Face) TableSide = (new(-162.75f, -3.92f, 117.2f), new(-162.75f, -3.25f, 116.3f));
    // A worker hammers only at a contact point within 0.35 m of his hip (0.9 m above his feet), so each Face sits at
    // that height above the work: the top of the stack, the post, the wall.
    internal static readonly (Vector3 At, Vector3 Face) StockFront = (new(-161.4f, -3.97f, 122.75f), new(-162.1f, -3.2f, 122.75f));
    internal static readonly (Vector3 At, Vector3 Face) StockSide = (new(-162.75f, -3.9f, 121.55f), new(-162.75f, -3.15f, 122.3f));

    // On the order day the roof is still on and the site not yet set up: the foreman looks the house over, the carpenter
    // checks the porch post and the east wall.
    private static readonly (Vector3 At, Vector3 Face) SurveyRoof = (new(-162.3f, -3.98f, 116.2f), new(-163.75f, -1f, 116.2f));
    private static readonly (Vector3 At, Vector3 Face) SurveyPorch = (new(-162.3f, -3.95f, 114.0f), new(-164.3f, -1.2f, 113.6f));
    private static readonly (Vector3 At, Vector3 Face) PorchPost = (new(-166.7f, -3.85f, 113.35f), new(-167.2f, -2.9f, 112.85f));
    private static readonly (Vector3 At, Vector3 Face) EastWallCheck = (new(-163.0f, -3.92f, 119.0f), new(-163.75f, -2.95f, 119.0f));

    // 1 for the floor, 2 the frame, 3 the walls, 4 the roof: the state that stands, from the quest's phase or the world
    // state's id. Anything else is 0.
    internal static int LevelOf(string? phase) => phase switch
    {
        "storey-floor" or BungalowStorey.FloorState => 1,
        "storey-frame" or BungalowStorey.FrameState => 2,
        "storey-walls" or BungalowStorey.WallsState => 3,
        "storey-roof" or BungalowStorey.RoofState => 4,
        "complete" => 5,
        _ => 0
    };

    // Where the fitter stands and what he welds on the day after a state stands: with the floor and stair in, the
    // stair's foot post, from the gravel east of the loading dock; with the frame up, the landing's south post, from
    // between the carport's posts. On the other days he is off the job.
    internal static (Vector3 Stand, Vector3 Spark)? Weld(int level) => level switch
    {
        1 => (new Vector3(-161.85f, -3.96f, 111.05f), new Vector3(-162.5f, -3.15f, 111.38f)),
        2 => (new Vector3(-167.7f, -4.02f, 110.55f), new Vector3(-168.42f, -2.3f, 111.42f)),
        _ => null
    };

    internal static (Vector3 At, Vector3 Face) Foreman(int level, bool secondary) => level == 0
        ? secondary ? SurveyPorch : SurveyRoof
        : secondary ? TableSide : TableFront;

    internal static (Vector3 At, Vector3 Face) Carpenter(int level, bool secondary) => level == 0
        ? secondary ? EastWallCheck : PorchPost
        : secondary ? StockSide : StockFront;

    internal static void Add(WorldStateDefinition state, int level)
    {
        var site = new CopyDefinition
        {
            Id = "storey-site", Template = Anchor, Frame = Property, OwnMesh = false, Obstacle = false,
            // The post only anchors the boxes; its collider child would stand as an unseen beam where the anchor lands.
            Exclude = { "Colliders" },
            At = new LocalPoint(PropertyPivot, PropertyYaw)
        };
        if (level <= 4) Site(state, site, level);
        else Cleared(state);
        if (site.Boxes.Count > 0) state.Copies.Add(site);
    }

    private static void Site(WorldStateDefinition state, CopyDefinition site, int level)
    {
        SiteTable(state, site);
        Stock(state, site, level switch { 1 => 12, 2 => 8, 3 => 4, _ => 2 });
        state.Copies.Add(Copy("site-fire-drum", Camp + "DrumFirepit", FireDrumPivot, FireDrumAt, 17));
        Block(site, "site-fire-drum-block", FireDrumAt.x - .32f, FireDrumAt.y, FireDrumAt.z - .32f, FireDrumAt.x + .32f, FireDrumAt.y + .9f, FireDrumAt.z + .32f);
        Cinderblock(state, "site-cinderblock-a", -160.95f, -3.98f, 114.25f, 23);
        Cinderblock(state, "site-cinderblock-b", -159.85f, -3.99f, 114.95f, -38);
        var generator = new Vector3(-160.0f, -4f, 116.4f);
        state.Copies.Add(Copy("site-generator", BuildSite + "SM_Prop_Generator_01", GeneratorPivot, generator, 85));
        Block(site, "site-generator-block", generator.x - .35f, generator.y, generator.z - .5f, generator.x + .35f, generator.y + .7f, generator.z + .5f);
        state.Copies.Add(Copy("site-jerry-can", Camp + "Jerry Can", JerryCanPivot, new Vector3(-159.6f, -4f, 115.6f), 152));
        state.Copies.Add(Copy("site-bucket", Camp + "SM_Prop_Bucket_01", BucketPivot, new Vector3(-163.3f, -3.99f - BucketUprightDrop, 114.3f), 47));
        // The site sign faces the street; the cones flank the way from the pavement to the stair.
        state.Copies.Add(Copy("site-sign", BuildSite + "CS_sign_02", SignPivot, new Vector3(-159.75f, -4.05f, 113.75f), -80));
        state.Copies.Add(Copy("site-cone-0", BuildSite + "TrafficCone/trafficcone", ConePivot, new Vector3(-160.9f, -3.92f + ConeCentre - .03f, 110.45f), 0));
        state.Copies.Add(Copy("site-cone-1", BuildSite + "TrafficCone/trafficcone", ConePivot, new Vector3(-159.7f, -3.92f + ConeCentre - .03f, 110.65f), 0));
        if (level <= 3)
        {
            var barrels = new Vector3(-161.2f, -3.99f, 124.25f);
            state.Copies.Add(Copy("site-barrels", ManorSite + "SM_Prop_BarrelStack_01", BarrelsPivot, barrels, 6));
            Block(site, "site-barrels-block", barrels.x - .74f, barrels.y, barrels.z - .74f, barrels.x + .74f, barrels.y + 1f, barrels.z + .74f);
        }

        // Offcuts: by the table outside on every day, on the open floor while the floor and frame stand, in the rooms
        // once the walls close; fewer when the roof is on.
        Offcuts(state, "ground-offcuts", -162.95f, -3.99f, 113.75f, .3f, 5);
        switch (level)
        {
            case 1:
                Offcuts(state, "floor-offcuts-south", -172.8f, UpperBoards, 112.6f, .35f, 11);
                Offcuts(state, "floor-offcuts-east", -166.5f, UpperBoards, 118.4f, .3f, 23);
                Offcuts(state, "floor-offcuts-stray", -174.6f, UpperBoards, 119.6f, .14f, 37);
                break;
            case 2:
                Offcuts(state, "frame-offcuts-south", -172.8f, UpperBoards, 112.6f, .42f, 13);
                Offcuts(state, "frame-offcuts-east", -166.5f, UpperBoards, 118.4f, .36f, 29);
                Offcuts(state, "frame-offcuts-north", -171.2f, UpperBoards, 119.8f, .3f, 41);
                Offcuts(state, "frame-offcuts-balcony", -167.4f, UpperBoards, 114.1f, .16f, 43);
                Offcuts(state, "frame-offcuts-stray", -174.6f, UpperBoards, 119.6f, .14f, 47);
                break;
            case 3:
                Offcuts(state, "room-offcuts-south", -173.2f, UpperBoards, 113.4f, .3f, 53);
                Offcuts(state, "room-offcuts-east", -166.8f, UpperBoards, 119.2f, .3f, 59);
                Offcuts(state, "room-offcuts-north", -172.6f, UpperBoards, 118.0f, .22f, 61);
                break;
            case 4:
                Offcuts(state, "room-offcuts-east", -166.8f, UpperBoards, 119.2f, .2f, 67);
                Offcuts(state, "room-offcuts-stray", -173.2f, UpperBoards, 113.4f, .14f, 71);
                break;
        }
    }

    // Once the storey is handed over, the site is gone; its trash waits by the house's north-east corner and the empty
    // crate by the street.
    private static void Cleared(WorldStateDefinition state)
    {
        state.Copies.Add(Copy("yard-trash-can", Camp + "Trash Can Prop", TrashCanPivot, new Vector3(-162.7f, -3.93f, 122.4f), 33));
        state.Copies.Add(Copy("yard-trash-drum", Camp + "Liquid Drum Hollow with trash", TrashDrumPivot, new Vector3(-162.0f, -3.95f, 123.05f), -61));
        state.Copies.Add(Copy("yard-crate", Camp + "Wood Crate Prop (1)", CratePivot, new Vector3(-160.0f, -4f, 114.6f), 52));
        Cinderblock(state, "yard-cinderblock", -160.65f, -3.98f, 114.25f, 141);
    }

    private static void SiteTable(WorldStateDefinition state, CopyDefinition site)
    {
        var at = TableAt;
        state.Copies.Add(Copy("site-table", FoldingTable, FoldingTablePivot, at, 90, TableScale));
        // Turned by 90°, the table's long side runs along the house.
        float halfX = .61f * TableScale, halfZ = 1.26f * TableScale, top = at.y + .75f * TableScale;
        Block(site, "site-table-block", at.x - halfX, at.y, at.z - halfZ, at.x + halfX, top, at.z + halfZ);
        Face(site, "site-table-plan", "clipboard_paper_mat", at.x - .16f, top + .002f, at.z - .25f, at.x + .16f, top + .004f, at.z + .25f);
    }

    // A pallet with planks stacked side by side across their length, four to a layer.
    private static void Stock(WorldStateDefinition state, CopyDefinition site, int planks)
    {
        var at = StockAt;
        state.Copies.Add(Copy("site-pallet", Pallet, PalletPivot, at, 0));
        Block(site, "site-pallet-block", at.x - .78f, at.y, at.z - .78f, at.x + .78f, at.y + .55f, at.z + .78f);
        float[] shift = { .015f, -.025f, .005f, -.01f, .03f, -.015f, .01f, -.03f, .02f, 0f, -.02f, .025f };
        // Planks side by side touch at their edges, so each of a layer lies 5 mm above the one before and no two tops share
        // a plane; the mesh leans by about 1 mm across its width, which a smaller step would not clear. The first layer lies 2 mm above the pallet deck and each further layer 2 mm above the one below, so no
        // plank's underside lies in the face it rests on.
        for (int i = 0; i < planks; i++)
            state.Copies.Add(Copy("site-plank-" + i, Plank, PlankPivot,
                new Vector3(at.x + shift[i],
                    at.y + PalletDeck + .002f + PlankThickness * (.5f + i / 4) + (i / 4) * .002f + (i % 4) * .005f,
                    at.z + ((i % 4) - 1.5f) * .125f), 90));
    }

    private static void Cinderblock(WorldStateDefinition state, string id, float x, float floor, float z, float yaw) =>
        state.Copies.Add(Copy(id, Camp + "Cinderblock (1)", CinderblockPivot, new Vector3(x, floor, z), yaw));

    // A low heap of sawn-off ends of timber and board. A box cannot turn on its own, so each handful is a copy with its
    // own yaw around the middle of the heap. Each piece sinks a little into the floor; its top and its sides come from
    // the seed, so no two pieces share a face.
    private static void Offcuts(WorldStateDefinition state, string id, float x, float floor, float z, float radius, int seed)
    {
        var random = new System.Random(seed);
        float Between(float from, float to) => from + (float)random.NextDouble() * (to - from);
        var centre = new Vector3(x, floor, z);
        int pieces = Math.Max(2, (int)(radius * 14f));
        // The three handfuls overlap, so tops and bottoms are kept apart across the whole heap.
        var levels = (Bottoms: new List<float>(), Tops: new List<float>());
        float start = Between(0f, 180f);
        for (int turn = 0; turn < 3; turn++)
        {
            // The handfuls turn 60 degrees apart, give or take 10, so no two of them lie nearly parallel or square to each
            // other, where the sides of their pieces could meet in one plane.
            float yaw = start + turn * 60f + Between(-10f, 10f);
            var handful = new CopyDefinition
            {
                Id = id + "-" + turn, Template = Anchor, Frame = Property, OwnMesh = false, Obstacle = false,
                // The post only anchors the boxes; its collider child would stand as an unseen beam where the anchor lands.
                Exclude = { "Colliders" },
                At = new LocalPoint(centre - Quaternion.Euler(0, yaw, 0) * (centre - PropertyPivot), yaw + PropertyYaw)
            };
            // Pieces of one handful share its yaw, so their ends and sides are kept apart the same way.
            var sides = (X: new List<float>(), Z: new List<float>());
            for (int i = 0; i < pieces; i++)
            {
                float angle = Between(0f, Mathf.PI * 2f), distance = radius * Mathf.Sqrt(Between(0f, 1f));
                float px = x + Mathf.Cos(angle) * distance, pz = z + Mathf.Sin(angle) * distance;
                bool board = random.Next(3) == 0;
                float halfLength = Between(.07f, .22f), halfWidth = board ? Between(.05f, .09f) : Between(.022f, .035f);
                float lift = Between(0f, (1f - distance / radius) * .06f);
                float tall = board ? Between(.016f, .022f) : Between(.04f, .055f);
                // The flicker check counts faces within 2 mm as one plane: each bottom and each top moves 3 mm until
                // no other piece on this spot has one that close.
                float low = Apart(levels.Bottoms, floor - .008f + lift, -.003f);
                float high = Apart(levels.Tops, floor + lift + tall, .003f);
                float west = Apart(sides.X, px - halfLength, -.003f), east = Apart(sides.X, px + halfLength, .003f);
                float south = Apart(sides.Z, pz - halfWidth, -.003f), north = Apart(sides.Z, pz + halfWidth, .003f);
                Face(handful, "piece-" + i, board ? Boards : Timber, west, low, south, east, high, north);
            }
            state.Copies.Add(handful);
        }
    }

    private static float Apart(List<float> used, float value, float step)
    {
        while (used.Any(u => Math.Abs(u - value) < .0025f)) value += step;
        used.Add(value);
        return value;
    }

    // Places a copy so that the source point lands on the target, turned by yaw and scaled around it. The frame is the
    // property root, which stands turned by PropertyYaw.
    private static CopyDefinition Copy(string id, string template, Vector3 source, Vector3 target, float yaw, float scale = 1f) => new()
    {
        Id = id, Template = template, Frame = Property, Scale = scale,
        At = new LocalPoint(target - Quaternion.Euler(0, yaw, 0) * (scale * (source - PropertyPivot)), yaw + PropertyYaw),
        IndependentMesh = true, VisualOnly = true, Obstacle = false
    };

    private static void Block(CopyDefinition copy, string id, float x1, float y1, float z1, float x2, float y2, float z2) =>
        copy.Boxes.Add(new BoxDefinition { Id = id, Kind = BoxKind.Block, Box = BoundsOf(x1, y1, z1, x2, y2, z2) });

    private static void Face(CopyDefinition copy, string id, string material, float x1, float y1, float z1, float x2, float y2, float z2) =>
        copy.Boxes.Add(new BoxDefinition { Id = id, Kind = BoxKind.Panel, Material = material, TileMetres = 1f, Box = BoundsOf(x1, y1, z1, x2, y2, z2) });

    private static Bounds BoundsOf(float x1, float y1, float z1, float x2, float y2, float z2)
    {
        var bounds = new Bounds();
        bounds.SetMinMax(new Vector3(x1, y1, z1), new Vector3(x2, y2, z2));
        return bounds;
    }
}
