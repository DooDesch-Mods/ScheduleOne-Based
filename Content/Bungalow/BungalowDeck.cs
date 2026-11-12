using MapExpansions;
using UnityEngine;

namespace Based.Content.Bungalow;

// The deck west of the garden, where the ground falls away to the sea: boards of the Slums Waterfront on stilts, a
// FenceBars railing on the three open sides and a step down to the gravel. The first night sets the stilts and beams,
// the second lays the boards. The deck lies at the height of the house floor, so the room's grid continues onto it.
public static class BungalowDeck
{
    public const string ExtensionId = "bungalow-deck";
    public const string StiltsState = "stilts";

    private const string Property = "@Properties/Bungalow";
    private const string Anchor = Property + "/bungalow/PorchBeam";
    private const string Fence = "Map/Hyland Point/Region_Docks/Dark Market Area/Docks Warehouse/InteriorDisable/Mezzanine/docks mezzanine/FenceBars";
    private static readonly Vector3 PropertyPivot = new(-170f, -4f, 115.5f);
    private const float PropertyYaw = 90f;
    // The rail pivot sits 0.7 m above the floor the vanilla piece stands on.
    private static readonly Vector3 FencePivot = new(-39.8f, 2.45f, 34.812f);
    private const float FenceAboveFloor = .7f;

    // The Waterfront boardwalk's own material.
    private const string Boards = "wood planks medium brown mat";
    private const string Timber = "wood brown";

    // The west room's grid lies at -3.70 and is turned with the property: tile (x, y) has its centre at
    // (-175.75 + 0.5 y, 115.10 - 0.5 x). The deck's edges lie on the tile edges, 12 tiles across and 20 along.
    private const int GridIndex = 1;
    private const float Top = -3.70f;
    private const float West = -194f, East = -188f, South = 107.85f, North = 117.85f;
    private const int TileXMin = -5, TileXMax = 14, TileYMin = -36, TileYMax = -25;
    private const float BoardThickness = .05f, BeamDepth = .2f;
    private static readonly float[] BeamX = { -193.8f, -191f, -188.2f };
    private static readonly float[] PostZ = { 108.05f, 110.45f, 112.85f, 115.25f, 117.65f };
    // The lowest ground under each beam, measured in the game; each post reaches 0.2 m into it.
    private static readonly float[] PostBottom = { -6.95f, -5.5f, -4.6f };
    // The step to the gravel runs where the ground lies at -4.0 to -4.15; beside it a fascia closes the deck's side.
    private const float StepOut = .4f, StepSouth = 110f, StepNorth = 116f, StepY = -3.85f, FasciaBottom = -4.6f;
    private const float GuardHeight = 2f;
    private const float RockX = -191.68f, RockZ = 117.07f;
    private static readonly Vector2 Tree = new(-191.03f, 112.67f);

    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        if (!MapExpansionsApi.Register(Create()))
            throw new InvalidOperationException("Based could not register the Bungalow deck; inspect the MapExpansions validation errors.");
        _registered = true;
    }

    public static ExpansionDefinition Create()
    {
        var definition = new ExpansionDefinition
        {
            Id = BungalowDefinition.DeckExpansionId, DisplayName = "Bungalow slope deck", Version = "1.0.0", Source = null
        };
        definition.WorldStates.Add(BuildState(StiltsState, false));
        definition.WorldStates.Add(BuildState("complete", true));
        return definition;
    }

    private static WorldStateDefinition BuildState(string id, bool finished)
    {
        var state = new WorldStateDefinition { Id = id };
        // The rock and one pine stand where the stilts go.
        state.Rules.Add(new ObjectRule
        {
            Id = "rock", Name = "Rock4", Parent = "Rocks", Side = WorldSide.Main, Effect = ObjectEffect.Remove,
            Area = BoundsOf(RockX - 1.3f, -7f, RockZ - 1.3f, RockX + 1.3f, -3f, RockZ + 1.3f)
        });
        MapTrees.Remove(state, "pine", Tree.x, Tree.y);

        var geometry = new CopyDefinition
        {
            Id = "deck-timber", Template = Anchor, Frame = Property, OwnMesh = false, Obstacle = false,
            At = new LocalPoint(PropertyPivot, PropertyYaw)
        };
        float beamTop = Top - BoardThickness, beamBottom = beamTop - BeamDepth;
        for (int i = 0; i < BeamX.Length; i++)
        {
            float x = BeamX[i];
            Solid(geometry, "beam-" + i, Timber, x - .08f, beamBottom, South + .05f, x + .08f, beamTop, North - .05f);
            for (int j = 0; j < PostZ.Length; j++)
                Solid(geometry, "post-" + i + "-" + j, Timber, x - .08f, PostBottom[i], PostZ[j] - .08f, x + .08f, beamBottom, PostZ[j] + .08f);
        }
        // Cross beams at the ends tie the three lines together.
        Solid(geometry, "beam-south", Timber, West, beamBottom, South, East, beamTop, South + .1f);
        Solid(geometry, "beam-north", Timber, West, beamBottom, North - .1f, East, beamTop, North);

        if (finished)
        {
            Solid(geometry, "boards", Boards, West, beamTop, South, East, Top, North);
            Face(geometry, "fascia-south", Timber, East, FasciaBottom, South, East + .03f, Top, StepSouth);
            Face(geometry, "fascia-north", Timber, East, FasciaBottom, StepNorth, East + .03f, Top, North);
            Face(geometry, "step-riser", Timber, East, FasciaBottom, StepSouth, East + .03f, StepY, StepNorth);
            Solid(geometry, "step", Boards, East, FasciaBottom, StepSouth, East + StepOut, StepY, StepNorth);

            float railY = Top + FenceAboveFloor, inset = .1f;
            FenceRun(state, geometry, "west", false, West + inset, South + inset, North - inset, startPost: false, endPost: false);
            FenceRun(state, geometry, "south", true, South + inset, West + inset, East - inset, railY);
            FenceRun(state, geometry, "north", true, North - inset, West + inset, East - inset, railY);

            var extension = new PropertyExtensionDefinition { Id = ExtensionId, PropertyCode = "bungalow", GridIndex = GridIndex };
            for (int x = TileXMin; x <= TileXMax; x++)
                for (int y = TileYMin; y <= TileYMax; y++)
                    extension.Tiles.Add(new GridCoordinate(x, y));
            extension.Bounds.Add(BoundsOf(West, Top - .3f, South, East + StepOut, Top + 3f, North));
            state.PropertyExtensions.Add(extension);

            // The volume starts above the slope under the deck, so the bake lays no second floor there. The strip of
            // gravel inside it keeps the vanilla NavMesh, and the bake joins the step to it.
            state.NavigationBounds = BoundsOf(West - .2f, -4.1f, South - .2f, East + 1f, -1f, North + .2f);
            state.NavigationAgents.AddRange(new[] { "Humanoid", "Ignore Costs", "Employee" });
            // The seam check samples 0.6 m to each side of the strip's edge, so the strip reaches well past the volume.
            state.NavigationExistingFloor.Add(BoundsOf(East + StepOut + .1f, -4.2f, South - .2f, East + 2f, -3.75f, North + .2f));
            state.NavigationProbes.Add(new Vector3(-186.5f, -4f, 113f));
            state.NavigationProbes.Add(new Vector3(-188.8f, Top, 113f));
            state.NavigationProbes.Add(new Vector3(-193.3f, Top, 108.5f));
            state.NavigationProbes.Add(new Vector3(-193.3f, Top, 117.2f));
        }
        state.Copies.Add(geometry);
        return state;
    }


    // The vanilla piece is 2.5 m long with a post at each end. A run that does not divide evenly ends in a piece laid
    // over the one before it, a few millimetres aside so the two rails do not flicker. Runs that meet at a corner leave
    // the shared post to one of them.
    private static void FenceRun(WorldStateDefinition state, CopyDefinition geometry, string id, bool alongX, float cross, float start, float end,
        float railY = Top + FenceAboveFloor, bool startPost = true, bool endPost = true)
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
            var target = alongX ? new Vector3(center, railY, side) : new Vector3(side, railY, center);
            var rail = Copy(id + "-rail-" + i, Fence, FencePivot, target, alongX ? 90 : 0);
            if (last && overlaps || i == 0 && !startPost) rail.Exclude.Add("FencePost");
            if (last ? !endPost : !(overlaps && i == count - 2 && rest >= 1f)) rail.Exclude.Add("FencePost.004");
            state.Copies.Add(rail);
        }
        // The rails are visual copies without colliders; an invisible wall keeps a jump from carrying over them.
        geometry.Boxes.Add(new BoxDefinition
        {
            Id = id + "-guard", Kind = BoxKind.Block,
            Box = alongX ? BoundsOf(start, Top, cross - .07f, end, Top + GuardHeight, cross + .07f)
                : BoundsOf(cross - .07f, Top, start, cross + .07f, Top + GuardHeight, end)
        });
    }

    private static void Solid(CopyDefinition copy, string id, string material, float x1, float y1, float z1, float x2, float y2, float z2)
    {
        Face(copy, id, material, x1, y1, z1, x2, y2, z2);
        copy.Boxes.Add(new BoxDefinition { Id = id + "-collision", Kind = BoxKind.Block, Box = BoundsOf(x1, y1, z1, x2, y2, z2) });
    }

    private static void Face(CopyDefinition copy, string id, string material, float x1, float y1, float z1, float x2, float y2, float z2) =>
        copy.Boxes.Add(new BoxDefinition
        {
            Id = id, Kind = BoxKind.Panel, Material = material, TileMetres = material == Boards ? 2f : 1f,
            Box = BoundsOf(x1, y1, z1, x2, y2, z2)
        });

    // Places a copy so that the source point lands on the target, turned by yaw. The frame is the property root, which
    // stands turned by PropertyYaw.
    private static CopyDefinition Copy(string id, string template, Vector3 source, Vector3 target, float yaw) => new()
    {
        Id = id, Template = template, Frame = Property,
        At = new LocalPoint(target - Quaternion.Euler(0, yaw, 0) * (source - PropertyPivot), yaw + PropertyYaw),
        IndependentMesh = true, VisualOnly = true, Obstacle = false
    };

    private static Bounds BoundsOf(float x1, float y1, float z1, float x2, float y2, float z2)
    {
        var bounds = new Bounds();
        bounds.SetMinMax(new Vector3(x1, y1, z1), new Vector3(x2, y2, z2));
        return bounds;
    }
}
