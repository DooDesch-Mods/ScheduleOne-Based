using System;
using Il2CppScheduleOne.Interaction;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace Based.Content.Bungalow;

// A small birthday cake for the tester izy on the Bungalow's kitchen counter. The counter is vanilla and lies outside
// every build grid, so the cake takes no buildable space. It stands there only on 23 November. Every peer builds its own
// copy and reads its own clock: nothing is saved or synced.
// Its only collider is a trigger that carries a label-only InteractableObject, so the player sees "Happy birthday, izy!"
// when looking at it while walking, placing and employees pass through it. It has no BuildableItem, so the planner's
// blocker list never lists it.
internal static class BungalowCake
{
    private const string Name = "Based_BirthdayCake";
    private const string CounterPath = "@Properties/Bungalow/bungalow/Bench/Cube";
    private const string Message = "Happy birthday, izy!";
    private const float CheckInterval = 1f;

    // Between the sink (z 118.07..119.33) and the counter's corner, under the wall cabinets (bottom near y -2.0).
    private const float AtX = -175.45f, AtZ = 119.95f;

    private const float BottomRadius = .13f, BottomHeight = .1f;
    private const float TopRadius = .09f, TopHeight = .08f;
    private const float RimHeight = .014f, RimOverhang = .006f;
    private const float CandleRadius = .006f, CandleHeight = .06f;

    private const float LightIntensity = .35f, LightRange = .9f;

    private static GameObject? _cake;
    private static Light? _light;
    private static float _next, _nextHoverLog;
    private static bool _missingLogged;

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
    }

    private static GameObject? Build(Vector3 at)
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null)
        {
            if (!_missingLogged) Mod.Warn("Birthday cake skipped: the URP Lit shader is not loaded.");
            _missingLogged = true;
            return null;
        }
        var unlit = Shader.Find("Universal Render Pipeline/Unlit");

        var root = new GameObject(Name);
        root.transform.position = at;

        var plate = Material(lit, "Based_CakePlate", new Color(.82f, .84f, .86f), .5f);
        var frosting = Material(lit, "Based_CakeFrosting", new Color(.96f, .94f, .9f), .25f);
        var pink = Material(lit, "Based_CakePink", new Color(.95f, .45f, .62f), .35f);
        var candle = Material(lit, "Based_CakeCandle", new Color(.55f, .75f, .95f), .3f);
        var flameColor = new Color(1f, .72f, .3f);
        var flame = unlit != null ? new Material(unlit) { name = "Based_CakeFlame" } : Material(lit, "Based_CakeFlame", flameColor, 0f);
        flame.SetColor("_BaseColor", flameColor);
        if (unlit == null)
        {
            flame.EnableKeyword("_EMISSION");
            flame.SetColor("_EmissionColor", flameColor * 2f);
        }

        float y = 0f;
        Disc(root, "Plate", .16f, .008f, ref y, plate);
        float bottomBase = y;
        Disc(root, "Bottom", BottomRadius, BottomHeight, ref y, frosting);
        Rim(root, "BottomRim", BottomRadius + RimOverhang, y, pink);
        Drips(root, "BottomDrip", BottomRadius, y, 10, pink);
        Disc(root, "Top", TopRadius, TopHeight, ref y, frosting);
        Rim(root, "TopRim", TopRadius + RimOverhang, y, pink);
        Drips(root, "TopDrip", TopRadius, y, 8, pink);
        float candleBase = y;
        Disc(root, "Candle", CandleRadius, CandleHeight, ref y, candle);

        var flameObject = Part(root, PrimitiveType.Sphere, "Flame", flame);
        flameObject.transform.localScale = new Vector3(.012f, .024f, .012f);
        flameObject.transform.localPosition = new Vector3(0f, y + .012f, 0f);
        _light = flameObject.AddComponent<Light>();
        _light.type = LightType.Point;
        _light.color = flameColor;
        _light.range = LightRange;
        _light.intensity = LightIntensity;
        _light.shadows = LightShadows.None;

        // The label trigger spans the whole cake; the prompt shows just above the flame.
        var label = new GameObject("Label");
        label.transform.SetParent(root.transform, false);
        int doorLayer = LayerMask.NameToLayer("Door");
        label.layer = doorLayer >= 0 ? doorLayer : 0;
        var box = label.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.center = new Vector3(0f, (y + .03f) / 2f, 0f);
        box.size = new Vector3(BottomRadius * 2f + .04f, y + .03f, BottomRadius * 2f + .04f);
        var point = new GameObject("LabelPoint");
        point.transform.SetParent(root.transform, false);
        point.transform.localPosition = new Vector3(0f, y + .08f, 0f);
        var interaction = label.AddComponent<InteractableObject>();
        interaction.SetMessage(Message);
        interaction.SetInteractableState(InteractableObject.EInteractableState.Label);
        interaction.MaxInteractionRange = 2.5f;
        interaction.displayLocationPoint = point.transform;
        // The game calls this every frame the player looks at the cake; one line per look shows the label was reached.
        interaction.onHovered.AddListener((UnityAction)(() =>
        {
            if (Time.unscaledTime >= _nextHoverLog) Mod.Log("Birthday cake label shown: '" + Message + "'.");
            _nextHoverLog = Time.unscaledTime + 10f;
        }));
        Mod.Log("Birthday cake built: tiers from " + bottomBase + " to " + candleBase + " above the counter, label on layer " + label.layer + ".");
        return root;
    }

    private static Material Material(Shader shader, string name, Color color, float smoothness)
    {
        var material = new Material(shader) { name = name };
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", smoothness);
        material.SetFloat("_Metallic", 0f);
        return material;
    }

    private static GameObject Part(GameObject root, PrimitiveType type, string name, Material material)
    {
        var part = GameObject.CreatePrimitive(type);
        part.name = name;
        Object.Destroy(part.GetComponent<Collider>());
        part.transform.SetParent(root.transform, false);
        var renderer = part.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return part;
    }

    // Unity's cylinder is two units tall, so a height h is a y scale of h / 2. The disc sits on y and moves y to its top.
    private static void Disc(GameObject root, string name, float radius, float height, ref float y, Material material)
    {
        var part = Part(root, PrimitiveType.Cylinder, name, material);
        part.transform.localScale = new Vector3(radius * 2f, height / 2f, radius * 2f);
        part.transform.localPosition = new Vector3(0f, y + height / 2f, 0f);
        y += height;
    }

    // A thin pink band just under the tier's top edge, slightly wider than the tier. Its top stays 1.5 mm under the
    // tier's top, so the two faces never share a plane.
    private static void Rim(GameObject root, string name, float radius, float top, Material material)
    {
        var part = Part(root, PrimitiveType.Cylinder, name, material);
        part.transform.localScale = new Vector3(radius * 2f, RimHeight / 2f, radius * 2f);
        part.transform.localPosition = new Vector3(0f, top - .0015f - RimHeight / 2f, 0f);
    }

    // Short pink drips down the tier's side, of alternating length.
    private static void Drips(GameObject root, string name, float radius, float top, int count, Material material)
    {
        for (int i = 0; i < count; i++)
        {
            float angle = (i + .5f) * Mathf.PI * 2f / count;
            float length = i % 2 == 0 ? .03f : .018f;
            var part = Part(root, PrimitiveType.Capsule, name + i, material);
            part.transform.localScale = new Vector3(.014f, length / 2f, .008f);
            part.transform.localPosition = new Vector3(Mathf.Cos(angle) * (radius + .002f), top - RimHeight - length / 2f + .006f, Mathf.Sin(angle) * (radius + .002f));
            part.transform.localRotation = Quaternion.Euler(0f, -angle * Mathf.Rad2Deg + 90f, 0f);
        }
    }

    // A new scene destroys the cake; the next tick builds it again.
    internal static void Reset()
    {
        _cake = null;
        _light = null;
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
            ", light " + (_light != null ? _light.intensity.ToString("0.00") : "none"));
}
