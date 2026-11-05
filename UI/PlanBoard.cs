using Based.Content;
using Sideload.Api;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Based.UI;

// The plan as a texture for the folder in the world. Every folder of one site shows the same page, so one hidden
// surface per site paints one texture, and the lit material lets it darken with the room. The camera only runs after
// the page changed.
internal static class PlanBoard
{
    private sealed class Board
    {
        public RenderTexture? Texture;
        public Material? Material;
        public GameObject? Root;
        public Camera? Camera;
        public SurfaceHandle? Surface;
        public string LastState = "";
        public float NextCheck;
        public int PaintUntil;
    }

    internal const string ModelName = "Based_ProjectFolder";
    // Large enough on the wall to read the page from across the room.
    internal const float Width = 1.5f, Height = .872f, Depth = .012f;
    private const int PixelWidth = 1720, PixelHeight = 1000;
    private const float CornerRadius = .03f, CheckInterval = 1f;
    private const int CornerSegments = 6, PaintFrames = 60, PaintEvery = 5;
    // Each site's hidden camera stands apart, so one board never films another.
    private static readonly Vector3 HiddenAt = new(0f, -4000f, 0f), BoardSpacing = new(20f, 0f, 0f);
    private static readonly Color BoardColour = new(.27f, .18f, .11f);
    private static readonly Dictionary<string, Board> Boards = new(StringComparer.Ordinal);
    private static Mesh? _mesh;

    private static Board Of(SiteDefinition site)
    {
        if (!Boards.TryGetValue(site.BoardSurfaceId, out var board)) Boards[site.BoardSurfaceId] = board = new Board();
        return board;
    }

    // The texture outlives scene changes, because the held model built once at game start points at this material.
    private static Material? MaterialOf(SiteDefinition site)
    {
        var board = Of(site);
        if (board.Material != null) return board.Material;
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null)
        {
            Mod.Warn("The URP Lit shader is not loaded; the project folder of " + site.Base.Id + " has no face.");
            return null;
        }
        board.Texture = new RenderTexture(PixelWidth, PixelHeight, 16, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
        {
            name = "Based_PlanBoard_" + site.BoardSurfaceId,
            useMipMap = true,
            autoGenerateMips = true,
            filterMode = FilterMode.Trilinear,
            anisoLevel = 4,
            hideFlags = HideFlags.HideAndDontSave
        };
        board.Material = new Material(lit) { name = "Based_PlanBoard_" + site.BoardSurfaceId, hideFlags = HideFlags.HideAndDontSave };
        board.Material.SetTexture("_BaseMap", board.Texture);
        board.Material.SetColor("_BaseColor", Color.white);
        board.Material.SetFloat("_Smoothness", .15f);
        UnityEngine.Object.DontDestroyOnLoad(board.Material);
        return board.Material;
    }

    internal static GameObject CreateModel(SiteDefinition site, Transform parent)
    {
        var model = new GameObject(ModelName);
        model.transform.SetParent(parent, false);
        model.AddComponent<MeshFilter>().sharedMesh = _mesh ??= BuildMesh();
        var renderer = model.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = MaterialOf(site);
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        return model;
    }

    // Runs while a folder of the site exists in the world or in a hand. Without one the page stays down.
    internal static void Tick(SiteDefinition site, bool wanted)
    {
        if (!wanted) return;
        var board = Of(site);
        // Leaving the save destroys the root with the scene, which leaves the handle behind it stale.
        if (board.Root == null)
        {
            Reset(site);
            if (!Mount(site, board)) return;
        }
        if (Time.unscaledTime >= board.NextCheck)
        {
            board.NextCheck = Time.unscaledTime + CheckInterval;
            string state = BuildPlan.WallState(site);
            if (state != board.LastState)
            {
                board.LastState = state;
                board.Surface!.Emit("plan.changed", state);
                board.PaintUntil = Time.frameCount + PaintFrames;
                Mod.Log("Project folder page repainted: " + site.Base.Id + ".");
            }
        }
        if (Time.frameCount <= board.PaintUntil && Time.frameCount % PaintEvery == 0) board.Camera!.Render();
    }

    internal static void Reset()
    {
        foreach (var site in Mod.Sites) Reset(site);
    }

    private static void Reset(SiteDefinition site)
    {
        var board = Of(site);
        if (board.Surface == null && board.Root == null) return;
        Surfaces.Unmount(site.BoardSurfaceId);
        if (board.Root != null) UnityEngine.Object.Destroy(board.Root);
        board.Root = null;
        board.Camera = null;
        board.Surface = null;
        board.LastState = "";
        Mod.Log("Project folder page taken down: " + site.Base.Id + ".");
    }

    private static bool Mount(SiteDefinition site, Board board)
    {
        if (!Surfaces.Available)
        {
            Mod.Warn("Sideload Surfaces is missing; the project folder stays blank.");
            return false;
        }
        if (MaterialOf(site) == null) return false;
        int index = 0;
        while (index < Mod.Sites.Count && Mod.Sites[index] != site) index++;
        var root = board.Root = new GameObject("Based_PlanBoard_" + site.BoardSurfaceId);
        root.transform.position = HiddenAt + BoardSpacing * index;
        var camera = board.Camera = root.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 1f;
        camera.nearClipPlane = .1f;
        camera.farClipPlane = 5f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = BoardColour;
        camera.allowHDR = false;
        camera.allowMSAA = false;
        camera.targetTexture = board.Texture;
        camera.enabled = false;
        var canvasObject = new GameObject("Canvas");
        canvasObject.transform.SetParent(root.transform, false);
        var canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 1f;
        var holder = new GameObject("Plan");
        var rect = holder.AddComponent<RectTransform>();
        rect.SetParent(canvasObject.transform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        board.Surface = Surfaces.Mount(rect, site.BoardSurfaceId, "Based.Assets.based", designShortSide: 500)
            .OnCall("plan.state", _ => BuildPlan.WallState(site));
        board.LastState = "";
        board.PaintUntil = Time.frameCount + PaintFrames;
        Mod.Log("Project folder page mounted: " + site.Base.Id + ".");
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
