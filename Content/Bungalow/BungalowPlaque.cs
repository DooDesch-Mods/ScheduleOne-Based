using System.IO;
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MapExpansions;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Based.Content.Bungalow;

// A small brass plate in the gap between the finished sunroom's slab and the house's foundation, readable only from a
// crouch at the gap's street end. Every peer places its own: it has no collider, no interaction and nothing to sync.
internal static class BungalowPlaque
{
    private const string Resource = "Based.Assets.sunroom-plaque.png";
    private const string Name = "Based_SunroomPlaque";
    private const float CheckInterval = 1f;

    // The gap runs between the slab's east edge (x -176.40) and the house (x -176.17), from the terrain at -3.92 up to the
    // floor at -3.70, and is closed on top; the slab's face at z 109.2 hides everything behind it above -3.70. The plate
    // fills the gap's mouth just behind that face and leans back 15 degrees: a crouching eye (0.65 of the standing height,
    // about -2.8 here) half a metre to a metre from the wall reads the whole text, while a standing eye looks down past the
    // floor's edge and sees at most its lower strip.
    private static readonly Vector3 At = new(-176.285f, -3.83f, 109.24f);
    private static readonly Vector3 Lean = new(15f, 0f, 0f);
    private const float Width = .2f, Height = .12f, Thickness = .006f;

    private static GameObject? _plate;
    private static Texture2D? _texture;
    private static float _next;
    private static bool _missingLogged;

    internal static void Tick()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + CheckInterval;
        bool wanted = MapExpansionsApi.IsWorldStateApplied(BungalowDefinition.SunroomExpansionId)
            && MapExpansionsApi.GetWorldState(BungalowDefinition.SunroomExpansionId) == "complete";
        if (!wanted)
        {
            if (_plate != null) { Mod.Log("Sunroom plaque removed: the sunroom is not complete on this peer."); Object.Destroy(_plate); }
            _plate = null;
            return;
        }
        if (_plate != null) return;
        _plate = Build();
        if (_plate != null) Mod.Log("Sunroom plaque placed at " + At + ".");
    }

    private static GameObject? Build()
    {
        var texture = LoadTexture();
        if (texture == null) return null;
        // URP Lit samples _BaseMap like the plan board does. The sunroom frame's world-space shader would ignore the
        // texture, so the frame's material is only the fallback when Lit is not loaded.
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        var template = lit != null ? new Material(lit) : TemplateMaterial();
        if (template == null) return null;
        if (lit == null) Mod.Warn("Sunroom plaque: the URP Lit shader is not loaded; the plate falls back to the frame's material and shows no engraving.");

        var root = new GameObject(Name);
        root.transform.SetPositionAndRotation(At, Quaternion.Euler(Lean));

        // The face is a quad, whose visible side looks down -z, toward the street.
        var face = GameObject.CreatePrimitive(PrimitiveType.Quad);
        face.name = "Face";
        Object.Destroy(face.GetComponent<Collider>());
        face.transform.SetParent(root.transform, false);
        face.transform.localScale = new Vector3(Width, Height, 1f);
        face.transform.localPosition = new Vector3(0f, 0f, -Thickness / 2f - .0005f);
        var brass = new Material(template) { name = "Based_PlaqueBrass", mainTexture = texture };
        brass.SetTexture("_BaseMap", texture);
        // The baked texture is bright; a muted tint turns it from sticker yellow into aged brass.
        brass.SetColor("_BaseColor", new Color(.62f, .6f, .56f));
        // The gap gets little light and a metallic surface would mirror the dark tunnel, so the brass stays mostly diffuse.
        brass.SetFloat("_Metallic", .2f);
        brass.SetFloat("_Smoothness", .4f);
        face.GetComponent<MeshRenderer>().sharedMaterial = brass;

        var back = GameObject.CreatePrimitive(PrimitiveType.Cube);
        back.name = "Plate";
        Object.Destroy(back.GetComponent<Collider>());
        back.transform.SetParent(root.transform, false);
        back.transform.localScale = new Vector3(Width + .004f, Height + .004f, Thickness);
        var edge = new Material(template) { name = "Based_PlaqueEdge" };
        edge.SetColor("_BaseColor", new Color(.35f, .27f, .1f));
        edge.SetFloat("_Metallic", .6f);
        edge.SetFloat("_Smoothness", .45f);
        back.GetComponent<MeshRenderer>().sharedMaterial = edge;
        Mod.Log("Sunroom plaque uses shader '" + template.shader.name + "'.");
        return root;
    }

    private const string FramePath = "MapExpansions.based_bungalow_sunroom/Copies/Frame";

    private static Material? TemplateMaterial()
    {
        var frame = GameObject.Find(FramePath);
        if (frame == null)
        {
            if (!_missingLogged) Mod.Warn("Sunroom plaque waits: the sunroom frame '" + FramePath + "' is not in the scene.");
            _missingLogged = true;
            return null;
        }
        foreach (var renderer in frame.GetComponentsInChildren<MeshRenderer>(true))
            if (renderer.sharedMaterial != null) return renderer.sharedMaterial;
        if (!_missingLogged) Mod.Warn("Sunroom plaque waits: the sunroom frame has no material to clone.");
        _missingLogged = true;
        return null;
    }

    private static Texture2D? LoadTexture()
    {
        if (_texture != null) return _texture;
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource);
        if (stream == null)
        {
            if (!_missingLogged) Mod.Warn("Sunroom plaque skipped: the resource " + Resource + " is missing.");
            _missingLogged = true;
            return null;
        }
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true) { filterMode = FilterMode.Trilinear, anisoLevel = 4 };
        if (!ImageConversion.LoadImage(texture, (Il2CppStructArray<byte>)buffer.ToArray(), false))
        {
            if (!_missingLogged) Mod.Warn("Sunroom plaque skipped: its texture could not be decoded.");
            _missingLogged = true;
            Object.Destroy(texture);
            return null;
        }
        texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return _texture = texture;
    }

    // A new scene destroys the plate; the next tick builds it again.
    internal static void Reset()
    {
        _plate = null;
        _next = 0;
    }

#if DEBUG
    internal static void DebugMove(Vector3 at, float lean)
    {
        if (_plate == null) { Mod.Warn("baseddiag plaque: no plate to move; the sunroom is not complete on this peer."); return; }
        _plate.transform.SetPositionAndRotation(at, Quaternion.Euler(lean, 0f, 0f));
    }
#endif

    internal static string Describe() =>
        _plate == null ? "plaque absent" : "plaque at " + _plate.transform.position + " lean " + _plate.transform.eulerAngles;
}
