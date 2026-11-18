using MapExpansions;
using UnityEngine;

namespace Based.Content.Bungalow;

// A second storey on the Bungalow: the house's own shell copied 3 m up, a balcony where the porch roof was, and a
// steel stair from the gravel south of the porch up to a landing beside it. The first night takes the roof off, lays
// the upper floor and sets the stair; the second stands the timber frame; the third closes the walls; the fourth sets
// the rafters; the fifth puts the roof back on and hangs the lamps; the last fits the sliding door and hands the rooms over. Every state holds the
// work of the states before it.
public static class BungalowStorey
{
    public const string FloorState = "floor", FrameState = "frame", WallsState = "walls", RaftersState = "rafters", RoofState = "roof";
    // The states in which the upper walls and their doors stand.
    public static readonly string[] WallStates = { WallsState, RaftersState, RoofState, "complete" };
    // The upper storey stands this far above the house; the door of the copy is found by it.
    public const float Lift = 3f;

    private const string Property = "@Properties/Bungalow";
    private const string House = Property + "/bungalow";
    private const string Anchor = House + "/PorchBeam";
    private static readonly Vector3 PropertyPivot = new(-170f, -4f, 115.5f);
    private const float PropertyYaw = 90f;

    private const string Boards = "wood planks medium brown mat";
    private const string Metal = "metal_darkgrey_mat";
    // Rough timber for the frame and the band that marks the floor line on the facade.
    private const string Timber = "wood brown";
    private const string Concrete = "concrete_grey2";

    // The ceiling's colliders end at -0.65; they are the upper floor. The boards are drawn just above them.
    internal const float UpperFloor = -.65f;
    private const float BoardsBottom = -.69f, BoardsTop = -.645f;
    // The outer walls are cut where their copy begins, so no band of wall is drawn twice; the white cladding's copy
    // reaches lower.
    private const float WallCut = -.7f, CladdingCut = -1f;
    // The house's outline and the rooms inside it: the west rooms run the full depth, the north-east room fills the rest.
    private const float HouseWest = -176.17f, HouseEast = -163.75f, HouseSouth = 109.23f, HouseNorth = 121.63f;
    // The outer face of the house's east wall (OuterWalls bounds), east of the cladding's edge at HouseEast.
    private const float OuterEast = -163.70f;
    private const float InWest = -176.05f, InEast = -163.85f, InSouth = 109.35f, InNorth = 121.55f;
    private const float EastWall = -169.85f, NorthRoomSouth = 115.35f;

    // The balcony on the porch's posts, where the porch roof was. Its open edges and the landing's lie on the lines of
    // the ground floor's grids. The porch roof is taken off from its own south edge on.
    private const float BalconyEast = -164.35f, BalconySouth = 112.85f, BalconyBottom = -.85f, PorchRoofSouth = 112.4f;

    // The docks shed's steel stair rises 3.05 m over a 5.43 m run, its top edge centred on this point. Turned to climb
    // west along the house's south front, it ends on a landing against the west room's east wall. The carport's posts
    // stand just south of the landing.
    private const string Stair = "@Properties/DocksWarehouse/IndustrialShed/dockshed/Stairs (1)";
    private static readonly Vector3 StairTop = new(-90.87f, .83f, -54.3f);
    // Scaled so its foot meets the gravel at y -3.96 east of the porch.
    private const float StairYaw = -60f, StairScale = 1.085f;
    private const float StairRise = 3.05f * StairScale, StairRun = 5.43f * StairScale;
    private const float LandingWest = EastWall, LandingEast = -168.35f, LandingSouth = 111.35f, LandingNorth = BalconySouth;
    private const float StairFoot = LandingEast + StairRun;

    private const float RailHeight = 1.05f, GuardHeight = 2.1f;

    // The upper sliding door opens on a terrace hung from the west wall. It lies on the south room's grid lines, two tiles
    // deep and six wide, and its steel frame ends above -0.85, where the sunroom's glass roof meets the house; the roof
    // falls away westward, so the gap below the terrace only widens toward its edge.
    private const float DoorPlane = -176.08f, DoorSouth = 110.25f, DoorNorth = 112.55f;
    // The terrace's grid ends on the south room's grid lines; its boards reach 8 cm past them on the three open sides, so
    // the railing's unseen wall, 5 cm either side of the posts, has its inner face on those lines.
    private const float TerraceGridWest = -177.5f, TerraceGridSouth = 109.85f, TerraceGridNorth = 112.85f;
    private const float TerraceWest = TerraceGridWest - .08f, TerraceSouth = TerraceGridSouth - .08f,
        TerraceNorth = TerraceGridNorth + .08f, TerraceEast = -176.2f;
    private const float TerraceFrameBottom = -.8f;
    private const string TerraceGridId = "bungalow-storey-terrace";

    // The house's three ceiling lights, copied 3 m up: each hangs under the upper ceiling where its original hangs under
    // the lower one, turned the same way, with the same range and brightness.
    private static readonly string[] Lamps = { "FluroBar", "FluroBar (1)", "FluroBar (2)" };
    // The house's two wall switches, copied 3 m up onto the same walls: the one beside the door from the landing switches
    // the west room's two lamps, the one on the partition the east room's lamp. Each is a circuit of its own, apart from
    // the ground floor's.
    private static readonly (string Id, string Template, int[] Lamps)[] Switches =
    {
        ("storey-switch-west", "ModularSwitch", new[] { 0, 1 }),
        ("storey-switch-east", "ModularSwitch (1)", new[] { 2 })
    };

    // Each room upstairs repeats the grid of the room below it, 3 m up: 12 by 12 tiles, tile x running south and tile y
    // east from the origin tile's centre, so every edge lies on the walls as it does downstairs. The ids are saved;
    // renaming one loses what stands in that room.
    private static readonly (string Id, int GridIndex, Vector3 Origin, float West, float South, float East, float North)[] Floors =
    {
        ("bungalow-storey-east", 0, new Vector3(-169.6f, UpperFloor, 121.25f), -169.85f, 115.5f, -163.85f, 121.5f),
        ("bungalow-storey-south", 1, new Vector3(-175.75f, UpperFloor, 115.1f), -176f, 109.35f, -170f, 115.35f),
        ("bungalow-storey-north", 2, new Vector3(-175.75f, UpperFloor, 121.25f), -176f, 115.5f, -170f, 121.5f)
    };
    private const int FloorTiles = 12;

    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        if (!MapExpansionsApi.Register(Create()))
            throw new InvalidOperationException("Based could not register the Bungalow storey; inspect the MapExpansions validation errors.");
        _registered = true;
    }

    public static ExpansionDefinition Create()
    {
        var definition = new ExpansionDefinition
        {
            Id = BungalowDefinition.StoreyExpansionId, DisplayName = "Bungalow second storey", Version = "1.0.0", Source = null
        };
        definition.WorldStates.Add(BuildState(FloorState, 1));
        definition.WorldStates.Add(BuildState(FrameState, 2));
        definition.WorldStates.Add(BuildState(WallsState, 3));
        definition.WorldStates.Add(BuildState(RaftersState, 4));
        definition.WorldStates.Add(BuildState(RoofState, 5));
        definition.WorldStates.Add(BuildState("complete", 6));
        return definition;
    }

    // Level 1 is the open floor with the stair, 2 the timber frame, 3 the walls, 4 the rafters, 5 the roof and lamps, 6
    // the finished storey.
    private static WorldStateDefinition BuildState(string id, int level)
    {
        var state = new WorldStateDefinition { Id = id };
        var house = BoundsOf(HouseWest - .3f, WallCut, HouseSouth - .3f, HouseEast + .3f, 6f, HouseNorth + .3f);
        // "Roof" also names the beams under the ceiling; the area starts above them.
        Rule(state, "roof", "Roof", ObjectEffect.Remove, BoundsOf(HouseWest - .5f, .3f, HouseSouth - .5f, HouseEast + .5f, 3f, HouseNorth + .5f));
        Rule(state, "porch-roof", "SmallRoof", ObjectEffect.Remove, BoundsOf(EastWall, -1.2f, PorchRoofSouth, HouseEast, -.4f, NorthRoomSouth + .2f));
        Rule(state, "walls", "OuterWalls", ObjectEffect.Cut, house);
        // The corner posts reach up to -0.5, past the floor line; their tops would stand inside the copies' feet.
        Rule(state, "corner-posts", "CornerPost", ObjectEffect.Cut, house);
        // The gable's vent sits above the old eave; with the roof gone it would hang in the air, and once the upper
        // storey stands it would sit in its wall. The copy of it rides up with the copied gable.
        Rule(state, "fake-window", "FakeWindow", ObjectEffect.Remove, BoundsOf(HouseEast - .15f, -.6f, 117.9f, HouseEast + .25f, 1f, 119.1f));
        Rule(state, "cladding", "Block", ObjectEffect.Cut, BoundsOf(HouseWest - .3f, CladdingCut, HouseSouth - .3f, HouseEast + .3f, 6f, HouseNorth + .3f));
        // The roof's shadow hull would darken the open floor; once the roof is back, the original and the copy cast both
        // storeys' shadows.
        if (level < 5) Rule(state, "shadow", "ShadowCaster", ObjectEffect.Remove, house);

        var geometry = new CopyDefinition
        {
            Id = "storey-build", Template = Anchor, Frame = Property, OwnMesh = false, Obstacle = false,
            // The post only anchors the boxes; its collider child would stand as an unseen beam where the anchor lands.
            Exclude = { "Colliders" },
            At = new LocalPoint(PropertyPivot, PropertyYaw)
        };
        // The boards end 2 cm inside the walls they run under, so their ends never lie in a wall's face. They carry their own
        // collision: the house below switches its colliders off while the player is far away, and a bake then finds no
        // floor upstairs, least of all under the hall threshold between the two boards.
        Solid(geometry, "boards-west", Boards, InWest, BoardsBottom, InSouth, EastWall - .02f, BoardsTop, InNorth);
        // The north-east boards start where the west boards end, so no gap opens under the wall between them.
        Solid(geometry, "boards-north-east", Boards, EastWall - .02f, BoardsBottom, NorthRoomSouth + .02f, InEast, BoardsTop, InNorth);
        // The balcony's top lies level with the boards and its edges meet theirs, so no slit opens between the two.
        Solid(geometry, "balcony", Boards, EastWall - .02f, BalconyBottom, BalconySouth, BalconyEast, BoardsTop, NorthRoomSouth + .02f);
        StairAndLanding(state, geometry);
        // The stair's foot stands on a cast pad, so its bottom tread ends on concrete, not in the gravel. The gravel under
        // it rises to -3.89; the pad's top stands clear of that everywhere and its bottom reaches well into the ground.
        Solid(geometry, "stair-footing", Concrete, StairFoot - .35f, -4.3f, LandingSouth - .15f, StairFoot + .75f, -3.86f, LandingNorth + .15f);
        // The two balcony railings share the corner post, which the south run sets.
        const float corner = .03f;
        Railing(geometry, "balcony-south", true, BalconySouth + corner, LandingEast, BalconyEast - corner + .025f);
        Railing(geometry, "balcony-east", false, BalconyEast - corner, BalconySouth + corner - .025f, NorthRoomSouth, firstPost: false);

        // Until the walls stand, the open edges of the floor are guarded: by a site railing on the first night, by the
        // frame and an unseen wall behind it on the second.
        if (level <= 2)
        {
            const float i = .1f;
            bool rails = level == 1;
            Edge(geometry, "site-west", rails, false, HouseWest + i, HouseSouth + i, HouseNorth - i);
            // The north run leaves the corner post to the west run.
            Edge(geometry, "site-north", rails, true, HouseNorth - i, HouseWest + i, HouseEast - i, firstPost: false);
            // The east run leaves the north-east corner post to the north run, the south run the south-west one to the west run.
            Edge(geometry, "site-east", rails, false, HouseEast - i, NorthRoomSouth, HouseNorth - i, lastPost: false);
            Edge(geometry, "site-south-east", rails, true, NorthRoomSouth + i, BalconyEast, HouseEast - i);
            Edge(geometry, "site-south", rails, true, HouseSouth + i, HouseWest + i, EastWall, firstPost: false);
            Edge(geometry, "site-landing", rails, false, EastWall - i, HouseSouth + i, LandingSouth);
        }
        if (level == 2) Frame(geometry);
        if (level >= 3)
        {
            state.Copies.Add(Shell(level));
            Terrace(geometry);
            Band(geometry);
        }
        if (level == 4) Rafters(state, geometry);

        if (level >= 5)
            foreach (var (switchId, template, lamps) in Switches)
            {
                // A networked copy, so the switch works and every peer sees the same state.
                var wallSwitch = Copy(switchId, House + "/" + template, PropertyPivot, PropertyPivot + new Vector3(0f, Lift, 0f), 0);
                wallSwitch.VisualOnly = false;
                wallSwitch.IndependentMesh = false;
                state.Copies.Add(wallSwitch);
                foreach (int i in lamps)
                {
                    var lamp = Copy("lamp-" + i, House + "/" + Lamps[i], PropertyPivot, PropertyPivot + new Vector3(0f, Lift, 0f), 0);
                    lamp.LightsOn = true;
                    lamp.LightSwitch = "copy:" + switchId;
                    state.Copies.Add(lamp);
                }
            }

        if (level == 6) Finish(state);
        BungalowSiteProps.Add(state, level);
        state.Copies.Add(geometry);
        return state;
    }

    // The house's own shell, 3 m up, without the ground floor's foundation, kitchen and porch. The roof and the ceiling
    // come with level 5, the ceiling joists under them a night earlier with the rafters, the sliding door with the
    // finished storey.
    private static CopyDefinition Shell(int level)
    {
        var shell = Copy("shell", House, PropertyPivot, PropertyPivot + new Vector3(0f, Lift, 0f), 0);
        // The house's switches and ceiling lights come as copies of their own: a visual copy would neither switch nor
        // light, and would hang in the same place as the switched lamp. "Block" is the house's stand-in for the far
        // view, a closed box with a pitched top that the game hides up close; its copy would stand inside the rooms.
        foreach (var part in new[] { "Foundation", "Bench", "Cabinets", "SmallRoof", "PorchBeam", "PorchBeam.001", "ModularSwitch", "ModularSwitch (1)", "FluroBar", "FluroBar (1)", "FluroBar (2)", "Block" })
            shell.Exclude.Add(part);
        // The gable's vent comes with the roof it sits under.
        if (level < 5)
            foreach (var part in new[] { "Roof", "ShadowCaster", "InnerRoof", "FakeWindow" })
                shell.Exclude.Add(part);
        if (level < 4) shell.Exclude.Add("RoofBeams");
        if (level < 6) shell.Exclude.Add("Sliding Glass Door");
        return shell;
    }

    // The upper roof is an L of two gables at the house's pitch: a west wing with its ridge along z over the west rooms
    // and a north wing with its ridge along x over the north room, meeting in a hip at the north-west corner and a valley
    // over the porch corner. The figures are the copied roof's top measured in the game, 3 m above the house's own.
    private const float RoofRidgeX = -173.04f, RoofRidgeZ = 118.53f, RoofPitch = .78f;
    // The rafters' centre line at the ridge; it falls at the roof's pitch and meets the wall tops at 2.3 at the eaves.
    private const float RafterLine = 4.8f, RafterWidth = .06f, RafterDepth = .16f;
    // Run from the ridge to the rafter feet, past the walls as far as the roof's eaves reach.
    private const float RafterRun = 3.25f;

    // The fourth night's roof timber, on the walls the night before closed: rafter pairs on every slope, a hip and a
    // valley rafter where the wings meet, and the two ridge boards. The roof of the next night covers it and it goes.
    private static void Rafters(WorldStateDefinition state, CopyDefinition geometry)
    {
        static float Line(float run) => RafterLine - RoofPitch * run;
        const float atRidge = .02f;
        var westSlope = new List<(Vector3, Vector3)>();
        var eastSlope = new List<(Vector3, Vector3)>();
        for (float z = 109.05f; z < 117.6f; z += 1.2f)
        {
            westSlope.Add((new Vector3(RoofRidgeX - atRidge, Line(atRidge), z), new Vector3(RoofRidgeX - RafterRun, Line(RafterRun), z)));
            // The east slope of the west wing ends at the valley, which starts over the porch corner at 115.43.
            // Its rafters sit beside the west slope's, 7 cm north, as on the north wing.
            if (z < 115.2f)
                eastSlope.Add((new Vector3(RoofRidgeX + atRidge, Line(atRidge), z + .07f), new Vector3(RoofRidgeX + RafterRun, Line(RafterRun), z + .07f)));
        }
        var northSlope = new List<(Vector3, Vector3)>();
        var southSlope = new List<(Vector3, Vector3)>();
        for (float x = -163.6f; x > -172.5f; x -= 1.2f)
        {
            northSlope.Add((new Vector3(x, Line(atRidge), RoofRidgeZ + atRidge), new Vector3(x, Line(RafterRun), RoofRidgeZ + RafterRun)));
            // West of the valley the north wing's south slope is the west wing's roof. Its rafters sit beside the north
            // slope's, 7 cm east, since at the ridge their top ends reach past each other and would share their sides.
            if (x > -169.8f)
                southSlope.Add((new Vector3(x + .07f, Line(atRidge), RoofRidgeZ - atRidge), new Vector3(x + .07f, Line(RafterRun), RoofRidgeZ - RafterRun)));
        }
        Timbers(state, "rafters-west", westSlope);
        Timbers(state, "rafters-east", eastSlope);
        Timbers(state, "rafters-north", northSlope);
        Timbers(state, "rafters-south", southSlope);
        // The hip and the valley rafter lie on one line through the ridges' meeting point; each starts 6 cm from it along
        // its own run, so their top ends stay apart and their sides, which lie in one plane, share no area.
        const float fromMeet = .06f;
        Timbers(state, "rafters-hip", new List<(Vector3, Vector3)> {
            (new Vector3(RoofRidgeX - fromMeet, Line(fromMeet), RoofRidgeZ + fromMeet), new Vector3(RoofRidgeX - RafterRun, Line(RafterRun), RoofRidgeZ + RafterRun)) });
        // The valley rafter runs down to the corner where the west wing's east wall meets the north room's south wall.
        float valleyRun = RoofRidgeZ - NorthRoomSouth;
        Timbers(state, "rafters-valley", new List<(Vector3, Vector3)> {
            (new Vector3(RoofRidgeX + fromMeet, Line(fromMeet), RoofRidgeZ - fromMeet), new Vector3(RoofRidgeX + valleyRun, Line(valleyRun), NorthRoomSouth)) });
        // The ridge boards stand above the rafter ends and below the roof's top at 5.05. The north board starts 1 cm
        // inside the west one, so its end face lies in none of the west board's faces.
        const float half = .02f, ridgeBottom = 4.62f, ridgeTop = 4.92f;
        Face(geometry, "ridge-west", Timber, RoofRidgeX - half, ridgeBottom, 108.95f, RoofRidgeX + half, ridgeTop, RoofRidgeZ + half - .005f);
        Face(geometry, "ridge-north", Timber, RoofRidgeX + half - .01f, ridgeBottom - .004f, RoofRidgeZ - half + .003f, -163.45f, ridgeTop - .004f, RoofRidgeZ + half - .003f);
    }

    // Parallel timbers from their high to their low end, as one copy turned so its bars run down the slope. A box cannot
    // turn on its own: each bar is laid west of the pivot at its place in the turned frame.
    private static void Timbers(WorldStateDefinition state, string id, List<(Vector3 High, Vector3 Low)> beams)
    {
        if (beams.Count == 0) return;
        var fall = (beams[0].Low - beams[0].High).normalized;
        // The look turn maps the frame's +z down the slope; the quarter turn before it maps its -x there, along which the
        // bars are laid. The bars' depth stays in the vertical plane through the slope.
        var turn = Quaternion.LookRotation(fall, Vector3.up) * Quaternion.Euler(0f, 90f, 0f);
        var copy = Tilted(id, Anchor, PropertyPivot, beams[0].High, turn);
        copy.OwnMesh = false;
        copy.Exclude.Add("Colliders");
        var back = Quaternion.Inverse(turn);
        for (int i = 0; i < beams.Count; i++)
        {
            var start = PropertyPivot + back * (beams[i].High - beams[0].High);
            float length = (beams[i].Low - beams[i].High).magnitude;
            Face(copy, "bar-" + i, Timber, start.x - length, start.y - RafterDepth * .5f, start.z - RafterWidth * .5f,
                start.x, start.y + RafterDepth * .5f, start.z + RafterWidth * .5f);
        }
        state.Copies.Add(copy);
    }

    // The upper walls' centre lines and their openings, from the walls of the house below. A door's opening runs to its
    // head; a window's has a sill.
    private const float DoorHead = UpperFloor + 2.2f, WindowSill = UpperFloor + 1.2f, WindowHead = UpperFloor + 2.15f;
    // The frame's top stands at the height of the outer walls' eaves, which the walls of the next night reach.
    private const float FrameTop = 1.8f, StudSpacing = .6f;

    // The second night's timber frame: studs on every wall line, plates top and bottom, headers over the doors and
    // windows. The unseen walls of Edge keep anyone from stepping off the floor; across the outer openings they stand
    // behind site boards, so the stop has something to see. The inner walls block between their doors.
    private static void Frame(CopyDefinition geometry)
    {
        var door = float.NaN;
        StudWall(geometry, "frame-west", false, -176.11f, HouseSouth, HouseNorth, (DoorSouth, DoorNorth, door), (117.65f, 119.13f, WindowSill));
        StudWall(geometry, "frame-north", true, 121.59f, HouseWest + .1f, HouseEast - .1f, (-172.38f, -170.9f, WindowSill));
        StudWall(geometry, "frame-east", false, -163.8f, NorthRoomSouth, HouseNorth - .1f);
        StudWall(geometry, "frame-south", true, 109.29f, HouseWest + .1f, EastWall);
        StudWall(geometry, "frame-west-room", false, -169.93f, HouseSouth + .1f, NorthRoomSouth, (113.2f, 114.45f, door));
        // The frame ends 5 mm short of the inner east face, where the north-east boards end, so the two end faces differ.
        StudWall(geometry, "frame-south-east", true, 115.42f, EastWall + .1f, HouseEast - .105f);
        // The partition between the two west rooms has two open doorways, as the walls copied onto it on the next night.
        StudWall(geometry, "frame-partition", true, 115.43f, HouseWest + .1f, -170.05f, (-175.55f, -174.25f, door), (-171.75f, -170.55f, door));
        StudWall(geometry, "frame-hall", false, -169.93f, 115.5f, HouseNorth - .1f, (117.97f, 119.03f, door));

        foreach (var (id, alongX, cross, start, end, doors) in new (string, bool, float, float, float, (float From, float To)[])[]
        {
            ("frame-west-room", false, -169.93f, HouseSouth + .1f, NorthRoomSouth, new[] { (113.2f, 114.45f) }),
            ("frame-south-east", true, 115.42f, EastWall + .1f, HouseEast - .105f, new (float, float)[0]),
            ("frame-partition", true, 115.43f, HouseWest + .1f, -170.05f, new[] { (-175.55f, -174.25f), (-171.75f, -170.55f) }),
            ("frame-hall", false, -169.93f, 115.5f, HouseNorth - .1f, new[] { (117.97f, 119.03f) })
        })
        {
            // The stretches between the doors block; the last one runs from the last door to the wall's end.
            float from = start;
            for (int d = 0; d <= doors.Length; d++)
            {
                var (doorFrom, doorTo) = d < doors.Length ? doors[d] : (end, end);
                if (doorFrom - from > .01f)
                    Block(geometry, id + "-block-" + Mathf.RoundToInt(from * 10), alongX ? from : cross - .05f, UpperFloor,
                        alongX ? cross - .05f : from, alongX ? doorFrom : cross + .05f, FrameTop, alongX ? cross + .05f : doorFrom);
                from = doorTo;
            }
        }

        // Two site boards across the sliding door's opening and one across each window, on the outside of the frame.
        SiteBoard(geometry, "frame-west-door-low", false, -176.2f, DoorSouth, DoorNorth, UpperFloor + .45f);
        SiteBoard(geometry, "frame-west-door-high", false, -176.2f, DoorSouth, DoorNorth, UpperFloor + .95f);
        SiteBoard(geometry, "frame-west-window", false, -176.2f, 117.65f, 119.13f, WindowSill + .4f);
        SiteBoard(geometry, "frame-north-window", true, 121.68f, -172.38f, -170.9f, WindowSill + .4f);
    }

    // A rough board nailed across an opening: 10 cm high, 2.5 cm thick, 10 cm past each side of the opening.
    private static void SiteBoard(CopyDefinition geometry, string id, bool alongX, float cross, float start, float end, float y)
    {
        if (alongX) Face(geometry, id, Timber, start - .1f, y, cross - .0125f, end + .1f, y + .1f, cross + .0125f);
        else Face(geometry, id, Timber, cross - .0125f, y, start - .1f, cross + .0125f, y + .1f, end + .1f);
    }

    // The plate level of each wall; two walls that meet never share one. The soles of levels 1 and 2 lie within 2 mm of
    // the top of the house's partition wall below, so the two walls standing on it take levels 3 and 4.
    private static readonly Dictionary<string, int> PlateLevels = new()
    {
        ["frame-west"] = 0, ["frame-north"] = 1, ["frame-east"] = 2, ["frame-south"] = 3,
        ["frame-west-room"] = 0, ["frame-south-east"] = 4, ["frame-partition"] = 3, ["frame-hall"] = 2
    };

    private static void StudWall(CopyDefinition geometry, string id, bool alongX, float cross, float start, float end,
        params (float From, float To, float Sill)[] openings)
    {
        // Plates are 11 cm deep. The two walls standing on the house's partition wall get 12 cm plates, so their sides
        // stand 5 mm clear of that 10 cm wall's faces; the others keep 11 cm, so plate ends at corners stay 5 mm apart.
        const float half = .025f, depth = .05f, plate = .05f;
        float plateDepth = id == "frame-partition" || id == "frame-south-east" ? .06f : .055f;
        // Plates of walls that meet overlap at the joint; each wall's plates sit on their own level, 3 mm apart, so no two
        // of them share a top or bottom face. The soles sink into the boards, the top plates rise.
        float step = .003f * PlateLevels[id];
        float bottom = BoardsTop - step, sole = bottom + plate, top = FrameTop + step, head = top - .08f;
        void Piece(string name, float a1, float y1, float a2, float y2, float d)
        {
            if (alongX) Face(geometry, id + "-" + name, Timber, a1, y1, cross - d, a2, y2, cross + d);
            else Face(geometry, id + "-" + name, Timber, cross - d, y1, a1, cross + d, y2, a2);
        }
        // Plates are a little deeper than the studs, so a stud's sides never lie in a plate's face.
        Piece("top-plate", start, head, end, top, plateDepth);
        float from = start;
        foreach (var opening in openings.OrderBy(o => o.From))
        {
            if (float.IsNaN(opening.Sill))
            {
                Piece("sole-" + Mathf.RoundToInt(from * 10), from, bottom, opening.From, sole, plateDepth);
                from = opening.To;
            }
            float headAt = float.IsNaN(opening.Sill) ? DoorHead : WindowHead;
            Piece("header-" + Mathf.RoundToInt(opening.From * 10), opening.From, headAt, opening.To, headAt + .12f, plateDepth);
            if (!float.IsNaN(opening.Sill))
                Piece("sill-" + Mathf.RoundToInt(opening.From * 10), opening.From, opening.Sill - .05f, opening.To, opening.Sill, plateDepth);
        }
        Piece("sole-" + Mathf.RoundToInt(from * 10), from, bottom, end, sole, plateDepth);

        // Studs stand evenly from end to end, with a jamb stud on each side of every opening. The end studs stand 5 mm in
        // from the plate ends, so their outer faces do not lie in the plates' end faces; the jamb studs stand 5 mm back
        // from a door's opening for the same reason, against the ends of the soles beside it.
        var at = new List<float>();
        int count = Math.Max(1, (int)Math.Ceiling((end - start) / StudSpacing));
        for (int i = 0; i <= count; i++) at.Add(Mathf.Lerp(start + half + .005f, end - half - .005f, i / (float)count));
        foreach (var opening in openings)
        {
            at.Add(opening.From - half - .005f);
            at.Add(opening.To + half + .005f);
        }
        at.Sort();
        float last = float.NegativeInfinity;
        foreach (float p in at)
        {
            // A jamb stud that lands on a stud of the even row takes its place.
            if (p - last < .06f) continue;
            last = p;
            // Each stud reaches a centimetre into the plates, so no end face lies in a plate's face.
            float low = sole - .01f, high = head + .01f;
            var inside = openings.Where(o => p > o.From && p < o.To).ToArray();
            if (inside.Length == 0)
            {
                Piece("stud-" + Mathf.RoundToInt(p * 100), p - half, low, p + half, high, depth);
                continue;
            }
            var o = inside[0];
            float headAt = float.IsNaN(o.Sill) ? DoorHead : WindowHead;
            Piece("cripple-" + Mathf.RoundToInt(p * 100), p - half, headAt + .11f, p + half, high, depth);
            if (!float.IsNaN(o.Sill)) Piece("under-" + Mathf.RoundToInt(p * 100), p - half, low, p + half, o.Sill - .04f, depth);
        }
    }

    // A railing on the first night; on the second the frame stands there and only the railing's unseen wall stays.
    private static void Edge(CopyDefinition geometry, string id, bool rails, bool alongX, float cross, float start, float end,
        bool firstPost = true, bool lastPost = true)
    {
        if (rails)
        {
            Railing(geometry, id, alongX, cross, start, end, firstPost, lastPost);
            return;
        }
        Block(geometry, id + "-guard", alongX ? start : cross - .05f, UpperFloor, alongX ? cross - .05f : start,
            alongX ? end : cross + .05f, UpperFloor + GuardHeight, alongX ? cross + .05f : end);
    }

    // A timber band where the upper floor meets the walls, so the two storeys read apart from outside. It stands 4 cm
    // proud of the cladding and reaches 1 cm into it.
    private static void Band(CopyDefinition geometry)
    {
        const float low = -.95f, high = -.62f, proud = .04f, sunk = .01f;
        float w = HouseWest, s = HouseSouth, n = HouseNorth;
        // The east wall's outer face stands 5 cm east of the cladding's edge, so the east band is set out from that face;
        // the north band reaches 5 mm into it, so no end face lies in the east band's inner face.
        float e = OuterEast;
        // Each band runs between the corner posts, its ends 5 mm inside them, so no band crosses a post and no end face
        // lies in a post's face. The posts' outer faces: south-west and north-west x -176.18 .. -175.97, south-east
        // x -170.03 .. -169.83 at z 109.17 .. 109.38, east and north-east x -163.88 .. -163.67, at z 115.32 .. 115.53 and
        // 121.47 .. 121.68.
        const float post = .005f;
        Face(geometry, "band-west", Timber, w - proud, low, 109.38f - post, w + sunk, high, 121.47f + post);
        Face(geometry, "band-north", Timber, -175.97f - post, low, n - sunk, -163.88f + post, high, n + proud);
        Face(geometry, "band-east", Timber, e - sunk, low, 115.53f - post, e + proud, high, 121.47f + post);
        Face(geometry, "band-south", Timber, -175.97f - post, low, s - proud, -170.03f + post, high, s + sunk);
        Face(geometry, "band-landing", Timber, EastWall - sunk, low, 109.38f - post, EastWall + proud, high, LandingSouth);
    }

    // Boards level with the floor inside, on a steel frame 1 cm into the timber band, railed on its three open sides.
    private static void Terrace(CopyDefinition geometry)
    {
        Solid(geometry, "terrace", Boards, TerraceWest, BoardsBottom, TerraceSouth, TerraceEast, BoardsTop, TerraceNorth);
        Solid(geometry, "terrace-frame", Metal, TerraceWest - .02f, TerraceFrameBottom, TerraceSouth - .02f, TerraceEast, BoardsBottom, TerraceNorth + .02f);
        // Between the terrace's boards and the boards inside runs the wall's thickness; a steel sill under the door closes
        // it. It reaches 1.5 cm into the terrace and 1 cm into the boards and stands 6 mm proud of both floors, so no face of
        // it lies in theirs and the door's track still clears it. Its ends stop 1 cm inside the door frame's faces.
        Solid(geometry, "terrace-sill", Metal, TerraceEast - .015f, BoardsBottom - .01f, DoorSouth + .01f, InWest + .01f, BoardsTop + .006f, DoorNorth - .01f);
        // The south and north runs set the outer corner posts; the west run stands between them.
        const float corner = .03f;
        Railing(geometry, "terrace-south", true, TerraceSouth + corner, TerraceWest + corner - .025f, TerraceEast);
        Railing(geometry, "terrace-north", true, TerraceNorth - corner, TerraceWest + corner - .025f, TerraceEast);
        Railing(geometry, "terrace-west", false, TerraceWest + corner, TerraceSouth + corner - .025f, TerraceNorth - corner + .025f,
            firstPost: false, lastPost: false);
    }

    private static void StairAndLanding(WorldStateDefinition state, CopyDefinition geometry)
    {
        float centre = (LandingSouth + LandingNorth) * .5f;
        state.Copies.Add(Copy("stair", Stair, StairTop, new Vector3(LandingEast, UpperFloor, centre), StairYaw, StairScale));
        Solid(geometry, "landing", Metal, LandingWest, BalconyBottom, LandingSouth, LandingEast, BoardsTop, LandingNorth);
        // A steel header under the landing's east edge takes the stair's stringers, whose top ends would otherwise hang
        // in the air below the landing. Its ends stop 5 mm inside the landing's, so their end faces lie in no shared plane.
        Face(geometry, "landing-header", Metal, LandingEast - .12f, BalconyBottom - .4f, LandingSouth + .005f, LandingEast + .12f, BalconyBottom + .01f, LandingNorth - .005f);
        // The stair's top tread ends a few centimetres short of the landing; an unseen strip closes the slit, which would
        // split the walking map there.
        Block(geometry, "landing-lip", LandingEast, BalconyBottom, LandingSouth, LandingEast + .3f, UpperFloor - .02f, LandingNorth);
        Post(geometry, "landing-post-south", LandingEast - .07f, LandingSouth + .07f, -4.2f, BalconyBottom);
        Post(geometry, "landing-post-north", LandingEast - .07f, LandingNorth - .07f, -4.2f, BalconyBottom);
        Railing(geometry, "landing-south", true, LandingSouth + .03f, LandingWest, LandingEast);
        StairRail(state, geometry, "south", LandingSouth + .03f);
        StairRail(state, geometry, "north", LandingNorth - .03f);
        StairSoffit(state);
    }

    // The docks stair's treads are open between the stringers; seen from above or from the side through the open risers
    // one looked into the dark under them. A steel plate under the treads, between the stringers and parallel to the
    // nosings, closes the stair from below. Its foot lies in the ground, its top end behind the landing's header.
    private static void StairSoffit(WorldStateDefinition state)
    {
        const float drop = .32f;
        float slope = Mathf.Atan2(StairRise, StairRun) * Mathf.Rad2Deg;
        float length = Mathf.Sqrt(StairRise * StairRise + StairRun * StairRun);
        var source = PropertyPivot;
        var plate = Tilted("stair-soffit", Anchor, source, new Vector3(StairFoot, UpperFloor - StairRise - drop, (LandingSouth + LandingNorth) * .5f),
            Quaternion.Euler(0f, 0f, -slope));
        plate.OwnMesh = false;
        plate.Exclude.Add("Colliders");
        float half = (LandingNorth - LandingSouth) * .5f - .07f;
        Face(plate, "plate", Metal, source.x - length, source.y - .02f, source.z - half, source.x, source.y, source.z + half);
        state.Copies.Add(plate);
    }

    // The game has no sloped railing at this pitch: upright posts, and rails in copies tilted to the slope of the
    // nosings. They meet the landing's railing at its corner.
    private static void StairRail(WorldStateDefinition state, CopyDefinition geometry, string side, float z)
    {
        const float half = .025f;
        float Nosing(float x) => UpperFloor - (x - LandingEast) * StairRise / StairRun;
        foreach (float x in new[] { StairFoot - .05f, StairFoot - 1.85f, StairFoot - 3.65f })
            Face(geometry, "stair-" + side + "-post-" + Mathf.RoundToInt(-x * 10), Metal, x - half, Nosing(x) - .3f, z - half, x + half, Nosing(x) + RailHeight, z + half);
        float slope = Mathf.Atan2(StairRise, StairRun) * Mathf.Rad2Deg;
        float length = Mathf.Sqrt(StairRise * StairRise + StairRun * StairRun);
        var source = PropertyPivot;
        foreach (var (name, above) in new[] { ("top", RailHeight - .02f), ("middle", .52f) })
        {
            // A box cannot tilt on its own: the copy carries a bar laid west of the pivot to the rail's foot and tips it
            // up the slope.
            var rail = Tilted("stair-" + side + "-rail-" + name, Anchor, source, new Vector3(StairFoot, Nosing(StairFoot) + above, z),
                Quaternion.Euler(0f, 0f, -slope));
            rail.OwnMesh = false;
            rail.Exclude.Add("Colliders");
            Face(rail, "bar", Metal, source.x - length, source.y - .02f, source.z - .02f, source.x, source.y + .02f, source.z + .02f);
            // The rails are drawn without colliders; an unseen wall along the top rail keeps a jump from carrying over it.
            if (name == "top")
                Block(rail, "guard", source.x - length, source.y - .5f, source.z - .05f, source.x, source.y + 1.07f, source.z + .05f);
            state.Copies.Add(rail);
        }
    }

    // The finished storey: the rooms upstairs get grids of their own, and employees walk up the stair.
    private static void Finish(WorldStateDefinition state)
    {
        foreach (var floor in Floors)
        {
            var extension = new PropertyExtensionDefinition
            {
                Id = floor.Id, PropertyCode = "bungalow", GridIndex = floor.GridIndex, GridOrigin = floor.Origin
            };
            for (int x = 0; x < FloorTiles; x++)
                for (int y = 0; y < FloorTiles; y++)
                    extension.Tiles.Add(new GridCoordinate(x, y));
            extension.Bounds.Add(BoundsOf(floor.West, UpperFloor - .1f, floor.South, floor.East, UpperFloor + 2.95f, floor.North));
            state.PropertyExtensions.Add(extension);
        }
        // The terrace repeats the south room's grid: tile x runs south from the door's north end, tile y east from the
        // outer edge.
        var terrace = new PropertyExtensionDefinition
        {
            Id = TerraceGridId, PropertyCode = "bungalow", GridIndex = 1,
            GridOrigin = new Vector3(TerraceGridWest + .25f, UpperFloor, TerraceGridNorth - .25f)
        };
        for (int x = 0; x < 6; x++)
            for (int y = 0; y < 2; y++)
                terrace.Tiles.Add(new GridCoordinate(x, y));
        terrace.Bounds.Add(BoundsOf(TerraceGridWest, UpperFloor - .1f, TerraceGridSouth, -176.5f, UpperFloor + 2.95f, TerraceGridNorth));
        state.PropertyExtensions.Add(terrace);

        state.NavigationBounds = BoundsOf(HouseWest - .2f, -4.3f, HouseSouth - .2f, StairFoot + .9f, UpperFloor + 2.5f, HouseNorth + .2f);
        state.NavigationAgents.AddRange(new[] { "Humanoid", "Ignore Costs", "Employee" });
        // The ground floor, the porch and the gravel around the stair keep the game's walking map; the bake adds the
        // stair and the floor above and joins them to it. Each box ends below the upper floor.
        state.NavigationExistingFloor.Add(BoundsOf(HouseWest - .2f, -4.2f, NorthRoomSouth, HouseEast + .2f, -1.5f, HouseNorth + .2f));
        state.NavigationExistingFloor.Add(BoundsOf(HouseWest - .2f, -4.2f, HouseSouth - .2f, EastWall, -1.5f, NorthRoomSouth + .1f));
        state.NavigationExistingFloor.Add(BoundsOf(EastWall, -4.3f, LandingNorth + .15f, HouseEast + .2f, -1.5f, NorthRoomSouth + .05f));
        state.NavigationExistingFloor.Add(BoundsOf(EastWall, -4.4f, HouseSouth - .2f, StairFoot + 1f, -3f, LandingSouth - .05f));
        // The seam check samples 0.6 m to each side of the strip's edge, so the strip at the stair's foot reaches well past
        // the volume.
        state.NavigationExistingFloor.Add(BoundsOf(StairFoot + .1f, -4.4f, LandingSouth - .1f, StairFoot + 2f, -3f, LandingNorth + .1f));
        // The gravel east of the house, which the paths from the street to the stair cross, and the ground under the
        // landing, where the stair's underside would otherwise cut a hole into the game's map.
        state.NavigationExistingFloor.Add(BoundsOf(HouseEast + .2f, -4.4f, NorthRoomSouth, StairFoot + 1f, -3f, HouseNorth + .2f));
        state.NavigationExistingFloor.Add(BoundsOf(EastWall, -4.4f, LandingSouth - .05f, LandingEast, -2.5f, LandingNorth + .15f));
        // The first two straddle the join: the gravel at the stair's foot and its second tread.
        float tread = StairFoot - .6f, middle = (LandingSouth + LandingNorth) * .5f;
        state.NavigationProbes.Add(new Vector3(StairFoot + .8f, -4f, middle));
        state.NavigationProbes.Add(new Vector3(tread, UpperFloor - (tread - LandingEast) * StairRise / StairRun, middle));
        state.NavigationProbes.Add(new Vector3(-169.1f, UpperFloor, middle));
        state.NavigationProbes.Add(new Vector3(-167f, UpperFloor, 114f));
        state.NavigationProbes.Add(new Vector3(-171f, UpperFloor, 113.8f));
        state.NavigationProbes.Add(new Vector3(-173f, UpperFloor, 118.5f));
        state.NavigationProbes.Add(new Vector3(-167f, UpperFloor, 118.5f));
    }

    private static void Rule(WorldStateDefinition state, string id, string name, ObjectEffect effect, Bounds area) =>
        state.Rules.Add(new ObjectRule { Id = id, Name = name, Parent = "bungalow", Side = WorldSide.Main, Effect = effect, Area = area });

    // Posts and two rails of dark steel, with an unseen wall to head height that keeps a jump from carrying over them.
    // The rails end inside the end posts and stop 1 cm under the posts' tops, so no rail face lies in a post's face.
    // Without its first or last post the run starts or ends in a post another run sets; its rails then stop clear of that
    // run's rails.
    private static void Railing(CopyDefinition geometry, string id, bool alongX, float cross, float start, float end,
        bool firstPost = true, bool lastPost = true)
    {
        const float half = .025f;
        float top = UpperFloor + RailHeight;
        int posts = Math.Max(2, (int)Math.Ceiling((end - start) / 1.2f) + 1);
        for (int i = firstPost ? 0 : 1; i < (lastPost ? posts : posts - 1); i++)
        {
            float at = Mathf.Lerp(start + half, end - half, i / (float)(posts - 1));
            if (alongX) Face(geometry, id + "-post-" + i, Metal, at - half, UpperFloor, cross - half, at + half, top, cross + half);
            else Face(geometry, id + "-post-" + i, Metal, cross - half, UpperFloor, at - half, cross + half, top, at + half);
        }
        float from = start + half + (firstPost ? 0f : .02f), to = end - half - (lastPost ? 0f : .02f);
        // A low rail just above the floor closes the gap a foot would slip through.
        foreach (var (name, y) in new[] { ("top", top - .05f), ("middle", UpperFloor + .5f), ("low", UpperFloor + .1f) })
            if (alongX) Face(geometry, id + "-rail-" + name, Metal, from, y, cross - .02f, to, y + .04f, cross + .02f);
            else Face(geometry, id + "-rail-" + name, Metal, cross - .02f, y, from, cross + .02f, y + .04f, to);
        Block(geometry, id + "-guard", alongX ? start : cross - .05f, UpperFloor, alongX ? cross - .05f : start,
            alongX ? end : cross + .05f, UpperFloor + GuardHeight, alongX ? cross + .05f : end);
    }

    private static void Post(CopyDefinition copy, string id, float x, float z, float bottom, float top) =>
        Solid(copy, id, Metal, x - .06f, bottom, z - .06f, x + .06f, top, z + .06f);

    // Places a copy so that the source point lands on the target, turned by yaw and scaled around it. The frame is the
    // property root, which stands turned by PropertyYaw.
    private static CopyDefinition Copy(string id, string template, Vector3 source, Vector3 target, float yaw, float scale = 1f) => new()
    {
        Id = id, Template = template, Frame = Property, Scale = scale,
        At = new LocalPoint(target - Quaternion.Euler(0, yaw, 0) * (scale * (source - PropertyPivot)), yaw + PropertyYaw),
        IndependentMesh = true, VisualOnly = true, Obstacle = false
    };

    // Places a copy so that the source point lands on the target, turned by the world rotation.
    private static CopyDefinition Tilted(string id, string template, Vector3 source, Vector3 target, Quaternion turn)
    {
        var frameTurn = turn * Quaternion.Euler(0f, PropertyYaw, 0f);
        return new CopyDefinition
        {
            Id = id, Template = template, Frame = Property,
            At = new LocalPoint(target - turn * (source - PropertyPivot), 0f),
            Tilt = frameTurn.eulerAngles,
            IndependentMesh = true, VisualOnly = true, Obstacle = false
        };
    }

    private static void Solid(CopyDefinition copy, string id, string material, float x1, float y1, float z1, float x2, float y2, float z2)
    {
        Face(copy, id, material, x1, y1, z1, x2, y2, z2);
        Block(copy, id + "-collision", x1, y1, z1, x2, y2, z2);
    }

    private static void Block(CopyDefinition copy, string id, float x1, float y1, float z1, float x2, float y2, float z2) =>
        copy.Boxes.Add(new BoxDefinition { Id = id, Kind = BoxKind.Block, Box = BoundsOf(x1, y1, z1, x2, y2, z2) });

    private static void Face(CopyDefinition copy, string id, string material, float x1, float y1, float z1, float x2, float y2, float z2) =>
        copy.Boxes.Add(new BoxDefinition
        {
            Id = id, Kind = BoxKind.Panel, Material = material, TileMetres = material == Boards ? 2f : 1f,
            Box = BoundsOf(x1, y1, z1, x2, y2, z2)
        });

    private static Bounds BoundsOf(float x1, float y1, float z1, float x2, float y2, float z2)
    {
        var bounds = new Bounds();
        bounds.SetMinMax(new Vector3(x1, y1, z1), new Vector3(x2, y2, z2));
        return bounds;
    }
}
