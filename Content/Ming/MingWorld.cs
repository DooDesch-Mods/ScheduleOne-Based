using MapExpansions;
using UnityEngine;

namespace Based.Content.Ming;

public static class MingWorld
{
    public const string RoomPath = "@Properties/Sweatshop/Chinese Restaurant/Chinese restaurant/UpperRoom";
    public const float FloorY = -0.25f;
    public static readonly Vector3 OpeningCenter = new(-59.925f, 1.175f, 136.925f);
    public static readonly Bounds Opening = BoundsOf(-60.04f, -0.249f, 135.9f, -55.78f, 2.4f, 141.17f);
    public static readonly Bounds Wing = BoundsOf(-60.04f, -0.45f, 135.9f, -54f, 3.2f, 145.1f);
    public static Vector3 ConstructionVanPosition { get; set; } = new(-66.55f, -3.45f, 137f);
    public static float ConstructionVanYaw { get; set; }
    private const string Property = "@Properties/Sweatshop";
    private const string Anchor = Property + "/InteriorSpawnPoint";
    private const string Window = Property + "/Chinese Restaurant/Window (3)";
    private const string Fence = "Map/Hyland Point/Region_Docks/Dark Market Area/Docks Warehouse/InteriorDisable/Mezzanine/docks mezzanine/FenceBars";
    private const string Pallet = "Map/Hyland Point/Region_Northtown/North Waterfront/Pallet Prop (2)";
    private const string Wood = "wood planks medium brown mat";
    private const string Trim = "wood brown";
    private const string Wall = "wallpaper light green";
    private const string Exterior = "concrete bar green";
    private const string RoofFinish = "concrete bar green darker";
    private static readonly Vector3 PropertyPivot = new(-62.5f, -.25f, 138f);
    private static readonly Vector3 WindowPivot = new(-55.925f, 1.462493f, 139.5f);
    private static readonly Vector3 FencePivot = new(-39.8f, 2.45f, 34.812f);
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
        definition.WorldStates.Add(new WorldStateDefinition { Id = "dormant" });
        definition.WorldStates.Add(BuildState("breakthrough", 1));
        definition.WorldStates.Add(BuildState("frame", 2));
        definition.WorldStates.Add(BuildState("roof", 3));
        definition.WorldStates.Add(BuildState("complete", 4));
        definition.Locations.Add(new LocationDefinition("foreman-plan", new Vector3(-57.6f, FloorY, 143.3f), 180));
        definition.Locations.Add(new LocationDefinition("south-bench", new Vector3(-57.8f, FloorY, 137.1f), 180));
        definition.Locations.Add(new LocationDefinition("north-frame", new Vector3(-56f, FloorY, 143.85f)));
        definition.Locations.Add(new LocationDefinition("east-fitout", new Vector3(-55.3f, FloorY, 140.1f), 90));
        return definition;
    }

    private static WorldStateDefinition BuildState(string id, int morning)
    {
        var state = new WorldStateDefinition
        {
            Id = id, NavigationBounds = BoundsOf(-65.5f, -1f, 134.5f, -53.5f, 3.5f, 145.5f)
        };
        state.NavigationAgents.AddRange(new[] { "Humanoid", "Ignore Costs", "Employee" });
        // One cut is shared by all four phases. It opens the former external walls while keeping
        // the original floors and roof above them; the main room and its former bay become one space.
        state.Rules.Add(new ObjectRule
        {
            Id = "upstairs-opening", Name = "UpperRoom", Parent = "Chinese restaurant",
            Side = WorldSide.Main, Effect = ObjectEffect.Cut, Area = Opening
        });
        Remove(state, "old-internal-window", "Window (3)", "Chinese Restaurant", BoundsOf(-56.05f, .8f, 138.65f, -55.8f, 2.2f, 140.35f));
        Remove(state, "terrace-pallet-a", "Pallet Prop (2)", "North Waterfront", BoundsOf(-58.8f, -.4f, 142.3f, -57.1f, .1f, 144f));
        Remove(state, "terrace-pallet-b", "Pallet Prop (3)", "North Waterfront", BoundsOf(-57.65f, -.3f, 142.8f, -55.9f, .3f, 144.5f));
        Remove(state, "old-upper-drain-a", "Water_Pipe_Long (3)", "Chinese Restaurant", BoundsOf(-58.08f, 1.2f, 141.4f, -57.82f, 2.3f, 141.65f));
        Remove(state, "old-upper-drain-b", "Water_Pipe_Long (4)", "Chinese Restaurant", BoundsOf(-58.08f, .25f, 141.4f, -57.82f, 1.3f, 141.65f));
        Remove(state, "old-drain-through-floor", "Water_Pipe_Long (1)", "Chinese Restaurant", BoundsOf(-58.08f, -.3f, 141.4f, -57.82f, .3f, 141.65f));
        Remove(state, "old-upper-drain-elbow", "Water_Pipe_right", "Chinese Restaurant", BoundsOf(-58.08f, 2.2f, 140.95f, -57.82f, 2.9f, 141.65f));
        if (morning >= 3)
            state.Copies.Add(Copy("relocated-room-ac", Property + "/Chinese Restaurant/SM_AC/SM_AC",
                new Vector3(-59.84402f, 2.253f, 136.34501f), new Vector3(-54.04402f, 2.253f, 140.5f)));
        Remove(state, "old-room-ac", "SM_AC", "Chinese Restaurant", BoundsOf(-59.95f, 1.7f, 135.8f, -59.4f, 2.7f, 136.9f));

        var geometry = new CopyDefinition
        {
            Id = "fitted-wing", Template = Anchor, Frame = Anchor,
            At = new LocalPoint(-63.949f, FloorY, 140.19f), OwnMesh = false, Obstacle = false
        };
        Solid(geometry, "floor-south", Wood, -60f, -.45f, 135.9f, -54f, FloorY, 137.9f);
        Solid(geometry, "floor-east", Wood, -56f, -.45f, 137.9f, -54f, FloorY, 141.1f);
        Solid(geometry, "floor-north", Wood, -60f, -.45f, 141.1f, -54f, FloorY, 145.1f);
        if (morning == 4)
        {
            geometry.Panel(new Vector3(-65f, -.248f, 135f), new Vector3(-60f, -.247f, 141f), Wood, "existing-main-floor-finish");
            geometry.Panel(new Vector3(-60f, -.248f, 138f), new Vector3(-56f, -.247f, 141f), Wood, "existing-bay-floor-finish");
        }

        // Headers meet the retained old roof. Uprights have real floor contact instead of floating beams.
        Solid(geometry, "old-bay-east-header", Trim, -56.02f, 2.38f, 137.8f, -55.78f, 2.65f, 141.18f);
        Solid(geometry, "old-bay-south-header", Trim, -60.04f, 2.38f, 137.8f, -55.78f, 2.65f, 138.05f);
        Solid(geometry, "old-bay-north-header", Trim, -60.04f, 2.38f, 140.95f, -55.78f, 2.65f, 141.18f);
        Solid(geometry, "old-main-east-header", Trim, -60.05f, 2.38f, 135.9f, -59.78f, 2.65f, 138.05f);
        Post(geometry, "junction-north", -59.94f, 141.07f, 2.65f);
        Post(geometry, "bay-northeast", -55.9f, 141.07f, 2.65f);
        Post(geometry, "bay-southeast", -55.9f, 137.93f, 2.65f);

        if (morning < 3)
        {
            FenceRun(state, geometry, "south", true, 136.0f, -59.85f, -54.15f);
            FenceRun(state, geometry, "east", false, -54.1f, 136.0f, 145.0f);
            FenceRun(state, geometry, "north", true, 145.0f, -59.85f, -54.15f);
            FenceRun(state, geometry, "west-north", false, -59.9f, 141.18f, 145.0f);
        }
        if (morning < 4)
        {
            Workbench(geometry, "bench", -58.4f, 136f, -57.2f, 136.5f);
            Workbench(geometry, "plan-table", -58.2f, 142.1f, -57.2f, 142.7f);
            geometry.Panel(new Vector3(-58.05f, .651f, 142.2f), new Vector3(-57.35f, .653f, 142.6f), "clipboard_paper_mat", "plan-paper");
            // The perimeter of the old usable bay marks the work zone; every temporary rail has posts.
            TimberRail(geometry, "work-zone-south", true, 137.93f, -59.8f, -56.15f);
            TimberRail(geometry, "work-zone-east", false, -55.95f, 138.08f, 140.92f);
            TimberRail(geometry, "work-zone-north", true, 141.07f, -59.8f, -56.15f);
            TimberRail(geometry, "opening-barrier", false, -59.9f, 136.1f, 137.7f);
            if (morning >= 2)
            {
                Workbench(geometry, "north-bench", -56.6f, 144.45f, -55.4f, 144.9f);
                Workbench(geometry, "east-bench", -54.7f, 139.6f, -54.2f, 140.6f);
            }
            // A single deliberate staging pallet replaces the two pre-existing terrace props.
            state.Copies.Add(Copy("staging-pallet", Pallet, new Vector3(-57.9526f, -.348f, 143.1308f), new Vector3(-58.8f, FloorY, 144f)));
            for (int i = 0; i < (morning == 1 ? 5 : morning == 2 ? 3 : 1); i++)
                Solid(geometry, "stock-board-" + i, Wood, -59.45f, -.05f + i * .045f, 143.6f, -58.2f, -.012f + i * .045f, 143.78f);
            state.Copies.Add(new CopyDefinition
            {
                Id = "construction-van", Resource = "vehicles/van/Van", VisualOnly = true,
                At = new LocalPoint(ConstructionVanPosition, ConstructionVanYaw), Obstacle = true
            });
            if (morning == 1)
                state.Copies.Add(new CopyDefinition
                {
                    Id = "breakthrough-rubble", Template = "@Properties/Manor/Destroyed/mansion combined destroyed/Rubble",
                    At = new LocalPoint(-58.5f, -.09f, 137.3f), Scale = .05f, IndependentMesh = true, Obstacle = false
                });
        }

        if (morning >= 2)
        {
            FrameWall(geometry, "east", false, -54.08f, 136.02f, 144.98f, new[] { 137.3f, 143f });
            FrameWall(geometry, "south", true, 135.98f, -59.88f, -54.08f, new[] { -57f });
            FrameWall(geometry, "north", true, 145.02f, -59.88f, -54.08f, new[] { -57f });
            FrameWall(geometry, "west-north", false, -59.95f, 141.18f, 145.02f, Array.Empty<float>());
        }
        if (morning >= 3)
        {
            // New roof strips overlap the retained roof edges, with matching underside and fascia heights.
            Solid(geometry, "roof-south", Exterior, -60.04f, 2.6f, 135.8f, -53.9f, 3.02f, 137.82f);
            Solid(geometry, "roof-east", Exterior, -55.82f, 2.6f, 137.75f, -53.9f, 3.02f, 141.25f);
            Solid(geometry, "roof-north", Exterior, -60.05f, 2.6f, 141.18f, -53.9f, 3.02f, 145.2f);
            Solid(geometry, "roof-finish-main", RoofFinish, -65.25f, 3.06f, 134.75f, -60.05f, 3.12f, 141.25f);
            Solid(geometry, "roof-finish-step", RoofFinish, -60.05f, 3.06f, 134.75f, -59.75f, 3.12f, 135.8f);
            Solid(geometry, "roof-finish-wing", RoofFinish, -60.05f, 3.06f, 135.8f, -53.9f, 3.12f, 145.2f);
            Shell(geometry, "east", false, -54.1f, -54f, 136f, 145f, new[] { 137.3f, 143f }, morning == 4);
            Shell(geometry, "south", true, 135.9f, 136.06f, -60.04f, -54.16f, new[] { -57f }, morning == 4);
            Shell(geometry, "north", true, 144.94f, 145.1f, -60.04f, -54.16f, new[] { -57f }, morning == 4);
            Shell(geometry, "west-north", false, -60.02f, -59.84f, 141.15f, 145.1f, Array.Empty<float>(), morning == 4);
            state.Copies.Add(Copy("east-window-south", Window, WindowPivot, new Vector3(-54.08f, 1.462493f, 137.3f)));
            state.Copies.Add(Copy("east-window-north", Window, WindowPivot, new Vector3(-54.08f, 1.462493f, 143f)));
            state.Copies.Add(Copy("south-window", Window, WindowPivot, new Vector3(-57f, 1.462493f, 135.98f), 90));
            state.Copies.Add(Copy("north-window", Window, WindowPivot, new Vector3(-57f, 1.462493f, 145.02f), -90));
            Lamp(state, "south-ceiling-light", -57.5f, 136.9f);
            Lamp(state, "north-ceiling-light", -57f, 143.2f);
        }
        if (morning == 4)
        {
            // Keep the original surface IDs, including the east pier centred at the saved clock's position.
            var property = new PropertyExtensionDefinition { Id = "ming-wing-floor", PropertyCode = "sweatshop", GridIndex = 0 };
            AddSurface(property, "east-wall", "east-finish-collision", PropertySurfaceKind.Wall, PropertySurfaceFace.Left);
            AddSurface(property, "north-wall", "north-finish-collision", PropertySurfaceKind.Wall, PropertySurfaceFace.Back);
            AddSurface(property, "south-wall", "south-finish-collision", PropertySurfaceKind.Wall, PropertySurfaceFace.Front);
            AddSurface(property, "west-north-wall", "west-north-finish-collision", PropertySurfaceKind.Wall, PropertySurfaceFace.Right);
            AddSurface(property, "ceiling-south", "roof-south-collision", PropertySurfaceKind.Roof, PropertySurfaceFace.Bottom);
            AddSurface(property, "ceiling-east", "roof-east-collision", PropertySurfaceKind.Roof, PropertySurfaceFace.Bottom);
            AddSurface(property, "ceiling-north", "roof-north-collision", PropertySurfaceKind.Roof, PropertySurfaceFace.Bottom);
            foreach (var wall in new[]
                {
                    (Id: "east", Face: PropertySurfaceFace.Left), (Id: "north", Face: PropertySurfaceFace.Back),
                    (Id: "south", Face: PropertySurfaceFace.Front), (Id: "west-north", Face: PropertySurfaceFace.Right)
                })
                foreach (var box in geometry.Boxes.Where(b => b.Kind == BoxKind.Block && b.Id.StartsWith(wall.Id + "-", StringComparison.Ordinal)
                    && b.Id.Contains("-finish", StringComparison.Ordinal) && b.Id.EndsWith("-collision", StringComparison.Ordinal)
                    && b.Id != wall.Id + "-finish-collision"))
                    AddSurface(property, wall.Id + "-wall-" + box.Id, box.Id, PropertySurfaceKind.Wall, wall.Face);
            for (int x = 10; x <= 20; x++)
                for (int y = 2; y <= 19; y++)
                {
                    float wx = -64.75f + x * .5f, wz = 135.25f + y * .5f;
                    if (wz < 137.9f || wx >= -56f && wz <= 141.1f || wz > 141.1f)
                        property.Tiles.Add(new GridCoordinate(x, y));
                }
            property.Bounds.Add(Wing);
            state.PropertyExtensions.Add(property);
        }
        if (morning == 4)
        {
            state.NavigationProbes.Add(new Vector3(-60.7f, FloorY, 136.7f));
            state.NavigationProbes.Add(new Vector3(-59.1f, FloorY, 136.7f));
            state.NavigationProbes.Add(new Vector3(-58f, FloorY, 139.5f));
        }
        state.NavigationProbes.Add(new Vector3(-55.2f, FloorY, 136.8f));
        state.NavigationProbes.Add(new Vector3(-55.2f, FloorY, 138.5f));
        state.NavigationProbes.Add(new Vector3(-56.5f, FloorY, 143f));
        if (morning < 4)
        {
            state.NavigationProbes.Add(new Vector3(-57.8f, FloorY, 137.1f));
            state.NavigationProbes.Add(new Vector3(-55.3f, FloorY, 140.1f));
            state.NavigationProbes.Add(new Vector3(-57.6f, FloorY, 143.3f));
            if (morning >= 2) state.NavigationProbes.Add(new Vector3(-56f, FloorY, 143.85f));
        }
        state.Copies.Add(geometry);
        return state;
    }

    private static CopyDefinition Copy(string id, string template, Vector3 sourcePivot, Vector3 targetPivot, float yaw = 0) => new()
    {
        Id = id, Template = template, Frame = Property,
        At = new LocalPoint(targetPivot - Quaternion.Euler(0, yaw, 0) * (sourcePivot - PropertyPivot), yaw),
        IndependentMesh = true, VisualOnly = true, Obstacle = false
    };

    private static void Remove(WorldStateDefinition state, string id, string name, string parent, Bounds area) => state.Rules.Add(new ObjectRule
    {
        Id = id, Name = name, Parent = parent, Side = WorldSide.Main, Effect = ObjectEffect.Remove, Area = area
    });

    private static void Lamp(WorldStateDefinition state, string id, float x, float z)
    {
        var light = Copy(id, Property + "/Chinese Restaurant/CeilingFan", new Vector3(-62.5f, 2.38f, 138.3f), new Vector3(x, 2.38f, z));
        light.LightsOn = true;
        state.Copies.Add(light);
    }

    private static void FenceRun(WorldStateDefinition state, CopyDefinition geometry, string id, bool alongX, float cross, float start, float end)
    {
        float cursor = start;
        int index = 0;
        while (end - cursor >= 2.5f - .001f)
        {
            float center = cursor + 1.25f;
            var target = alongX ? new Vector3(center, .45f, cross) : new Vector3(cross, .45f, center);
            state.Copies.Add(Copy(id + "-guardrail-" + index++, Fence, FencePivot, target, alongX ? 90 : 0));
            cursor += 2.5f;
        }
        if (end - cursor > .05f) TimberRail(geometry, id + "-guardrail-end", alongX, cross, cursor, end);
    }

    private static void TimberRail(CopyDefinition geometry, string id, bool alongX, float cross, float start, float end)
    {
        int spans = Math.Max(1, (int)Math.Ceiling((end - start) / 1.25f));
        for (int i = 0; i <= spans; i++)
        {
            float position = Mathf.Lerp(start, end, (float)i / spans);
            Post(geometry, id + "-post-" + i, alongX ? position : cross, alongX ? cross : position, .89f);
        }
        Run(geometry, id + "-toe", Trim, alongX, cross, start, end, FloorY, -.1f, .07f);
        Run(geometry, id + "-mid", Trim, alongX, cross, start, end, .25f, .35f, .07f);
        Run(geometry, id + "-top", Trim, alongX, cross, start, end, .79f, .89f, .07f);
    }

    private static void Workbench(CopyDefinition geometry, string id, float x0, float z0, float x1, float z1)
    {
        Solid(geometry, id, Wood, x0, .55f, z0, x1, .65f, z1);
        for (int i = 0; i < 4; i++)
        {
            float x = i % 2 == 0 ? x0 + .04f : x1 - .11f;
            float z = i / 2 == 0 ? z0 + .04f : z1 - .11f;
            Solid(geometry, id + "-leg-" + i, Trim, x, FloorY, z, x + .07f, .55f, z + .07f);
        }
        Solid(geometry, id + "-brace", Trim, x0 + .04f, -.05f, (z0 + z1) * .5f - .035f, x1 - .04f, .02f, (z0 + z1) * .5f + .035f);
    }

    private static void FrameWall(CopyDefinition geometry, string id, bool alongX, float cross, float start, float end, float[] windows)
    {
        Run(geometry, id + "-bottom-plate", Trim, alongX, cross, start, end, FloorY, -.15f, .06f);
        Run(geometry, id + "-top-plate", Trim, alongX, cross, start, end, 2.5f, 2.65f, .06f);
        var studs = new SortedSet<float> { start, end };
        for (float p = start + 1f; p < end; p += 1f) studs.Add(p);
        foreach (float window in windows) { studs.Add(window - .8f); studs.Add(window + .8f); }
        int n = 0;
        foreach (float p in studs)
        {
            bool inOpening = windows.Any(w => Math.Abs(p - w) < .75f);
            if (!inOpening) Stud(geometry, id + "-stud-" + n, alongX, cross, p, FloorY, 2.65f);
            else
            {
                Stud(geometry, id + "-sill-stud-" + n, alongX, cross, p, FloorY, .91f);
                Stud(geometry, id + "-lintel-stud-" + n, alongX, cross, p, 2.01f, 2.65f);
            }
            n++;
        }
        foreach (float window in windows)
        {
            Run(geometry, id + "-window-" + window + "-sill", Trim, alongX, cross, window - .8f, window + .8f, .86f, .9625f, .07f);
            Run(geometry, id + "-window-" + window + "-header", Trim, alongX, cross, window - .8f, window + .8f, 1.9625f, 2.12f, .07f);
        }
    }

    private static void Shell(CopyDefinition geometry, string id, bool alongX, float low, float high, float start, float end, float[] windows, bool finished)
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
                WallBox(geometry, prefix + "-below", Exterior, alongX, low, high, segment.Start, segment.End, FloorY, .9725f);
                WallBox(geometry, prefix + "-above", Exterior, alongX, low, high, segment.Start, segment.End, 1.9525f, 2.8f);
            }
            else WallBox(geometry, prefix, Exterior, alongX, low, high, segment.Start, segment.End, FloorY, 2.8f);
            if (!finished) continue;
            bool principal = id == "east" ? !segment.Window && segment.Start < 140.5f && segment.End > 140.5f
                : id == "west-north" || index == 1;
            float innerLow = id == "east" ? low - .06f : id == "west-north" ? high : id == "north" ? low - .04f : high;
            float innerHigh = innerLow + (id == "east" ? .06f : .04f);
            string finishId = principal ? id + "-finish" : prefix + "-finish";
            if (segment.Window)
            {
                WallBox(geometry, finishId + "-below", Wall, alongX, innerLow, innerHigh, segment.Start, segment.End, FloorY, .9725f);
                WallBox(geometry, finishId + "-above", Wall, alongX, innerLow, innerHigh, segment.Start, segment.End, 1.9525f, 2.8f);
            }
            else WallBox(geometry, finishId, Wall, alongX, innerLow, innerHigh, segment.Start, segment.End, FloorY, 2.8f);
            WallBox(geometry, prefix + "-skirting", Trim, alongX, innerLow - .015f, innerHigh + .015f, segment.Start, segment.End, FloorY, -.1f);
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
        var bounds = BoundsOf(x1, y1, z1, x2, y2, z2);
        copy.Boxes.Add(new BoxDefinition { Id = id, Kind = BoxKind.Panel, Material = material, Box = bounds });
        copy.Boxes.Add(new BoxDefinition { Id = id + "-collision", Kind = BoxKind.Block, Box = bounds });
    }

    private static Bounds BoundsOf(float x1, float y1, float z1, float x2, float y2, float z2)
    {
        var bounds = new Bounds();
        bounds.SetMinMax(new Vector3(x1, y1, z1), new Vector3(x2, y2, z2));
        return bounds;
    }
}
