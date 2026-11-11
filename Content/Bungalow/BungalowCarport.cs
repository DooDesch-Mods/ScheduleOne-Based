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
    // The vanilla dock stands at (-166.06, -4, 109.35); the second bay lies south of it under the same roof.
    private static readonly Vector3 DockPosition = new(-166.06f, -4f, 106.45f);
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
    // Puts the sheets onto the post beams, as the porch roof sits on its own.
    private const float RoofCentreY = -1.045f;
    private const float RoofCentreX = -165.35f;

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

    public static ExpansionDefinition Create()
    {
        var complete = new WorldStateDefinition { Id = "complete" };

        var extension = new PropertyExtensionDefinition { Id = ExtensionId, PropertyCode = "bungalow" };
        extension.Docks.Add(new PropertyDockDefinition
        {
            Id = DockId, Index = 1, TemplatePropertyCode = TemplateProperty, TemplateDockIndex = TemplateDock,
            At = new LocalPoint(DockPosition, DockYaw)
        });
        complete.PropertyExtensions.Add(extension);

        for (int i = 0; i < Pines.Length; i++)
        {
            var p = Pines[i];
            var area = BoundsOf(p.x - .8f, -20f, p.z - .8f, p.x + .8f, 40f, p.z + .8f);
            complete.Rules.Add(new ObjectRule
            {
                Id = "pine-" + i, Name = "InstanceWorldObject", Parent = "InstancedMapTrees",
                Side = WorldSide.Main, Effect = ObjectEffect.Remove, Area = area
            });
            // Each map tree has a mesh the NavMesh bake cut around; it goes with the tree.
            complete.Rules.Add(new ObjectRule
            {
                Id = "pine-navcut-" + i, Name = "Nav mesh cut", Parent = "Tree rustle sound",
                Side = WorldSide.Main, Effect = ObjectEffect.Remove, Area = area
            });
        }

        float half = RoofDepth * RoofScale * .5f - RidgeOverlap * .5f;
        complete.Copies.Add(Copy("roof-south", Roof, RoofPivot, new Vector3(RoofCentreX, RoofCentreY, RidgeZ - half), 0, RoofScale));
        complete.Copies.Add(Copy("roof-north", Roof, RoofPivot, new Vector3(RoofCentreX, RoofCentreY, RidgeZ + half), 180, RoofScale));

        // The posts stand at the long sides, so the vans drive in and out along the bays.
        var blocks = new CopyDefinition
        {
            Id = "carport-blocks", Template = Post, Frame = Property, OwnMesh = false, Obstacle = false,
            At = new LocalPoint(PropertyPivot, PropertyYaw)
        };
        float centreY = (PostBottom + PostTop) * .5f;
        for (int i = 0; i < PostX.Length; i++)
        {
            float x = PostX[i];
            complete.Copies.Add(Copy("post-south-" + i, Post, PostPivot, new Vector3(x, centreY, SouthEave + PostFromEnd), 0));
            complete.Copies.Add(Copy("post-north-" + i, Post, PostPivot, new Vector3(x, centreY, NorthEave - PostFromEnd), 180));
            blocks.Boxes.Add(new BoxDefinition { Id = "post-south-" + i, Kind = BoxKind.Block, Box = BoundsOf(x - .1f, PostBottom, SouthEave - .1f, x + .1f, PostTop, SouthEave + .1f) });
            blocks.Boxes.Add(new BoxDefinition { Id = "post-north-" + i, Kind = BoxKind.Block, Box = BoundsOf(x - .1f, PostBottom, NorthEave - .1f, x + .1f, PostTop, NorthEave + .1f) });
        }
        complete.Copies.Add(blocks);

        var definition = new ExpansionDefinition
        {
            Id = BungalowDefinition.CarportExpansionId, DisplayName = "Bungalow double carport", Version = "1.0.0", Source = null
        };
        definition.WorldStates.Add(complete);
        return definition;
    }

    // Places a copy so that the source point lands on the target, turned by yaw and scaled around it. The frame is the
    // property root, which stands turned by PropertyYaw; the copy keeps the tilt of its template.
    private static CopyDefinition Copy(string id, string template, Vector3 source, Vector3 target, float yaw, float scale = 1f) => new()
    {
        Id = id, Template = template, Frame = Property, Scale = scale,
        At = new LocalPoint(target - Quaternion.Euler(0, yaw, 0) * (scale * (source - PropertyPivot)), yaw + PropertyYaw),
        IndependentMesh = true, VisualOnly = true, Obstacle = false
    };

    private static Bounds BoundsOf(float x1, float y1, float z1, float x2, float y2, float z2)
    {
        var bounds = new Bounds();
        bounds.SetMinMax(new Vector3(x1, y1, z1), new Vector3(x2, y2, z2));
        return bounds;
    }
}
