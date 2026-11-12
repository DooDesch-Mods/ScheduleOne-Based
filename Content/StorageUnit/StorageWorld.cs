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
    // Turned to climb north along the west wall and stretched to the 3.30 m of the ledge on top of the lower wall.
    private const float StairYaw = 30f, StairScale = 1.082f;
    private const string Metal = "metal_darkgrey_mat";

    // The upper floor lies at 3.25; the ledge along the west wall and the landing in front of it end at 3.30.
    private const float UpperY = 3.25f, LandingTop = 3.30f, LandingBottom = 3.20f;
    private const float LandingWest = -10.65f, LandingEast = -9.01f, LandingSouth = 103.6f, LandingNorth = 105.3f;
    private const float RailTop = LandingTop + 1.05f, GuardTop = LandingTop + 2.1f;

    // The four small units above Unit 1 (Unit6) and the upper west wall in front of them.
    private const float UnitWest = -8.75f, UnitEast = -2.05f, UnitSouth = 103.8f, UnitNorth = 113.8f, UnitTop = 6.25f;
    private const string UnitSheet = "alum sheet med grey", FloorSheet = "metal_mediumgrey_mat";
    // The back of the walkway's unit faces inside the block, and the loft's ceiling under the roof's eave.
    private const float FacesBack = -2.25f, LoftCeiling = 6.0f;
    // The door through the west wall, over the landing.
    private const float DoorSouth = 103.9f, DoorNorth = 105.1f, DoorSill = 3.31f, DoorHead = 5.35f;
    private static readonly Bounds DoorCut = BoundsOf(-9.3f, DoorSill, DoorSouth, -8.7f, DoorHead, DoorNorth);
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
    private const string Wood = "wood planks medium brown mat";
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
        state.Copies.Add(geometry);
        return state;
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
        if (level < 6)
            Solid(geometry, "walkway-site-wall", Wood, FacesBack, UpperY, UnitSouth - .04f, EastFront, UpperY + SiteWallHeight, UnitSouth + .04f);
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
        GalleryRail(state, geometry);

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
        // would lie on top of the world's and never join it. The ground box ends below the stair's first step (0.28).
        state.NavigationBounds = BoundsOf(-10.9f, -.5f, south, east, UnitTop, UnitNorth);
        state.NavigationAgents.AddRange(new[] { "Humanoid", "Ignore Costs", "Employee" });
        state.NavigationExistingFloor.Add(BoundsOf(-10.9f, -.3f, south, -8.75f, .2f, UnitNorth));
        state.NavigationExistingFloor.Add(BoundsOf(-8.75f, -.3f, south, east, 3f, UnitNorth));
        // The first two straddle the seam at the stair's foot: the world's ground, then the lowest point of the stair's
        // own walking map (the agent's radius takes off the steps below it). Then the landing, the loft behind its
        // door, the far corner of the loft and its middle.
        state.NavigationProbes.Add(new Vector3(-9.84f, 0f, 97.4f));
        state.NavigationProbes.Add(new Vector3(-9.84f, .92f, 99.2f));
        state.NavigationProbes.Add(new Vector3(-9.6f, LandingTop, 104.5f));
        state.NavigationProbes.Add(new Vector3(-8.1f, UpperY, 104.4f));
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
                if (!(x >= 10 && y >= 16)) property.Tiles.Add(new GridCoordinate(x, y));
        property.Bounds.Add(BoundsOf(UnitWest, UpperY - .1f, UnitSouth - .2f, FacesBack, LoftCeiling, UnitNorth));
        state.PropertyExtensions.Add(property);
    }

    // The walkway's railing continues along the opening and meets it at its last post. The vanilla piece is 1.9 m long
    // with a post at each end; the run's last piece lies over the one before it, a few millimetres aside.
    private static void GalleryRail(WorldStateDefinition state, CopyDefinition geometry)
    {
        const float piece = 1.9f, start = UnitWest + .05f;
        float end = RailEnd.x, cross = RailEnd.z;
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
            // The walkway's railing already stands at the run's end.
            if (last) rail.Exclude.Add("outdoorfence/Post.001");
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
        Face(geometry, "door-jamb-south", Metal, outside, DoorSill, DoorSouth - .02f, inside, DoorHead, DoorSouth);
        Face(geometry, "door-jamb-north", Metal, outside, DoorSill, DoorNorth, inside, DoorHead, DoorNorth + .02f);
        Face(geometry, "door-head", Metal, outside, DoorHead, DoorSouth - .02f, inside, DoorHead + .02f, DoorNorth + .02f);
        Face(geometry, "door-sill", Metal, outside, DoorSill - .02f, DoorSouth, inside, DoorSill, DoorNorth);

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
        // The stair is 1.62 m wide after scaling and runs from x -10.65 to -9.03, clear of the bus stop's pillar.
        var target = new Vector3(-9.84f, LandingTop, LandingSouth);
        state.Copies.Add(Copy("west-stair", Stair, StairTop, target, StairYaw, StairScale));
        Solid(geometry, "landing", Metal, LandingWest, LandingBottom, LandingSouth, LandingEast, LandingTop, LandingNorth);
        // The stair's top tread ends a few centimetres short of the landing. The open slit would split the walking
        // map there, so an unseen strip closes it under the tread's edge.
        Block(geometry, "landing-lip", LandingWest, LandingBottom, LandingSouth - .3f, LandingEast, LandingTop - .02f, LandingSouth);
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
