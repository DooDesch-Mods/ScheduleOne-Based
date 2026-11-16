using MapExpansions;
using UnityEngine;

namespace Based.Content.Bungalow;

// A second storey on the Bungalow: the house's own shell copied 3 m up, a balcony where the porch roof was, and a
// steel stair from the gravel south of the porch up to a landing beside it. The first night takes the roof off, lays
// the upper floor and sets the stair; the second raises the walls; the fourth puts the roof back on and hangs the
// lamps; the last fits the sliding door and hands the rooms over. Every state holds the work of the states before it.
public static class BungalowStorey
{
    // Saved id of the upper floor's property extension; renaming it loses what stands upstairs.
    public const string ExtensionId = "bungalow-storey";
    public const string FloorState = "floor", WallsState = "walls", RoofState = "roof";
    // The upper storey stands this far above the house; the door of the copy is found by it.
    public const float Lift = 3f;

    private const string Property = "@Properties/Bungalow";
    private const string House = Property + "/bungalow";
    private const string Anchor = House + "/PorchBeam";
    private static readonly Vector3 PropertyPivot = new(-170f, -4f, 115.5f);
    private const float PropertyYaw = 90f;

    private const string Boards = "wood planks medium brown mat";
    private const string Metal = "metal_darkgrey_mat";

    // The ceiling's colliders end at -0.65; they are the upper floor. The boards are drawn just above them.
    private const float UpperFloor = -.65f, BoardsBottom = -.69f, BoardsTop = -.645f;
    // The outer walls are cut where their copy begins; the white cladding's copy reaches 0.35 m lower.
    private const float WallCut = -.65f, CladdingCut = -1f;
    // The house's outline and the rooms inside it: the west rooms run the full depth, the north-east room fills the rest.
    private const float HouseWest = -176.17f, HouseEast = -163.75f, HouseSouth = 109.23f, HouseNorth = 121.63f;
    private const float InWest = -176.05f, InEast = -163.85f, InSouth = 109.35f, InNorth = 121.55f;
    private const float EastWall = -169.85f, NorthRoomSouth = 115.35f;

    // The balcony on the porch's posts, where the porch roof was.
    private const float BalconyEast = -164.4f, BalconySouth = 112.6f, BalconyBottom = -.85f;

    // The docks shed's steel stair rises 3.05 m over a 5.43 m run, its top edge centred on this point. Turned to climb
    // west along the house's south front, it ends on a landing against the west room's east wall.
    private const string Stair = "@Properties/DocksWarehouse/IndustrialShed/dockshed/Stairs (1)";
    private static readonly Vector3 StairTop = new(-90.87f, .83f, -54.3f);
    private const float StairYaw = -60f, StairScale = 1.015f;
    private const float StairRise = 3.05f * StairScale, StairRun = 5.43f * StairScale;
    private const float LandingWest = EastWall, LandingEast = -168.35f, LandingSouth = 111.1f, LandingNorth = BalconySouth;
    private const float StairFoot = LandingEast + StairRun;

    private const float RailHeight = 1.05f, GuardHeight = 2.1f;

    // The upper sliding door opens on a French balcony: a railing just outside its frame.
    private const float DoorPlane = -176.08f, DoorSouth = 110.25f, DoorNorth = 112.55f;

    // The Storage Unit's ceiling light, hung as far under the upper ceiling as it hangs under the unit's.
    private const string LampModel = "Map/Hyland Point/Region_Northtown/Storage warehouse/Storage Unit/FluroBar (1)";
    private static readonly Vector3 LampPivot = new(-6.52f, 2.87f, 107.23f);
    private const float LampY = UpperFloor + 2.95f - .23f, LampRange = 3.6f;
    private static readonly Vector2[] Lamps = { new(-173f, 112.35f), new(-173f, 118.5f), new(-166.9f, 118.5f) };

    // The upper grid is turned like the house's own and starts at the north-west corner, so its tiles count from 0:
    // tile x runs south from z 121.5, tile y east from x -176.0. The walls between the rooms take row 12 and column 12.
    private const int GridIndex = 1;
    private static readonly Vector3 GridOrigin = new(-176f, UpperFloor, 121.5f);
    private static readonly (int X0, int X1, int Y0, int Y1)[] Rooms = { (0, 11, 0, 11), (13, 23, 0, 11), (0, 11, 13, 23) };

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
        definition.WorldStates.Add(BuildState(WallsState, 2));
        definition.WorldStates.Add(BuildState(RoofState, 3));
        definition.WorldStates.Add(BuildState("complete", 4));
        return definition;
    }

    // Level 1 is the open floor with the stair, 2 the walls, 3 the roof and lamps, 4 the finished storey.
    private static WorldStateDefinition BuildState(string id, int level)
    {
        var state = new WorldStateDefinition { Id = id };
        var house = BoundsOf(HouseWest - .3f, WallCut, HouseSouth - .3f, HouseEast + .3f, 6f, HouseNorth + .3f);
        // "Roof" also names the beams under the ceiling; the area starts above them.
        Rule(state, "roof", "Roof", ObjectEffect.Remove, BoundsOf(HouseWest - .5f, .3f, HouseSouth - .5f, HouseEast + .5f, 3f, HouseNorth + .5f));
        Rule(state, "porch-roof", "SmallRoof", ObjectEffect.Remove, BoundsOf(EastWall, -1.2f, BalconySouth - .2f, HouseEast, -.4f, NorthRoomSouth + .2f));
        Rule(state, "walls", "OuterWalls", ObjectEffect.Cut, house);
        Rule(state, "cladding", "Block", ObjectEffect.Cut, BoundsOf(HouseWest - .3f, CladdingCut, HouseSouth - .3f, HouseEast + .3f, 6f, HouseNorth + .3f));
        // The roof's shadow hull would darken the open floor; once the roof is back, the original and the copy cast both
        // storeys' shadows.
        if (level < 3) Rule(state, "shadow", "ShadowCaster", ObjectEffect.Remove, house);

        var geometry = new CopyDefinition
        {
            Id = "storey-build", Template = Anchor, Frame = Property, OwnMesh = false, Obstacle = false,
            At = new LocalPoint(PropertyPivot, PropertyYaw)
        };
        Face(geometry, "boards-west", Boards, InWest, BoardsBottom, InSouth, EastWall, BoardsTop, InNorth);
        Face(geometry, "boards-north-east", Boards, EastWall, BoardsBottom, NorthRoomSouth, InEast, BoardsTop, InNorth);
        Solid(geometry, "balcony", Boards, EastWall, BalconyBottom, BalconySouth, BalconyEast, UpperFloor, NorthRoomSouth);
        StairAndLanding(state, geometry);
        Railing(geometry, "balcony-south", true, BalconySouth + .03f, LandingEast, BalconyEast - .03f);
        Railing(geometry, "balcony-east", false, BalconyEast - .03f, BalconySouth, NorthRoomSouth);

        if (level == 1)
        {
            // Until the walls stand, a site railing runs along the open edges of the floor.
            const float i = .1f;
            Railing(geometry, "site-west", false, HouseWest + i, HouseSouth + i, HouseNorth - i);
            Railing(geometry, "site-north", true, HouseNorth - i, HouseWest + i, HouseEast - i);
            Railing(geometry, "site-east", false, HouseEast - i, NorthRoomSouth, HouseNorth - i);
            Railing(geometry, "site-south-east", true, NorthRoomSouth + i, BalconyEast, HouseEast - i);
            Railing(geometry, "site-south", true, HouseSouth + i, HouseWest + i, EastWall);
            Railing(geometry, "site-landing", false, EastWall - i, HouseSouth + i, LandingSouth);
        }
        else
        {
            state.Copies.Add(Shell(level));
            Railing(geometry, "door-balcony", false, DoorPlane - .25f, DoorSouth, DoorNorth);
        }

        if (level >= 3)
            for (int i = 0; i < Lamps.Length; i++)
            {
                var lamp = Copy("lamp-" + i, LampModel, LampPivot, new Vector3(Lamps[i].x, LampY, Lamps[i].y), 0);
                lamp.LightsOn = true;
                lamp.LightRange = LampRange;
                state.Copies.Add(lamp);
            }

        if (level == 4) Finish(state);
        state.Copies.Add(geometry);
        return state;
    }

    // The house's own shell, 3 m up, without the ground floor's foundation, kitchen and porch. The roof and the ceiling
    // come with level 3, the sliding door with the finished storey.
    private static CopyDefinition Shell(int level)
    {
        var shell = Copy("shell", House, PropertyPivot, PropertyPivot + new Vector3(0f, Lift, 0f), 0);
        foreach (var part in new[] { "Foundation", "Bench", "Cabinets", "Sink", "SmallRoof", "PorchBeam", "PorchBeam.001" })
            shell.Exclude.Add(part);
        if (level < 3)
            foreach (var part in new[] { "Roof", "ShadowCaster", "InnerRoof", "RoofBeams" })
                shell.Exclude.Add(part);
        if (level < 4) shell.Exclude.Add("Sliding Glass Door");
        return shell;
    }

    private static void StairAndLanding(WorldStateDefinition state, CopyDefinition geometry)
    {
        float centre = (LandingSouth + LandingNorth) * .5f;
        state.Copies.Add(Copy("stair", Stair, StairTop, new Vector3(LandingEast, UpperFloor, centre), StairYaw, StairScale));
        Solid(geometry, "landing", Metal, LandingWest, BalconyBottom, LandingSouth, LandingEast, UpperFloor, LandingNorth);
        // The stair's top tread ends a few centimetres short of the landing; an unseen strip closes the slit, which would
        // split the walking map there.
        Block(geometry, "landing-lip", LandingEast, BalconyBottom, LandingSouth, LandingEast + .3f, UpperFloor - .02f, LandingNorth);
        Post(geometry, "landing-post-south", LandingEast - .07f, LandingSouth + .07f, -4.2f, BalconyBottom);
        Post(geometry, "landing-post-north", LandingEast - .07f, LandingNorth - .07f, -4.2f, BalconyBottom);
        Railing(geometry, "landing-south", true, LandingSouth + .03f, LandingWest, LandingEast);
        StairRail(state, geometry, "south", LandingSouth + .03f);
        StairRail(state, geometry, "north", LandingNorth - .03f);
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
            Face(rail, "bar", Metal, source.x - length, source.y - .02f, source.z - .02f, source.x, source.y + .02f, source.z + .02f);
            // The rails are drawn without colliders; an unseen wall along the top rail keeps a jump from carrying over it.
            if (name == "top")
                Block(rail, "guard", source.x - length, source.y - .5f, source.z - .05f, source.x, source.y + 1.07f, source.z + .05f);
            state.Copies.Add(rail);
        }
    }

    // The finished storey: the rooms upstairs join the property's grid, and employees walk up the stair.
    private static void Finish(WorldStateDefinition state)
    {
        var extension = new PropertyExtensionDefinition
        {
            Id = ExtensionId, PropertyCode = "bungalow", GridIndex = GridIndex, GridOrigin = GridOrigin
        };
        foreach (var (x0, x1, y0, y1) in Rooms)
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                    extension.Tiles.Add(new GridCoordinate(x, y));
        extension.Bounds.Add(BoundsOf(InWest, UpperFloor - .1f, InSouth, InEast, UpperFloor + 2.95f, InNorth));
        state.PropertyExtensions.Add(extension);

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
        state.NavigationProbes.Add(new Vector3(StairFoot + .8f, -4f, 111.85f));
        state.NavigationProbes.Add(new Vector3(-169.1f, UpperFloor, 111.85f));
        state.NavigationProbes.Add(new Vector3(-167f, UpperFloor, 114f));
        state.NavigationProbes.Add(new Vector3(-171f, UpperFloor, 113.8f));
        state.NavigationProbes.Add(new Vector3(-173f, UpperFloor, 118.5f));
        state.NavigationProbes.Add(new Vector3(-167f, UpperFloor, 118.5f));
    }

    private static void Rule(WorldStateDefinition state, string id, string name, ObjectEffect effect, Bounds area) =>
        state.Rules.Add(new ObjectRule { Id = id, Name = name, Parent = "bungalow", Side = WorldSide.Main, Effect = effect, Area = area });

    // Posts and two rails of dark steel, with an unseen wall to head height that keeps a jump from carrying over them.
    private static void Railing(CopyDefinition geometry, string id, bool alongX, float cross, float start, float end)
    {
        const float half = .025f;
        float top = UpperFloor + RailHeight;
        int posts = Math.Max(2, (int)Math.Ceiling((end - start) / 1.2f) + 1);
        for (int i = 0; i < posts; i++)
        {
            float at = Mathf.Lerp(start + half, end - half, i / (float)(posts - 1));
            if (alongX) Face(geometry, id + "-post-" + i, Metal, at - half, UpperFloor, cross - half, at + half, top, cross + half);
            else Face(geometry, id + "-post-" + i, Metal, cross - half, UpperFloor, at - half, cross + half, top, at + half);
        }
        foreach (var (name, y) in new[] { ("top", top - .04f), ("middle", UpperFloor + .5f) })
            if (alongX) Face(geometry, id + "-rail-" + name, Metal, start, y, cross - .02f, end, y + .04f, cross + .02f);
            else Face(geometry, id + "-rail-" + name, Metal, cross - .02f, y, start, cross + .02f, y + .04f, end);
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
