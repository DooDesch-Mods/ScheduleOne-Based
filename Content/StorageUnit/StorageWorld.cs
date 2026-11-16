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

    // A steel stair of the docks shed rises 3.05 m over a 5.43 m run, its top edge centred on this point. "Stairs (1)",
    // because the first "Stairs" in that shed shares its name with a white frame object that a path lookup finds first.
    private const string Stair = "@Properties/DocksWarehouse/IndustrialShed/dockshed/Stairs (1)";
    private static readonly Vector3 StairTop = new(-90.87f, .83f, -54.3f);
    // Turned to climb north along the west wall. At its own size it reaches from the ground to the 3.30 m of the ledge
    // on top of the lower wall; its nosings rise 3.05 m from the first step at 0.25.
    private const float StairYaw = 30f, StairScale = 1f, StairRise = 3.05f, StairRun = 5.43f;
    // The stringers end in a wedge: 0.15 m short of the stair's top edge they reach its full height, then fall away by
    // 0.25 m. The top tread lies 0.24 m below that height. The stair reaches this far into the landing and sits 1 cm
    // lower, so the wedge's peak meets the landing's south edge inside the deck plate and its end tucks under the frame.
    private const float StairInto = .15f, StairDrop = .01f;
    private const string Metal = "metal_darkgrey_mat";

    // The upper floor lies at 3.25; the ledge along the west wall and the landing in front of it end at 3.30.
    private const float UpperY = 3.25f, LandingTop = 3.30f, LandingBottom = 3.20f;
    internal const float UpperFloor = UpperY;
    // The landing is centred on the door.
    private const float LandingWest = -10.65f, LandingEast = -9.01f, LandingSouth = 104.6f, LandingNorth = 106.3f;
    private const float RailTop = LandingTop + 1.05f, GuardTop = LandingTop + 2.1f;

    // The four small units above Unit 1 (Unit6) and the upper west wall in front of them.
    private const float UnitWest = -8.75f, UnitEast = -2.05f, UnitSouth = 103.8f, UnitNorth = 113.8f, UnitTop = 6.25f;
    private const string UnitSheet = "alum sheet med grey", FloorSheet = "metal_mediumgrey_mat";
    // The back of the walkway's unit faces inside the block, and the loft's ceiling under the roof's eave.
    private const float FacesBack = -2.25f, LoftCeiling = 6.0f;
    // The door through the west wall, over the landing. It takes the place of the third window from the south: the cut
    // has the window's width and head, down to the floor, so no part of the window's recess is left beside it. The
    // leaf is narrower than the window; steel jambs of equal width on both sides centre its opening in the cut.
    private const float WindowSouth = 104.85f, WindowNorth = 106.10f, DoorSill = 3.31f, DoorHead = 5.65f;
    private const float DoorLeafWidth = 1.06f, DoorClear = DoorLeafWidth + .04f;
    private const float DoorSouth = (WindowSouth + WindowNorth - DoorClear) / 2f, DoorNorth = DoorSouth + DoorClear;
    private static readonly Bounds DoorCut = BoundsOf(-9.3f, DoorSill, WindowSouth, -8.7f, DoorHead, WindowNorth);
    private const string DoorModel = "@Properties/StorageUnit/Industrial Metal Door (2)/Container/IndustrialMetalDoor/industrialmetaldoor/";
    private const string DoorLeaf = DoorModel + "Door", DoorHandle = DoorModel + "Handle";
    // The leaf's hinge edge at the floor; the handle sits at its east edge.
    private static readonly Vector3 DoorHinge = new(-5.78f, .1f, 103.9f);

    // The walkway's railing along the hall, one 1.9 m piece; its post at the walkway's north end is where the gallery's
    // railing ends.
    private const string Fence = "Map/Hyland Point/Region_Northtown/Storage warehouse/Storage warehouse/OutdoorFence";
    private static readonly Vector3 FencePost = new(-2.1f, 3.25f, 101.79f);
    private static readonly Vector3 RailEnd = new(-2.1f, 3.25f, 103.69f);
    // Unit 1's ceiling light, hung the same distance under the loft's ceiling, on Unit 1's switch.
    private const string LampModel = "Map/Hyland Point/Region_Northtown/Storage warehouse/Storage Unit/FluroBar (1)";
    private static readonly Vector3 LampPivot = new(-6.52f, 2.87f, 107.23f);
    private const float UnitCeiling = 3.1f, LampRange = 3.6f;
    private const string RoomSwitch = "Map/Hyland Point/Region_Northtown/Storage warehouse/Storage Unit/ModularSwitch";
    // Saved id of the loft's property extension; renaming it loses what stands in the loft.
    public const string LoftExtensionId = "storage-loft-floor";
    // The unit's own grid has its tile (0, 0) at (-4.00, 0.10, 113.75). The loft grid starts 1.5 m further east, at
    // the loft's east wall, so all its tiles have coordinates from 0 up, the range the heatmap colours.
    private const float LoftGridX = -2.5f, LoftGridZ = 113.75f;

    // B: the walkway east of the loft (x -2.05 .. 0.25) and Unit5 behind it, up to the hall's east wall.
    public const string EastExtensionId = "storage-east-floor";
    private const float EastFront = .25f, EastWall = 3.25f, SiteWallHeight = LoftCeiling - UpperY;
    private const string Wood = "wood planks medium brown mat", Trim = "wood brown", Brick = "brick dark light orange";

    // The crew's site, with the same templates as the Sweatshop's build.
    private const string Pallet = "Map/Hyland Point/Region_Northtown/North Waterfront/Pallet Prop (2)";
    private static readonly Vector3 PalletPivot = new(-57.9526f, -.348f, 143.1308f);
    private const string Plank = "Map/Hyland Point/Region_Westville/Tattoo Parlour New/Interior/GameObject/SM_Prop_Plank_01";
    private static readonly Vector3 PlankPivot = new(-131.62f, -3f, 69.53f);
    // A pallet's deck lies 0.2 above its pivot; the pivot of a plank sits halfway up its thickness.
    private const float PalletDeck = .2f, PlankThickness = .04f;
    private const string ManorSite = "@Properties/Manor/Construction/";
    private const string BuildSite = "Map/Hyland Point/Region_Westville/Construction Site/";
    private static readonly Vector3 BricksPivot = new(169.16f, 10.12f, -43.91f);
    private static readonly Vector3 BarrelsPivot = new(171.41f, 10.15f, -42.39f);
    private static readonly Vector3 GeneratorPivot = new(-127.05f, -4f, 94.47f);
    private static readonly Vector3 SignPivot = new(-135.09f, -4.26f, 93.17f);
    private static readonly Vector3 ConePivot = new(-132.98f, -3.77f, 99.88f);
    private const float ConeCentre = .23f;
    private const string FoldingTable = "Map/SpecialCustomerCamps/BikerCamp/Container/Props/Table_Foldout/Table_Foldout_LOD0";
    private static readonly Vector3 FoldingTablePivot = new(-142.02f, -3.9f, 50.01f);
    private const float TableScale = .9f;
    // Trash and tools from the camp in Westville. Each pivot is the bottom centre of the prop's main mesh.
    private const string Camp = "Map/Hyland Point/Region_Westville/Encampment/";
    private static readonly Vector3 FireDrumPivot = new(-96.49f, -4.06f, 59.52f);
    private static readonly Vector3 TrashCanPivot = new(-94.74f, -4.03f, 59.14f);
    private static readonly Vector3 TrashDrumPivot = new(-91.39f, -3.89f, 62.26f);
    private static readonly Vector3 CratePivot = new(-91.51f, -4.00f, 57.01f);
    private static readonly Vector3 JerryCanPivot = new(-95.21f, -4.05f, 58.92f);
    private static readonly Vector3 BucketPivot = new(-95.43f, -3.28f, 58.37f);
    private static readonly Vector3 GasBottlePivot = new(-95.93f, -4.33f, 54.70f);
    private static readonly Vector3 CinderblockPivot = new(-96.74f, -4.08f, 59.92f);
    // The fire barrel stands in the yard west of the stair, south of the bush (x -13.11 .. -10.63, z 103.08 .. 105.48) and
    // clear of the stair (x from -10.49) and the street lamp's base (x -12.5 .. -11.5, z 100.5 .. 101.5).
    private static readonly Vector3 FireDrumAt = new(-12.35f, 0f, 102.35f);
    internal static readonly Vector3 FireSpot = FireDrumAt + new Vector3(0, .85f, 0);
    // One row of lamps over the middle; within this reach the light ends before the outside of the east wall (3.51).
    private const float EastLampX = .6f, EastLampRange = 2.9f;

    // C: the walkway south of B (x -2.05 .. 0.25) and the three units of Unit4 east of it. The elevator stands between
    // them and B's room; its shaft behind it stays closed.
    public const string SouthExtensionId = "storage-south-floor";
    private const float SouthEnd = 94f, SouthUnitNorth = 101.5f, SouthGridZ = 103.75f;
    private const float ElevatorWest = .71f;
    // Within this reach the light ends before the outside of the east wall (3.51) and the south wall (93.5).
    private const float SouthLampX = 1.1f, SouthLampRange = 2.35f;

    // Order of the states; each level includes all lower ones.
    private static readonly string[] States = { "a-stair", "a-door", "a-complete", "b-open", "b-complete", "c-open", "c-complete" };

    private static bool _registered;

    // 1 for the first state, 0 for none or an unknown one.
    internal static int LevelOf(string? state) => state == null ? 0 : Array.IndexOf(States, state) + 1;

    // Where the crew cuts or welds on the night that builds the stage after this one, and which way the sparks fly.
    internal static (Vector3 At, float Yaw)? SparkSpot(int level) => level switch
    {
        0 => (new Vector3(-9.4f, 2.2f, 102f), 0f),
        1 => (new Vector3(-9.4f, LandingTop + 1.1f, (WindowSouth + WindowNorth) / 2f), 0f),
        2 => (new Vector3(-6.5f, LoftCeiling - .45f, UnitSouth + .2f), 0f),
        3 => (new Vector3(UnitEast - .1f, UpperY + 1.5f, 108.8f), 0f),
        4 => (new Vector3(EastFront + .1f, UpperY + 1.5f, 108.8f), 0f),
        5 => (new Vector3(-1f, UpperY + 1.4f, UnitSouth + .25f), 0f),
        6 => (new Vector3(ElevatorWest - .15f, UpperY + 1.4f, 102.6f), 0f),
        _ => null
    };

    public static void Register()
    {
        if (_registered) return;
        if (!MapExpansionsApi.Register(Create()))
            throw new InvalidOperationException("Based could not register the Storage Unit world states; inspect the MapExpansions validation errors.");
        _registered = true;
        StorageDock.Register();
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
        if (level >= 2) Doorway(state, geometry, level);
        if (level >= 3) Gallery(state, geometry, level);
        if (level >= 4) WalkwayOpen(state, geometry, level);
        if (level >= 5) EastRoom(state, geometry);
        if (level >= 6) SouthOpen(state, geometry);
        if (level >= 7) SouthRoom(state, geometry);
        SiteProps(state, geometry, level);
        state.Copies.Add(geometry);
        return state;
    }

    // A tipped bucket rests on its rim, a jerry can on its broad side; the lift keeps either out of the floor.
    private static readonly Vector3 BucketTipped = new(88f, 0f, 0f), JerryCanFlat = new(0f, 0f, 90f);
    private const float BucketTippedLift = .17f, JerryCanFlatLift = .08f;

    // What the crew leaves on site for one night of work. The timber stock shrinks from build to build; each night's site
    // stands where that night's work is and is gone once the work is done. Props sit in small groups with one stray,
    // a hand's width to half a metre off the wall, turned off the grid; every offset is a fixed number, the same on
    // every machine.
    private static void SiteProps(WorldStateDefinition state, CopyDefinition geometry, int level)
    {
        // The fire barrel stays in the yard with two blocks beside it; the effects burn in it at night.
        state.Copies.Add(Copy("yard-fire-drum", Camp + "DrumFirepit", FireDrumPivot, FireDrumAt, 17));
        Cinderblock(state, "yard-cinderblock-a", -12.95f, 0f, 102.85f, 23);
        Cinderblock(state, "yard-cinderblock-b", -11.7f, 0f, 102.05f, -38);
        // Once a stage is done the crew's site goes, and its trash ends up in the yard: two bins together by the stair
        // foot's north end, the empty crate on its own further south with a block tipped beside it.
        if (level >= 3)
        {
            state.Copies.Add(Copy("yard-trash-can", Camp + "Trash Can Prop", TrashCanPivot, new Vector3(-10.92f, 0f, 102.6f), 33));
            state.Copies.Add(Copy("yard-trash-drum", Camp + "Liquid Drum Hollow with trash", TrashDrumPivot, new Vector3(-11.05f, 0f, 101.85f), -61));
            state.Copies.Add(Copy("yard-crate", Camp + "Wood Crate Prop (1)", CratePivot, new Vector3(-12.6f, 0f, 99.6f), 52));
            state.Copies.Add(Copy("yard-cinderblock-tilted", Camp + "Cinderblock (1)", CinderblockPivot, new Vector3(-12.05f, .04f, 99.35f), 141,
                tilt: new Vector3(0f, 0f, 14f)));
        }
        switch (level)
        {
            case 1:
                Outside(state);
                state.Copies.Add(Copy("outside-barrels", ManorSite + "SM_Prop_BarrelStack_01", BarrelsPivot, new Vector3(-11.0f, 0f, 100.95f), 27));
                break;
            case 2:
                Outside(state);
                Stock(state, geometry, "loft", -3.3f, UpperY, 112.7f, 12);
                state.Copies.Add(Copy("loft-barrels", ManorSite + "SM_Prop_BarrelStack_01", BarrelsPivot, new Vector3(-7.85f, UpperY, 112.95f), 13));
                state.Copies.Add(Copy("loft-generator", BuildSite + "SM_Prop_Generator_01", GeneratorPivot, new Vector3(-3.25f, UpperY, 104.85f), -7));
                SiteTable(state, geometry, "loft-table", -5.5f, UpperY, 108.8f, 4);
                Clutter(state, geometry, "loft-trash-can", Camp + "Trash Can Prop", TrashCanPivot, -6.55f, UpperY, 113.25f, 37, .32f, .77f);
                Clutter(state, geometry, "loft-crate", Camp + "Wood Crate Prop (1)", CratePivot, -8.2f, UpperY, 111.45f, 14, .45f, .8f);
                Clutter(state, geometry, "loft-gas-bottle", Camp + "Gas Bottle Prop (1)", GasBottlePivot, -2.48f, UpperY, 106.14f, 21, .32f, .72f);
                // The jerry can lies on its side by the generator, the bucket is tipped over beside the table.
                state.Copies.Add(Copy("loft-jerry-can", Camp + "Jerry Can", JerryCanPivot, new Vector3(-3.9f, UpperY + JerryCanFlatLift, 104.3f), 68,
                    tilt: JerryCanFlat));
                state.Copies.Add(Copy("loft-bucket", Camp + "SM_Prop_Bucket_01", BucketPivot, new Vector3(-4.1f, UpperY + BucketTippedLift, 108.3f), 133,
                    tilt: BucketTipped));
                Cinderblock(state, "loft-cinderblock-a", -6.05f, UpperY, 113.4f, 11);
                Cinderblock(state, "loft-cinderblock-b", -8.35f, UpperY, 110.25f, 71);
                Cinderblock(state, "loft-cinderblock-c", -2.45f, UpperY, 110.95f, -26);
                // Brick from the cut through the west wall, inside and out.
                RubblePile(state, "door-rubble", -8.2f, UpperY, 107f, .35f, 11);
                RubblePile(state, "door-rubble-stray", -7.55f, UpperY, 105.25f, .14f, 23);
                RubblePile(state, "landing-rubble", -9.85f, LandingTop, 106f, .14f, 37);
                WorkLight(state, "loft-work-light", -5.5f, 108.8f, LampRange);
                break;
            case 4:
                Stock(state, geometry, "walkway", -1f, UpperY, 112.9f, 8);
                state.Copies.Add(Copy("walkway-barrels", ManorSite + "SM_Prop_BarrelStack_01", BarrelsPivot, new Vector3(-.42f, UpperY, 104.6f), -11));
                state.Copies.Add(Copy("walkway-generator", BuildSite + "SM_Prop_Generator_01", GeneratorPivot, new Vector3(-1.62f, UpperY, 104.42f), 8));
                SiteTable(state, geometry, "walkway-table", -.35f, UpperY, 108.8f, 86);
                state.Copies.Add(Copy("walkway-jerry-can", Camp + "Jerry Can", JerryCanPivot, new Vector3(-.08f, UpperY, 105.45f), 152));
                Clutter(state, geometry, "walkway-gas-bottle", Camp + "Gas Bottle Prop (1)", GasBottlePivot, -1.93f, UpperY, 111.8f, -29, .32f, .72f);
                Clutter(state, geometry, "walkway-trash-can", Camp + "Trash Can Prop", TrashCanPivot, -1.88f, UpperY, 109.95f, -19, .32f, .77f);
                state.Copies.Add(Copy("walkway-bucket", Camp + "SM_Prop_Bucket_01", BucketPivot, new Vector3(-1.86f, UpperY, 110.7f), 47));
                Cinderblock(state, "walkway-cinderblock", .08f, UpperY, 106.25f, 41);
                break;
            case 6:
                Stock(state, geometry, "south", 2.4f, UpperY, 95f, 4);
                state.Copies.Add(Copy("south-barrels", ManorSite + "SM_Prop_BarrelStack_01", BarrelsPivot, new Vector3(2.68f, UpperY, 100.85f), 19));
                state.Copies.Add(Copy("south-generator", BuildSite + "SM_Prop_Generator_01", GeneratorPivot, new Vector3(.93f, UpperY, 94.93f), -14));
                SiteTable(state, geometry, "south-table", 2.7f, UpperY, 98.6f, 93);
                WorkLight(state, "south-work-light", SouthLampX, 97.7f, SouthLampRange);
                // The jerry can lies on its side by the generator, the bucket is tipped over by the table.
                state.Copies.Add(Copy("south-jerry-can", Camp + "Jerry Can", JerryCanPivot, new Vector3(1.6f, UpperY + JerryCanFlatLift, 94.5f), 107,
                    tilt: JerryCanFlat));
                Clutter(state, geometry, "south-gas-bottle", Camp + "Gas Bottle Prop (1)", GasBottlePivot, .73f, UpperY, 96.13f, 24, .32f, .72f);
                state.Copies.Add(Copy("south-bucket", Camp + "SM_Prop_Bucket_01", BucketPivot, new Vector3(3.0f, UpperY + BucketTippedLift, 96.55f), -58,
                    tilt: BucketTipped));
                Cinderblock(state, "south-cinderblock-a", 1.63f, UpperY, 97.42f, 17);
                Cinderblock(state, "south-cinderblock-b", 2.95f, UpperY, 97.25f, 63);
                break;
        }
    }

    // A loose cinderblock; low enough that the walking map steps over it, so it needs no block.
    private static void Cinderblock(WorldStateDefinition state, string id, float x, float floor, float z, float yaw) =>
        state.Copies.Add(Copy(id, Camp + "Cinderblock (1)", CinderblockPivot, new Vector3(x, floor, z), yaw));

    // A larger prop of the crew's; like the pallet, it gets a block so the walking map goes round it, not over it.
    private static void Clutter(WorldStateDefinition state, CopyDefinition geometry, string id, string template, Vector3 pivot,
        float x, float floor, float z, float yaw, float half, float height)
    {
        state.Copies.Add(Copy(id, template, pivot, new Vector3(x, floor, z), yaw));
        Block(geometry, id + "-block", x - half, floor, z - half, x + half, floor + height, z + half);
    }

    // Cones and the site sign on the grass strip in front of the stair, and the bricks for the door against the wall.
    private static void Outside(WorldStateDefinition state)
    {
        state.Copies.Add(Copy("site-sign", BuildSite + "CS_sign_02", SignPivot, new Vector3(-11.25f, -.05f, 99.6f), -10));
        var cones = new[] { new Vector2(-11.45f, 95.4f), new Vector2(-9.55f, 95.4f), new Vector2(-11.3f, 97.6f) };
        for (int i = 0; i < cones.Length; i++)
            state.Copies.Add(Copy("site-cone-" + i, BuildSite + "TrafficCone/trafficcone", ConePivot, new Vector3(cones[i].x, ConeCentre - .03f, cones[i].y)));
        state.Copies.Add(Copy("outside-bricks", ManorSite + "SM_Prop_Brick_Stack_01", BricksPivot, new Vector3(-9.75f, 0f, 96.5f), scale: .75f));
    }

    // A pallet with planks stacked side by side across their length, four to a layer.
    private static void Stock(WorldStateDefinition state, CopyDefinition geometry, string id, float x, float floor, float z, int planks)
    {
        state.Copies.Add(Copy(id + "-pallet", Pallet, PalletPivot, new Vector3(x, floor, z)));
        // Without a block the walking map climbs the pallet.
        Block(geometry, id + "-pallet-block", x - .78f, floor, z - .78f, x + .78f, floor + .55f, z + .78f);
        float[] shift = { .015f, -.025f, .005f, -.01f, .03f, -.015f, .01f, -.03f, .02f, 0f, -.02f, .025f };
        for (int i = 0; i < planks; i++)
            state.Copies.Add(Copy(id + "-plank-" + i, Plank, PlankPivot,
                new Vector3(x + shift[i], floor + PalletDeck + PlankThickness * (.5f + i / 4), z + ((i % 4) - 1.5f) * .125f), 90));
    }

    // The game's folding table with the plan on it. The copy is visual only, so a block stands in for its collider.
    private static void SiteTable(WorldStateDefinition state, CopyDefinition geometry, string id, float x, float floor, float z, float yaw)
    {
        state.Copies.Add(Copy(id, FoldingTable, FoldingTablePivot, new Vector3(x, floor, z), yaw, TableScale));
        float across = 1.26f * TableScale, along = .61f * TableScale, top = floor + .75f * TableScale;
        // The block follows the nearer of the two axes; a few degrees off it stay inside the table's footprint.
        bool turned = Mathf.Abs(Mathf.DeltaAngle(yaw, 90f)) < 45f;
        float halfX = turned ? along : across, halfZ = turned ? across : along;
        Block(geometry, id + "-block", x - halfX, floor, z - halfZ, x + halfX, top, z + halfZ);
        float paperX = turned ? .16f : .25f, paperZ = turned ? .25f : .16f;
        Face(geometry, id + "-plan", "clipboard_paper_mat", x - paperX, top + .002f, z - paperZ, x + paperX, top + .004f, z + paperZ);
    }

    // The crew's lamp burns all night, on no switch, until the room gets its own.
    private static void WorkLight(WorldStateDefinition state, string id, float x, float z, float range)
    {
        var light = Copy(id, LampModel, LampPivot, new Vector3(x, LoftCeiling - (UnitCeiling - LampPivot.y), z));
        light.LightsOn = true;
        light.LightRange = range;
        state.Copies.Add(light);
    }

    // A low mound of broken brick with flat scraps of the units' sheet. A box cannot turn on its own, so each handful
    // is a copy with its own yaw around the middle of the heap.
    private static void RubblePile(WorldStateDefinition state, string id, float x, float floor, float z, float radius, int seed)
    {
        var random = new System.Random(seed);
        float Between(float from, float to) => from + (float)random.NextDouble() * (to - from);
        var centre = new Vector3(x, floor, z);
        for (int turn = 0; turn < 3; turn++)
        {
            float yaw = Between(0f, 180f);
            var handful = new CopyDefinition
            {
                Id = id + "-" + turn, Template = Anchor, Frame = Anchor, OwnMesh = false, Obstacle = false,
                At = new LocalPoint(centre - Quaternion.Euler(0, yaw, 0) * (centre - AnchorPoint), yaw)
            };
            int pieces = Math.Max(2, (int)(radius * 18f));
            for (int i = 0; i < pieces; i++)
            {
                float angle = Between(0f, Mathf.PI * 2f), distance = radius * Mathf.Sqrt(Between(0f, 1f));
                float px = x + Mathf.Cos(angle) * distance, pz = z + Mathf.Sin(angle) * distance;
                float bottom = floor + Between(0f, (1f - distance / radius) * radius * .5f);
                bool scrap = random.Next(4) == 0;
                float halfLength = Between(.05f, .12f), halfWidth = Between(.03f, .06f), tall = scrap ? .012f : Between(.05f, .085f);
                Face(handful, "piece-" + i, scrap ? UnitSheet : Brick, px - halfLength, bottom, pz - halfWidth, px + halfLength, bottom + tall, pz + halfWidth);
            }
            state.Copies.Add(handful);
        }
    }

    // C, night 1: the plank wall goes, the roller-door faces of Unit4 go, and its three units become one room open to
    // the walkway, under the ceiling height of the rooms north of it.
    private static void SouthOpen(WorldStateDefinition state, CopyDefinition geometry)
    {
        // "UnitFace", "UnitFace (1)" and "UnitFace (2)"; names match by prefix, the area keeps B's faces out.
        state.Rules.Add(Rule("south-faces", "UnitFace", "Storage warehouse", ObjectEffect.Remove,
            BoundsOf(EastFront - .05f, UpperY - .05f, SouthEnd - .05f, EastFront + .25f, UnitTop + .05f, SouthUnitNorth + .05f)));
        var units = BoundsOf(EastFront, UpperY, SouthEnd, EastWall, UnitTop, SouthUnitNorth);
        state.Rules.Add(Rule("south-skin", "Unit4", "Storage warehouse", ObjectEffect.Hide, units));
        var room = Rule("south-room", "Unit4", "Storage warehouse", ObjectEffect.Hollow, units);
        room.Openings.Add(BoundsOf(EastFront - .1f, UpperY, SouthEnd + .01f, EastFront + .1f, LoftCeiling, SouthUnitNorth - .01f));
        room.Bands.Add((0f, UnitSheet));
        room.CeilingMaterial = UnitSheet;
        room.FloorMaterial = FloorSheet;
        room.FrameMaterial = Metal;
        state.Rules.Add(room);
        Face(geometry, "south-ceiling", UnitSheet, EastFront, LoftCeiling, SouthEnd, EastWall, LoftCeiling + .006f, SouthUnitNorth);
        // The walkway stays open to the hall's roof; a steel beam carries the south edge of B's ceiling over it.
        Face(geometry, "south-beam", Metal, UnitEast, LoftCeiling - .15f, UnitSouth - .07f, EastFront, UnitTop, UnitSouth + .07f);
    }

    // C, night 2: sheet metal clads the elevator's face to the walkway, lamps hang in the south room on Unit 1's
    // switch, and the floor belongs to the property.
    private static void SouthRoom(WorldStateDefinition state, CopyDefinition geometry)
    {
        Face(geometry, "elevator-cladding", UnitSheet, ElevatorWest - .012f, UpperY, SouthUnitNorth, ElevatorWest - .004f, UnitTop, UnitSouth);
        foreach (var (id, z) in new[] { ("south-light-south", 96.0f), ("south-light-north", 99.4f) })
        {
            var light = Copy(id, LampModel, LampPivot, new Vector3(SouthLampX, LoftCeiling - (UnitCeiling - LampPivot.y), z));
            light.LightsOn = true;
            light.LightSwitch = RoomSwitch;
            light.LightRange = SouthLampRange;
            state.Copies.Add(light);
        }

        // Turned like the other grids: tile (x, y) covers world x 2.75-x/2 .. 3.25-x/2 and z 103.25-y/2 .. 103.75-y/2.
        // The tiles end in front of the railing to the hall (x -1.75) and leave out the elevator and its shaft.
        var property = new PropertyExtensionDefinition
        {
            Id = SouthExtensionId, PropertyCode = "storageunit", GridIndex = 0,
            GridOrigin = new Vector3(EastWall, UpperY, SouthGridZ),
        };
        for (int x = 0; x <= 9; x++)
            for (int y = 0; y <= 18; y++)
                if (!(x <= 5 && y <= 4)) property.Tiles.Add(new GridCoordinate(x, y));
        property.Bounds.Add(BoundsOf(FacesBack, UpperY - .1f, SouthEnd, EastWall, LoftCeiling, UnitSouth));
        state.PropertyExtensions.Add(property);
    }

    // B, night 1: the roller-door faces between the loft and the walkway go, and a plank wall closes the walkway to the
    // south, where C has not been built yet.
    private static void WalkwayOpen(WorldStateDefinition state, CopyDefinition geometry, int level)
    {
        for (int i = 7; i <= 10; i++)
            state.Rules.Add(Rule("loft-face-" + i, $"UnitFace ({i})", "Storage warehouse", ObjectEffect.Remove,
                BoundsOf(FacesBack - .05f, UpperY - .05f, UnitSouth - .05f, UnitEast + .05f, UnitTop + .05f, UnitNorth + .05f)));
        // The loft's ceiling ends at the old wall; a steel beam carries its edge over the opening.
        if (level < 5) Face(geometry, "walkway-beam", Metal, FacesBack, LoftCeiling - .15f, UnitSouth, UnitEast, UnitTop, UnitNorth);
        if (level < 6) SiteWall(geometry);
    }

    // A temporary wall closes the walkway to the south until C opens it: boards nailed to a timber frame, the frame on
    // the side of the rooms.
    private static void SiteWall(CopyDefinition geometry)
    {
        const float back = UnitSouth + .04f, front = back + .1f, top = UpperY + SiteWallHeight;
        Solid(geometry, "walkway-site-wall", Wood, FacesBack, UpperY, UnitSouth - .04f, EastFront, top, back);
        Face(geometry, "walkway-site-wall-sole", Trim, FacesBack, UpperY, back, EastFront, UpperY + .1f, front);
        Face(geometry, "walkway-site-wall-head", Trim, FacesBack, top - .1f, back, EastFront, top, front);
        // The noggin lies a little behind the studs' faces, so the two never share a plane.
        Face(geometry, "walkway-site-wall-noggin", Trim, FacesBack, UpperY + 1.2f, back, EastFront, UpperY + 1.3f, front - .02f);
        for (int i = 0; i <= 4; i++)
        {
            float x = Mathf.Lerp(FacesBack + .03f, EastFront - .03f, i / 4f);
            Face(geometry, "walkway-site-wall-stud-" + i, Trim, x - .025f, UpperY + .1f, back, x + .025f, top - .1f, front);
        }
    }

    // B, night 2: the units east of the walkway (Unit5) become one room with the walkway and the loft, under one ceiling
    // with lamps on Unit 1's switch, and the floor belongs to the property.
    private static void EastRoom(WorldStateDefinition state, CopyDefinition geometry)
    {
        var units = BoundsOf(EastFront, UpperY, UnitSouth, EastWall, UnitTop, UnitNorth);
        for (int i = 3; i <= 6; i++)
            state.Rules.Add(Rule("east-face-" + i, $"UnitFace ({i})", "Storage warehouse", ObjectEffect.Remove,
                BoundsOf(EastFront - .05f, UpperY - .05f, UnitSouth - .05f, EastFront + .25f, UnitTop + .05f, UnitNorth + .05f)));
        state.Rules.Add(Rule("east-skin", "Unit5", "Storage warehouse", ObjectEffect.Hide, units));
        var room = Rule("east-room", "Unit5", "Storage warehouse", ObjectEffect.Hollow, units);
        // The whole face to the walkway is open; the room keeps its east, north and south walls.
        room.Openings.Add(BoundsOf(EastFront - .1f, UpperY, UnitSouth + .01f, EastFront + .1f, LoftCeiling, UnitNorth - .01f));
        room.Bands.Add((0f, UnitSheet));
        room.CeilingMaterial = UnitSheet;
        room.FloorMaterial = FloorSheet;
        room.FrameMaterial = Metal;
        state.Rules.Add(room);
        // The roof's eaves dip below the blocks' tops at both walls; the loft's ceiling height runs on to the east wall.
        Face(geometry, "east-ceiling", UnitSheet, UnitEast, LoftCeiling, UnitSouth, EastWall, LoftCeiling + .006f, UnitNorth);

        foreach (var (id, z) in new[] { ("east-light-south", 106.0f), ("east-light-middle", 108.8f), ("east-light-north", 111.6f) })
        {
            var light = Copy(id, LampModel, LampPivot, new Vector3(EastLampX, LoftCeiling - (UnitCeiling - LampPivot.y), z));
            light.LightsOn = true;
            light.LightSwitch = RoomSwitch;
            // Within this reach the light ends before the outside of the east wall.
            light.LightRange = EastLampRange;
            state.Copies.Add(light);
        }

        // The walkway and the east room on a grid of their own, turned like the loft's: tile (x, y) covers world
        // x 2.75-x/2 .. 3.25-x/2 and z 113.25-y/2 .. 113.75-y/2, from the east wall to the loft's edge.
        var property = new PropertyExtensionDefinition
        {
            Id = EastExtensionId, PropertyCode = "storageunit", GridIndex = 0,
            GridOrigin = new Vector3(EastWall, UpperY, LoftGridZ),
        };
        for (int x = 0; x <= 10; x++)
            for (int y = 0; y <= 18; y++)
                property.Tiles.Add(new GridCoordinate(x, y));
        property.Bounds.Add(BoundsOf(FacesBack, UpperY - .1f, UnitSouth - .2f, EastWall, LoftCeiling, UnitNorth));
        state.PropertyExtensions.Add(property);
    }

    // Night 3: the loft's south wall opens onto the hall behind a railing, lamps hang under the ceiling, and employees
    // can walk up the stair into the room.
    private static void Gallery(WorldStateDefinition state, CopyDefinition geometry, int level)
    {
        // From B's second night the walkway and Unit5 join the volume; the hall below them is Unit 2's and keeps the
        // world's walking map like Unit 1.
        float east = level >= 5 ? EastWall : FacesBack;
        // From C's first night the walkway south of B and Unit4 reach to the hall's south wall.
        float south = level >= 6 ? SouthEnd - .05f : 95.4f;
        // The sign and the stain were painted on the wall that opens.
        state.Rules.Add(Rule("hall-logo", "Logo (1)", "Storage warehouse", ObjectEffect.Remove,
            BoundsOf(-4.85f, 3.85f, 103.7f, -2.5f, 6.2f, 103.85f)));
        state.Rules.Add(Rule("hall-stain", "Quad", "Decals 0004 (1)", ObjectEffect.Remove,
            BoundsOf(-8.85f, 3.15f, 103.7f, -4.5f, 6.25f, 103.85f)));
        // A steel beam closes the opening under the block's top and hides the edge of the loft's ceiling.
        Face(geometry, "gallery-beam", Metal, UnitWest, LoftCeiling - .15f, UnitSouth - .07f, UnitEast, UnitTop, UnitSouth + .07f);
        GalleryRail(state, geometry, level);

        foreach (var (id, z) in new[] { ("loft-light-south", 106.0f), ("loft-light-middle", 108.8f), ("loft-light-north", 111.6f) })
        {
            var light = Copy(id, LampModel, LampPivot, new Vector3(-5.5f, LoftCeiling - (UnitCeiling - LampPivot.y), z));
            light.LightsOn = true;
            light.LightSwitch = RoomSwitch;
            // Walls do not stop this light; within this reach it ends before the outside of the west and north walls.
            light.LightRange = LampRange;
            state.Copies.Add(light);
        }

        // One volume from the ground in front of the stair to the loft. The stair stands outside the west wall. The
        // ground west of the unit and the hall with Unit 1 keep the world's walking map; ground baked a second time
        // would lie on top of the world's and never join it. The ground box ends below the stair's first step (0.25).
        state.NavigationBounds = BoundsOf(-10.9f, -.5f, south, east, UnitTop, UnitNorth);
        state.NavigationAgents.AddRange(new[] { "Humanoid", "Ignore Costs", "Employee" });
        state.NavigationExistingFloor.Add(BoundsOf(-10.9f, -.3f, south, -8.75f, .2f, UnitNorth));
        state.NavigationExistingFloor.Add(BoundsOf(-8.75f, -.3f, south, east, 3f, UnitNorth));
        // The first two straddle the seam at the stair's foot: the world's ground, then the lowest point of the stair's
        // own walking map (the agent's radius takes off the steps below it). Then the landing, the loft behind its
        // door, the far corner of the loft and its middle.
        state.NavigationProbes.Add(new Vector3(-9.84f, 0f, 98.4f));
        state.NavigationProbes.Add(new Vector3(-9.84f, .91f, 100.5f));
        state.NavigationProbes.Add(new Vector3(-9.6f, LandingTop, 105.5f));
        state.NavigationProbes.Add(new Vector3(-8.1f, UpperY, 105.45f));
        state.NavigationProbes.Add(new Vector3(-3f, UpperY, 112.8f));
        state.NavigationProbes.Add(new Vector3(-5.5f, UpperY, 109f));
        // The walkway through the old wall, then the middle of the east room.
        if (level >= 5)
        {
            state.NavigationProbes.Add(new Vector3(-.9f, UpperY, 109f));
            state.NavigationProbes.Add(new Vector3(1.75f, UpperY, 109f));
        }
        // The walkway beside the elevator, its south end, and the middle of the south room.
        if (level >= 6)
        {
            state.NavigationProbes.Add(new Vector3(-.9f, UpperY, 102.6f));
            state.NavigationProbes.Add(new Vector3(-.9f, UpperY, 95f));
            state.NavigationProbes.Add(new Vector3(1.75f, UpperY, 97.75f));
        }

        // The loft belongs to the property, so employees reach what stands in it.
        // Its own grid lies on the loft floor, turned like the unit's grid (180°): tile (x, y) covers world
        // x -3-x/2 .. -2.5-x/2 and z 113.25-y/2 .. 113.75-y/2. The tiles fill the loft from wall to wall and from the
        // north wall to the railing, without the corner the door swings through.
        var property = new PropertyExtensionDefinition
        {
            Id = LoftExtensionId, PropertyCode = "storageunit", GridIndex = 0,
            GridOrigin = new Vector3(LoftGridX, UpperY, LoftGridZ),
        };
        for (int x = 0; x <= 11; x++)
            for (int y = 0; y <= 18; y++)
                if (!(x >= 10 && y >= 15 && y <= 17)) property.Tiles.Add(new GridCoordinate(x, y));
        property.Bounds.Add(BoundsOf(UnitWest, UpperY - .1f, UnitSouth - .2f, FacesBack, LoftCeiling, UnitNorth));
        state.PropertyExtensions.Add(property);
    }

    // The walkway's railing continues along the opening and meets it at its last post. The vanilla piece is 1.9 m long
    // with a post at each end; the run's last piece lies over the one before it, a few millimetres aside. From the south
    // stage on the run stops short of the walkway's post and leaves the opening above the ladder from the hall.
    private static void GalleryRail(WorldStateDefinition state, CopyDefinition geometry, int level)
    {
        const float piece = 1.9f, start = UnitWest + .05f;
        bool ladder = level >= StorageLadder.FromLevel;
        float end = ladder ? StorageLadder.OpeningWest : RailEnd.x, cross = RailEnd.z;
        int full = (int)((end - start + .001f) / piece);
        float rest = end - start - full * piece;
        bool overlaps = rest > .05f;
        int count = overlaps ? full + 1 : full;
        for (int i = 0; i < count; i++)
        {
            bool last = i == count - 1;
            float from = last ? end - piece : start + i * piece;
            float side = cross - (last && overlaps ? .004f : 0f);
            var rail = Copy("gallery-rail-" + i, Fence, FencePost, new Vector3(from, UpperY, side), 90);
            if (last && overlaps) rail.Exclude.Add("outdoorfence/Post");
            // The walkway's railing already stands at the run's end; at the opening the run's own post ends it.
            if (last && !ladder) rail.Exclude.Add("outdoorfence/Post.001");
            state.Copies.Add(rail);
        }
        geometry.Boxes.Add(new BoxDefinition
        {
            Id = "gallery-guard", Kind = BoxKind.Block, Box = BoundsOf(start, UpperY, cross - .07f, end, UpperY + 2.1f, cross + .07f)
        });
    }

    // Night 2: a door breaks through the upper west wall onto the landing, and the four small units above Unit 1 become
    // one room. The units are one block, Unit6, drawn as an outer skin around a single solid box collider.
    private static void Doorway(WorldStateDefinition state, CopyDefinition geometry, int level)
    {
        // The cut takes the brick skin and the upper wall with its collider; the wall below 3.31 stays as the threshold,
        // the ledge and the landing outside end at 3.30.
        state.Rules.Add(Rule("door-brick", "Main", "Storage warehouse", ObjectEffect.Cut, DoorCut));
        state.Rules.Add(Rule("door-brick-inner", "Block", "Storage warehouse", ObjectEffect.Cut, DoorCut));
        // The units' skin with its partitions goes; the lining draws the room inside, a sheet keeps the face to the hall.
        // The skin is hidden before it is hollowed, so the lining the hollow hangs under it stays visible.
        var units = BoundsOf(UnitWest, UpperY, UnitSouth, UnitEast, UnitTop, UnitNorth);
        state.Rules.Add(Rule("unit-skin", "Unit6", "Storage warehouse", ObjectEffect.Hide, units));
        // Until the wall opens, a sheet keeps the face to the hall. It lies behind the plane of the hall's decals
        // (z 103.80) and in front of the lining (103.81).
        if (level < 3)
        {
            Face(geometry, "unit-face-hall", UnitSheet, UnitWest, UpperY, UnitSouth + .002f, UnitEast, UnitTop, UnitSouth + .008f);
            // The hidden skin carried a dark green band along the bottom of this face, under the hall sign.
            Face(geometry, "unit-face-hall-band", "alum sheet dark green", UnitWest, UpperY, UnitSouth - .002f, UnitEast, UpperY + .7f, UnitSouth + .001f);
        }
        // The hall's roof trusses run through the block; inside it they would stand in the room. From B's second night
        // their braces at the east wall would stand in the east room. All trusses are one renderer, which a state cuts
        // once, so one area covers both rooms.
        float trussEast = level >= 5 ? EastWall : UnitEast;
        var trusses = Rule("unit-trusses", "Truss", "Storage warehouse", ObjectEffect.Cut,
            BoundsOf(UnitWest + .01f, UpperY + .01f, UnitSouth + .01f, trussEast - .01f, UnitTop - .01f, UnitNorth - .01f));
        // From C's first night Unit4 is a room too; the walkway between it and the hall keeps its trusses.
        if (level >= 6)
            trusses.Openings.Add(BoundsOf(EastFront + .01f, UpperY + .01f, SouthEnd + .01f, EastWall - .01f, UnitTop - .01f, SouthUnitNorth - .01f));
        state.Rules.Add(trusses);
        var room = Rule("unit-room", "Unit6", "Storage warehouse", ObjectEffect.Hollow, units);
        room.Openings.Add(DoorCut);
        // Night 3 opens the south wall from the floor to the loft's ceiling, between the west wall and the walkway's faces.
        if (level >= 3) room.Openings.Add(BoundsOf(UnitWest + .01f, UpperY, UnitSouth - .1f, FacesBack, LoftCeiling, UnitSouth + .1f));
        // B's first night opens the east wall to the walkway along its whole length.
        if (level >= 4) room.Openings.Add(BoundsOf(UnitEast - .1f, UpperY, UnitSouth + .01f, UnitEast + .1f, LoftCeiling, UnitNorth - .01f));
        room.Bands.Add((0f, UnitSheet));
        room.CeilingMaterial = UnitSheet;
        room.FloorMaterial = FloorSheet;
        room.FrameMaterial = Metal;
        state.Rules.Add(room);
        state.Rules.Add(Rule("door-grate", "Grate (2)", "Storage warehouse", ObjectEffect.Remove,
            BoundsOf(-9.05f, 3.6f, 104.8f, -8.9f, 5.7f, 106.15f)));
        // The walkway's unit faces with their roller doors reach 0.2 m into the block; a sheet inside covers their backs.
        if (level < 4) Face(geometry, "unit-face-walkway", UnitSheet, FacesBack - .006f, UpperY, UnitSouth, FacesBack - .002f, LoftCeiling, UnitNorth);
        // The roof's eave dips below the block's top at the west wall; a lower ceiling keeps it out of the room.
        Face(geometry, "loft-ceiling", UnitSheet, UnitWest, LoftCeiling, UnitSouth, UnitEast, LoftCeiling + .006f, UnitNorth);
        // Rust decals on Unit 1's walls below reach up through the loft floor; only the part above the floor goes.
        state.Rules.Add(Rule("rust-east", "Quad", "Big rust 2", ObjectEffect.Cut, BoundsOf(-3.8f, UpperY - .1f, 107.4f, -3.7f, 3.8f, 114.3f)));
        state.Rules.Add(Rule("rust-west", "Quad", "Big rust 1", ObjectEffect.Cut, BoundsOf(-8.8f, UpperY - .1f, 107.9f, -8.7f, 3.5f, 114.1f)));

        // Steel reveals line the cut through the brick, the inner leaf and the units' skin.
        const float outside = -9.26f, inside = UnitWest + .01f;
        Solid(geometry, "door-jamb-south", Metal, outside, DoorSill, WindowSouth - .02f, inside, DoorHead, DoorSouth);
        Solid(geometry, "door-jamb-north", Metal, outside, DoorSill, DoorNorth, inside, DoorHead, WindowNorth + .02f);
        Face(geometry, "door-head", Metal, outside, DoorHead, WindowSouth - .02f, inside, DoorHead + .02f, WindowNorth + .02f);
        Face(geometry, "door-sill", Metal, outside, DoorSill - .02f, WindowSouth, inside, DoorSill, WindowNorth);

        // The Storage Unit's own green door leaf stands open into the room, hinged on the north jamb.
        var hinge = new Vector3(UnitWest + .01f, UpperY + .01f, DoorNorth - .05f);
        state.Copies.Add(Copy("door-leaf", DoorLeaf, DoorHinge, hinge));
        state.Copies.Add(Copy("door-handle", DoorHandle, DoorHinge, hinge));
        Block(geometry, "door-leaf-collision", hinge.x, UpperY, DoorNorth - .09f, hinge.x + 1.06f, UpperY + 2.11f, DoorNorth - .01f);
    }

    private static ObjectRule Rule(string id, string name, string parent, ObjectEffect effect, Bounds area) => new()
    {
        Id = id, Name = name, Parent = parent, Side = WorldSide.Main, Effect = effect, Area = area
    };

    private static void Block(CopyDefinition copy, string id, float x1, float y1, float z1, float x2, float y2, float z2) =>
        copy.Boxes.Add(new BoxDefinition { Id = id, Kind = BoxKind.Block, Box = BoundsOf(x1, y1, z1, x2, y2, z2) });

    // Night 1: the stair stands against the west wall and ends on a landing in front of the closed upper wall.
    private static void StairAndLanding(WorldStateDefinition state, CopyDefinition geometry)
    {
        // The stair is 1.5 m wide and runs from x -10.59 to -9.09, clear of the bus stop's pillar.
        var target = new Vector3(-9.84f, LandingTop - StairDrop, LandingSouth + StairInto);
        state.Copies.Add(Copy("west-stair", Stair, StairTop, target, StairYaw, StairScale));
        StairRail(state, geometry);
        Solid(geometry, "landing", Metal, LandingWest, LandingBottom, LandingSouth, LandingEast, LandingTop, LandingNorth);
        // The top tread ends 2.5 cm short of the landing, a riser's height below the deck. A kick plate between the
        // stringers closes that slit and the open riser up to the deck, for the eye and for the walking map, which the
        // slit would split.
        float tread = LandingTop - StairDrop - .242f;
        Solid(geometry, "landing-riser", Metal, -10.49f, tread - .01f, LandingSouth - .03f, -9.19f, LandingBottom, LandingSouth);
        // Two posts carry the landing's open corners; the shipping container stands under its north half.
        Post(geometry, "landing-post-sw", LandingWest + .04f, LandingSouth + .04f, 0f, LandingBottom);
        Post(geometry, "landing-post-se", LandingEast - .06f, LandingSouth + .04f, 0f, LandingBottom);
        // A railing on the open west and north edges; the stair comes up on the south edge, the wall closes the east.
        Railing(geometry, "landing-west", false, LandingWest + .03f, LandingSouth, LandingNorth);
        Railing(geometry, "landing-north", true, LandingNorth - .03f, LandingWest, LandingEast);
    }

    // The game has no sloped railing at this pitch, so the landing's railing runs on down the stair's open west side:
    // upright posts, and rails in copies tilted to the slope of the nosings. They meet the landing's railing at its corner.
    private static void StairRail(WorldStateDefinition state, CopyDefinition geometry)
    {
        const float half = .025f, cross = LandingWest + .03f, top = LandingSouth + StairInto, foot = top - StairRun;
        float Nosing(float z) => LandingTop - StairDrop - (top - z) * StairRise / StairRun;
        foreach (float z in new[] { foot + .05f, foot + 1.85f, foot + 3.65f })
            Face(geometry, "stair-rail-post-" + Mathf.RoundToInt(z * 10), Metal, cross - half, Nosing(z) - .3f, z - half, cross + half, Nosing(z) + 1.05f, z + half);
        float slope = Mathf.Atan2(StairRise, StairRun) * Mathf.Rad2Deg;
        float length = Mathf.Sqrt(StairRise * StairRise + StairRun * StairRun);
        foreach (var (name, above) in new[] { ("top", 1.03f), ("middle", .52f) })
        {
            // A box cannot tilt on its own: the copy carries it from the anchor to the rail's foot and tips it up the slope.
            var rail = new CopyDefinition
            {
                Id = "stair-rail-" + name, Template = Anchor, Frame = Anchor, OwnMesh = false, Obstacle = false,
                At = new LocalPoint(new Vector3(cross, Nosing(foot) + above, foot), 0), Tilt = new Vector3(-slope, 0, 0)
            };
            Face(rail, "bar", Metal, AnchorPoint.x - .02f, AnchorPoint.y - .02f, AnchorPoint.z, AnchorPoint.x + .02f, AnchorPoint.y + .02f, AnchorPoint.z + length);
            // The rails are drawn without colliders; an unseen wall along the top rail keeps a jump from carrying over it.
            if (name == "top")
                rail.Boxes.Add(new BoxDefinition
                {
                    Id = "guard", Kind = BoxKind.Block,
                    Box = BoundsOf(AnchorPoint.x - .05f, AnchorPoint.y - .5f, AnchorPoint.z, AnchorPoint.x + .05f, AnchorPoint.y + 1.07f, AnchorPoint.z + length)
                });
            state.Copies.Add(rail);
        }
        // The treads have open risers and stop a few centimetres short of the wall. A steel sheet under them, from
        // stringer to wall, closes the view through to the grass. It lies below the treads' back edges and inside the
        // stringers, and has no collider, so the walking map follows the treads.
        var soffit = new CopyDefinition
        {
            Id = "stair-soffit", Template = Anchor, Frame = Anchor, OwnMesh = false, Obstacle = false,
            At = new LocalPoint(new Vector3(-9.82f, Nosing(foot) - .27f, foot), 0), Tilt = new Vector3(-slope, 0, 0)
        };
        Face(soffit, "sheet", Metal, AnchorPoint.x - .75f, AnchorPoint.y - .03f, AnchorPoint.z, AnchorPoint.x + .75f, AnchorPoint.y, AnchorPoint.z + length);
        state.Copies.Add(soffit);
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

    // The template's pivot lands on the target. A tilt turns the copy about its own pivot after the yaw, so a prop can
    // lie on its side or lean with the pivot still on the floor.
    private static CopyDefinition Copy(string id, string template, Vector3 sourcePivot, Vector3 targetPivot, float yaw = 0, float scale = 1f,
        Vector3 tilt = default) => new()
    {
        Id = id, Template = template, Frame = Frame, Scale = scale, Tilt = tilt,
        At = new LocalPoint(targetPivot - Quaternion.Euler(0, yaw, 0) * Quaternion.Euler(tilt) * (scale * (sourcePivot - FramePivot)), yaw),
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
