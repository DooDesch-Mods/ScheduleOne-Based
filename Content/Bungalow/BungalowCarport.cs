using MapExpansions;
using UnityEngine;

namespace Based.Content.Bungalow;

// The double carport south of the Bungalow's porch: a gable roof of two porch roofs, six porch posts, and a second
// delivery spot beside the vanilla one. The pines on the spot go, near and far.
public static class BungalowCarport
{
    public const string ExtensionId = "bungalow-carport";
    // The IDs and the template decide the dock's saved GUIDs; changing them strands deliveries in old saves.
    public const string DockId = "carport-dock";
    private const string TemplateProperty = "bungalow";
    private const int TemplateDock = 0;
    // The vanilla dock stands at (-166.06, -4, 109.35); the second bay lies south of it under the same roof, far enough
    // that the two painted outlines on the ground do not overlap.
    private static readonly Vector3 DockPosition = new(-166.06f, -4f, 106.05f);
    private const float DockYaw = 90f;

    private const string Property = "@Properties/Bungalow";
    private const string Roof = Property + "/bungalow/SmallRoof";
    private const string Post = Property + "/bungalow/PorchBeam";
    // The property root stands turned by 90 degrees; copies placed in its frame turn back by that much.
    private static readonly Vector3 PropertyPivot = new(-170f, -4f, 115.5f);
    private const float PropertyYaw = 90f;

    // Centre of the porch roof's bounds, 2.73 m deep; the sheet falls to the south. A pivot off the centre would shift
    // the turned half sideways against the other one.
    private static readonly Vector3 RoofPivot = new(-167.15f, -.8f, 113.975f);
    private const float RoofDepth = 2.73f;
    private const float RoofScale = 1.25f;
    // The halves overlap by 2 cm so no light shows through the ridge.
    private const float RidgeOverlap = .02f;
    private const float RidgeZ = 107.65f;
    // Puts the sheets onto the post beams, which rise at the roof's slope: their tops stood about 0.15 m under the sheets'
    // undersides, and the sheets now sit on them.
    private const float RoofCentreY = -1.185f;
    private const float RoofCentreX = -165.35f;
    // The scaled sheet, measured in the game: its top at the eave lies 0.259 m under the centre's height and rises 0.18 per
    // metre to the ridge; it is 0.166 m thick and 6.75 m wide.
    private const float EaveTopBelowCentre = .259f, RoofRise = .18f, SheetThickness = .166f, RoofHalfWidth = 3.375f;
    // Each half is a whole scaled porch roof deep, from its eave to the ridge.
    private const float RoofRun = RoofDepth * RoofScale;
    private const string Timber = "wood brown", Metal = "metal_darkgrey_mat";

    // Centre of the porch post's bounds: a post at the south end and a beam 2.5 m to the north, 3 m tall.
    private static readonly Vector3 PostPivot = new(-164.8f, -2.2f, 114.1f);
    private const float PostFromEnd = 1.2f;
    private const float PostBottom = -4.1f;
    private const float PostTop = -1.1f;
    private static readonly float[] PostX = { -168.5f, -165.35f, -162.2f };
    private const float SouthEave = 104.45f;
    private const float NorthEave = 110.85f;

    // Trunks of the pines on the spot, measured in the game.
    private static readonly Vector3[] Pines =
    {
        new(-165.9f, -4f, 104.1f),
        new(-169.7f, -4f, 105.9f),
        new(-161.5f, -4f, 104.2f)
    };

    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        if (!MapExpansionsApi.Register(Create()))
            throw new InvalidOperationException("Based could not register the Bungalow carport; inspect the MapExpansions validation errors.");
        _registered = true;
    }

    // The first night fells the pines and sets the posts; the kit stands in the second bay. The second puts the roof on
    // and opens the second bay.
    public const string PostsState = "posts";

    public static ExpansionDefinition Create()
    {
        var definition = new ExpansionDefinition
        {
            Id = BungalowDefinition.CarportExpansionId, DisplayName = "Bungalow double carport", Version = "1.0.0", Source = null
        };
        definition.WorldStates.Add(BuildState(PostsState, false));
        definition.WorldStates.Add(BuildState("complete", true));
        return definition;
    }

    private static WorldStateDefinition BuildState(string id, bool finished)
    {
        var state = new WorldStateDefinition { Id = id };
        Ground(state);
        Posts(state, finished);
        if (finished) Bay(state);
        if (finished) RoofOn(state);
        else BungalowSiteProps.CarportKit(state, 8, 131);
        return state;
    }

    // The pines on the spot go and the rock in the second bay with them, from the first night.
    private static void Ground(WorldStateDefinition state)
    {
        for (int i = 0; i < Pines.Length; i++) MapTrees.Remove(state, "pine-" + i, Pines[i].x, Pines[i].z);
        // A rock juts 15 cm out of the ground in the second bay; a car parked there would stand on it.
        state.Rules.Add(new ObjectRule
        {
            Id = "bay-rock", Name = "Rock1 (10)", Parent = "Rocks", Side = WorldSide.Main, Effect = ObjectEffect.Remove,
            Area = BoundsOf(-164.6f, -6f, 104.1f, -162.1f, -3f, 106.5f)
        });
        // A shrub by the apartment wall grows over the second bay's street corner and hides half its marking.
        state.Rules.Add(new ObjectRule
        {
            Id = "bay-shrub", Name = "Shrub (46)", Parent = "Bushes", Side = WorldSide.Main, Effect = ObjectEffect.Remove,
            Area = BoundsOf(-163f, -6f, 103f, -160f, -2f, 106f)
        });
    }

    // Under the roof the grass goes: the game draws grass only near the player, so up close it stood over the bay's
    // markings. The ground takes the tint of the house's own parking bay, painted with a soft edge. The cleared ground
    // reaches past the eaves to the apartment wall in the south and a metre toward the street, so no blade at the
    // area's edge leans over the markings, which run to x -161.82.
    private const string BayLayer = BungalowSiteProps.ForecourtLayer;
    private static void Bay(WorldStateDefinition state)
    {
        var area = BoundsOf(PostX[0] - .4f, -20f, 103.2f, -160.9f, 20f, NorthEave + .3f);
        state.Rules.Add(new ObjectRule { Id = "bay-grass", Name = "", Side = WorldSide.Main, Effect = ObjectEffect.ClearDetails, Area = area });
        state.Rules.Add(new ObjectRule
        {
            Id = "bay-ground", Name = "", Side = WorldSide.Main, Effect = ObjectEffect.Paint,
            PaintLayer = BayLayer, PaintStrength = 1f, Area = area
        });
    }

    private static void RoofOn(WorldStateDefinition complete)
    {
        var extension = new PropertyExtensionDefinition { Id = ExtensionId, PropertyCode = "bungalow" };
        extension.Docks.Add(new PropertyDockDefinition
        {
            Id = DockId, Index = 1, TemplatePropertyCode = TemplateProperty, TemplateDockIndex = TemplateDock,
            At = new LocalPoint(DockPosition, DockYaw)
        });
        complete.PropertyExtensions.Add(extension);

        // Under the roof the rain stops; the game's rain mask was baked without it.
        float roofHalfX = 3.5f * RoofScale;
        complete.WeatherCover.Add(BoundsOf(RoofCentreX - roofHalfX, PostBottom, SouthEave, RoofCentreX + roofHalfX, PostTop + .5f, NorthEave));

        float half = RoofDepth * RoofScale * .5f - RidgeOverlap * .5f;
        complete.Copies.Add(Copy("roof-south", Roof, RoofPivot, new Vector3(RoofCentreX, RoofCentreY, RidgeZ - half), 0, RoofScale));
        complete.Copies.Add(Copy("roof-north", Roof, RoofPivot, new Vector3(RoofCentreX, RoofCentreY, RidgeZ + half), 180, RoofScale));
        Trim(complete, "south", RidgeZ - (RoofRun - RidgeOverlap * .5f), 1f, 0f);
        Trim(complete, "north", RidgeZ + (RoofRun - RidgeOverlap * .5f), -1f, .004f);
        RidgeCap(complete);
    }

    // The sheets' cut edges show as plain faces: a timber board covers each gable edge and the eave, laid out level from
    // the eave's top edge and tilted up the slope. toRidge is +1 when the ridge lies north of the eave. The north half's
    // gable boards stand 4 mm further out, so where the two halves' boards meet at the ridge no two faces share a plane.
    private static void Trim(WorldStateDefinition state, string side, float eaveZ, float toRidge, float outward)
    {
        float angle = Mathf.Atan(RoofRise) * Mathf.Rad2Deg;
        float length = RoofRun / Mathf.Cos(Mathf.Atan(RoofRise)) + .01f;
        float thick = SheetThickness * Mathf.Cos(Mathf.Atan(RoofRise));
        var pivot = new Vector3(RoofCentreX, RoofCentreY - EaveTopBelowCentre, eaveZ);
        var trim = Tilted("roof-trim-" + side, Post, pivot, pivot, Quaternion.Euler(-angle * toRidge, 0f, 0f));
        trim.OwnMesh = false;
        trim.Exclude.Add("Colliders");
        float west = RoofCentreX - RoofHalfWidth, east = RoofCentreX + RoofHalfWidth;
        float Z(float along) => eaveZ + along * toRidge;
        float z0 = Mathf.Min(Z(-.03f), Z(length)), z1 = Mathf.Max(Z(-.03f), Z(length));
        // Each gable board reaches 1 cm into the sheet and stands 3.5 cm out from its edge; the outward shift moves the
        // whole board, its inner face too, so the halves' inner faces under the ridge stay apart as well.
        Face(trim, "gable-west", Timber, west - .035f - outward, pivot.y - thick - .06f, z0, west + .01f - outward, pivot.y + .025f, z1);
        Face(trim, "gable-east", Timber, east - .01f + outward, pivot.y - thick - .06f, z0, east + .035f + outward, pivot.y + .025f, z1);
        // The eave board runs between the gable boards' outer faces and ends inside them; its top and bottom stay 5 mm
        // inside theirs.
        float f0 = Mathf.Min(Z(-.035f), Z(.01f)), f1 = Mathf.Max(Z(-.035f), Z(.01f));
        Face(trim, "eave", Timber, west - .025f, pivot.y - thick - .055f, f0, east + .025f, pivot.y + .02f, f1);
        state.Copies.Add(trim);
    }

    // The two halves meet at the ridge with a seam between their top edges; a steel cap runs over it from gable to gable,
    // a centimetre past the boards on each end.
    private static void RidgeCap(WorldStateDefinition state)
    {
        var cap = new CopyDefinition
        {
            Id = "roof-ridge-cap", Template = Post, Frame = Property, OwnMesh = false, Obstacle = false,
            Exclude = { "Colliders" },
            At = new LocalPoint(PropertyPivot, PropertyYaw)
        };
        float ridgeTop = RoofCentreY - EaveTopBelowCentre + RoofRun * RoofRise;
        const float halfDepth = .18f;
        Face(cap, "cap", Metal, RoofCentreX - RoofHalfWidth - .055f, ridgeTop - halfDepth * RoofRise - .025f, RidgeZ - halfDepth,
            RoofCentreX + RoofHalfWidth + .055f, ridgeTop + .035f, RidgeZ + halfDepth);
        state.Copies.Add(cap);
    }

    // The posts stand at the long sides, so the vans drive in and out along the bays.
    private static void Posts(WorldStateDefinition state, bool finished)
    {
        var blocks = new CopyDefinition
        {
            Id = "carport-blocks", Template = Post, Frame = Property, OwnMesh = false, Obstacle = false,
            // The post only anchors the boxes; its collider child would stand as an unseen beam where the anchor lands.
            Exclude = { "Colliders" },
            At = new LocalPoint(PropertyPivot, PropertyYaw)
        };
        float centreY = (PostBottom + PostTop) * .5f;
        for (int i = 0; i < PostX.Length; i++)
        {
            float x = PostX[i];
            state.Copies.Add(Copy("post-south-" + i, Post, PostPivot, new Vector3(x, centreY, SouthEave + PostFromEnd), 0));
            state.Copies.Add(Copy("post-north-" + i, Post, PostPivot, new Vector3(x, centreY, NorthEave - PostFromEnd), 180));
            blocks.Boxes.Add(new BoxDefinition { Id = "post-south-" + i, Kind = BoxKind.Block, Box = BoundsOf(x - .1f, PostBottom, SouthEave - .1f, x + .1f, PostTop, SouthEave + .1f) });
            blocks.Boxes.Add(new BoxDefinition { Id = "post-north-" + i, Kind = BoxKind.Block, Box = BoundsOf(x - .1f, PostBottom, NorthEave - .1f, x + .1f, PostTop, NorthEave + .1f) });
        }
        // The roof sheets are copies without a collider; this slab under them is what the rain cover's raycast hits.
        float slabHalfX = 3.5f * RoofScale;
        if (finished)
            blocks.Boxes.Add(new BoxDefinition { Id = "roof", Kind = BoxKind.Block, Box = BoundsOf(RoofCentreX - slabHalfX, PostTop, SouthEave, RoofCentreX + slabHalfX, PostTop + .1f, NorthEave) });
        state.Copies.Add(blocks);
    }

    // Places a copy so that the source point lands on the target, turned by yaw and scaled around it. The frame is the
    // property root, which stands turned by PropertyYaw; the copy keeps the tilt of its template.
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

    private static void Face(CopyDefinition copy, string id, string material, float x1, float y1, float z1, float x2, float y2, float z2) =>
        copy.Boxes.Add(new BoxDefinition { Id = id, Kind = BoxKind.Panel, Material = material, TileMetres = 1f, Box = BoundsOf(x1, y1, z1, x2, y2, z2) });

    private static Bounds BoundsOf(float x1, float y1, float z1, float x2, float y2, float z2)
    {
        var bounds = new Bounds();
        bounds.SetMinMax(new Vector3(x1, y1, z1), new Vector3(x2, y2, z2));
        return bounds;
    }
}
