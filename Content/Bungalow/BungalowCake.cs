using System;
using System.IO;
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Interaction;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.UI;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace Based.Content.Bungalow;

// A small birthday cake for the tester izy on the Bungalow's kitchen counter. The counter is vanilla and lies outside
// every build grid, so the cake takes no buildable space. It stands there only on 23 November. Every peer builds its own
// copy and reads its own clock: nothing is saved or synced.
// The model is the birthday cake from Kenney's Food Kit (CC0, Assets/cake/License.txt): its mesh, converted from the GLB,
// and its colour atlas are embedded resources.
// Its only collider is a trigger that carries a label-only InteractableObject, so the player sees "Happy birthday, izy!"
// when looking at it while walking, placing and employees pass through it. It has no BuildableItem, so the planner's
// blocker list never lists it.
internal static class BungalowCake
{
    private const string Name = "Based_BirthdayCake";
    private const string CounterPath = "@Properties/Bungalow/bungalow/Bench/Cube";
    private const string Message = "Happy birthday, izy!";
    private const string MeshResource = "Based.Assets.cake.cake-birthday.mesh";
    private const string TextureResource = "Based.Assets.cake.cake-colormap.png";
    private const float CheckInterval = 1f;

    // Between the sink (z 118.07..119.33) and the counter's corner, under the wall cabinets (bottom near y -2.0).
    private const float AtX = -175.45f, AtZ = 119.95f;

    // The model is 0.639 units across and 0.364 tall with its candles; this scale makes a 26 cm cake about 10 cm high.
    private const float Diameter = .26f, ModelDiameter = .6392f, Scale = Diameter / ModelDiameter;
    // The label floats this far above the candle tips.
    private const float LabelAbove = .07f;

    private const float LightIntensity = .35f, LightRange = .9f;

    private static GameObject? _cake;
    private static Light? _light;
    private static Transform? _labelPoint;
    private static Vector3 _labelAnchor;
    private static float _next, _nextHoverLog, _lastShift;
    private static bool _shiftMeasured, _waitLogged;
    private static bool _missingLogged, _buildFailed;

    // izy's birthday. The cake shows on this day of every year by the local clock of each peer and hides on every other
    // day; the check runs every second, so it appears and goes at local midnight without a reload.
    private const int BirthdayMonth = 11, BirthdayDay = 23;

    // A date the debug command sets in place of the clock; null uses DateTime.Now.
    internal static DateTime? ForcedDate;

    private static bool? _shown;

    private static DateTime Today => (ForcedDate ?? DateTime.Now).Date;

    private static bool IsBirthday(DateTime day) => day.Month == BirthdayMonth && day.Day == BirthdayDay;

    internal static void Tick()
    {
        if (_light != null && _cake != null && _cake.activeSelf)
        {
            // Two noise rates mix into a soft candle flicker between about 70 and 110 percent of the base intensity.
            float t = Time.time;
            float noise = Mathf.PerlinNoise(t * 7f, .3f) * .7f + Mathf.PerlinNoise(t * 19f, 1.7f) * .3f;
            _light.intensity = LightIntensity * (.7f + .4f * noise);
        }
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + CheckInterval;
        var today = Today;
        bool show = IsBirthday(today);
        if (_shown != show)
        {
            _shown = show;
            Mod.Log("Birthday cake " + (show ? "shown" : "hidden") + ": the date is " + today.ToString("yyyy-MM-dd") +
                    (ForcedDate.HasValue ? " (forced)" : " (local clock)") + ", the cake shows on " + BirthdayMonth + "-" + BirthdayDay + ".");
        }
        if (!show)
        {
            if (_cake != null && _cake.activeSelf) _cake.SetActive(false);
            return;
        }
        if (_cake != null)
        {
            if (!_cake.activeSelf) _cake.SetActive(true);
            return;
        }
        // A missing resource or shader does not come back in this session; the build is not tried again.
        if (_buildFailed) return;
        var counter = GameObject.Find(CounterPath);
        if (counter == null)
        {
            if (!_missingLogged) Mod.Log("Birthday cake waits: the Bungalow counter '" + CounterPath + "' is not in the scene.");
            _missingLogged = true;
            return;
        }
        var renderer = counter.GetComponent<Renderer>();
        if (renderer == null)
        {
            if (!_missingLogged) Mod.Warn("Birthday cake skipped: the Bungalow counter has no renderer to measure its top.");
            _missingLogged = true;
            return;
        }
        var at = new Vector3(AtX, renderer.bounds.max.y, AtZ);
        _cake = Build(at);
        if (_cake != null) Mod.Log("Birthday cake placed at " + at + " with the label '" + Message + "'.");
        else _buildFailed = true;
    }

    private static GameObject? Build(Vector3 at)
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null)
        {
            Mod.Warn("Birthday cake skipped: the URP Lit shader is not loaded.");
            return null;
        }
        var mesh = LoadMesh();
        if (mesh == null) return null;
        var texture = LoadTexture();
        if (texture == null)
        {
            Object.Destroy(mesh);
            return null;
        }

        var root = new GameObject(Name);
        root.transform.position = at;

        var model = new GameObject("Model");
        model.transform.SetParent(root.transform, false);
        model.transform.localScale = Vector3.one * Scale;
        model.AddComponent<MeshFilter>().sharedMesh = mesh;
        var material = new Material(lit) { name = "Based_CakeKenney" };
        material.SetTexture("_BaseMap", texture);
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Smoothness", .25f);
        material.SetFloat("_Metallic", 0f);
        // The Kenney material is double-sided.
        material.SetFloat("_Cull", 0f);
        var meshRenderer = model.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        float top = mesh.bounds.max.y * Scale;

        var flame = new GameObject("CandleLight");
        flame.transform.SetParent(root.transform, false);
        flame.transform.localPosition = new Vector3(0f, top + .01f, 0f);
        _light = flame.AddComponent<Light>();
        _light.type = LightType.Point;
        _light.color = new Color(1f, .72f, .3f);
        _light.range = LightRange;
        _light.intensity = LightIntensity;
        _light.shadows = LightShadows.None;

        // The label trigger spans the whole cake; the prompt shows centred just above the candles.
        var label = new GameObject("Label");
        label.transform.SetParent(root.transform, false);
        int doorLayer = LayerMask.NameToLayer("Door");
        label.layer = doorLayer >= 0 ? doorLayer : 0;
        var box = label.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.center = new Vector3(0f, (top + .02f) / 2f, 0f);
        box.size = new Vector3(Diameter + .04f, top + .02f, Diameter + .04f);
        var point = new GameObject("LabelPoint");
        point.transform.SetParent(root.transform, false);
        point.transform.localPosition = new Vector3(0f, top + LabelAbove, 0f);
        _labelPoint = point.transform;
        _labelAnchor = point.transform.position;
        var interaction = label.AddComponent<InteractableObject>();
        interaction.SetMessage(Message);
        interaction.SetInteractableState(InteractableObject.EInteractableState.Label);
        interaction.MaxInteractionRange = 2.5f;
        interaction.displayLocationPoint = point.transform;
        // The game calls this every frame the player looks at the cake, just before it places the prompt; one line per
        // look shows the label was reached.
        interaction.onHovered.AddListener((UnityAction)(() =>
        {
            CentreLabel();
            if (Time.unscaledTime < _nextHoverLog) return;
            // The first frames of a look still carry another prompt's layout; the line waits until the shift is measured.
            if (_shiftMeasured)
            {
                Mod.Log("Birthday cake label shown: '" + Message + "', centred over the cake (text moved " + _lastShift.ToString("0") + " px).");
                _nextHoverLog = Time.unscaledTime + 10f;
            }
            else if (!_waitLogged)
            {
                _waitLogged = true;
                Mod.Log("Birthday cake label shown: '" + Message + "', centring waits for the prompt's layout.");
            }
        }));
        Mod.Log("Birthday cake built: Kenney model " + Diameter * 100f + " cm across, " + top.ToString("0.000") + " m high with its candles, "
                + mesh.vertexCount + " vertices, label on layer " + label.layer + ".");
        return root;
    }

    // The game's prompt starts at the projected point and runs to the right, so its text would sit right of the cake.
    // The prompt keeps its layout from the last frame; the point moves left by the distance from the prompt's anchor to
    // its text's centre, measured on screen, so the text's centre lands over the cake's centre.
    private static void CentreLabel()
    {
        if (_labelPoint == null) return;
        var canvas = Singleton<InteractionCanvas>.Instance;
        var camera = PlayerSingleton<PlayerCamera>.Instance;
        if (canvas == null || camera == null || camera.Camera == null) return;
        var text = canvas.MessageText;
        var screen = camera.Camera.WorldToScreenPoint(_labelAnchor);
        if (screen.z <= 0f) return;
        // Before the prompt has shown this message once, its layout belongs to another prompt; the shift stays as it was.
        if (text != null && text.text == Message && canvas.Container.gameObject.activeInHierarchy)
        {
            var centre = text.rectTransform.TransformPoint(text.textBounds.center);
            _lastShift = centre.x - canvas.Container.position.x;
            _shiftMeasured = true;
        }
        screen.x -= _lastShift;
        _labelPoint.position = camera.Camera.ScreenToWorldPoint(screen);
    }

    private static Mesh? LoadMesh()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(MeshResource);
        if (stream == null)
        {
            Mod.Warn("Birthday cake skipped: the resource " + MeshResource + " is missing.");
            return null;
        }
        using var reader = new BinaryReader(stream);
        if (new string(reader.ReadChars(4)) != "BCK1")
        {
            Mod.Warn("Birthday cake skipped: " + MeshResource + " is not a cake mesh.");
            return null;
        }
        int vertexCount = reader.ReadInt32(), indexCount = reader.ReadInt32();
        var positions = new Vector3[vertexCount];
        var normals = new Vector3[vertexCount];
        var uvs = new Vector2[vertexCount];
        var indices = new int[indexCount];
        for (int i = 0; i < vertexCount; i++) positions[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        for (int i = 0; i < vertexCount; i++) normals[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        for (int i = 0; i < vertexCount; i++) uvs[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
        for (int i = 0; i < indexCount; i++) indices[i] = reader.ReadUInt16();
        var mesh = new Mesh { name = "Based_CakeKenney" };
        mesh.vertices = (Il2CppStructArray<Vector3>)positions;
        mesh.normals = (Il2CppStructArray<Vector3>)normals;
        mesh.uv = (Il2CppStructArray<Vector2>)uvs;
        mesh.triangles = (Il2CppStructArray<int>)indices;
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Texture2D? LoadTexture()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(TextureResource);
        if (stream == null)
        {
            Mod.Warn("Birthday cake skipped: the resource " + TextureResource + " is missing.");
            return null;
        }
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        // The atlas is a grid of flat colour cells; point filtering keeps neighbouring cells from bleeding in, as in the GLB.
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        if (!ImageConversion.LoadImage(texture, (Il2CppStructArray<byte>)buffer.ToArray(), false))
        {
            Mod.Warn("Birthday cake skipped: its colour atlas could not be decoded.");
            Object.Destroy(texture);
            return null;
        }
        return texture;
    }

    // A new scene destroys the cake; the next tick builds it again.
    internal static void Reset()
    {
        _cake = null;
        _light = null;
        _labelPoint = null;
        _shiftMeasured = _waitLogged = false;
        _next = 0;
        _missingLogged = false;
        _shown = null;
    }

    // Takes "MM-DD" for a forced day of this year, or "off" for the local clock again. The next tick applies it.
    internal static string ForceDate(string value)
    {
        if (value == "off") ForcedDate = null;
        else
        {
            var parts = value.Split('-');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int month) || !int.TryParse(parts[1], out int day) ||
                month < 1 || month > 12 || day < 1 || day > DateTime.DaysInMonth(DateTime.Now.Year, month))
                return "cake date wants MM-DD or off, got '" + value + "'";
            ForcedDate = new DateTime(DateTime.Now.Year, month, day);
        }
        _next = 0;
        return "cake date " + (ForcedDate.HasValue ? ForcedDate.Value.ToString("yyyy-MM-dd") : "from the local clock");
    }

    internal static string Describe() =>
        "date " + Today.ToString("yyyy-MM-dd") + (ForcedDate.HasValue ? " (forced)" : "") + ", " +
        (_cake == null ? "cake absent" : (_cake.activeSelf ? "cake shown" : "cake hidden") + " at " + _cake.transform.position +
            ", light " + (_light != null ? _light.intensity.ToString("0.00") : "none") + ", label shift " + _lastShift.ToString("0") + " px");
}
