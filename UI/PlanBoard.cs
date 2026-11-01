using Based.Content;
using Sideload.Api;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Based.UI;

// The plan as a texture for the folder in the world. Every folder shows the same page, so one hidden surface paints one
// texture, and the lit material lets it darken with the room. The camera only runs after the page changed.
internal static class PlanBoard
{
    internal const string ModelName = "Based_ProjectFolder";
    // Large enough on the wall to read the page from across the room.
    internal const float Width = 1.5f, Height = .872f, Depth = .012f;
    private const string SurfaceId = "based-plan-board";
    private const int PixelWidth = 1720, PixelHeight = 1000;
    private const float CornerRadius = .03f, CheckInterval = 1f;
    private const int CornerSegments = 6, PaintFrames = 60, PaintEvery = 5;
    private static readonly Vector3 HiddenAt = new(0f, -4000f, 0f);
    private static readonly Color BoardColour = new(.27f, .18f, .11f);
    private static RenderTexture? _texture;
    private static Material? _material;
    private static Mesh? _mesh;
    private static GameObject? _root;
    private static Camera? _camera;
    private static SurfaceHandle? _surface;
    private static string _lastState = "";
    private static float _nextCheck;
    private static int _paintUntil;

    // The texture outlives scene changes, because the held model built once at game start points at this material.
    internal static Material? Material
    {
        get
        {
            if (_material != null) return _material;
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null)
            {
                Mod.Warn("The URP Lit shader is not loaded; the project folder has no face.");
                return null;
            }
            _texture = new RenderTexture(PixelWidth, PixelHeight, 16, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "Based_PlanBoard",
                useMipMap = true,
                autoGenerateMips = true,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
                hideFlags = HideFlags.HideAndDontSave
            };
            _material = new Material(lit) { name = "Based_PlanBoard", hideFlags = HideFlags.HideAndDontSave };
            _material.SetTexture("_BaseMap", _texture);
            _material.SetColor("_BaseColor", Color.white);
            _material.SetFloat("_Smoothness", .15f);
            UnityEngine.Object.DontDestroyOnLoad(_material);
            return _material;
        }
    }

    internal static GameObject CreateModel(Transform parent)
    {
        var model = new GameObject(ModelName);
        model.transform.SetParent(parent, false);
        model.AddComponent<MeshFilter>().sharedMesh = _mesh ??= BuildMesh();
        var renderer = model.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = Material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        return model;
    }

    // Runs while a folder exists in the world or in a hand. Without one the page stays down.
    internal static void Tick(SiteDefinition site, bool wanted)
    {
        if (!wanted) return;
        // Leaving the save destroys the root with the scene, which leaves the handle behind it stale.
        if (_root == null)
        {
            Reset();
            if (!Mount(site)) return;
        }
        if (Time.unscaledTime >= _nextCheck)
        {
            _nextCheck = Time.unscaledTime + CheckInterval;
            string state = BuildPlan.WallState(site);
            if (state != _lastState)
            {
                _lastState = state;
                _surface!.Emit("plan.changed", state);
                _paintUntil = Time.frameCount + PaintFrames;
                Mod.Log("Project folder page repainted.");
            }
        }
        if (Time.frameCount <= _paintUntil && Time.frameCount % PaintEvery == 0) _camera!.Render();
    }

    internal static void Reset()
    {
        if (_surface == null && _root == null) return;
        Surfaces.Unmount(SurfaceId);
        if (_root != null) UnityEngine.Object.Destroy(_root);
        _root = null;
        _camera = null;
        _surface = null;
        _lastState = "";
        Mod.Log("Project folder page taken down.");
    }

    private static bool Mount(SiteDefinition site)
    {
        if (!Surfaces.Available)
        {
            Mod.Warn("Sideload Surfaces is missing; the project folder stays blank.");
            return false;
        }
        if (Material == null) return false;
        _root = new GameObject("Based_PlanBoard");
        _root.transform.position = HiddenAt;
        _camera = _root.AddComponent<Camera>();
        _camera.orthographic = true;
        _camera.orthographicSize = 1f;
        _camera.nearClipPlane = .1f;
        _camera.farClipPlane = 5f;
        _camera.clearFlags = CameraClearFlags.SolidColor;
        _camera.backgroundColor = BoardColour;
        _camera.allowHDR = false;
        _camera.allowMSAA = false;
        _camera.targetTexture = _texture;
        _camera.enabled = false;
        var canvasObject = new GameObject("Canvas");
        canvasObject.transform.SetParent(_root.transform, false);
        var canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = _camera;
        canvas.planeDistance = 1f;
        var holder = new GameObject("Plan");
        var rect = holder.AddComponent<RectTransform>();
        rect.SetParent(canvasObject.transform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        _surface = Surfaces.Mount(rect, SurfaceId, "Based.Assets.based", designShortSide: 500)
            .OnCall("plan.state", _ => BuildPlan.WallState(site));
        _lastState = "";
        _paintUntil = Time.frameCount + PaintFrames;
        Mod.Log("Project folder page mounted.");
        return true;
    }

    // A rounded slab with its face towards +z. Looking at it from the front, +x runs to the viewer's left.
    private static Mesh BuildMesh()
    {
        var outline = new List<Vector2>();
        float halfW = Width / 2f, halfH = Height / 2f, r = CornerRadius;
        for (int corner = 0; corner < 4; corner++)
        {
            float cx = corner is 0 or 3 ? halfW - r : -halfW + r;
            float cy = corner is 0 or 1 ? halfH - r : -halfH + r;
            for (int i = 0; i <= CornerSegments; i++)
            {
                float angle = (corner + (float)i / CornerSegments) * Mathf.PI / 2f;
                outline.Add(new Vector2(cx + Mathf.Cos(angle) * r, cy + Mathf.Sin(angle) * r));
            }
        }
        var vertices = new List<Vector3> { new(0f, 0f, Depth) };
        var normals = new List<Vector3> { Vector3.forward };
        var uvs = new List<Vector2> { new(.5f, .5f) };
        var triangles = new List<int>();
        Vector2 Uv(Vector2 p) => new(.5f - p.x / Width, .5f + p.y / Height);
        foreach (var point in outline)
        {
            vertices.Add(new Vector3(point.x, point.y, Depth));
            normals.Add(Vector3.forward);
            uvs.Add(Uv(point));
        }
        for (int i = 0; i < outline.Count; i++)
        {
            triangles.Add(0);
            triangles.Add(1 + i);
            triangles.Add(1 + (i + 1) % outline.Count);
        }
        for (int i = 0; i < outline.Count; i++)
        {
            Vector2 a = outline[i], b = outline[(i + 1) % outline.Count];
            Vector2 side = (b - a).normalized;
            var outward = new Vector3(side.y, -side.x, 0f);
            int at = vertices.Count;
            vertices.Add(new Vector3(a.x, a.y, Depth));
            vertices.Add(new Vector3(b.x, b.y, Depth));
            vertices.Add(new Vector3(b.x, b.y, 0f));
            vertices.Add(new Vector3(a.x, a.y, 0f));
            for (int n = 0; n < 4; n++) normals.Add(outward);
            uvs.Add(Uv(a * .98f));
            uvs.Add(Uv(b * .98f));
            uvs.Add(Uv(b * .98f));
            uvs.Add(Uv(a * .98f));
            triangles.AddRange(new[] { at + 2, at + 1, at, at + 2, at, at + 3 });
        }
        var mesh = new Mesh { name = ModelName, hideFlags = HideFlags.HideAndDontSave };
        mesh.vertices = vertices.ToArray();
        mesh.normals = normals.ToArray();
        mesh.uv = uvs.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.RecalculateBounds();
        return mesh;
    }
}
