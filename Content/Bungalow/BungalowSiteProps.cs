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
    // The terrain layer of the gravel forecourt in front of the house, read with mxsplat.
    internal const string ForecourtLayer = "Terrain_Gravel";

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

    // The way from the street to the stair's foot and the front door runs east of the house between z 110.9 and 115.3;
    // nothing of the site stands in it. The table stands north of it, long side across the yard, between the house and
    // the skip, on ground from -3.93 to -3.97. The foreman reads the plan from its east end and walks to its west end.
    private static readonly Vector3 TableAt = new(-161.4f, -3.96f, 116.35f);
    // The timber stock north of the house, between the house's corner and the tree.
    private static readonly Vector3 StockAt = new(-162.75f, -3.95f, 122.75f);
    private static readonly Vector3 FireDrumAt = new(-159.4f, -4f, 118.6f);
    internal static readonly Vector3 FireSpot = FireDrumAt + new Vector3(0, .85f, 0);

    internal static readonly (Vector3 At, Vector3 Face) TableFront = (new(-159.6f, -4f, 116.35f), new(-160.5f, -3.25f, 116.35f));
    internal static readonly (Vector3 At, Vector3 Face) TableSide = (new(-162.95f, -3.93f, 116.35f), new(-162.3f, -3.25f, 116.35f));

    // The pine before the stair falls on the day the storey is ordered; the woodcutter stands on its north side.
    internal static readonly Vector2 StairTree = new(-161.36f, 112.48f);
    internal static readonly (Vector3 At, Vector3 Face) StairTreeCut = (new(-161.36f, -3.94f, 113.15f), new(-161.36f, -3.05f, 112.55f));

    // The west kit stands on the gravel between the deck and the sunroom while either is built: a table with the plan
    // north of the garden table, the pallet south of the west door, a bucket and offcuts. Each stands at least 0.6 m from
    // the shrub (x -185.98 .. -182.82, z from 118.79), the garden table (x -186.84 .. -184.43, z 113.42 .. 115.45) and
    // the trunks at (-186.7, 117.3) and (-186.2, 107.8); the way out of the west door (z 110.15 .. 112.65) stays open.
    // Table and pallet stand a few degrees off the house's lines. The foreman reads at the table from its south side.
    private static readonly Vector3 WestTableAt = new(-184.75f, -4f, 117.4f);
    private const float WestTableYaw = 8f;
    private static readonly Vector3 WestStockAt = new(-184.3f, -3.94f, 108.9f);
    private const float WestStockYaw = 12f;
    internal static readonly (Vector3 At, Vector3 Face) WestTableFront = (new(-184.4f, -4f, 116.05f), new(-184.6f, -3.25f, 116.9f));
    internal static readonly (Vector3 At, Vector3 Face) WestTableSide = (new(-185.6f, -4f, 116.2f), new(-185.5f, -3.25f, 117.0f));
    // North of the west stock pile, toward the door, and west of it.
    internal static readonly (Vector3 At, Vector3 Face) WestStockFront = (new(-184.3f, -3.99f, 110.6f), new(-184.3f, -3.3f, 109.75f));
    internal static readonly (Vector3 At, Vector3 Face) WestStockSide = (new(-185.7f, -4.04f, 109.2f), new(-185.0f, -3.3f, 109.05f));

    // Once the frames stand, the kit moves into the sunroom onto the slab: the table in the north half, the pallet
    // against the house wall north of the way between the two doors (z 109.3 .. 113), which stays open.
    private const float SunroomFloor = -3.70f;
    private static readonly Vector3 InsideTableAt = new(-180.8f, SunroomFloor, 118.6f);
    private const float InsideTableYaw = 70f;
    private static readonly Vector3 InsideStockAt = new(-178.6f, SunroomFloor, 114.3f);
    private const float InsideStockYaw = -8f;
    internal static readonly (Vector3 At, Vector3 Face) InsideTableFront = (new(-179.6f, SunroomFloor, 118.9f), new(-180.2f, -2.95f, 118.9f));
    internal static readonly (Vector3 At, Vector3 Face) InsideTableSide = (new(-181.0f, SunroomFloor, 116.75f), new(-180.7f, -2.95f, 117.4f));
    internal static readonly (Vector3 At, Vector3 Face) InsideStockFront = (new(-178.6f, SunroomFloor, 115.95f), new(-178.6f, -3.0f, 115.1f));
    internal static readonly (Vector3 At, Vector3 Face) InsideStockSide = (new(-180.1f, SunroomFloor, 114.3f), new(-179.4f, -3.0f, 114.3f));
    // On the carport's one day the foreman watches the pines come down from the street side.
    internal static readonly (Vector3 At, Vector3 Face) CarportWatch = (new(-160.5f, -3.92f, 108.8f), new(-165.9f, -2.8f, 104.4f));
    // In the north bay, facing the posts on the bay line.
    internal static readonly (Vector3 At, Vector3 Face) CarportMeasure = (new(-161.2f, -3.92f, 110.0f), new(-163.5f, -3.0f, 109.5f));
    internal static readonly (Vector3 At, Vector3 Face) CarportMeasureSide = (new(-160.9f, -3.92f, 107.6f), new(-163.2f, -3.0f, 107.0f));
    internal static readonly (Vector3 At, Vector3 Face) CarportWatchSide =(new(-159.6f, -3.92f, 106.6f), new(-161.5f, -3f, 104.45f));
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

    // 1 for the floor, 2 the frame, 3 the walls, 4 the rafters, 5 the roof: the state that stands, from the quest's phase
    // or the world state's id. Anything else is 0.
    internal static int LevelOf(string? phase) => phase switch
    {
        "storey-floor" or BungalowStorey.FloorState => 1,
        "storey-frame" or BungalowStorey.FrameState => 2,
        "storey-walls" or BungalowStorey.WallsState => 3,
        "storey-rafters" or BungalowStorey.RaftersState => 4,
        "storey-roof" or BungalowStorey.RoofState => 5,
        "complete" => 6,
        _ => 0
    };

    // Where the fitter stands and what he welds on the days after the floor and the frame stand: the bracket of the wall
    // lantern under the landing, from the ground below it (-3.93 to -3.96). On the other days he is off the job.
    internal static (Vector3 Stand, Vector3 Spark)? Weld(int level) => level is 1 or 2
        ? (new Vector3(-168.85f, -3.96f, 112.5f), new Vector3(-169.68f, -1.85f, 112.5f))
        : null;

    internal static (Vector3 At, Vector3 Face) Helper(bool secondary) => secondary ? EastWallCheck : PorchPost;

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
        if (level >= 1) MapTrees.Remove(state, "stair-tree", StairTree.x, StairTree.y);
        if (level >= 1) Path(state, level);
        if (level <= 5) Site(state, site, level);
        else Cleared(state);
        if (site.Boxes.Count > 0) state.Copies.Add(site);
    }

    // The crew's feet wear the grass away between the pavement and the stair foot, wider with every night of work; the
    // trodden strip stays once the storey is done.
    private const float PathFromX = -161.75f;
    private const float PathToX = -159.05f;
    private const float PathCentreZ = 112.1f;

    internal static float PathWidth(int level) => Mathf.Min(.5f + .25f * (Mathf.Min(level, 5) - 1), 1.5f);

    private static void Path(WorldStateDefinition state, int level)
    {
        float half = PathWidth(level) * .5f;
        state.Rules.Add(new ObjectRule
        {
            Id = "crew-path", Name = "", Side = WorldSide.Main, Effect = ObjectEffect.ClearDetails,
            Area = BoundsOf(PathFromX, -20f, PathCentreZ - half, PathToX, 20f, PathCentreZ + half)
        });
        // Once the storey is done the strip is laid with the forecourt's gravel, as a patch from the stair's foot to the
        // pavement that reaches north into the forecourt; a painted line this short frays to a quarter of the layer.
        if (level >= 6)
        {
            state.Rules.Add(new ObjectRule
            {
                Id = "crew-path-gravel", Name = "", Side = WorldSide.Main, Effect = ObjectEffect.Paint,
                PaintLayer = ForecourtLayer, PaintStrength = 1f,
                Area = BoundsOf(PathFromX - .3f, -20f, PathCentreZ - .9f, PathToX + .2f, 20f, PathCentreZ + 1.6f)
            });
            return;
        }
        // Where the grass is gone the ground shows as trodden dirt, along a line that bends a little on its way.
        var dirt = new ObjectRule
        {
            Id = "crew-path-dirt", Name = "", Side = WorldSide.Main, Effect = ObjectEffect.Paint,
            PaintLayer = "Terrain_Dirt", PaintStrength = .85f, PaintWidth = PathWidth(level)
        };
        dirt.PaintPath.AddRange(new[] { new Vector2(PathFromX, PathCentreZ), new Vector2(-160.5f, 111.85f), new Vector2(-159.3f, 111.95f), new Vector2(PathToX, PathCentreZ) });
        state.Rules.Add(dirt);
    }

    private static void Site(WorldStateDefinition state, CopyDefinition site, int level)
    {
        SiteTable(state);
        Stock(state, level switch { 1 => 12, 2 => 8, 3 => 6, 4 => 4, _ => 2 });
        state.Copies.Add(Copy("site-fire-drum", Camp + "DrumFirepit", FireDrumPivot, FireDrumAt, 17));
        Block(site, "site-fire-drum-block", FireDrumAt.x - .32f, FireDrumAt.y, FireDrumAt.z - .32f, FireDrumAt.x + .32f, FireDrumAt.y + .9f, FireDrumAt.z + .32f);
        Cinderblock(state, "site-cinderblock-a", -158.85f, -3.95f, 118.05f, 23);
        Cinderblock(state, "site-cinderblock-b", -159.75f, -4f, 119.45f, -38);
        var generator = new Vector3(-159.3f, -4f, 120.6f);
        state.Copies.Add(Copy("site-generator", BuildSite + "SM_Prop_Generator_01", GeneratorPivot, generator, 85));
        Block(site, "site-generator-block", generator.x - .35f, generator.y, generator.z - .5f, generator.x + .35f, generator.y + .7f, generator.z + .5f);
        state.Copies.Add(Copy("site-jerry-can", Camp + "Jerry Can", JerryCanPivot, new Vector3(-158.8f, -3.95f, 121.4f), 152));
        state.Copies.Add(Copy("site-bucket", Camp + "SM_Prop_Bucket_01", BucketPivot, new Vector3(-163.35f, -3.83f - BucketUprightDrop, 119.4f), 47));
        // The site sign faces the street south of the way in; the cones flank the way from the pavement to the stair.
        state.Copies.Add(Copy("site-sign", BuildSite + "CS_sign_02", SignPivot, new Vector3(-158.9f, -3.9f, 109.4f), -80));
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
        Offcuts(state, "ground-offcuts", -162.6f, -3.9f, 119.6f, .3f, 5);
        switch (level)
        {
            case 1:
                // The night the old roof came off: its timber lies in heaps on the new floor and by the east wall.
                Offcuts(state, "roof-debris-middle", -170.4f, UpperBoards, 116.6f, .5f, 83);
                Offcuts(state, "roof-debris-west", -175.0f, UpperBoards, 110.6f, .4f, 89);
                Offcuts(state, "roof-debris-north", -167.6f, UpperBoards, 120.6f, .38f, 97);
                Offcuts(state, "roof-debris-ground", -163.25f, -3.83f, 120.4f, .42f, 79);
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
                // Rafter ends sawn off at the eaves lie in the rooms under the open roof.
                Offcuts(state, "rafter-offcuts-south", -173.2f, UpperBoards, 113.4f, .32f, 101);
                Offcuts(state, "rafter-offcuts-east", -166.8f, UpperBoards, 119.2f, .26f, 103);
                Offcuts(state, "rafter-offcuts-north", -172.6f, UpperBoards, 118.0f, .2f, 107);
                break;
            case 5:
                Offcuts(state, "room-offcuts-east", -166.8f, UpperBoards, 119.2f, .2f, 67);
                Offcuts(state, "room-offcuts-stray", -173.2f, UpperBoards, 113.4f, .14f, 71);
                break;
        }
    }

    // Once the storey is handed over, the site is gone; its trash, the empty crate and a block wait by the house's
    // north-east corner, out of the way to the stair.
    private static void Cleared(WorldStateDefinition state)
    {
        state.Copies.Add(Copy("yard-trash-can", Camp + "Trash Can Prop", TrashCanPivot, new Vector3(-162.7f, -3.93f, 122.4f), 33));
        state.Copies.Add(Copy("yard-trash-drum", Camp + "Liquid Drum Hollow with trash", TrashDrumPivot, new Vector3(-162.0f, -3.95f, 123.05f), -61));
        state.Copies.Add(Copy("yard-crate", Camp + "Wood Crate Prop (1)", CratePivot, new Vector3(-161.2f, -3.98f, 123.9f), 52));
        Cinderblock(state, "yard-cinderblock", -160.6f, -4f, 123.3f, 141);
    }

    // The west kit for the deck's stilts day and the sunroom's slab day.
    internal static void WestKit(WorldStateDefinition state, string prefix, int planks, int seed) =>
        Kit(state, prefix, WestTableAt, WestTableYaw, WestStockAt, WestStockYaw, new Vector3(-183.5f, -4f, 116.2f), 113, new Vector3(-185.8f, -4.03f, 110.4f), planks, seed);

    // The kit inside the sunroom on its frames day.
    internal static void InsideKit(WorldStateDefinition state, string prefix, int planks, int seed) =>
        Kit(state, prefix, InsideTableAt, InsideTableYaw, InsideStockAt, InsideStockYaw, new Vector3(-181.6f, SunroomFloor, 114.9f), 40, new Vector3(-179.8f, SunroomFloor, 116.6f), planks, seed);

    // The carport's kit on its posts day stands in the second bay, which has no dock until the roof is on: the table
    // between the west and the middle post line, the stock between the middle and the east one. The bay's ground lies
    // between -3.93 and -4.02 there.
    private static readonly Vector3 CarportTableAt = new(-167.0f, -3.97f, 106.6f);
    private static readonly Vector3 CarportStockAt = new(-163.6f, -3.95f, 106.7f);
    internal static readonly (Vector3 At, Vector3 Face) CarportTableFront = (new(-167.0f, -3.98f, 107.45f), new(-167.0f, -3.2f, 106.75f));
    internal static readonly (Vector3 At, Vector3 Face) CarportTableSide = (new(-165.4f, -3.97f, 106.6f), new(-166.0f, -3.2f, 106.6f));
    internal static readonly (Vector3 At, Vector3 Face) CarportStockFront = (new(-163.6f, -3.97f, 107.75f), new(-163.6f, -3.35f, 107.0f));
    internal static readonly (Vector3 At, Vector3 Face) CarportStockSide = (new(-162.55f, -3.96f, 107.1f), new(-163.0f, -3.35f, 106.9f));

    internal static void CarportKit(WorldStateDefinition state, int planks, int seed) =>
        Kit(state, "carport-kit", CarportTableAt, 0f, CarportStockAt, 0f, new Vector3(-164.9f, -3.96f, 107.6f), 61, new Vector3(-165.4f, -3.96f, 107.95f), planks, seed);

    private static void Kit(WorldStateDefinition state, string prefix, Vector3 tableAt, float tableYaw, Vector3 stockAt, float stockYaw, Vector3 bucket, float bucketYaw, Vector3 offcuts, int planks, int seed)
    {
        SiteTable(state, prefix, tableAt, tableYaw);
        Stock(state, prefix, stockAt, stockYaw, planks);
        state.Copies.Add(Copy(prefix + "-bucket", Camp + "SM_Prop_Bucket_01", BucketPivot, bucket - new Vector3(0f, BucketUprightDrop, 0f), bucketYaw));
        Offcuts(state, prefix + "-offcuts", offcuts.x, offcuts.y, offcuts.z, .25f, seed);
    }

    // Kit footprints (centre, yaw, half size along the turned x and z) for the vegetation check in tests.
    internal static readonly (string Id, Vector3 At, float Yaw, float HalfX, float HalfZ)[] WestFootprints =
    {
        ("west-table", WestTableAt, WestTableYaw, 1.26f * TableScale, .61f * TableScale),
        ("west-stock", WestStockAt, WestStockYaw, .78f, .78f),
        ("inside-table", InsideTableAt, InsideTableYaw, 1.26f * TableScale, .61f * TableScale),
        ("inside-stock", InsideStockAt, InsideStockYaw, .78f, .78f)
    };

    // A copy of the anchor turned by yaw about centre. Its boxes are given level, in world coordinates as if unturned.
    private static CopyDefinition Turned(string id, Vector3 centre, float yaw) => new()
    {
        Id = id, Template = Anchor, Frame = Property, OwnMesh = false, Obstacle = false,
        // The post only anchors the boxes; its collider child would stand as an unseen beam where the anchor lands.
        Exclude = { "Colliders" },
        At = new LocalPoint(centre - Quaternion.Euler(0, yaw, 0) * (centre - PropertyPivot), yaw + PropertyYaw)
    };

    private static void SiteTable(WorldStateDefinition state) => SiteTable(state, "site", TableAt, 0f);

    // At yaw 0 the long side runs along x; the block and the plan turn with the table.
    private static void SiteTable(WorldStateDefinition state, string prefix, Vector3 at, float yaw)
    {
        state.Copies.Add(Copy(prefix + "-table", FoldingTable, FoldingTablePivot, at, yaw, TableScale));
        float halfZ = .61f * TableScale, halfX = 1.26f * TableScale, top = at.y + .75f * TableScale;
        var table = Turned(prefix + "-table-boxes", at, yaw);
        Block(table, prefix + "-table-block", at.x - halfX, at.y, at.z - halfZ, at.x + halfX, top, at.z + halfZ);
        Face(table, prefix + "-table-plan", "clipboard_paper_mat", at.x - .25f, top + .002f, at.z - .16f, at.x + .25f, top + .004f, at.z + .16f);
        state.Copies.Add(table);
    }

    private static void Stock(WorldStateDefinition state, int planks) => Stock(state, "site", StockAt, 0f, planks);

    // A pallet with planks stacked side by side across their length, four to a layer; the planks turn with the pallet.
    private static void Stock(WorldStateDefinition state, string prefix, Vector3 at, float yaw, int planks)
    {
        state.Copies.Add(Copy(prefix + "-pallet", Pallet, PalletPivot, at, yaw));
        var pallet = Turned(prefix + "-pallet-boxes", at, yaw);
        Block(pallet, prefix + "-pallet-block", at.x - .78f, at.y, at.z - .78f, at.x + .78f, at.y + .55f, at.z + .78f);
        state.Copies.Add(pallet);
        var turn = Quaternion.Euler(0, yaw, 0);
        float[] shift = { .015f, -.025f, .005f, -.01f, .03f, -.015f, .01f, -.03f, .02f, 0f, -.02f, .025f };
        // Planks side by side touch at their edges, so each of a layer lies 5 mm above the one before and no two tops share
        // a plane; the mesh leans by about 1 mm across its width, which a smaller step would not clear. The first layer lies 2 mm above the pallet deck and each further layer 2 mm above the one below, so no
        // plank's underside lies in the face it rests on.
        for (int i = 0; i < planks; i++)
            state.Copies.Add(Copy(prefix + "-plank-" + i, Plank, PlankPivot,
                at + turn * new Vector3(shift[i], 0f, ((i % 4) - 1.5f) * .125f)
                   + new Vector3(0f, PalletDeck + .002f + PlankThickness * (.5f + i / 4) + (i / 4) * .002f + (i % 4) * .005f, 0f),
                90 + yaw));
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
        var levels = (Bottoms: new List<(float Y, float X, float Z, float R)>(), Tops: new List<float>());
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
                // Bottoms only clash where two pieces cover the same floor, so they are compared within reach: the piece's
                // middle turned into the heap's frame, and the circle round its corners.
                var middle = Quaternion.Euler(0, yaw, 0) * new Vector3(px - x, 0f, pz - z);
                float low = SinkApart(levels.Bottoms, floor - .008f + lift, floor, middle.x, middle.z,
                    Mathf.Sqrt(halfLength * halfLength + halfWidth * halfWidth));
                float high = Apart(levels.Tops, floor + lift + tall, .003f);
                float west = Apart(sides.X, px - halfLength, -.003f), east = Apart(sides.X, px + halfLength, .003f);
                float south = Apart(sides.Z, pz - halfWidth, -.003f), north = Apart(sides.Z, pz + halfWidth, .003f);
                Face(handful, "piece-" + i, board ? Boards : Timber, west, low, south, east, high, north);
            }
            state.Copies.Add(handful);
        }
    }

    // A piece's bottom moves in 3 mm steps either way from where it would lie until no piece within reach has one that
    // close, but stays no lower than 3.5 cm under the floor's top, and a piece lying flat stays at least 2 mm in the
    // floor: one way only and across the whole heap, a run of flat pieces walked down through the boards to their
    // underside.
    private static float SinkApart(List<(float Y, float X, float Z, float R)> used, float value, float floor, float x, float z, float reach)
    {
        float lowest = floor - .035f, highest = Math.Max(value, floor - .002f);
        bool Free(float y) => !used.Any(u => Math.Abs(u.Y - y) < .0025f && (u.X - x) * (u.X - x) + (u.Z - z) * (u.Z - z) < (u.R + reach) * (u.R + reach));
        float chosen = Mathf.Clamp(value, lowest, highest);
        for (int k = 0; k < 24; k++)
        {
            float down = value - k * .003f, up = value + k * .003f;
            if (down >= lowest && down <= highest && Free(down)) { chosen = down; break; }
            if (up >= lowest && up <= highest && Free(up)) { chosen = up; break; }
        }
        used.Add((chosen, x, z, reach));
        return chosen;
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
