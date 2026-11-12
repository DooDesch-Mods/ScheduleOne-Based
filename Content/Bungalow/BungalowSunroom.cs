using MapExpansions;
using UnityEngine;

namespace Based.Content.Bungalow;

// The glass room in front of the house's sliding door, built from the glass front of Ray's office: three frames along the
// west side, two on each end, and three laid down as a roof that falls from under the house's eave to the west front.
// The first night clears three pines and pours the slab, the second sets the frames, the third glazes them. The slab
// lies at the height of the house floor, so the west room's grid continues into the sunroom.
public static class BungalowSunroom
{
    public const string ExtensionId = "bungalow-sunroom";
    public const string BaseState = "base";
    public const string FrameState = "glass";

    private const string Property = "@Properties/Bungalow";
    private const string Anchor = Property + "/bungalow/PorchBeam";
    private static readonly Vector3 PropertyPivot = new(-170f, -4f, 115.5f);
    private const float PropertyYaw = 90f;

    // Ray's glass front: 7 m wide along z, 4 m high, 0.1 m deep along x, pivot in its middle; six panes by two.
    private const string RayFrame = "Map/Hyland Point/Region_Downtown/RE Office/RE office/Frame";
    private const string RayGlass = "Glass.001";
    private static readonly Vector3 FramePivot = new(86.15f, 2.1f, -15.1f);
    private const float FrameWidth = 7f, FrameHeight = 4f, FrameDepth = .1f;

    private const string Concrete = "concrete_grey2";
    private const string Glass = "window_glass_material";
    private const string Metal = "metal light black";

    // The wall planes. The east side is the house's west wall; the ends line up with the house's corner posts.
    private const float Floor = -3.70f, SlabBottom = -4.15f;
    private const float West = -183.05f, House = -176.17f, South = 109.23f, North = 121.63f;
    private const float SlabWest = -183.15f, SlabEast = -176.4f, SlabSouth = 109.13f, SlabNorth = 121.73f;
    // The roof's top meets the house 0.15 m under the eave at -0.7.
    private const float RoofAtHouse = -.85f;

    // The west room's grid: tile (x, y) has its centre at (-175.75 + 0.5 y, 115.10 - 0.5 x).
    private const int GridIndex = 1;
    private const int TileXMin = -12, TileXMax = 11, TileYMin = -14, TileYMax = -2;

    // Two pines stand on the slab; the third stands 2 m in front of the west door and would block the way out.
    private static readonly Vector2[] Pines = { new(-178.56f, 117.62f), new(-182.53f, 118.09f), new(-185.30f, 112.64f) };

    // The house's own sliding door, copied into the west front on the line of the house's door and the deck's step. A
    // fixed light of the same width stands on each side of its frame, two of Ray's frames fill the rest to the north.
    private const string HouseDoor = Property + "/bungalow/Sliding Glass Door";
    private const string HouseDoorFrame = Property + "/bungalow/DoorFrame";
    private const float HouseDoorPlane = -176.08f, DoorSouth = 110.25f, DoorNorth = 112.55f, DoorTop = -1.5f;
    private const float HouseDoorLeafSouth = 111.4f;
    private const float SideLight = DoorSouth - South;
    private const float FramesSouth = DoorNorth + SideLight;

    private const float WestScale = (North - FramesSouth) / (2 * FrameWidth);
    private const float EndScale = (House - West) / (2 * FrameWidth);
    private const float RoofScale = (North - South) / (3 * FrameHeight);
    private const int PanesPerFrame = 6;

    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        if (!MapExpansionsApi.Register(Create()))
            throw new InvalidOperationException("Based could not register the Bungalow sunroom; inspect the MapExpansions validation errors.");
        BungalowSunroomDoor.Init();
        _registered = true;
    }

    public static ExpansionDefinition Create()
    {
        var definition = new ExpansionDefinition
        {
            Id = BungalowDefinition.SunroomExpansionId, DisplayName = "Bungalow sunroom", Version = "1.0.0", Source = null
        };
        definition.WorldStates.Add(BuildState(BaseState, 0));
        definition.WorldStates.Add(BuildState(FrameState, 1));
        definition.WorldStates.Add(BuildState("complete", 2));
        return definition;
    }

    // Stage 0 is the slab, 1 the bare frames, 2 the glazed room.
    private static WorldStateDefinition BuildState(string id, int stage)
    {
        var state = new WorldStateDefinition { Id = id };
        for (int i = 0; i < Pines.Length; i++) MapTrees.Remove(state, "pine-" + i, Pines[i].x, Pines[i].y);
        // The garden table stood where the slab goes; it moves to the gravel south-west of the room.
        state.Moves.Add(new ObjectMoveDefinition { Id = "table", Path = Property + "/Round Outdoor Set", Offset = new Vector3(-4.1f, 0f, -6.1f) });
        // Terrain grass would grow through the slab; a tuft rooted just outside its edge leans over it, hence the margin.
        var grass = new Bounds();
        grass.SetMinMax(new Vector3(SlabWest - .3f, -20f, SlabSouth - .3f), new Vector3(House, 20f, SlabNorth + .3f));
        state.Rules.Add(new ObjectRule { Id = "grass", Name = "", Side = WorldSide.Main, Effect = ObjectEffect.ClearDetails, Area = grass });

        var geometry = new CopyDefinition
        {
            Id = "sunroom-shell", Template = Anchor, Frame = Property, OwnMesh = false, Obstacle = false,
            At = new LocalPoint(PropertyPivot, PropertyYaw)
        };
        Solid(geometry, "slab", Concrete, SlabWest, SlabBottom, SlabSouth, SlabEast, Floor, SlabNorth);
        // The slab stops short of the house's foundation; a thin top closes the strip of ground in between.
        Face(geometry, "slab-to-house", Concrete, SlabEast, Floor - .05f, SlabSouth, House - .01f, Floor, SlabNorth);

        if (stage >= 1)
        {
            bool glazed = stage == 2;
            float westHeight = FrameHeight * WestScale, endHeight = FrameHeight * EndScale;
            // Ray's glass on the west frames renders as white noise from inside the room, so these frames come without
            // it and get one pane per frame in their plane, like the side lights beside the door.
            for (int i = 0; i < 2; i++)
            {
                float span = FrameWidth * WestScale, z0 = FramesSouth + span * i;
                AddFrame(state, "west-" + i, new Vector3(West, Floor + westHeight * .5f, z0 + span * .5f), Quaternion.identity, WestScale, false);
                if (glazed) Face(geometry, "west-" + i + "-pane", Glass, West - .01f, Floor, z0, West + .01f, Floor + westHeight, z0 + span);
            }
            for (int i = 0; i < 3; i++)
            {
                float z = South + FrameHeight * RoofScale * (i + .5f);
                // Seen from above, Ray's glass renders as white noise too, so the roof gets its own pane in the
                // template's coordinates, where the copy turns and scales it with the frame.
                var roof = Tilted("roof-" + i, RayFrame, FramePivot, RoofCentre(z), RoofTurn(), RoofScale);
                roof.Exclude.Add(RayGlass);
                if (glazed) Face(roof, "pane", Glass, 86.14f, .15f, -18.6f, 86.16f, 4.05f, -11.6f);
                state.Copies.Add(roof);
            }
            float top = Floor + westHeight;
            SideLightOf(geometry, "west-light-south", South, DoorSouth, top, glazed);
            SideLightOf(geometry, "west-light-north", DoorNorth, FramesSouth, top, glazed);
            Face(geometry, "west-transom", Metal, West - .05f, DoorTop, DoorSouth, West + .05f, top, DoorNorth);

            var shift = new Vector3(West - HouseDoorPlane, 0f, 0f);
            var frame = Tilted("west-door-frame", HouseDoorFrame, new Vector3(HouseDoorPlane, Floor, DoorSouth), new Vector3(West, Floor, DoorSouth), Quaternion.identity, 1f);
            state.Copies.Add(frame);
            if (glazed)
            {
                // A visual copy; BungalowSunroomDoor slides its leaf and keeps it in step between peers.
                state.Copies.Add(Tilted("west-door", HouseDoor, new Vector3(HouseDoorPlane, Floor, DoorSouth), new Vector3(HouseDoorPlane, Floor, DoorSouth) + shift, Quaternion.identity, 1f));
                // The fixed half of the door, which the open leaf slides over.
                Block(geometry, "west-door-fixed", West - .05f, Floor, DoorSouth, West + .05f, DoorTop, HouseDoorLeafSouth);
            }
            for (int i = 0; i < 2; i++)
            {
                float x = West + FrameWidth * EndScale * (i + .5f);
                var turn = Quaternion.Euler(0f, 90f, 0f);
                AddFrame(state, "south-" + i, new Vector3(x, Floor + endHeight * .5f, South), turn, EndScale, glazed);
                AddFrame(state, "north-" + i, new Vector3(x, Floor + endHeight * .5f, North), turn, EndScale, glazed);
            }
            Gable(geometry, "south", South, Floor + endHeight, glazed);
            Gable(geometry, "north", North, Floor + endHeight, glazed);

            // The frames are visual copies without colliders; invisible walls stand in their planes.
            Block(geometry, "west-wall-south", West - .05f, Floor, South - .05f, West + .05f, Floor + westHeight, DoorSouth);
            Block(geometry, "west-wall-north", West - .05f, Floor, DoorNorth, West + .05f, Floor + westHeight, North + .05f);
            Block(geometry, "west-wall-transom", West - .05f, DoorTop, DoorSouth, West + .05f, Floor + westHeight, DoorNorth);
            Block(geometry, "south-wall", West, Floor, South - .05f, House, Floor + westHeight, South + .05f);
            Block(geometry, "north-wall", West, Floor, North - .05f, House, Floor + westHeight, North + .05f);
        }

        if (stage == 2)
        {
            // Stepped blocks under the glass give the roof a collider, which the rain cover's raycast hits.
            const int steps = 7;
            float run = (House - West) / steps;
            for (int i = 0; i < steps; i++)
            {
                float x0 = West + i * run, top = RoofUnderside(x0);
                Block(geometry, "roof-" + i, x0, top - .1f, South, x0 + run, top, North);
            }

            var extension = new PropertyExtensionDefinition { Id = ExtensionId, PropertyCode = "bungalow", GridIndex = GridIndex };
            for (int x = TileXMin; x <= TileXMax; x++)
                for (int y = TileYMin; y <= TileYMax; y++)
                    extension.Tiles.Add(new GridCoordinate(x, y));
            extension.Bounds.Add(BoundsOf(West, Floor - .3f, South, House, Floor + 3f, North));
            state.PropertyExtensions.Add(extension);

            state.NavigationBounds = BoundsOf(West - .2f, Floor - .3f, South - .2f, -174.4f, -1f, North + .2f);
            state.NavigationAgents.AddRange(new[] { "Humanoid", "Ignore Costs", "Employee" });
            // The house floor behind the sliding door; the seam check samples 0.6 m to each side of its west edge.
            state.NavigationExistingFloor.Add(BoundsOf(-176f, Floor - .2f, 110.4f, -174.6f, Floor + .1f, 112.4f));
            state.NavigationProbes.Add(new Vector3(-175.3f, Floor, 111.4f));
            state.NavigationProbes.Add(new Vector3(-176.9f, Floor, 111.4f));
            state.NavigationProbes.Add(new Vector3(-182.5f, Floor, 109.8f));
            state.NavigationProbes.Add(new Vector3(-182.5f, Floor, 121.1f));
            state.NavigationProbes.Add(new Vector3(-177f, Floor, 121.1f));
        }
        state.Copies.Add(geometry);
        return state;
    }

    // The roof's mid-plane rises from the top of the west front to under the house's eave.
    private static float RoofSlope => (RoofAtHouse - RoofDepth * .5f - (Floor + FrameHeight * WestScale + RoofDepth * .5f)) / (House - West);
    private static float RoofDepth => FrameDepth * RoofScale;
    private static float RoofMid(float x) => RoofAtHouse - RoofDepth * .5f - (House - x) * RoofSlope;
    private static float RoofUnderside(float x) => RoofMid(x) - RoofDepth * .5f;

    // Laid down with the 7 m side across the room, falling west.
    private static Quaternion RoofTurn() =>
        Quaternion.Euler(0f, 0f, Mathf.Atan(RoofSlope) * Mathf.Rad2Deg) * Quaternion.Euler(90f, 0f, 0f) * Quaternion.Euler(0f, 90f, 0f);

    private static Vector3 RoofCentre(float z)
    {
        float half = FrameWidth * RoofScale * .5f * Mathf.Cos(Mathf.Atan(RoofSlope));
        float x = House - half;
        return new Vector3(x, RoofMid(x), z);
    }

    private static void AddFrame(WorldStateDefinition state, string id, Vector3 centre, Quaternion turn, float scale, bool glazed)
    {
        var copy = Tilted(id, RayFrame, FramePivot, centre, turn, scale);
        if (!glazed) copy.Exclude.Add(RayGlass);
        state.Copies.Add(copy);
    }

    // The end walls' frames stop below the sloped roof. Above them a band of panes follows the roof, one pane per column
    // of the frames below, each reaching up to the roof's underside at its higher end.
    private static void Gable(CopyDefinition geometry, string id, float z, float bottom, bool glazed)
    {
        int columns = 2 * PanesPerFrame;
        float width = (House - West) / columns;
        for (int k = 0; k <= columns; k++)
        {
            float x = West + k * width;
            float x0 = Mathf.Max(West, x - .02f), x1 = Mathf.Min(House, x + .02f);
            Face(geometry, id + "-mullion-" + k, Metal, x0, bottom, z - .03f, x1, RoofUnderside(x1), z + .03f);
            if (!glazed || k == columns) continue;
            Face(geometry, id + "-pane-" + k, Glass, x, bottom, z - .01f, x + width, RoofUnderside(x + width), z + .01f);
        }
    }

    // A fixed light beside the door: a metal border and a middle rail like Ray's frames, one pane above and one below.
    private static void SideLightOf(CopyDefinition geometry, string id, float z0, float z1, float top, bool glazed)
    {
        const float bar = .04f;
        float mid = Floor + (top - Floor) * .5f;
        Face(geometry, id + "-edge-0", Metal, West - .03f, Floor, z0, West + .03f, top, z0 + bar);
        Face(geometry, id + "-edge-1", Metal, West - .03f, Floor, z1 - bar, West + .03f, top, z1);
        Face(geometry, id + "-sill", Metal, West - .03f, Floor, z0, West + .03f, Floor + bar, z1);
        Face(geometry, id + "-rail", Metal, West - .03f, mid - bar * .5f, z0, West + .03f, mid + bar * .5f, z1);
        Face(geometry, id + "-head", Metal, West - .03f, top - bar, z0, West + .03f, top, z1);
        if (glazed) Face(geometry, id + "-pane", Glass, West - .01f, Floor, z0, West + .01f, top, z1);
    }

    // Places a copy so that the source point lands on the target, turned by the world rotation and scaled about it. The
    // frame is the property root, which stands turned by PropertyYaw.
    private static CopyDefinition Tilted(string id, string template, Vector3 source, Vector3 target, Quaternion turn, float scale)
    {
        var frameTurn = turn * Quaternion.Euler(0f, PropertyYaw, 0f);
        return new CopyDefinition
        {
            Id = id, Template = template, Frame = Property,
            At = new LocalPoint(target - turn * (source - PropertyPivot) * scale, 0f),
            OntoRotation = frameTurn.eulerAngles, Scale = scale,
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
            Id = id, Kind = BoxKind.Panel, Material = material, TileMetres = 1f,
            Box = BoundsOf(x1, y1, z1, x2, y2, z2)
        });

    private static Bounds BoundsOf(float x1, float y1, float z1, float x2, float y2, float z2)
    {
        var bounds = new Bounds();
        bounds.SetMinMax(new Vector3(x1, y1, z1), new Vector3(x2, y2, z2));
        return bounds;
    }
}
