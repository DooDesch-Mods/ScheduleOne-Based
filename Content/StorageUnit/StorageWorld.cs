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
    // on top of the lower wall; its nosings rise 3.05 m from the first step at 0.25. The model's own lowest tread lies at
    // 0.40; the added first tread at 0.20 halves that step from the grass.
    private const float StairYaw = 30f, StairScale = 1f, StairRise = 3.05f, StairRun = 5.43f, FirstTread = .20f;
    // The stringers end in a wedge: 0.15 m short of the stair's top edge they reach its full height, then fall away by
    // 0.25 m. The top tread lies 0.24 m below that height. The stair reaches this far into the landing and sits 1 cm
    // lower, so the wedge's peak meets the landing's south edge inside the deck plate and its end tucks under the frame.
    private const float StairInto = .15f, StairDrop = .01f;
    private const string Metal = "metal_darkgrey_mat";

    // The upper floor lies at 3.25; the ledge along the west wall and the landing in front of it end at 3.30.
    private const float UpperY = 3.25f, LandingTop = 3.30f, LandingBottom = 3.20f, LandingDeck = LandingTop - .006f;
    internal const float UpperFloor = UpperY, LandingFloor = LandingTop;
    // The landing's edges lie on the grid's 0.5 m lines (x .25, z .3 past the half metre); the east edge reaches into
    // the wall. Its railing stands on its own line over the stair's west stringer, inside the deck's west edge.
    private const float LandingWest = -10.75f, LandingEast = -9.01f, LandingSouth = 104.8f, LandingNorth = 106.3f;
    private const float LandingRail = -10.62f;
    // The unseen walls behind railings end just above the rail, as the vanilla fence's collider does (top at 4.48), so
    // nothing stops the player above them.
    private const float RailTop = LandingTop + 1.05f, GuardTop = RailTop + .05f, GuardHeight = 1.25f;

    // The four small units above Unit 1 (Unit6) and the upper west wall in front of them.
    private const float UnitWest = -8.75f, UnitEast = -2.05f, UnitSouth = 103.8f, UnitNorth = 113.8f, UnitTop = 6.25f;
    // A room's lining stands 1 cm inside its box; the steel post at the opening's corner is 14 cm wide.
    private const float LiningGap = .01f, CornerPostEast = UnitEast + .07f;
    private const string UnitSheet = "alum sheet med grey", FloorSheet = "metal_mediumgrey_mat";
    // The back of the walkway's unit faces inside the block, and the loft's ceiling under the roof's eave.
    private const float FacesBack = -2.25f, LoftCeiling = 6.0f;
    // The door through the west wall, over the landing. It takes the place of the third window from the south: the cut
    // has the window's width and head, down to the floor, so no part of the window's recess is left beside it. The
    // leaf is narrower than the window; steel jambs of equal width on both sides centre its opening in the cut.
    private const float WindowSouth = 104.85f, WindowNorth = 106.10f, DoorSill = 3.31f, DoorHead = 5.65f;
    // The leaf keeps 6 mm to each jamb, the gap of the vanilla door.
    private const float DoorLeafWidth = 1.06f, DoorLeafHeight = 2.10f, DoorGap = .006f, DoorClear = DoorLeafWidth + 2 * DoorGap;
    private const float DoorSouth = (WindowSouth + WindowNorth - DoorClear) / 2f, DoorNorth = DoorSouth + DoorClear;
    private static readonly Bounds DoorCut = BoundsOf(-9.3f, DoorSill, WindowSouth, -8.7f, DoorHead, WindowNorth);
    private const string DoorRoot = "@Properties/StorageUnit/Industrial Metal Door (2)";
    // The leaf's hinge edge at the floor; the handle sits at its east edge.
    private static readonly Vector3 DoorHinge = new(-5.78f, .1f, 103.9f);

    // The walkway's railing along the hall, one 1.9 m piece; its post at the walkway's north end is where the gallery's
    // railing ends.
    private const string Fence = "Map/Hyland Point/Region_Northtown/Storage warehouse/Storage warehouse/OutdoorFence";
    private static readonly Vector3 FencePost = new(-2.1f, 3.25f, 101.79f);
    private static readonly Vector3 RailEnd = new(-2.1f, 3.25f, 103.69f);
    // Unit 1's ceiling light, on Unit 1's switch. The lamp's body reaches 3.2 cm above its pivot, so a pivot 3.3 cm under
    // the loft's ceiling panel mounts it flat against the panel without reaching into it.
    private const string LampModel = "Map/Hyland Point/Region_Northtown/Storage warehouse/Storage Unit/FluroBar (1)";
    private static readonly Vector3 LampPivot = new(-6.52f, 2.87f, 107.23f);
    // The bars hang 2.7 m over the floor, like Unit 1's own bar over its floor. URP fades a light towards the end of its
    // reach, so a reach of 3.6 left the room's far floor dark. The reach is 6 like the house bars, not Unit 1's 8: extra
    // lights cast no shadows here, and Unit 1's floor 5.9 m under the loft bars would be lit through the loft floor. The
    // higher intensity makes up for the shorter reach, so the floor gets about as much light as Unit 1's.
    private const float LampRange = 6f, LampIntensity = 2.8f, LampY = LoftCeiling - .033f;
    private const string RoomSwitch = "Map/Hyland Point/Region_Northtown/Storage warehouse/Storage Unit/ModularSwitch";
    // Unit 1's wall switch: the centre of its back plate, 1.2 m above Unit 1's floor on the unit's west wall, facing east.
    private static readonly Vector3 SwitchPivot = new(-4.62f, 1.3f, 104.78f);
    private const float SwitchHeight = 1.2f;
    // Each new room's wall switch is a networked copy of Unit 1's; its lamps follow it through these ids.
    private const string LoftSwitch = "loft-switch", EastSwitch = "east-switch", SouthSwitch = "south-switch";
    // The upper floor has one grid. The loft's night builds it with the loft's tiles; the walkway with the east room
    // and the south room add theirs when they are done, so an item can stand across the lines between them. Its id is
    // the one the east room's own grid had up to Based 1.2.2, so that grid's GUID, and what stands on it in a save,
    // stay. Renaming it loses what stands upstairs. StorageGridMigration moves what stood on the loft's and the south
    // room's own grids of then onto it.
    public const string UpperGridExtensionId = "storage-east-floor";
    // The ids of the loft's and the south room's own grids up to Based 1.2.2; old saves hold items on their GUIDs.
    internal const string OldLoftGridId = "storage-loft-floor", OldSouthGridId = "storage-south-floor";
    // Vanilla's floor is grid 0 of the property; the loft's extension builds the upper grid right after it.
    internal const int UpperGridIndex = 1;
    // Tile (x, y) of the loft's old grid is tile (x + 11, y) of the upper grid, tile (x, y) of the south room's old
    // grid is tile (x, y + 20).
    internal const int LoftShiftX = 11, SouthShiftY = 20;
    // A tile is centred on its coordinate, 0.5 m square, and the grid is turned by 180°: tile (x, y) has its centre at
    // origin - (x/2, 0, y/2). The lines run across x on the loft's west wall (-8.75) and the hall's east wall (3.25),
    // across z on the north and south walls of Unit6 and Unit5 (113.8, 103.8). Walls and railings stand on these lines,
    // so every tile ends at one. Tile (0, 0) lies at the east room's north-east corner, so all tiles have coordinates
    // from 0 up, the range the heatmap colours.
    private const float GridZ = 113.55f, GalleryEdge = 103.8f;
    // The bars of the gallery's railing, and its unseen wall up to the line.
    private const float GalleryRailZ = 103.76f;
    // The upper floor's walls and railings lie within 1 cm of the line; these hold them there.
    private const float WallSkin = .004f;

    // B: the walkway east of the loft (x -2.05 .. 0.25) and Unit5 behind it, up to the hall's east wall.
    public const string EastExtensionId = "storage-east-room";
    private const float EastFront = .25f, EastWall = 3.25f, SiteWallHeight = LoftCeiling - UpperY, EastGridX = 3.0f;
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
    private static readonly Vector3 GasBottlePivot = new(-95.93f, -4.00f, 54.70f);
    // The bucket's pivot lies 0.20 under its base.
    private const float BucketUprightDrop = .20f;
    private static readonly Vector3 CinderblockPivot = new(-96.74f, -4.08f, 59.92f);
    // The fire barrel stands in the yard west of the stair, south of the bush (x -13.11 .. -10.63, z 103.08 .. 105.48) and
    // clear of the stair (x from -10.49) and the street lamp's base (x -12.5 .. -11.5, z 100.5 .. 101.5).
    private static readonly Vector3 FireDrumAt = new(-12.35f, 0f, 102.35f);
    internal static readonly Vector3 FireSpot = FireDrumAt + new Vector3(0, .85f, 0);
    // One row of lamps over the middle.
    private const float EastLampX = .6f;

    // C: the walkway south of B (x -2.05 .. 0.25) and the three units of Unit4 east of it. The elevator stands between
    // them and B's room; its shaft behind it stays closed.
    public const string SouthExtensionId = "storage-south-room";
    private const float SouthEnd = 94f, SouthUnitNorth = 101.5f, SouthGridZ = GridZ - 10f;
    private const float ElevatorWest = .71f;
    // Unit4 and the walkway end 0.3 m and 0.2 m short of the grid's lines; a wall on each line closes them there: across
    // the south end of the walkway and Unit4, and in front of Unit4's north wall. The elevator's face is boxed in up to
    // the open faces of the rooms (x 0.25), so the walkway keeps one width beside it.
    private const float SouthWall = SouthGridZ - 9.25f, SouthRoomNorth = SouthGridZ - 2.25f;
    // The walkway's railing to the hall (x -2.1) stands between two lines; a steel kerb in front of it reaches to the
    // line at x -1.75 and leaves out the top of the ladder.
    private const float KerbEdge = -1.75f, KerbHeight = .1f, KerbBack = -2.08f;
    private const float SouthLampX = 1.1f;

    // Order of the states; each level includes all lower ones.
    internal static readonly string[] States = { "a-stair", "a-door", "a-complete", "b-open", "b-complete", "c-open", "c-complete" };

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
        4 => (new Vector3(EastFront - .1f, UpperY + 1.5f, 110.6f), 0f),
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
        MapExpansionsApi.WorldStateApplied += StorageGridMigration.CheckUpperGridIndex;
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
        Navigation(state, level);
        if (level >= 3) Gallery(state, geometry, level);
        if (level >= 4) WalkwayOpen(state, geometry, level);
        if (level >= 5) EastRoom(state, geometry);
        if (level >= 6) SouthOpen(state, geometry);
        if (level >= StorageLadder.FromLevel) LadderOpening(state, geometry);
        if (level >= 7) SouthRoom(state, geometry);
        SiteProps(state, geometry, level);
        state.Copies.Add(geometry);
        return state;
    }

    // A jerry can on its broad side; the lift keeps it out of the floor.
    private static readonly Vector3 JerryCanFlat = new(0f, 0f, 90f);
    private const float JerryCanFlatLift = .08f;

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
                // South-west of the stair, clear of its stringers (x from -10.59), the overpass leg (x -12.35 .. -11.65,
                // z 100.65 .. 101.35) and the sign board (z from 99.55).
                state.Copies.Add(Copy("outside-barrels", ManorSite + "SM_Prop_BarrelStack_01", BarrelsPivot, new Vector3(-12.3f, 0f, 98.6f), 8));
                break;
            case 2:
                Outside(state);
                Stock(state, geometry, "loft", -3.3f, UpperY, 112.7f, 12);
                // The stack's base is 1.47 m square; turned by 5° it keeps 5 cm off the west and north linings.
                state.Copies.Add(Copy("loft-barrels", ManorSite + "SM_Prop_BarrelStack_01", BarrelsPivot, new Vector3(-7.88f, UpperY, 112.93f), 5));
                state.Copies.Add(Copy("loft-generator", BuildSite + "SM_Prop_Generator_01", GeneratorPivot, new Vector3(-3.25f, UpperY, 104.85f), -7));
                SiteTable(state, geometry, "loft-table", -5.5f, UpperY, 108.8f, 4);
                Clutter(state, geometry, "loft-trash-can", Camp + "Trash Can Prop", TrashCanPivot, -6.55f, UpperY, 113.25f, 37, .32f, .77f);
                Clutter(state, geometry, "loft-crate", Camp + "Wood Crate Prop (1)", CratePivot, -8.2f, UpperY, 111.45f, 14, .45f, .8f);
                Clutter(state, geometry, "loft-gas-bottle", Camp + "Gas Bottle Prop (1)", GasBottlePivot, -2.48f, UpperY, 106.14f, 21, .32f, .72f);
                // The jerry can lies on its side by the generator, the bucket stands beside the table.
                state.Copies.Add(Copy("loft-jerry-can", Camp + "Jerry Can", JerryCanPivot, new Vector3(-3.9f, UpperY + JerryCanFlatLift, 104.3f), 68,
                    tilt: JerryCanFlat));
                state.Copies.Add(Copy("loft-bucket", Camp + "SM_Prop_Bucket_01", BucketPivot, new Vector3(-3.98f, UpperY - BucketUprightDrop, 108.3f), 133));
                Cinderblock(state, "loft-cinderblock-a", -5.9f, UpperY, 113.4f, 11);
                Cinderblock(state, "loft-cinderblock-b", -8.35f, UpperY, 110.25f, 71);
                Cinderblock(state, "loft-cinderblock-c", -2.45f, UpperY, 110.95f, -26);
                // Brick from the cut through the west wall, inside and out.
                RubblePile(state, "door-rubble", -8.2f, UpperY, 107f, .35f, 11);
                RubblePile(state, "door-rubble-stray", -7.55f, UpperY, 105.25f, .14f, 23);
                RubblePile(state, "landing-rubble", -9.85f, LandingDeck, 106f, .14f, 37);
                WorkLight(state, "loft-work-light", -5.5f, 108.8f);
                break;
            case 4:
                Stock(state, geometry, "walkway", -1f, UpperY, 112.9f, 8);
                // The walkway is 2.3 m wide between the roller-door faces (x 0.25) and the loft; the barrels stand square
                // north of the plank wall (z to 103.94), the generator and the can beside each other north of them.
                state.Copies.Add(Copy("walkway-barrels", ManorSite + "SM_Prop_BarrelStack_01", BarrelsPivot, new Vector3(-.6f, UpperY, 104.9f)));
                state.Copies.Add(Copy("walkway-generator", BuildSite + "SM_Prop_Generator_01", GeneratorPivot, new Vector3(-1.55f, UpperY, 106.45f), 8));
                SiteTable(state, geometry, "walkway-table", -.42f, UpperY, 108.8f, 86);
                state.Copies.Add(Copy("walkway-jerry-can", Camp + "Jerry Can", JerryCanPivot, new Vector3(-.35f, UpperY, 106.35f), 152));
                Clutter(state, geometry, "walkway-gas-bottle", Camp + "Gas Bottle Prop (1)", GasBottlePivot, -1.93f, UpperY, 111.62f, -29, .32f, .72f);
                Clutter(state, geometry, "walkway-trash-can", Camp + "Trash Can Prop", TrashCanPivot, -1.88f, UpperY, 109.95f, -19, .32f, .77f);
                state.Copies.Add(Copy("walkway-bucket", Camp + "SM_Prop_Bucket_01", BucketPivot, new Vector3(-1.86f, UpperY - BucketUprightDrop, 110.7f), 47));
                Cinderblock(state, "walkway-cinderblock", -1.7f, UpperY, 107.6f, 41);
                break;
            case 6:
                // The room's linings lie at x 3.24, z 94.31 and z 101.29. From the south: the generator, the pallet and the
                // jerry can between them, the gas bottle and the bucket with two blocks, the table, the barrels in the
                // north-east corner; each keeps a few centimetres off the linings and its neighbours.
                Stock(state, geometry, "south", 2.4f, UpperY, 95.15f, 4);
                state.Copies.Add(Copy("south-barrels", ManorSite + "SM_Prop_BarrelStack_01", BarrelsPivot, new Vector3(2.35f, UpperY, 100.4f), 8));
                state.Copies.Add(Copy("south-generator", BuildSite + "SM_Prop_Generator_01", GeneratorPivot, new Vector3(.93f, UpperY, 94.93f), -14));
                SiteTable(state, geometry, "south-table", 2.6f, UpperY, 98.3f, 93);
                WorkLight(state, "south-work-light", SouthLampX, 97.7f);
                // The jerry can lies on its side by the generator, the bucket stands by the blocks.
                state.Copies.Add(Copy("south-jerry-can", Camp + "Jerry Can", JerryCanPivot, new Vector3(1.23f, UpperY + JerryCanFlatLift, 95.36f), 107,
                    tilt: JerryCanFlat));
                Clutter(state, geometry, "south-gas-bottle", Camp + "Gas Bottle Prop (1)", GasBottlePivot, .73f, UpperY, 96.45f, 24, .32f, .72f);
                state.Copies.Add(Copy("south-bucket", Camp + "SM_Prop_Bucket_01", BucketPivot, new Vector3(1.05f, UpperY - BucketUprightDrop, 97.1f), -58));
                Cinderblock(state, "south-cinderblock-a", 1.63f, UpperY, 97.42f, 17);
                Cinderblock(state, "south-cinderblock-b", 1.6f, UpperY, 98.2f, 63);
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
    // The cones keep clear of the bus stop (walls from x -11.52, base to z 97.39 west of x -11.30).
    private static void Outside(WorldStateDefinition state)
    {
        state.Copies.Add(Copy("site-sign", BuildSite + "CS_sign_02", SignPivot, new Vector3(-11.25f, -.05f, 99.6f), -10));
        var cones = new[] { new Vector2(-10.85f, 95.3f), new Vector2(-9.55f, 95.3f), new Vector2(-10.75f, 97.9f) };
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
    private static void WorkLight(WorldStateDefinition state, string id, float x, float z) => state.Copies.Add(Lamp(id, x, z, null));

    // A copy of Unit 1's ceiling bar under the upper level's ceiling; with a switch id it follows that switch.
    private static CopyDefinition Lamp(string id, float x, float z, string? lightSwitch)
    {
        var light = Copy(id, LampModel, LampPivot, new Vector3(x, LampY, z));
        light.LightsOn = true;
        light.LightRange = LampRange;
        light.LightIntensity = LampIntensity;
        if (lightSwitch != null) light.LightSwitch = "copy:" + lightSwitch;
        return light;
    }

    // A low mound of broken brick with flat scraps of the units' sheet. A box cannot turn on its own, so each handful
    // is a copy with its own yaw around the middle of the heap.
    private static void RubblePile(WorldStateDefinition state, string id, float x, float floor, float z, float radius, int seed)
    {
        var random = new System.Random(seed);
        float Between(float from, float to) => from + (float)random.NextDouble() * (to - from);
        var centre = new Vector3(x, floor, z);
        var laid = new System.Collections.Generic.List<RubblePiece>();
        for (int turn = 0; turn < 3; turn++)
        {
            float yaw = Between(0f, 180f);
            var turnBy = Quaternion.Euler(0, yaw, 0);
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
                // A piece whose face lies within a few millimetres of a parallel face of a piece it touches grows a
                // little until no two faces share a plane; its bottom sinks into the floor or the piece below.
                // Its place in the world is known before the check, which compares it with the other handfuls' pieces there.
                var piece = new RubblePiece { Turn = turn, X = px, Z = pz, HalfLength = halfLength, HalfWidth = halfWidth, Bottom = bottom, Top = bottom + tall,
                    At = centre + turnBy * (new Vector3(px, floor, pz) - centre) };
                for (int tries = 0; tries < 12 && laid.Exists(other => piece.Clashes(other)); tries++)
                {
                    piece.Top += .007f;
                    piece.Bottom -= .007f;
                    piece.HalfLength += .007f;
                    piece.HalfWidth += .007f;
                }
                laid.Add(piece);
                Face(handful, "piece-" + i, scrap ? UnitSheet : Brick, px - piece.HalfLength, piece.Bottom, pz - piece.HalfWidth,
                    px + piece.HalfLength, piece.Top, pz + piece.HalfWidth);
            }
            state.Copies.Add(handful);
        }
    }

    // One box of a rubble pile: its place in the handful's own frame and, for pieces of other handfuls, in the world.
    private sealed class RubblePiece
    {
        private const float Gap = .006f;
        public int Turn;
        public Vector3 At;
        public float X, Z, HalfLength, HalfWidth, Bottom, Top;

        public bool Clashes(RubblePiece other)
        {
            var at = Turn == other.Turn ? new Vector3(X, 0f, Z) : At;
            var otherAt = Turn == other.Turn ? new Vector3(other.X, 0f, other.Z) : other.At;
            float reach = new Vector2(HalfLength, HalfWidth).magnitude + new Vector2(other.HalfLength, other.HalfWidth).magnitude;
            if (new Vector2(at.x - otherAt.x, at.z - otherAt.z).magnitude > reach) return false;
            // Tops and bottoms face up and down whatever the yaw.
            if (Mathf.Abs(Top - other.Top) < Gap || Mathf.Abs(Bottom - other.Bottom) < Gap) return true;
            // The pieces of one handful share its yaw, so their sides are parallel.
            if (Turn != other.Turn || Bottom >= other.Top || other.Bottom >= Top) return false;
            bool acrossX = X - HalfLength < other.X + other.HalfLength && other.X - other.HalfLength < X + HalfLength;
            bool acrossZ = Z - HalfWidth < other.Z + other.HalfWidth && other.Z - other.HalfWidth < Z + HalfWidth;
            if (acrossZ && (Mathf.Abs(X - HalfLength - (other.X - other.HalfLength)) < Gap || Mathf.Abs(X + HalfLength - (other.X + other.HalfLength)) < Gap)) return true;
            return acrossX && (Mathf.Abs(Z - HalfWidth - (other.Z - other.HalfWidth)) < Gap || Mathf.Abs(Z + HalfWidth - (other.Z + other.HalfWidth)) < Gap);
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
        room.Openings.Add(BoundsOf(EastFront - .1f, UpperY, SouthEnd + .01f, EastFront + .1f, LoftCeiling + .02f, SouthUnitNorth - .01f));
        room.Bands.Add((0f, UnitSheet));
        room.CeilingMaterial = UnitSheet;
        room.FloorMaterial = FloorSheet;
        room.FrameMaterial = Metal;
        state.Rules.Add(room);
        Face(geometry, "south-ceiling", UnitSheet, EastFront + .006f, LoftCeiling, SouthEnd, EastWall - .012f, LoftCeiling + .006f, SouthUnitNorth);
        // A steel beam over the room's open face to the walkway hides the top edge of the lining. Its ends lie inside
        // the walls on the grid's lines.
        Face(geometry, "south-room-beam", Metal, EastFront - .07f, LoftCeiling - .15f, SouthWall - .05f, EastFront + .07f, UnitTop - .012f, SouthRoomNorth + .005f);
        // The walls on the grid's lines: across the walkway and Unit4 at the south end, up to the units' top, and in
        // front of Unit4's north wall up to the room's ceiling. Their ends and feet reach into what they meet.
        Solid(geometry, "south-end-wall", UnitSheet, KerbBack - .048f, UpperY, SouthEnd - .01f, EastWall - .005f, UnitTop - WallSkin, SouthWall);
        Solid(geometry, "south-room-north-wall", UnitSheet, EastFront, UpperY - .006f, SouthRoomNorth, EastWall - .005f, LoftCeiling, SouthRoomNorth + .01f);
        // The elevator's face boxed in from the walkway, flush with the rooms' open faces.
        Solid(geometry, "elevator-box", UnitSheet, EastFront, UpperY, SouthRoomNorth + .01f, ElevatorWest - .004f, UnitTop - WallSkin, UnitSouth);
        // The kerb in front of the walkway's railing, on both sides of the ladder's top.
        Solid(geometry, "walkway-kerb-south", Metal, KerbBack, UpperY - .006f, SouthWall, KerbEdge, UpperY + KerbHeight, LadderSouth);
        Solid(geometry, "walkway-kerb-north", Metal, KerbBack, UpperY - .006f, LadderNorth, KerbEdge, UpperY + KerbHeight, GalleryEdge - .01f);
    }

    // C, night 2: sheet metal clads the elevator's face to the walkway, lamps hang in the south room on Unit 1's
    // switch, and the floor belongs to the property.
    private static void SouthRoom(WorldStateDefinition state, CopyDefinition geometry)
    {
        foreach (var (id, z) in new[] { ("south-light-south", 96.0f), ("south-light-north", 99.4f) })
            state.Copies.Add(Lamp(id, SouthLampX, z, SouthSwitch));
        // On the room's north wall beside the way in from the walkway, facing south into the room.
        state.Copies.Add(WallSwitch(SouthSwitch, EastFront + .3f, SouthRoomNorth - SwitchProud, -90));

        // On the upper grid, tiles y 20 .. 38 cover world z 103.3-(y-20)/2 .. 103.8-(y-20)/2, from the east wall to the
        // kerb (x -1.75) and from B's tiles to the south end wall (z 94.3), without the elevator's box (x 0.25 .. 3.25,
        // z 101.3 .. 103.8).
        var property = new PropertyExtensionDefinition
        {
            Id = SouthExtensionId, PropertyCode = "storageunit", GridIndex = UpperGridIndex,
        };
        for (int x = 0; x <= 9; x++)
            for (int y = 0; y <= 18; y++)
                if (!(x <= 5 && y <= 4)) property.Tiles.Add(new GridCoordinate(x, y + SouthShiftY));
        property.Bounds.Add(BoundsOf(FacesBack, UpperY - .1f, SouthEnd, EastWall, LoftCeiling, UnitSouth));
        // The room's walls take wall items: the east wall up to the elevator's box, the south end wall, and the
        // elevator's south face east of the way in.
        WallSurfaces.Add(property, "south-room-east-wall", PropertySurfaceFace.Left, EastWall - LiningGap, SouthWall + .05f, SouthRoomNorth - .05f, WallBottom, WallTop);
        WallSurfaces.Add(property, "south-room-south-wall", PropertySurfaceFace.Front, SouthWall, KerbEdge + .05f, EastWall - .05f, WallBottom, WallTop);
        WallSurfaces.Add(property, "south-room-north-wall", PropertySurfaceFace.Back, SouthRoomNorth, EastFront + .05f, EastWall - .05f, WallBottom, WallTop);
        state.PropertyExtensions.Add(property);
    }

    // Wall surfaces upstairs run from just above the floor to just below the ceiling.
    private const float WallBottom = UpperY + .05f, WallTop = LoftCeiling - .05f;

    // B, night 1: the roller-door faces between the loft and the walkway go, and a plank wall closes the walkway to the
    // south, where C has not been built yet.
    private static void WalkwayOpen(WorldStateDefinition state, CopyDefinition geometry, int level)
    {
        for (int i = 7; i <= 10; i++)
            state.Rules.Add(Rule("loft-face-" + i, $"UnitFace ({i})", "Storage warehouse", ObjectEffect.Remove,
                BoundsOf(FacesBack - .05f, UpperY - .05f, UnitSouth - .05f, UnitEast + .05f, UnitTop + .05f, UnitNorth + .05f)));
        // The loft's ceiling ends at the old wall; a steel beam carries its edge over the opening.
        if (level < 5) Face(geometry, "walkway-beam", Metal, FacesBack - .006f, LoftCeiling - .156f, UnitSouth + .006f, UnitEast, UnitTop - .02f, UnitNorth);
        if (level < 6) SiteWall(geometry);
        // The linings of the loft and of Unit5 both end at the block's north face; a sheet closes the walkway between
        // them, in front of the hall's brick. The openings to the walkway also cut 10 cm off each lining's north wall
        // beside them, so the sheet reaches on behind both linings. Its face stands 5 mm behind their plane and 5 mm in
        // front of the brick: closer than that, the depth buffer flickers between the two where they overlap.
        Solid(geometry, "walkway-north-end", UnitSheet, UnitEast - .16f, UpperY, UnitNorth - .005f, EastFront + .16f, UnitTop - WallSkin, UnitNorth - .002f);
        // The same cut takes the south walls' last 10 cm beside both openings; a sheet behind each closes the slit. It is
        // 1 mm thin and 4.5 mm off both the lining and the block's face, so it never draws at their depth.
        Face(geometry, "loft-south-end", UnitSheet, UnitEast - .16f, UpperY, UnitSouth + .0045f, UnitEast, LoftCeiling, UnitSouth + .0055f);
        Face(geometry, "east-south-end", UnitSheet, EastFront, UpperY, UnitSouth + .0045f, EastFront + .16f, LoftCeiling, UnitSouth + .0055f);
        EndCover(geometry, "loft-north-cover", UnitEast - .18f, UnitEast, UnitNorth - LiningGap, -1);
    }

    // A room's lining runs a 6 cm steel frame around the opening of its open face, and the opening cuts that frame and a
    // slit beside it into each end wall it reaches, floor to ceiling, where they show as a black line. A strip of the
    // wall's sheet 5 mm in front of the end wall's lining (plane z) covers both; inward points into the room.
    private static void EndCover(CopyDefinition geometry, string id, float x1, float x2, float z, float inward)
    {
        float near = z + inward * .005f, far = z + inward * .006f;
        Face(geometry, id, UnitSheet, x1, UpperY, Mathf.Min(near, far), x2, LoftCeiling, Mathf.Max(near, far));
    }

    // A temporary wall closes the walkway to the south until C opens it: boards nailed to a timber frame, the frame on
    // the side of the rooms and its face on the grid's line. It starts inside the corner post.
    private static void SiteWall(CopyDefinition geometry)
    {
        // The face stands a few millimetres behind the corner post's north face.
        const float front = GalleryEdge - WallSkin - .006f, back = front - .1f, top = UpperY + SiteWallHeight, west = CornerPostEast - .02f;
        Solid(geometry, "walkway-site-wall", Wood, west, UpperY, back - .04f, EastFront, top, back);
        Face(geometry, "walkway-site-wall-sole", Trim, west, UpperY, back, EastFront, UpperY + .1f, front);
        Face(geometry, "walkway-site-wall-head", Trim, west, top - .1f, back, EastFront, top, front);
        // The noggin lies a little behind the studs' faces, so the two never share a plane.
        Face(geometry, "walkway-site-wall-noggin", Trim, west, UpperY + 1.2f, back + .006f, EastFront, UpperY + 1.3f, front - .02f);
        for (int i = 0; i <= 4; i++)
        {
            // The last stud's east face stays clear of the gallery beam's end, which stops 6 mm short of Unit5's face.
            float x = Mathf.Lerp(CornerPostEast + .03f, EastFront - .04f, i / 4f);
            Face(geometry, "walkway-site-wall-stud-" + i, Trim, x - .025f, UpperY + .1f, back, x + .025f, top - .1f, front);
        }
        // The frame is drawn without a collider; an unseen wall over its depth ends on the grid's line, which the
        // drawn faces stay a few millimetres behind.
        Block(geometry, "walkway-site-wall-frame", west, UpperY, back, EastFront, top, GalleryEdge - WallSkin);
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
        room.Openings.Add(BoundsOf(EastFront - .1f, UpperY, UnitSouth + .01f, EastFront + .1f, LoftCeiling + .02f, UnitNorth - .01f));
        room.Bands.Add((0f, UnitSheet));
        room.CeilingMaterial = UnitSheet;
        room.FloorMaterial = FloorSheet;
        room.FrameMaterial = Metal;
        state.Rules.Add(room);
        EndCover(geometry, "east-south-cover", EastFront, EastFront + .17f, UnitSouth + LiningGap, 1);
        EndCover(geometry, "east-north-cover", EastFront, EastFront + .17f, UnitNorth - LiningGap, -1);

        foreach (var (id, z) in new[] { ("east-light-south", 106.0f), ("east-light-middle", 108.8f), ("east-light-north", 111.6f) })
            state.Copies.Add(Lamp(id, EastLampX, z, EastSwitch));
        // On Unit 5's south wall at the corner where the walkway leads in, facing north into the room.
        state.Copies.Add(WallSwitch(EastSwitch, EastFront + .3f, UnitSouth + LiningGap + SwitchProud, 90));

        // The walkway and the east room on the upper grid beside the loft: tile (x, y) covers world x 2.75-x/2 ..
        // 3.25-x/2 and z 113.3-y/2 .. 113.8-y/2, from the east wall to the loft's edge (x -2.25) and from the north wall
        // to Unit5's south wall and the walkway's south end (z 103.8).
        var property = new PropertyExtensionDefinition
        {
            Id = EastExtensionId, PropertyCode = "storageunit", GridIndex = UpperGridIndex,
        };
        for (int x = 0; x <= 10; x++)
            for (int y = 0; y <= 19; y++)
                property.Tiles.Add(new GridCoordinate(x, y));
        property.Bounds.Add(BoundsOf(FacesBack, UpperY - .1f, UnitSouth - .2f, EastWall, LoftCeiling, UnitNorth));
        // Wall items hang on the east room's east, north and south walls and on the walkway's north end.
        WallSurfaces.Add(property, "east-room-east-wall", PropertySurfaceFace.Left, EastWall - LiningGap, UnitSouth + .05f, UnitNorth - .05f, WallBottom, WallTop);
        WallSurfaces.Add(property, "east-room-north-wall", PropertySurfaceFace.Back, UnitNorth - LiningGap, FacesBack + .05f, EastWall - .05f, WallBottom, WallTop);
        WallSurfaces.Add(property, "east-room-south-wall", PropertySurfaceFace.Front, UnitSouth + LiningGap, EastFront + .05f, EastWall - .05f, WallBottom, WallTop);
        state.PropertyExtensions.Add(property);
    }

    // Night 3: the loft's south wall opens onto the hall behind a railing, lamps hang under the ceiling, and employees
    // can walk up the stair into the room.
    private static void Gallery(WorldStateDefinition state, CopyDefinition geometry, int level)
    {
        // The sign and the stain were painted on the wall that opens.
        state.Rules.Add(Rule("hall-logo", "Logo (1)", "Storage warehouse", ObjectEffect.Remove,
            BoundsOf(-4.85f, 3.85f, 103.7f, -2.5f, 6.2f, 103.85f)));
        state.Rules.Add(Rule("hall-stain", "Quad", "Decals 0004 (1)", ObjectEffect.Remove,
            BoundsOf(-8.85f, 3.15f, 103.7f, -4.5f, 6.25f, 103.85f)));
        // A steel beam closes the opening under the block's top and hides the edge of the loft's ceiling; from B's second
        // night it runs on over the walkway to Unit5's face.
        float beamEast = (level >= 5 ? EastFront : UnitEast) - .006f;
        Face(geometry, "gallery-beam", Metal, UnitWest + .006f, LoftCeiling - .15f, UnitSouth - .07f, beamEast, UnitTop - .012f, UnitSouth + .07f);
        // A steel cap covers the cut west wall at the opening's west end, 5 mm proud of the beam's sides.
        Face(geometry, "gallery-west-cap", Metal, UnitWest - .26f, UpperY, UnitSouth - .075f, UnitWest + .02f, UnitTop, UnitSouth + .075f);
        // A steel post stands at the corner where the opening meets the walkway. It takes in the end of the gallery's
        // railing and the walkway railing's last post, and its north face lies on the grid's line. Its top stays inside
        // the beam.
        // The post reaches west to the loft's edge: before B the end of the walkway's roller face beside it has no side of
        // its own, and from B on the steel frame around the gallery opening ends there. Both show as a dark line next to
        // a narrower post.
        Face(geometry, "walkway-corner-post", Metal, FacesBack - .006f, UpperY - .013f, RailEnd.z - .03f, CornerPostEast, UnitTop - WallSkin, GalleryEdge - WallSkin);
        GalleryRail(state, geometry);

        foreach (var (id, z) in new[] { ("loft-light-south", 106.0f), ("loft-light-middle", 108.8f), ("loft-light-north", 111.6f) })
            state.Copies.Add(Lamp(id, -5.5f, z, LoftSwitch));
        // On the west wall just south of the door from the landing; the door swings open against the wall north of it.
        state.Copies.Add(WallSwitch(LoftSwitch, UnitWest + LiningGap + SwitchProud, WindowSouth - .35f, 180));

        // The loft belongs to the property, so employees reach what stands in it.
        // Its extension builds the upper floor's grid, which stays the same from here on so the build never rebuilds
        // it under what stands on it. Tiles x 11 .. 23 cover world x 2.75-x/2 .. 3.25-x/2 and z 113.3-y/2 ..
        // 113.8-y/2: the loft from the west wall (x -8.75) to the walkway's faces (x -2.25) and from the north wall to
        // the railing (z 103.8), without the corner the door swings through (x -8.75 .. -7.25, z 104.8 .. 106.3).
        var property = new PropertyExtensionDefinition
        {
            Id = UpperGridExtensionId, PropertyCode = "storageunit", GridIndex = 0,
            GridOrigin = new Vector3(EastGridX, UpperY, GridZ),
        };
        for (int x = 0; x <= 12; x++)
            for (int y = 0; y <= 19; y++)
                if (!(x >= 10 && y >= 15 && y <= 17)) property.Tiles.Add(new GridCoordinate(x + LoftShiftX, y));
        property.Bounds.Add(BoundsOf(UnitWest, UpperY - .1f, UnitSouth - .2f, FacesBack, LoftCeiling, UnitNorth));
        // Wall items hang on the loft's west wall north of the door's swing and on its north wall.
        WallSurfaces.Add(property, "loft-west-wall", PropertySurfaceFace.Right, UnitWest + LiningGap, LandingNorth + .05f, UnitNorth - .05f, WallBottom, WallTop);
        WallSurfaces.Add(property, "loft-north-wall", PropertySurfaceFace.Back, UnitNorth - LiningGap, UnitWest + .05f, FacesBack - .05f, WallBottom, WallTop);
        state.PropertyExtensions.Add(property);
    }

    // The stair and the landing are one walking volume with the ground in front of the stair, so the fitter reaches the
    // landing while the door is cut. From the door's night the loft behind the door joins it.
    private static void Navigation(WorldStateDefinition state, int level)
    {
        if (level < 2)
        {
            // The loft is still a closed block; the volume ends at the west wall and holds only the stair and the landing.
            state.NavigationBounds = BoundsOf(-10.9f, -.5f, 95.4f, UnitWest - .2f, UnitTop, UnitNorth);
            state.NavigationAgents.AddRange(new[] { "Humanoid", "Ignore Costs", "Employee" });
            state.NavigationExistingFloor.Add(BoundsOf(-10.9f, -.3f, 95.4f, UnitWest - .2f, .2f, UnitNorth));
            state.NavigationProbes.Add(new Vector3(-9.84f, 0f, 98.4f));
            state.NavigationProbes.Add(new Vector3(-9.84f, .91f, 100.5f));
            state.NavigationProbes.Add(new Vector3(-9.6f, LandingTop, 105.5f));
            return;
        }
        // From B's second night the walkway and Unit5 join the volume; the hall below them is Unit 2's and keeps the
        // world's walking map like Unit 1.
        // From B's first night the walkway in front of Unit5 joins it, so the crew reaches its stations there.
        float east = level >= 5 ? EastWall : level >= 4 ? EastFront : FacesBack;
        // From C's first night the walkway south of B and Unit4 reach to the hall's south wall.
        float south = level >= 6 ? SouthEnd - .05f : 95.4f;
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
        if (level >= 4) state.NavigationProbes.Add(new Vector3(-.9f, UpperY, 109f));
        if (level >= 5) state.NavigationProbes.Add(new Vector3(1.75f, UpperY, 109f));
        // The walkway beside the elevator, its south end, and the middle of the south room.
        if (level >= 6)
        {
            state.NavigationProbes.Add(new Vector3(-.9f, UpperY, 102.6f));
            state.NavigationProbes.Add(new Vector3(-.9f, UpperY, 95f));
            state.NavigationProbes.Add(new Vector3(1.75f, UpperY, 97.75f));
        }
    }

    // The walkway's railing continues along the opening to the corner post. The vanilla piece is 1.9 m long with a post
    // at each end; each piece after the first leaves out its first post, where the piece before ends, and the run's last
    // piece lies over the one before it. Every second piece sits a few millimetres aside and above its neighbours, so
    // the ends that meet or overlap never share a plane. Its unseen wall ends on the grid's line.
    private static void GalleryRail(WorldStateDefinition state, CopyDefinition geometry)
    {
        const float piece = 1.9f, start = UnitWest + .05f, cross = GalleryRailZ;
        float end = RailEnd.x;
        int full = (int)((end - start + .001f) / piece);
        float rest = end - start - full * piece;
        bool overlaps = rest > .05f;
        int count = overlaps ? full + 1 : full;
        for (int i = 0; i < count; i++)
        {
            bool last = i == count - 1, aside = i % 2 == 1;
            float from = last ? end - piece : start + i * piece;
            var rail = Copy("gallery-rail-" + i, Fence, FencePost, new Vector3(from, UpperY + (aside ? RailLift : 0f), cross - (aside ? RailLift : 0f)), 90);
            if (i > 0) rail.Exclude.Add("outdoorfence/Post");
            // The corner post takes in the run's end.
            if (last) rail.Exclude.Add("outdoorfence/Post.001");
            state.Copies.Add(rail);
        }
        geometry.Boxes.Add(new BoxDefinition
        {
            Id = "gallery-guard", Kind = BoxKind.Block, Box = BoundsOf(start, UpperY, cross - .07f, end, UpperY + GuardHeight, GalleryEdge - WallSkin)
        });
    }

    // The ladder from the hall comes up through the walkway's railing over Unit 3. The vanilla piece above it,
    // "OutdoorFence (1)" from z 99.89 to 101.79, loses its bars and keeps its posts. The opening runs from the piece's
    // south post at z 99.89 to the post of a copy a few millimetres behind the vanilla run, shifted four whole bar
    // spaces of 0.19 m north so its bars cover the vanilla bars: 1.14 m to climb through. Steel brackets hold the
    // rails to Unit 3's front and reach round behind them.
    private const float BarSpace = .19f, UnitThreeFront = -1.76f, LadderSouth = 99.89f;
    private static readonly float LadderNorth = FencePost.z - 4 * BarSpace;
    // A copy laid over vanilla rails sits this far above them as well as aside, so their flat faces never share a plane.
    // The fence's bar billboards lie 6 mm outside its crossbeams and a post's faces 13 mm outside them, so the step
    // keeps clear of 0, 6 and 13 mm.
    private const float RailLift = .020f;
    private static readonly float[] BracketHeights = { 1.0f, 2.3f };

    private static void LadderOpening(WorldStateDefinition state, CopyDefinition geometry)
    {
        const float vanillaSouth = LadderSouth;
        // The copy's bars line up with the vanilla piece north of it, which it overlaps; it also steps north, so the
        // bars' sides never share a plane.
        float x = FencePost.x + RailLift, north = LadderNorth + RailLift;
        state.Rules.Add(Rule("ladder-rail-bars", "OutdoorFence (1)", "Storage warehouse", ObjectEffect.Remove,
            BoundsOf(FencePost.x - .1f, UpperY + .05f, vanillaSouth + .07f, FencePost.x + .1f, UpperY + 2.1f, FencePost.z - .07f)));
        // North of the opening the copy starts with its own post.
        var northRail = Copy("ladder-rail-north", Fence, FencePost, new Vector3(x, UpperY + RailLift, north));
        northRail.Exclude.Add("outdoorfence/Post.001");
        state.Copies.Add(northRail);
        // The copy carries no collider; an unseen wall takes the place of the vanilla bars north of the opening.
        geometry.Boxes.Add(new BoxDefinition
        {
            Id = "ladder-rail-guard-north", Kind = BoxKind.Block,
            Box = BoundsOf(FencePost.x - .07f, UpperY, north, FencePost.x + .07f, UpperY + GuardHeight, FencePost.z)
        });
        foreach (float z in new[] { StorageLadder.SouthRail, StorageLadder.NorthRail })
            foreach (float y in BracketHeights)
            {
                string id = Mathf.RoundToInt(z * 100) + "-" + Mathf.RoundToInt(y * 10);
                Face(geometry, "ladder-bracket-" + id, Metal, StorageLadder.BackX - .22f, y - .006f, z - .02f, UnitThreeFront - .006f, y + .056f, z + .02f);
                // A plate on Unit 3's front carries each bracket, so it reads as bolted to the wall.
                Face(geometry, "ladder-bracket-plate-" + id, Metal, UnitThreeFront - .02f, y - .04f, z - .07f, UnitThreeFront, y + .09f, z + .07f);
            }
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
            Face(geometry, "unit-face-hall", UnitSheet, UnitWest, UpperY, UnitSouth + .002f, UnitEast - .006f, UnitTop, UnitSouth + .008f);
            // The hidden skin carried a dark green band along the bottom of this face, under the hall sign. It lies in
            // front of the decals' plane.
            Face(geometry, "unit-face-hall-band", "alum sheet dark green", UnitWest, UpperY, UnitSouth - .008f, UnitEast, UpperY + .7f, UnitSouth - .004f);
        }
        // The hall's roof trusses run through the block; inside it they would stand in the room. From B's second night
        // their braces at the east wall would stand in the east room. All trusses are one renderer, which a state cuts
        // once, so one area covers both rooms. The cut reaches 5 mm behind the linings and under the floor's lining, so
        // the cut faces never lie in a lining's plane.
        float trussEast = level >= 5 ? EastWall : UnitEast;
        var trusses = Rule("unit-trusses", "Truss", "Storage warehouse", ObjectEffect.Cut,
            BoundsOf(UnitWest - .005f, UpperY - .005f, UnitSouth - .005f, trussEast + .005f, UnitTop - .01f, UnitNorth + .005f));
        // From C's first night Unit4 is a room too; the walkway between it and the hall keeps its trusses.
        if (level >= 6)
            trusses.Openings.Add(BoundsOf(EastFront + .01f, UpperY - .005f, SouthEnd + .01f, EastWall + .005f, UnitTop - .01f, SouthUnitNorth - .01f));
        state.Rules.Add(trusses);
        var room = Rule("unit-room", "Unit6", "Storage warehouse", ObjectEffect.Hollow, units);
        room.Openings.Add(DoorCut);
        // Night 3 opens the south wall from the floor to the loft's ceiling, between the west wall and the walkway's faces.
        if (level >= 3) room.Openings.Add(BoundsOf(UnitWest + .01f, UpperY, UnitSouth - .1f, FacesBack, LoftCeiling + .02f, UnitSouth + .1f));
        // B's first night opens the east wall to the walkway along its whole length.
        if (level >= 4) room.Openings.Add(BoundsOf(UnitEast - .1f, UpperY, UnitSouth + .01f, UnitEast + .1f, LoftCeiling + .02f, UnitNorth - .01f));
        room.Bands.Add((0f, UnitSheet));
        room.CeilingMaterial = UnitSheet;
        room.FloorMaterial = FloorSheet;
        room.FrameMaterial = Metal;
        state.Rules.Add(room);
        state.Rules.Add(Rule("door-grate", "Grate (2)", "Storage warehouse", ObjectEffect.Remove,
            BoundsOf(-9.05f, 3.6f, 104.8f, -8.9f, 5.7f, 106.15f)));
        // The walkway's unit faces with their roller doors reach 0.2 m into the block; a sheet inside covers their backs.
        if (level < 4) Solid(geometry, "unit-face-walkway", UnitSheet, FacesBack - .006f, UpperY, UnitSouth + .012f, FacesBack - .002f, LoftCeiling, UnitNorth);
        // The roof's eave dips below the block's top at the west wall; a lower ceiling keeps it out of the room.
        // From B's second night one panel runs on over the walkway and Unit5 to the east wall, without a seam. Its
        // edges stop short of the walls' faces.
        float ceilingEast = level >= 5 ? EastWall - .006f : UnitEast - .012f;
        Face(geometry, "loft-ceiling", UnitSheet, UnitWest, LoftCeiling, UnitSouth + .01f, ceilingEast, LoftCeiling + .006f, UnitNorth - .01f);
        // Rust decals on Unit 1's walls below reach up through the loft floor; only the part above the floor goes.
        state.Rules.Add(Rule("rust-east", "Quad", "Big rust 2", ObjectEffect.Cut, BoundsOf(-3.8f, UpperY - .1f, 107.4f, -3.7f, 3.8f, 114.3f)));
        state.Rules.Add(Rule("rust-west", "Quad", "Big rust 1", ObjectEffect.Cut, BoundsOf(-8.8f, UpperY - .1f, 107.9f, -8.7f, 3.5f, 114.1f)));

        // Steel reveals line the cut through the brick, the inner leaf and the units' skin.
        // The head and the sill reach a few millimetres over the jambs' ends and sides, the transom stops a few
        // millimetres inside the jambs' faces and runs into them, and the jambs end inside the casing, so no two faces
        // lie in one plane.
        const float outside = -9.26f, inside = UnitWest + LiningGap + .005f;
        // The jambs' feet sink into the sill, below the copied door's threshold at the sill's line.
        Solid(geometry, "door-jamb-south", Metal, outside, DoorSill - .006f, WindowSouth - .02f, inside, DoorHead, DoorSouth);
        Solid(geometry, "door-jamb-north", Metal, outside, DoorSill - .006f, DoorNorth, inside, DoorHead, WindowNorth + .02f);
        Face(geometry, "door-head", Metal, outside - .006f, DoorHead - .006f, WindowSouth - .026f, inside + .006f, DoorHead + .02f, WindowNorth + .026f);
        Face(geometry, "door-sill", Metal, outside - .006f, DoorSill - .02f, WindowSouth, inside + .006f, DoorSill + .003f, WindowNorth);
        // The copied leaf is 2.10 m tall, the cut 2.34 m; a steel transom closes the gap above the leaf.
        Solid(geometry, "door-transom", Metal, outside + .006f, DoorSill + DoorLeafHeight, DoorSouth - .006f, inside - .006f, DoorHead - .006f, DoorNorth + .006f);
        // A steel casing, 6 cm wide, frames the opening on the face of the room and on the brick outside.
        foreach (var (side, from, to) in new[] { ("inside", UnitWest + LiningGap + .002f, UnitWest + LiningGap + .022f), ("outside", outside - .02f, outside) })
        {
            Face(geometry, "door-casing-" + side + "-south", Metal, from, DoorSill, WindowSouth - .08f, to, DoorHead + .06f, WindowSouth - .02f);
            Face(geometry, "door-casing-" + side + "-north", Metal, from, DoorSill, WindowNorth + .02f, to, DoorHead + .06f, WindowNorth + .08f);
            Face(geometry, "door-casing-" + side + "-head", Metal, from, DoorHead, WindowSouth - .02f, to, DoorHead + .06f, WindowNorth + .02f);
        }

        // A working copy of the Storage Unit's own green door, networked like the original so it opens for everyone.
        // Its leaf stands in the middle of the wall, hinged at the north jamb; turned by 90° it runs south and its
        // inside faces the loft. It stands on the sill; its 2 m frame sits inside the brick around the cut.
        var hinge = new Vector3((outside + UnitWest) / 2f, DoorSill, DoorNorth - DoorGap);
        var door = Copy("loft-door", DoorRoot, DoorHinge, hinge, 90);
        door.VisualOnly = false;
        door.IndependentMesh = false;
        state.Copies.Add(door);
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
        StairUnderside(state);
        // The deck reaches into the brick, so its top never meets the wall's face in one edge. Its top lies 6 mm under the
        // ledge's along the wall, which ends at the same height, and its bottom 6 mm under the posts' tops.
        Solid(geometry, "landing", Metal, LandingWest, LandingBottom - .006f, LandingSouth, LandingEast + .06f, LandingDeck, LandingNorth);
        // The top tread ends 2.5 cm short of the landing, a riser's height below the deck. A kick plate between the
        // stringers closes that slit and the open riser up to the deck, for the eye and for the walking map, which the
        // slit would split.
        float tread = LandingTop - StairDrop - .242f;
        Solid(geometry, "landing-riser", Metal, -10.49f, tread - .01f, LandingSouth - .03f, -9.19f, LandingBottom, LandingSouth);
        // The docks stair stands on a raised floor: its lowest tread lies 0.40 above the grass, two of its 0.20 m rises.
        // One more tread, a going of 0.29 in front of it, makes the step up from the ground even. It is steel like the
        // stair and spans from stringer to stringer, a few millimetres into each.
        Solid(geometry, "stair-first-tread", Metal, -10.493f, FirstTread - .075f, 99.585f, -9.187f, FirstTread, 99.935f);
        // The stringers' feet are cut on a slant and open at the cut. A steel shoe closes each foot down into the grass,
        // a few millimetres wider than the stringer and just under the top of the cut.
        Solid(geometry, "stair-stringer-foot-west", Metal, -10.595f, -.02f, 99.519f, -10.485f, .233f, 99.65f);
        Solid(geometry, "stair-stringer-foot-east", Metal, -9.195f, -.02f, 99.519f, -9.085f, .233f, 99.65f);
        // Two posts carry the landing's open corners; the shipping container stands under its north half.
        Post(geometry, "landing-post-sw", LandingWest + .04f, LandingSouth + .04f, 0f, LandingBottom);
        Post(geometry, "landing-post-se", LandingEast - .06f, LandingSouth + .04f, 0f, LandingBottom);
        // A railing on the open west and north edges; the stair comes up on the south edge, the wall closes the east.
        // The west railing's last post stands on the north railing's line and its rails end inside the north rails;
        // the north railing starts inside the west railing's rails, without a first post of its own. The west railing
        // starts 1 cm north of the deck's south face, so its first post never shares that face's plane.
        Railing(geometry, "landing-west", false, LandingRail, LandingSouth + .01f, LandingNorth - .005f, railEnd: .012f);
        Railing(geometry, "landing-north", true, LandingNorth - .03f, LandingRail - .015f, LandingEast, firstPost: false);
    }

    // The game has no sloped railing at this pitch, so the landing's railing runs on down the stair's open west side:
    // upright posts, and rails in copies tilted to the slope of the nosings. They meet the landing's railing at its corner.
    private static void StairRail(WorldStateDefinition state, CopyDefinition geometry)
    {
        const float half = .025f, cross = LandingRail, top = LandingSouth + StairInto, foot = top - StairRun;
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
            // A little slimmer than the landing's rails, so where the two meet their sides never share a plane.
            Face(rail, "bar", Metal, AnchorPoint.x - .015f, AnchorPoint.y - .015f, AnchorPoint.z, AnchorPoint.x + .015f, AnchorPoint.y + .015f, AnchorPoint.z + length);
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

    // The world's walking map runs on over the ground under the stair, so an NPC there walks inside the steps. Unseen
    // blocks under the stair, stepped below its soffit, carve that ground out; NPCs go round the stair or up it. A copy
    // carves one box around all of its colliders, so each step is a copy of its own. The steps start a little up the
    // stair, clear of the seam where its own walking map meets the ground.
    private static void StairUnderside(WorldStateDefinition state)
    {
        const float top = LandingSouth + StairInto, from = 100.1f, below = .35f;
        const int steps = 8;
        float Nosing(float z) => LandingTop - StairDrop - (top - z) * StairRise / StairRun;
        float length = (LandingSouth - from) / steps;
        for (int i = 0; i < steps; i++)
        {
            float z0 = from + i * length, z1 = z0 + length;
            var step = new CopyDefinition
            {
                Id = "stair-underside-" + i, Template = Anchor, Frame = Anchor, OwnMesh = false, Obstacle = true,
                At = new LocalPoint(AnchorPoint, 0)
            };
            Block(step, "block", -10.55f, -.15f, z0, -9.13f, Nosing(z0) - below, z1);
            state.Copies.Add(step);
        }
    }

    private static void Railing(CopyDefinition geometry, string id, bool alongX, float cross, float start, float end, bool firstPost = true,
        float railEnd = .005f)
    {
        const float half = .025f;
        int posts = Math.Max(2, (int)Math.Ceiling((end - start) / 1.2f) + 1);
        // The posts stand in the deck, whose top lies below the walking height.
        for (int i = firstPost ? 0 : 1; i < posts; i++)
        {
            float at = Mathf.Lerp(start + half, end - half, i / (float)(posts - 1));
            if (alongX) Face(geometry, id + "-post-" + i, Metal, at - half, LandingTop - .02f, cross - half, at + half, RailTop - .005f, cross + half);
            else Face(geometry, id + "-post-" + i, Metal, cross - half, LandingTop - .02f, at - half, cross + half, RailTop - .005f, at + half);
        }
        // The rails end inside the end posts and stand over the posts' tops; where two railings meet at a corner, the
        // one along x lies 6 mm higher, so their rails cross without sharing a face.
        float lift = alongX ? .006f : 0f;
        foreach (var (name, y) in new[] { ("top", RailTop - .04f + lift), ("middle", LandingTop + .5f + lift) })
            if (alongX) Face(geometry, id + "-rail-" + name, Metal, start + railEnd, y, cross - .02f, end - railEnd, y + .04f, cross + .02f);
            else Face(geometry, id + "-rail-" + name, Metal, cross - .02f, y, start + railEnd, cross + .02f, y + .04f, end - railEnd);
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

    // The vanilla switch's pivot is its plate's front face; Unit 1's plate stands 4 cm proud of the wall and reaches 1 cm
    // into it. A copy stands a little closer to its wall, so the plate's back stays clear of the wall's back face.
    private const float SwitchProud = .034f;

    private static CopyDefinition WallSwitch(string id, float x, float z, float yaw)
    {
        var copy = Copy(id, RoomSwitch, SwitchPivot, new Vector3(x, UpperY + SwitchHeight, z), yaw);
        copy.VisualOnly = false;
        copy.IndependentMesh = false;
        return copy;
    }

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
