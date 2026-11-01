using MapExpansions;
using UnityEngine;

namespace Based.Content.Ming;

public static class MingWorld
{
    public const float FloorY = -0.25f;
    public static readonly Bounds Opening = BoundsOf(-60.04f, -0.251f, 135.9f, -55.78f, 2.4f, 141.17f);
    // Once the new ceiling stands, nothing of the old walls stays below it.
    private static readonly Bounds OpeningFinished = BoundsOf(-60.04f, -0.251f, 135.9f, -55.78f, 2.82f, 141.17f);
    public static readonly Bounds Wing = BoundsOf(-60.12f, -0.45f, 135.88f, -53.88f, 3.2f, 145.12f);
    // The walls the cut removes, with room for whatever stands against them or hangs on them.
    public static readonly Bounds[] KeepClear =
    {
        BoundsOf(-60.5f, -.2f, 135.9f, -59.5f, 2.4f, 138f),
        BoundsOf(-60.1f, -.2f, 137.7f, -55.7f, 2.4f, 138.25f),
        BoundsOf(-56.35f, -.2f, 137.8f, -55.7f, 2.4f, 141.2f),
        BoundsOf(-60.1f, -.2f, 140.75f, -55.7f, 2.4f, 141.3f)
    };
    private const string Property = "@Properties/Sweatshop";
    private const string Anchor = Property + "/InteriorSpawnPoint";
    private const string Window = Property + "/Chinese Restaurant/Window (3)";
    private const string Sink = Property + "/Standalone Sink";
    // Puts the sink's back flush against the new north wall.
    private static readonly Vector3 SinkOffset = new(0f, 0f, 4f);
    private const string NorthAc = Property + "/Chinese Restaurant/SM_AC (1)";
    private const string NorthAcStain = Property + "/Chinese Restaurant/Interior/Rust 1 (3)";
    // The unit hangs in the corner the new west wall takes, and its stain runs down below it.
    private static readonly Vector3 NorthAcOffset = new(-.4f, 0f, 0f);
    private const string RoomSwitch = Property + "/Contents/ModularSwitch";
    private const string Blinds = Property + "/Blinds (1)";
    private static readonly Vector3 BlindsPivot = new(-62.5f, 2.02f, 134.99f);
    private const string RoofCaster = Property + "/Chinese Restaurant/Chinese restaurant/UpperRoof/Cube";
    private static readonly Vector3 RoofCasterPivot = new(-62.5f, 2.935f, 138f);
    private const float BlindsY = 2.02f;
    private const string Fence = "Map/Hyland Point/Region_Docks/Dark Market Area/Docks Warehouse/InteriorDisable/Mezzanine/docks mezzanine/FenceBars";
    private const string Pallet = "Map/Hyland Point/Region_Northtown/North Waterfront/Pallet Prop (2)";
    private const string ManorSite = "@Properties/Manor/Construction/";
    private const string BuildSite = "Map/Hyland Point/Region_Westville/Construction Site/";
    private const string Plank = "Map/Hyland Point/Region_Westville/Tattoo Parlour New/Interior/GameObject/SM_Prop_Plank_01";
    private const string Tarp = "Map/Hyland Point/Region_Westville/Encampment/SM_Prop_Scaffold_Stackable_01_Tarp_1";
    private const string FoldingTable = "Map/SpecialCustomerCamps/BikerCamp/Container/Props/Table_Foldout/Table_Foldout_LOD0";

    // The building's own materials, so that the wing reads as part of it.
    private const string Wood = "wood planks medium brown mat";
    private const string Trim = "wood brown";
    private const string Wall = "wallpaper light green";
    private const string Exterior = "concrete bar green";
    private const string RoofEdge = "concrete bar green darker";
    private const string RoofTop = "concrete beige";
    private const string Brick = "brick dark light orange";

    // Heights of the existing upper floor: ceiling boards lie on the joists, the roof ends flush with its top.
    private const float JoistBottom = 2.51f;
    private const float CeilingY = 2.81f;
    private const float GuardTop = 2.4f;
    // Where the old room's wood panelling ends and its wallpaper starts.
    private const float WainscotTop = .5f;
    private const float RoofEdgeBottom = 2.85f;
    private const float RoofSheetY = 2.925f;
    private const float RoofRimTop = 3.025f;
    private const float RoofCollisionTop = 3.02f;
    private const float WallBase = -.4f;
    private const float StreetY = -3.9f;

    private static readonly Vector3 PropertyPivot = new(-62.5f, -.25f, 138f);
    private static readonly Vector3 AnchorPoint = new(-63.949f, FloorY, 140.19f);
    private static readonly Vector3 FoldingTablePivot = new(-142.02f, -3.9f, 50.01f);
    private const float TableScale = .9f;
    private const float TableTop = FloorY + .75f * TableScale;
    private static readonly Vector3 WindowPivot = new(-55.925f, 1.462493f, 139.5f);
    private static readonly Vector3 FencePivot = new(-39.8f, 2.45f, 34.812f);
    private static readonly Vector3 VanPosition = new(-66.55f, -3.45f, 137f);
    private static readonly Vector3 PalletPivot = new(-57.9526f, -.348f, 143.1308f);
    private static readonly Vector3 PlankPivot = new(-131.62f, -3f, 69.53f);
    // The pivot of a plank sits halfway up its thickness.
    private const float PalletTop = -.05f, PlankThickness = .04f;
    private static readonly Vector3 TarpPivot = new(-89.95f, -3.7f, 62.7f);
    private static readonly Vector3 BricksPivot = new(169.16f, 10.12f, -43.91f);
    private static readonly Vector3 BarrelsPivot = new(171.41f, 10.15f, -42.39f);
    private static readonly Vector3 MixerPivot = new(151.89f, 10.14f, -51.31f);
    private static readonly Vector3 GeneratorPivot = new(-127.05f, -4f, 94.47f);
    private static readonly Vector3 SignPivot = new(-135.09f, -4.26f, 93.17f);
    private static readonly Vector3 ConePivot = new(-132.98f, -3.77f, 99.88f);
    private const float ConeCentre = .23f;
    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        if (!MapExpansionsApi.Register(Create()))
            throw new InvalidOperationException("Based could not register Ming world states; inspect the MapExpansions validation errors.");
        _registered = true;
    }

    public static ExpansionDefinition Create()
    {
        var definition = new ExpansionDefinition
        {
            Id = MingDefinition.ExpansionId, DisplayName = "Ming upstairs expansion", Version = "0.2.0", Source = null
        };
        definition.WorldStates.Add(BuildState("breakthrough", 1));
        definition.WorldStates.Add(BuildState("frame", 2));
        definition.WorldStates.Add(BuildState("roof", 3));
        definition.WorldStates.Add(BuildState("complete", 4));
        definition.Locations.Add(new LocationDefinition("foreman-plan", new Vector3(-57.5f, FloorY, 143.2f), 180));
        definition.Locations.Add(new LocationDefinition("south-bench", new Vector3(-57.8f, FloorY, 137.55f), 180));
        definition.Locations.Add(new LocationDefinition("north-frame", new Vector3(-56.4f, FloorY, 143.45f)));
        definition.Locations.Add(new LocationDefinition("east-fitout", new Vector3(-54.7f, FloorY, 140.1f), 90));
        return definition;
    }

    private static WorldStateDefinition BuildState(string id, int morning)
    {
        var state = new WorldStateDefinition
        {
            Id = id, NavigationBounds = BoundsOf(-65.5f, -1f, 134.5f, -53.5f, 3.5f, 145.5f)
        };
        state.NavigationAgents.AddRange(new[] { "Humanoid", "Ignore Costs", "Employee" });
        // The cut opens the former external walls while keeping the roof above them; the main room and its
        // former bay become one space. It takes the old floor along, so the new sheet continues it at the same height.
        state.Rules.Add(new ObjectRule
        {
            Id = "upstairs-opening", Name = "UpperRoom", Parent = "Chinese restaurant",
            Side = WorldSide.Main, Effect = ObjectEffect.Cut, Area = morning == 4 ? OpeningFinished : Opening
        });
        Remove(state, "old-internal-window", "Window (3)", "Chinese Restaurant", BoundsOf(-56.05f, .8f, 138.65f, -55.8f, 2.2f, 140.35f));
        Remove(state, "terrace-pallet-a", "Pallet Prop (2)", "North Waterfront", BoundsOf(-58.8f, -.4f, 142.3f, -57.1f, .1f, 144f));
        Remove(state, "terrace-pallet-b", "Pallet Prop (3)", "North Waterfront", BoundsOf(-57.65f, -.3f, 142.8f, -55.9f, .3f, 144.5f));
        Remove(state, "old-upper-drain-a", "Water_Pipe_Long (3)", "Chinese Restaurant", BoundsOf(-58.08f, 1.2f, 141.4f, -57.82f, 2.3f, 141.65f));
        Remove(state, "old-upper-drain-b", "Water_Pipe_Long (4)", "Chinese Restaurant", BoundsOf(-58.08f, .25f, 141.4f, -57.82f, 1.3f, 141.65f));
        Remove(state, "old-drain-through-floor", "Water_Pipe_Long (1)", "Chinese Restaurant", BoundsOf(-58.08f, -.3f, 141.4f, -57.82f, .3f, 141.65f));
        Remove(state, "old-upper-drain-elbow", "Water_Pipe_right", "Chinese Restaurant", BoundsOf(-58.08f, 2.2f, 140.95f, -57.82f, 2.9f, 141.65f));
        if (morning >= 3)
        {
            state.Copies.Add(Copy("relocated-room-ac", Property + "/Chinese Restaurant/SM_AC/SM_AC",
                new Vector3(-59.84402f, 2.253f, 136.34501f), new Vector3(-53.87402f, 2.253f, 140.5f)));
            // Its rust stain runs down the facade below it, a hair in front of the wall.
            state.Copies.Add(Copy("relocated-room-ac-rust", Property + "/Chinese Restaurant/Interior/Rust 1 (2)",
                new Vector3(-59.84402f, 2.253f, 136.34501f), new Vector3(-53.86902f, 2.253f, 140.5f)));
        }
        Remove(state, "old-room-ac", "SM_AC", "Chinese Restaurant", BoundsOf(-59.95f, 1.7f, 135.8f, -59.4f, 2.7f, 136.9f));
        // These stains are painted on the walls the cut removes and would stay in the air.
        Remove(state, "old-east-wall-stain", "Decals 053", "Interior", BoundsOf(-60.1f, .7f, 135f, -59.9f, 3.7f, 138f));
        Remove(state, "old-east-outer-rust", "Rust 1 (2)", "Interior", BoundsOf(-59.95f, -.2f, 135.8f, -59.75f, 2f, 136.9f));
        Remove(state, "old-bay-south-rust", "Rust 1 (4)", "Interior", BoundsOf(-57.7f, .2f, 137.9f, -56.3f, 2.8f, 138.1f));
        Remove(state, "old-bay-north-stain", "Decals 0004 (2)", "Interior", BoundsOf(-58.1f, -.3f, 140.9f, -56f, 1.8f, 141.1f));
        // This one lies across the old floor's edge and would end there in a straight line.
        Remove(state, "old-floor-grime", "Grime 0", "Interior", BoundsOf(-60.5f, -.3f, 139.4f, -58.5f, -.2f, 141.4f));
        // The sink hangs on a wall the cut removes. It stays usable through the work and moves when the new wall stands.
        if (morning == 4) state.Moves.Add(new ObjectMoveDefinition { Id = "room-sink", Path = Sink, Offset = SinkOffset });
        state.Moves.Add(new ObjectMoveDefinition { Id = "north-ac", Path = NorthAc, Offset = NorthAcOffset });
        state.Moves.Add(new ObjectMoveDefinition { Id = "north-ac-stain", Path = NorthAcStain, Offset = NorthAcOffset });

        var geometry = new CopyDefinition
        {
            Id = "fitted-wing", Template = Anchor, Frame = Anchor,
            At = new LocalPoint(AnchorPoint, 0), OwnMesh = false, Obstacle = false
        };
        // Once the walls stand, the floor stops inside them instead of sharing their outer faces.
        float edge = morning >= 3 ? .05f : 0f, east = morning >= 2 ? -53.91f : -54f;
        Solid(geometry, "floor-south", Wood, -60f, -.45f, 135.9f + edge, east, FloorY, 137.9f);
        Solid(geometry, "floor-east", Wood, -56f, -.45f, 137.9f, east, FloorY, 141.1f);
        Solid(geometry, "floor-north", Wood, -60f, -.45f, 141.1f, east, FloorY, 145.1f - edge);
        // One sheet continues the old floor where the cut ends it and covers what the cut leaves of the old walls.
        Face(geometry, "floor-finish", Wood, -60.045f, -.26f, 135.9f, east, -.2495f, 145.1f - edge);

        // During the work the cut leaves the old outer walls above door height. Timber headers wrap those remains
        // into one beam line and props hold them; the finished room has neither.
        if (morning < 4)
        {
            Solid(geometry, "old-bay-east-header", Trim, -56.02f, 2.38f, 137.8f, -55.78f, CeilingY, 141.18f);
            Solid(geometry, "old-bay-south-header", Trim, -60.04f, 2.38f, 137.8f, -55.78f, CeilingY, 138.05f);
            Solid(geometry, "old-bay-north-header", Trim, -60.04f, 2.38f, 140.95f, -55.78f, CeilingY, 141.18f);
            Solid(geometry, "old-main-east-header", Trim, -60.05f, 2.38f, 135.9f, -59.78f, CeilingY, 138.05f);
            Post(geometry, "junction-north", -59.94f, 141.07f, 2.38f);
            Post(geometry, "bay-northeast", -55.9f, 141.07f, 2.38f);
            Post(geometry, "bay-southeast", -55.9f, 137.93f, 2.38f);
        }

        if (morning < 3)
        {
            FenceRun(state, geometry, "south", true, 136.0f, -59.85f, -54.1f, endPost: false);
            FenceRun(state, geometry, "east", false, -54.1f, 136.0f, 145.0f);
            FenceRun(state, geometry, "north", true, 145.0f, -59.9f, -54.1f, startPost: false, endPost: false);
            FenceRun(state, geometry, "west-north", false, -59.9f, 141.18f, 145.0f);
        }
        if (morning < 4)
        {
            SiteTable(state, geometry, "south-table", -57.8f, 136.65f);
            SiteTable(state, geometry, "plan-table", -57.2f, 142.05f);
            geometry.Panel(new Vector3(-57.85f, TableTop + .002f, 141.89f), new Vector3(-57.35f, TableTop + .004f, 142.21f), "clipboard_paper_mat", "plan-paper");
            // A temporary stud wall carries the sink's pipes while the old wall behind it is gone.
            Solid(geometry, "sink-stud-wall", Wood, -60.02f, FloorY, 141f, -58.95f, 1.5f, 141.06f);
            Solid(geometry, "sink-stud-west", Trim, -60.02f, FloorY, 141.06f, -59.92f, 1.5f, 141.14f);
            Solid(geometry, "sink-stud-east", Trim, -59.05f, FloorY, 141.06f, -58.95f, 1.5f, 141.14f);
            if (morning >= 2) SiteTable(state, geometry, "north-table", -56.9f, 144.4f);
            AddSiteProps(state, geometry, morning);
        }
        if (morning == 1)
        {
            BrokenWall(geometry, "broken-main-east", false, -60f, -59.85f, false, 135.9f, 137.9f, 2.3f, .9f);
            BrokenWall(geometry, "broken-bay-south", true, 137.9f, 138.05f, true, -59.85f, -56f, .9f, 1.6f);
            BrokenWall(geometry, "broken-bay-east", false, -56f, -55.85f, false, 138.05f, 141f, 1.6f, 1.3f);
            BrokenWall(geometry, "broken-bay-north", true, 141f, 141.15f, false, -58.95f, -56f, .7f, 1.3f);
        }
        if (morning <= 2) Rubble(state, morning);

        if (morning is 2 or 3)
        {
            FrameWall(geometry, "east", false, -53.97f, 136.02f, 144.98f, new[] { 137.3f, 143f });
            FrameWall(geometry, "south", true, 135.98f, -59.88f, -53.97f, new[] { -57f });
            FrameWall(geometry, "north", true, 145.02f, -59.88f, -53.97f, new[] { -57f });
            FrameWall(geometry, "west-north", false, -60.02f, 141.18f, 145.02f, Array.Empty<float>());
        }
        if (morning >= 2) Joists(geometry);
        if (morning >= 3)
        {
            Solid(geometry, "ceiling-south", Wood, -60.04f, CeilingY, 135.95f, -53.95f, RoofEdgeBottom, 137.9f);
            Solid(geometry, "ceiling-east", Wood, -55.78f, CeilingY, 137.9f, -53.95f, RoofEdgeBottom, 141.1f);
            Solid(geometry, "ceiling-north", Wood, -60.04f, CeilingY, 141.1f, -53.95f, RoofEdgeBottom, 145.05f);
            Roof(state, geometry);
            // The east wall runs past both corners and the west wall reaches into the old one, so no joint stays open.
            // The room faces lie on the grid's half-metre lines like the old room's, so the last row of tiles ends at the wall.
            Shell(geometry, "east", false, -53.94f, -53.88f, 135.88f, 145.12f, new[] { 137.3f, 143f }, morning == 4, 136f, 145f);
            Shell(geometry, "south", true, 135.88f, 135.96f, -59.995f, -53.94f, new[] { -57f }, morning == 4, -59.98f, -54f);
            Shell(geometry, "north", true, 145.04f, 145.12f, -60.12f, -53.94f, new[] { -57f }, morning == 4, -60f, -54f);
            Shell(geometry, "west-north", false, -60.12f, -60.04f, 141.05f, 145.04f, Array.Empty<float>(), morning == 4, 141f, 145f);
            // The new wall starts just north of the old one. Until the finished return covers it, this stud closes the joint.
            if (morning == 3) Solid(geometry, "west-north-joint", Trim, -60.12f, FloorY, 141f, -59.98f, CeilingY, 141.06f);
            state.Copies.Add(Copy("east-window-south", Window, WindowPivot, new Vector3(-53.94f, 1.462493f, 137.3f)));
            state.Copies.Add(Copy("east-window-north", Window, WindowPivot, new Vector3(-53.94f, 1.462493f, 143f)));
            state.Copies.Add(Copy("south-window", Window, WindowPivot, new Vector3(-57f, 1.462493f, 135.94f), 90));
            state.Copies.Add(Copy("north-window", Window, WindowPivot, new Vector3(-57f, 1.462493f, 145.06f), -90));
            // The fan blades reach 1.05 m around the lamp, and the headers of the old room still hang in the roof stage.
            if (morning == 4) Lamp(state, "south-ceiling-light", -56.5f, 137.575f, true);
            else Lamp(state, "south-ceiling-light", -54.99f, 137.01f, false);
            Lamp(state, "north-ceiling-light", -57f, 143.61f, morning == 4);
        }
        if (morning == 4)
        {
            // Flush with the room's old walls, so that wallpaper and panelling run on without a step.
            Return(geometry, "west-north-return", -60.06f, 140.998f, -60f, 141.02f, -59.985f, 141.02f);
            Return(geometry, "south-return", -60.002f, 135.84f, -59.98f, 136f, -59.98f, 136.015f);
            // One sheet over the bay's old ceiling and the new boards, under the joists' upper ends.
            Face(geometry, "ceiling-finish", Wood, -60.04f, CeilingY - .004f, 135.98f, -53.97f, CeilingY, 145.03f);
            // Reaches under the old ceiling, whose cut edge would otherwise leave a line of daylight.
            Face(geometry, "ceiling-seam", Wood, -60.09f, CeilingY - .002f, 135.9f, -60.03f, CeilingY + .002f, 141f);
            state.Copies.Add(Blind("east-blinds-south", new Vector3(-53.99f, BlindsY, 137.3f), -90));
            state.Copies.Add(Blind("east-blinds-north", new Vector3(-53.99f, BlindsY, 143f), -90));
            state.Copies.Add(Blind("south-blinds", new Vector3(-57f, BlindsY, 135.99f), 0));
            state.Copies.Add(Blind("north-blinds", new Vector3(-57f, BlindsY, 145.01f), 180));
            // Keep the original surface IDs, including the east pier centred at the saved clock's position.
            var property = new PropertyExtensionDefinition { Id = "ming-wing-floor", PropertyCode = "sweatshop", GridIndex = 0 };
            AddSurface(property, "east-wall", "east-finish-collision", PropertySurfaceKind.Wall, PropertySurfaceFace.Left);
            AddSurface(property, "north-wall", "north-finish-collision", PropertySurfaceKind.Wall, PropertySurfaceFace.Back);
            AddSurface(property, "south-wall", "south-finish-collision", PropertySurfaceKind.Wall, PropertySurfaceFace.Front);
            AddSurface(property, "west-north-wall", "west-north-finish-collision", PropertySurfaceKind.Wall, PropertySurfaceFace.Right);
            AddSurface(property, "ceiling-south", "ceiling-south-collision", PropertySurfaceKind.Roof, PropertySurfaceFace.Bottom);
            AddSurface(property, "ceiling-east", "ceiling-east-collision", PropertySurfaceKind.Roof, PropertySurfaceFace.Bottom);
            AddSurface(property, "ceiling-north", "ceiling-north-collision", PropertySurfaceKind.Roof, PropertySurfaceFace.Bottom);
            foreach (var wall in new[]
                {
                    (Id: "east", Face: PropertySurfaceFace.Left), (Id: "north", Face: PropertySurfaceFace.Back),
                    (Id: "south", Face: PropertySurfaceFace.Front), (Id: "west-north", Face: PropertySurfaceFace.Right)
                })
                foreach (var box in geometry.Boxes.Where(b => b.Kind == BoxKind.Block && b.Id.StartsWith(wall.Id + "-", StringComparison.Ordinal)
                    && b.Id.Contains("-finish", StringComparison.Ordinal) && b.Id.EndsWith("-collision", StringComparison.Ordinal)
                    && b.Id != wall.Id + "-finish-collision"))
                    AddSurface(property, wall.Id + "-wall-" + box.Id, box.Id, PropertySurfaceKind.Wall, wall.Face);
            AddPlacementTiles(property);
            property.Bounds.Add(Wing);
            state.PropertyExtensions.Add(property);
        }
        if (morning == 4)
        {
            state.NavigationProbes.Add(new Vector3(-60.7f, FloorY, 136.7f));
            state.NavigationProbes.Add(new Vector3(-59.1f, FloorY, 136.7f));
            state.NavigationProbes.Add(new Vector3(-58f, FloorY, 139.5f));
        }
        // The mixer stands in the south-east corner while it is on site.
        state.NavigationProbes.Add(morning is 2 or 3 ? new Vector3(-56.3f, FloorY, 137f) : new Vector3(-55.2f, FloorY, 136.8f));
        state.NavigationProbes.Add(new Vector3(-55.2f, FloorY, 138.5f));
        // The plan table stands where the finished room is open floor.
        state.NavigationProbes.Add(morning < 4 ? new Vector3(-55.85f, FloorY, 143.2f) : new Vector3(-56.5f, FloorY, 143f));
        if (morning < 4)
        {
            state.NavigationProbes.Add(new Vector3(-57.8f, FloorY, 137.55f));
            state.NavigationProbes.Add(new Vector3(-54.7f, FloorY, 140.1f));
            state.NavigationProbes.Add(new Vector3(-54.7f, FloorY, 138.4f));
            state.NavigationProbes.Add(new Vector3(-57.5f, FloorY, 143.2f));
            if (morning >= 2) state.NavigationProbes.Add(new Vector3(-56.4f, FloorY, 143.45f));
        }
        state.Copies.Add(geometry);
        return state;
    }

    // What stands on the site and in front of the building for one construction state.
    private static void AddSiteProps(WorldStateDefinition state, CopyDefinition geometry, int morning)
    {
        // Only the existing terrace pallets leave; this one holds the timber stock.
        state.Copies.Add(Copy("staging-pallet", Pallet, PalletPivot, new Vector3(-58.8f, FloorY, 144f)));
        // Without a block the navigation surface climbs the pallet and lifts anyone standing nearby off the floor.
        geometry.Boxes.Add(new BoxDefinition { Id = "staging-pallet-block", Kind = BoxKind.Block, Box = BoundsOf(-59.58f, FloorY, 143.22f, -58.02f, .3f, 144.78f) });
        // Stacked side by side across their length; the stock shrinks as the frame goes up.
        float[] plankShift = { .015f, -.025f, .005f, -.01f, .03f, -.015f, .01f, -.03f, .02f, 0f, -.02f, .025f };
        for (int i = 0; i < (morning == 1 ? 12 : morning == 2 ? 8 : 4); i++)
            state.Copies.Add(Copy("stock-plank-" + i, Plank, PlankPivot, new Vector3(-58.8f + plankShift[i], PalletTop + PlankThickness * (.5f + i / 4), 144f + ((i % 4) - 1.5f) * .125f), 90));

        state.Copies.Add(Copy("barrels", ManorSite + "SM_Prop_BarrelStack_01", BarrelsPivot, new Vector3(-59.1f, FloorY, 141.95f)));
        if (morning <= 2)
            state.Copies.Add(Copy("bricks", ManorSite + "SM_Prop_Brick_Stack_01", BricksPivot, new Vector3(-54.85f, FloorY, 144.4f), scale: .75f));
        if (morning >= 2)
            state.Copies.Add(Copy("mixer", ManorSite + "SM_Prop_Concrete_Mixer_01", MixerPivot, new Vector3(-55.05f, FloorY, 136.9f)));
        if (morning == 3)
            state.Copies.Add(Copy("generator", BuildSite + "SM_Prop_Generator_01", GeneratorPivot, new Vector3(-54.9f, FloorY, 144.4f)));
        if (morning >= 2) Scaffold(state, geometry);

        state.Copies.Add(new CopyDefinition
        {
            Id = "construction-van", Resource = "vehicles/van/Van", VisualOnly = true,
            At = new LocalPoint(VanPosition, 0), Obstacle = true
        });
        state.Copies.Add(Copy("site-sign", BuildSite + "CS_sign_02", SignPivot, new Vector3(-66.3f, StreetY - .1f, 133.6f), 170));
        var cones = new[] { new Vector2(-65.9f, 133.8f), new Vector2(-65.9f, 141.2f), new Vector2(-67.5f, 134f), new Vector2(-67.5f, 141f) };
        for (int i = 0; i < cones.Length; i++)
            state.Copies.Add(Copy("site-cone-" + i, BuildSite + "TrafficCone/trafficcone", ConePivot, new Vector3(cones[i].x, StreetY - .1f + ConeCentre, cones[i].y)));
    }

    // A timber scaffold on the street side, built like the wooden site frames the game already has.
    private static void Scaffold(WorldStateDefinition state, CopyDefinition geometry)
    {
        const float wall = 135.42f, outer = 134.86f, top = 2.3f;
        // The pavement drops toward the street, so the posts start below its lowest point.
        const float foot = StreetY - .4f;
        for (int i = 0; i < 3; i++)
        {
            float x = -59.75f + i * 2.8f;
            Face(geometry, "scaffold-post-inner-" + i, Trim, x - .05f, foot, wall - .05f, x + .05f, top, wall + .05f);
            Face(geometry, "scaffold-post-outer-" + i, Trim, x - .05f, foot, outer - .05f, x + .05f, top + .9f, outer + .05f);
        }
        foreach (float y in new[] { -1.9f, .1f, 2.1f })
        {
            string level = y.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
            Face(geometry, "scaffold-ledger-inner-" + level, Trim, -59.85f, y - .1f, wall - .04f, -54.05f, y, wall + .04f);
            Face(geometry, "scaffold-ledger-outer-" + level, Trim, -59.85f, y - .1f, outer - .04f, -54.05f, y, outer + .04f);
            Face(geometry, "scaffold-deck-" + level, Wood, -59.85f, y, outer, -54.05f, y + .04f, wall);
            Face(geometry, "scaffold-rail-" + level, Trim, -59.85f, y + .85f, outer - .1f, -54.05f, y + .93f, outer - .05f);
        }
        // The tarps hang outside the rail of the middle level, facing the street.
        for (int i = 0; i < 2; i++)
            state.Copies.Add(Copy("scaffold-tarp-" + i, Tarp, TarpPivot, new Vector3(-58.35f + i * 2.8f, .25f, outer - .9f), 90));
    }

    // Carries the old room's joists on across the whole wing: same rows, same width, same height.
    // They end inside the walls at x -60 and x -56, and the ceiling boards close above them.
    private static void Joists(CopyDefinition geometry)
    {
        foreach (float z in new[] { 136.45f, 137.2f, 137.95f }) Joist(geometry, z, -60.002f);
        foreach (float z in new[] { 138.725f, 139.475f, 140.225f }) Joist(geometry, z, -56.002f);
        Joist(geometry, 140.975f, -60.002f);
        foreach (float z in new[] { 141.725f, 142.475f, 143.225f, 143.975f, 144.725f }) Joist(geometry, z, -60.002f);
    }

    private static void Joist(CopyDefinition geometry, float z, float start) =>
        Face(geometry, "joist-" + (int)(z * 10), Trim, start, JoistBottom, z - .05f, -53.97f, CeilingY, z + .05f);

    // What the breakers leave of a wall: toothed brick stumps at both ends, scraps of its foot between them.
    private static void BrokenWall(CopyDefinition geometry, string id, bool alongX, float low, float high, bool roomOnHigh, float start, float end, float startHeight, float endHeight)
    {
        float[] widths = { .09f, .07f, .1f, .06f, .08f, .11f, .07f, .09f, .06f, .1f };
        float length = widths.Sum();
        int seed = id.Sum(c => c);
        Stump(geometry, id + "-a", alongX, low, high, roomOnHigh, start, 1, widths, startHeight, seed);
        Stump(geometry, id + "-b", alongX, low, high, roomOnHigh, end, -1, widths, endHeight, seed + 3);
        float[] pieces = { .16f, .09f, .2f, .12f, .18f, .07f, .14f, .1f };
        float[] gaps = { .12f, .26f, .08f, .34f, .17f, .4f, .14f };
        float[] heights = { .07f, .03f, .1f, .05f, .08f, .02f, .06f };
        float cursor = start + length;
        for (int i = 0; cursor + .05f < end - length; i++)
        {
            float next = Math.Min(cursor + pieces[(i + seed) % pieces.Length], end - length);
            float top = FloorY + heights[(i + seed) % heights.Length], inset = i % 3 * .012f;
            if (alongX) Face(geometry, id + "-foot-" + i, Brick, cursor, FloorY, low + inset, next, top, high - inset);
            else Face(geometry, id + "-foot-" + i, Brick, low + inset, FloorY, cursor, high - inset, top, next);
            cursor = next + gaps[(i + seed) % gaps.Length];
        }
    }

    private static void Stump(CopyDefinition geometry, string id, bool alongX, float low, float high, bool roomOnHigh, float from, int direction, float[] widths, float height, int seed)
    {
        const float course = .075f;
        // Whole courses of brick fall away from the standing wall, each course a little longer or shorter than the last.
        float[] profile = { 1f, .9f, .94f, .72f, .78f, .55f, .6f, .36f, .22f, .1f };
        float[] wobble = { .03f, -.04f, .02f, .05f, -.03f, 0f, -.02f };
        // The plaster came off higher up than the brick behind it, and further the nearer it is to the tear.
        float[] tear = { .02f, .08f, .05f, .15f, .11f, .24f, .18f, .3f, .25f, .33f };
        float cursor = from, roomEdge = float.MaxValue, outerEdge = float.MaxValue;
        for (int i = 0; i < widths.Length; i++)
        {
            float next = cursor + direction * widths[i], a = Math.Min(cursor, next), b = Math.Max(cursor, next);
            float rise = Math.Max(course, Mathf.Round(height * (profile[i] + wobble[(i + seed) % wobble.Length]) / course) * course);
            float top = FloorY + rise;
            string part = id + "-" + i;
            // Only the courses a player could walk into block; the low ones are stepped over.
            if (rise > .4f) WallBox(geometry, part, Brick, alongX, low, high, a, b, FloorY, top);
            else if (alongX) Face(geometry, part, Brick, a, FloorY, low, b, top, high);
            else Face(geometry, part, Brick, low, FloorY, a, high, top, b);

            // The edge only falls towards the tear, so no brick shows as a slit inside standing plaster.
            float roomSkin = roomEdge = Math.Min(roomEdge, top - height * tear[i]);
            float outerSkin = outerEdge = Math.Min(outerEdge, top - height * tear[Math.Min(i + 1 + seed % 2, tear.Length - 1)]);
            string roomMaterial = roomOnHigh ? Wall : Exterior, outerMaterial = roomOnHigh ? Exterior : Wall;
            if (roomSkin > FloorY + .03f)
            {
                SkinFace(geometry, part + "-room", roomMaterial, alongX, high, high + .012f, a, b, roomSkin);
            }
            if (outerSkin > FloorY + .03f)
            {
                SkinFace(geometry, part + "-outer", outerMaterial, alongX, low - .012f, low, a, b, outerSkin);
            }
            // The room's wood panelling stays on its plaster.
            float panel = Math.Min(roomOnHigh ? roomSkin : outerSkin, WainscotTop);
            if (panel > FloorY + .03f)
            {
                if (roomOnHigh) SkinFace(geometry, part + "-panel", Trim, alongX, high + .012f, high + .024f, a, b, panel);
                else SkinFace(geometry, part + "-panel", Trim, alongX, low - .024f, low - .012f, a, b, panel);
            }
            cursor = next;
        }
    }

    private static void SkinFace(CopyDefinition geometry, string id, string material, bool alongX, float low, float high, float start, float end, float top)
    {
        if (alongX) Face(geometry, id, material, start, FloorY, low, end, top, high);
        else Face(geometry, id, material, low, FloorY, start, high, top, end);
    }

    // Heaps at the foot of the broken walls and a few stray bricks. By the second morning most of it is swept up.
    private static void Rubble(WorldStateDefinition state, int morning)
    {
        RubblePile(state, "rubble-main", -59.45f, 136.4f, morning == 1 ? .38f : .28f, 11);
        RubblePile(state, "rubble-bay-north", -58.7f, 140.65f, .28f, 53);
        if (morning != 1) return;
        RubblePile(state, "rubble-corner", -59.3f, 137.55f, .3f, 23);
        RubblePile(state, "rubble-bay-south", -56.3f, 138.45f, .42f, 37);
        RubblePile(state, "rubble-bay-east", -56.4f, 140.6f, .38f, 41);
        RubblePile(state, "rubble-stray-a", -57.5f, 138.6f, .14f, 61);
        RubblePile(state, "rubble-stray-b", -56.95f, 141.45f, .14f, 67);
        RubblePile(state, "rubble-stray-c", -55.35f, 139.35f, .14f, 71);
    }

    // A low mound of broken brick with some flat shards of plaster. A box cannot turn on its own, so each handful
    // is a copy with its own yaw around the middle of the heap.
    private static void RubblePile(WorldStateDefinition state, string id, float x, float z, float radius, int seed)
    {
        var random = new System.Random(seed);
        float Between(float from, float to) => from + (float)random.NextDouble() * (to - from);
        var centre = new Vector3(x, FloorY, z);
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
                float bottom = FloorY + Between(0f, (1f - distance / radius) * radius * .5f);
                bool shard = random.Next(4) == 0;
                string material = shard ? (random.Next(2) == 0 ? Wall : Exterior) : Brick;
                float halfLength = Between(.05f, .12f), halfWidth = Between(.03f, .06f), tall = shard ? .012f : Between(.05f, .085f);
                Face(handful, "piece-" + i, material, px - halfLength, bottom, pz - halfWidth, px + halfLength, bottom + tall, pz + halfWidth);
            }
            state.Copies.Add(handful);
        }
    }

    // The closing piece of a new wall where it meets an old one, in the room's finish. Its wainscot reaches
    // past the end so it covers the end of the new wall's wainscot.
    // Wallpaper above the panelling and panelling below it share one face. The top reaches past the old ceiling.
    private static void Return(CopyDefinition geometry, string id, float x1, float z1, float x2, float z2, float panelX2, float panelZ2)
    {
        Solid(geometry, id, Wall, x1, WainscotTop, z1, x2, 2.83f, z2);
        Solid(geometry, id + "-wainscot", Trim, x1, FloorY, z1, panelX2, WainscotTop, panelZ2);
    }

    // A working copy of the room's own roller blind: the same hover text, the same animation.
    private static CopyDefinition Blind(string id, Vector3 target, float yaw)
    {
        var blind = Copy(id, Blinds, BlindsPivot, target, yaw);
        blind.VisualOnly = false;
        return blind;
    }

    // Built like the old roof: a low sheet inside a raised rim. The ribs on it are not geometry but the shadow of an
    // invisible caster that lies around the sheet, so the wing gets the same caster and the pattern runs on across both
    // and changes with the sun like the old one. The rim between the two parts goes, the sheet stays.
    private static void Roof(WorldStateDefinition state, CopyDefinition geometry)
    {
        state.Rules.Add(new ObjectRule
        {
            Id = "old-roof-inner-rim", Name = "UpperRoof", Parent = "Chinese restaurant", Side = WorldSide.Main,
            Effect = ObjectEffect.Cut, Area = BoundsOf(-60.06f, RoofSheetY + .0005f, 136.1f, -55.74f, 3.04f, 141.26f)
        });
        // Like the old roof's colliders, these stand above the drawn sheet, level with the rim.
        Block(geometry, "roof-south-collision", -60.04f, 135.8f, -53.78f, 137.82f);
        Block(geometry, "roof-east-collision", -55.82f, 137.75f, -53.78f, 141.25f);
        Block(geometry, "roof-north-collision", -60.22f, 141.18f, -53.78f, 145.2f);

        Rim(geometry, "roof-rim-east", -54.08f, 135.8f, -53.78f, 145.2f);
        Rim(geometry, "roof-rim-south", -59.75f, 135.8f, -54.08f, 136.1f);
        Rim(geometry, "roof-rim-north", -60.22f, 144.9f, -54.08f, 145.2f);
        Rim(geometry, "roof-rim-west", -60.22f, 141.25f, -59.92f, 144.9f);
        Rim(geometry, "roof-rim-junction", -60.07f, 140.95f, -59.92f, 141.25f);
        // Closes the old rim where the cut ends it.
        Rim(geometry, "roof-rim-cap", -60.06f, 136.095f, -59.75f, 136.1f);

        Sheet(geometry, "roof-sheet-south", -60.09f, 136.1f, -54.08f, 138.08f);
        Sheet(geometry, "roof-sheet-east", -56.08f, 138.08f, -54.08f, 140.92f);
        Sheet(geometry, "roof-sheet-north", -59.92f, 140.92f, -54.08f, 144.9f);

        state.Copies.Add(Copy("roof-caster-south", RoofCaster, RoofCasterPivot, new Vector3(-57.02f, RoofCasterPivot.y, 138.55f), 90));
        state.Copies.Add(Copy("roof-caster-north", RoofCaster, RoofCasterPivot, new Vector3(-57.02f, RoofCasterPivot.y, 142.45f), 90));
    }

    private static void Rim(CopyDefinition copy, string id, float x1, float z1, float x2, float z2) =>
        Face(copy, id, RoofEdge, x1, RoofEdgeBottom, z1, x2, RoofRimTop, z2);

    // A hair above the old sheet and lapping onto it, so it hides what the cut leaves of the old rim.
    private static void Sheet(CopyDefinition copy, string id, float x1, float z1, float x2, float z2) =>
        Face(copy, id, RoofTop, x1, RoofSheetY - .02f, z1, x2, RoofSheetY + .001f, z2);

    private static void Block(CopyDefinition copy, string id, float x1, float z1, float x2, float z2) =>
        copy.Boxes.Add(new BoxDefinition { Id = id, Kind = BoxKind.Block, Box = BoundsOf(x1, RoofEdgeBottom, z1, x2, RoofCollisionTop, z2) });

    // Half-metre grid cells of the new floor, from wall to wall like the old room's.
    private static void AddPlacementTiles(PropertyExtensionDefinition property)
    {
        const float margin = .01f;
        var rooms = new[]
        {
            BoundsOf(-60f, FloorY, 136f, -54f, 3f, 138f),
            BoundsOf(-56f, FloorY, 138f, -54f, 3f, 141f),
            BoundsOf(-60f, FloorY, 141f, -54f, 3f, 145f),
            // The cells the sink stood on, which the game had left out of the grid.
            BoundsOf(-60f, FloorY, 140f, -59f, 3f, 141f)
        };
        bool Inside(float x, float z) => rooms.Any(r => x >= r.min.x - margin && x <= r.max.x + margin && z >= r.min.z - margin && z <= r.max.z + margin);
        for (int x = 10; x <= 21; x++)
            for (int y = 1; y <= 20; y++)
            {
                float wx = -64.75f + x * .5f, wz = 135.25f + y * .5f;
                // The sink's new place stays free.
                if (x is 10 or 11 && y is 18 or 19) continue;
                if (Inside(wx - .25f, wz - .25f) && Inside(wx + .25f, wz - .25f) && Inside(wx - .25f, wz + .25f) && Inside(wx + .25f, wz + .25f) && Inside(wx, wz))
                    property.Tiles.Add(new GridCoordinate(x, y));
            }
    }

    private static CopyDefinition Copy(string id, string template, Vector3 sourcePivot, Vector3 targetPivot, float yaw = 0, float scale = 1f) => new()
    {
        Id = id, Template = template, Frame = Property, Scale = scale,
        At = new LocalPoint(targetPivot - Quaternion.Euler(0, yaw, 0) * (scale * (sourcePivot - PropertyPivot)), yaw),
        IndependentMesh = true, VisualOnly = true, Obstacle = false
    };

    private static void Remove(WorldStateDefinition state, string id, string name, string parent, Bounds area) => state.Rules.Add(new ObjectRule
    {
        Id = id, Name = name, Parent = parent, Side = WorldSide.Main, Effect = ObjectEffect.Remove, Area = area
    });

    // The crew's lamps burn all the time; in the finished room the room's own switch runs them.
    private static void Lamp(WorldStateDefinition state, string id, float x, float z, bool onSwitch)
    {
        var light = Copy(id, Property + "/Chinese Restaurant/CeilingFan", new Vector3(-62.5f, 2.38f, 138.3f), new Vector3(x, 2.38f, z));
        light.LightsOn = true;
        // Walls do not stop this light at any quality level, and these lamps hang closer to the outer walls than the
        // vanilla fan does. Its 6 m reach would light the building's outside, so they reach less and burn brighter.
        light.LightRange = 4f;
        light.LightIntensity = 3.5f;
        if (onSwitch) light.LightSwitch = RoomSwitch;
        state.Copies.Add(light);
    }

    // The vanilla piece is 2.5 m long with a post at each end. A run that does not divide evenly ends in a piece
    // laid over the one before it, a few millimetres aside so the two rails do not flicker. Runs that meet at a
    // corner leave the shared post to one of them.
    private static void FenceRun(WorldStateDefinition state, CopyDefinition geometry, string id, bool alongX, float cross, float start, float end, bool startPost = true, bool endPost = true)
    {
        const float piece = 2.5f;
        int full = (int)((end - start + .001f) / piece);
        float rest = end - start - full * piece;
        bool overlaps = rest > .05f;
        int count = overlaps ? full + 1 : full;
        for (int i = 0; i < count; i++)
        {
            bool last = i == count - 1;
            float center = (last ? end - piece : start + i * piece) + piece * .5f;
            float side = cross + (last && overlaps ? .004f : 0f);
            var target = alongX ? new Vector3(center, .45f, side) : new Vector3(side, .45f, center);
            var rail = Copy(id + "-guardrail-" + i, Fence, FencePivot, target, alongX ? 90 : 0);
            if (last && overlaps || i == 0 && !startPost) rail.Exclude.Add("FencePost");
            // A short rest needs no post of its own between the last two ends.
            if (last ? !endPost : !(overlaps && i == count - 2 && rest >= 1f)) rail.Exclude.Add("FencePost.004");
            state.Copies.Add(rail);
        }
        // The guardrails are visual copies without colliders, and the open floor edge lies behind them.
        Guard(geometry, id + "-edge-guard", alongX, cross, start, end, FloorY);
    }

    // An invisible wall up to head height, so a jump does not carry the player over a rail.
    private static void Guard(CopyDefinition copy, string id, bool alongX, float cross, float start, float end, float bottom) =>
        copy.Boxes.Add(new BoxDefinition
        {
            Id = id, Kind = BoxKind.Block,
            Box = alongX ? BoundsOf(start, bottom, cross - .07f, end, GuardTop, cross + .07f) : BoundsOf(cross - .07f, bottom, start, cross + .07f, GuardTop, end)
        });

    // The game's own folding table. The copy is visual only, so a block from the floor to the top stands in for its collider.
    private static void SiteTable(WorldStateDefinition state, CopyDefinition geometry, string id, float x, float z, float yaw = 0)
    {
        state.Copies.Add(Copy(id, FoldingTable, FoldingTablePivot, new Vector3(x, FloorY, z), yaw, TableScale));
        float across = 1.26f * TableScale, along = .61f * TableScale;
        float halfX = yaw == 0 ? across : along, halfZ = yaw == 0 ? along : across;
        geometry.Boxes.Add(new BoxDefinition
        {
            Id = id + "-block", Kind = BoxKind.Block,
            Box = BoundsOf(x - halfX, FloorY, z - halfZ, x + halfX, TableTop, z + halfZ)
        });
    }

    private static void FrameWall(CopyDefinition geometry, string id, bool alongX, float cross, float start, float end, float[] windows)
    {
        Run(geometry, id + "-bottom-plate", Trim, alongX, cross, start, end, FloorY, -.15f, .06f);
        Run(geometry, id + "-top-plate", Trim, alongX, cross, start, end, JoistBottom - .15f, JoistBottom, .06f);
        var studs = new SortedSet<float> { start, end };
        for (float p = start + 1f; p < end; p += 1f) studs.Add(p);
        foreach (float window in windows) { studs.Add(window - .8f); studs.Add(window + .8f); }
        int n = 0;
        foreach (float p in studs)
        {
            bool inOpening = windows.Any(w => Math.Abs(p - w) < .75f);
            if (!inOpening) Stud(geometry, id + "-stud-" + n, alongX, cross, p, FloorY, JoistBottom);
            else
            {
                Stud(geometry, id + "-sill-stud-" + n, alongX, cross, p, FloorY, .91f);
                Stud(geometry, id + "-lintel-stud-" + n, alongX, cross, p, 2.01f, JoistBottom);
            }
            n++;
        }
        foreach (float window in windows)
        {
            Run(geometry, id + "-window-" + window + "-sill", Trim, alongX, cross, window - .8f, window + .8f, .86f, .9625f, .07f);
            Run(geometry, id + "-window-" + window + "-header", Trim, alongX, cross, window - .8f, window + .8f, 1.9625f, 2.12f, .07f);
        }
    }

    // The outer wall runs from start to end, its room finish from finishStart to finishEnd.
    private static void Shell(CopyDefinition geometry, string id, bool alongX, float low, float high, float start, float end, float[] windows, bool finished, float finishStart, float finishEnd)
    {
        var segments = new List<(float Start, float End, bool Window)>();
        float cursor = start;
        foreach (float window in windows.OrderBy(x => x))
        {
            segments.Add((cursor, window - .74f, false));
            segments.Add((window - .74f, window + .74f, true));
            cursor = window + .74f;
        }
        segments.Add((cursor, end, false));
        if (id == "east")
        {
            var split = new List<(float Start, float End, bool Window)>();
            foreach (var segment in segments)
                if (!segment.Window && segment.Start < 139.5f && segment.End > 141.5f)
                {
                    split.Add((segment.Start, 139.5f, false));
                    split.Add((139.5f, 141.5f, false));
                    split.Add((141.5f, segment.End, false));
                }
                else split.Add(segment);
            segments = split;
        }
        int index = 0;
        foreach (var segment in segments)
        {
            string prefix = id + "-shell-" + index++;
            if (segment.Window)
            {
                WallBox(geometry, prefix + "-below", Exterior, alongX, low, high, segment.Start, segment.End, WallBase, .9725f);
                WallBox(geometry, prefix + "-above", Exterior, alongX, low, high, segment.Start, segment.End, 1.9525f, RoofEdgeBottom);
            }
            else WallBox(geometry, prefix, Exterior, alongX, low, high, segment.Start, segment.End, WallBase, RoofEdgeBottom);
            if (!finished) continue;
            bool principal = id == "east" ? !segment.Window && segment.Start < 140.5f && segment.End > 140.5f
                : id == "west-north" || index == 1;
            float innerLow = id == "east" ? low - .06f : id == "west-north" ? high : id == "north" ? low - .04f : high;
            float innerHigh = innerLow + (id == "east" ? .06f : .04f);
            string finishId = principal ? id + "-finish" : prefix + "-finish";
            float from = index == 1 ? finishStart : Math.Max(segment.Start, finishStart), to = Math.Min(segment.End, finishEnd);
            if (segment.Window)
            {
                WallBox(geometry, finishId + "-below", Wall, alongX, innerLow, innerHigh, from, to, FloorY, .9725f);
                WallBox(geometry, finishId + "-above", Wall, alongX, innerLow, innerHigh, from, to, 1.9525f, CeilingY);
            }
            else WallBox(geometry, finishId, Wall, alongX, innerLow, innerHigh, from, to, FloorY, CeilingY);
            // Without a collider, so wall items still find the finish behind it.
            if (alongX) Face(geometry, prefix + "-wainscot", Trim, from, FloorY, innerLow - .015f, to, WainscotTop, innerHigh + .015f);
            else Face(geometry, prefix + "-wainscot", Trim, innerLow - .015f, FloorY, from, innerHigh + .015f, WainscotTop, to);
        }
    }

    private static void Stud(CopyDefinition copy, string id, bool alongX, float cross, float at, float bottom, float top) =>
        WallBox(copy, id, Trim, alongX, cross - .06f, cross + .06f, at - .05f, at + .05f, bottom, top);

    private static void Run(CopyDefinition copy, string id, string material, bool alongX, float cross, float start, float end, float bottom, float top, float halfDepth) =>
        WallBox(copy, id, material, alongX, cross - halfDepth, cross + halfDepth, start, end, bottom, top);

    private static void WallBox(CopyDefinition copy, string id, string material, bool alongX, float low, float high, float start, float end, float bottom, float top)
    {
        if (alongX) Solid(copy, id, material, start, bottom, low, end, top, high);
        else Solid(copy, id, material, low, bottom, start, high, top, end);
    }

    private static void Post(CopyDefinition copy, string id, float x, float z, float top) =>
        Solid(copy, id, Trim, x - .06f, FloorY, z - .06f, x + .06f, top, z + .06f);

    private static void AddSurface(PropertyExtensionDefinition property, string id, string box, PropertySurfaceKind kind, PropertySurfaceFace face)
    {
        var surface = new PropertySurfaceDefinition { Id = id, CopyId = "fitted-wing", BoxId = box, SurfaceType = kind };
        surface.ValidFaces.Clear();
        surface.ValidFaces.Add(face);
        property.Surfaces.Add(surface);
    }

    private static void Solid(CopyDefinition copy, string id, string material, float x1, float y1, float z1, float x2, float y2, float z2)
    {
        Face(copy, id, material, x1, y1, z1, x2, y2, z2);
        copy.Boxes.Add(new BoxDefinition { Id = id + "-collision", Kind = BoxKind.Block, Box = BoundsOf(x1, y1, z1, x2, y2, z2) });
    }

    // A visible box without a collider, for parts nobody can walk into or that an owner already blocks.
    private static void Face(CopyDefinition copy, string id, string material, float x1, float y1, float z1, float x2, float y2, float z2) =>
        copy.Boxes.Add(new BoxDefinition { Id = id, Kind = BoxKind.Panel, Material = material, TileMetres = TileOf(material), Box = BoundsOf(x1, y1, z1, x2, y2, z2) });

    // Metres per texture repeat, matched to how each material looks on the old room's own walls and floor.
    private static float TileOf(string material) => material switch
    {
        Wall => 1.2f,
        Wood => 2f,
        Trim => 1f,
        Exterior or RoofEdge or RoofTop => 2.5f,
        _ => 0f
    };

    private static Bounds BoundsOf(float x1, float y1, float z1, float x2, float y2, float z2)
    {
        var bounds = new Bounds();
        bounds.SetMinMax(new Vector3(x1, y1, z1), new Vector3(x2, y2, z2));
        return bounds;
    }
}
