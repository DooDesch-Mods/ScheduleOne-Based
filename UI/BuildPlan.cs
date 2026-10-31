using System;
using System.Globalization;
using System.Linq;
using Based.Content;
using Based.Core;
using Based.Runtime;
using Newtonsoft.Json;
using Sideload.Api;
using UnityEngine;
using UnityEngine.UI;

namespace Based.UI;

internal static class BuildPlan
{
    internal const string SurfaceId = "based-plan";
    private const float PushInterval = .25f;
    private const float PixelsPerMetre = 24f;
    private static GameObject? _root;
    private static SurfaceHandle? _surface;
    private static bool _closeRequested;
    private static string _notice = "", _lastPushed = "";
    private static float _nextPush;
    internal static SiteDefinition? Site { get; private set; }

    internal static void Open(SiteDefinition site)
    {
        if (_root != null) return;
        if (!Surfaces.Available) throw new InvalidOperationException("Sideload Surfaces is required for the construction plan.");
        _root = new GameObject("Based_ConstructionPlan");
        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 2000;
        _root.AddComponent<GraphicRaycaster>();
        var scaler = _root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = .5f;
        var holder = new GameObject("Plan");
        var rect = holder.AddComponent<RectTransform>();
        rect.SetParent(_root.transform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        Site = site;
        _notice = _lastPushed = "";
        Mod.PlanOpened(site);
        _surface = Surfaces.Mount(rect, SurfaceId, "Based.Assets.based", designShortSide: 540)
            .OnCall("plan.state", _ => State(site))
            .OnCall("plan.buy", kind =>
            {
                if (kind != "rights" && kind != "construction")
                {
                    Mod.Warn("Construction plan sent an unknown purchase: " + kind);
                    return "invalid-purchase";
                }
                _notice = Mod.Purchase(site, kind == "rights" ? PurchaseKind.Rights : PurchaseKind.Construction);
                return _notice;
            })
            .OnCall("plan.close", _ => { _closeRequested = true; return "ok"; });
        Mod.Log("Construction plan opened for " + site.Base.Id + ".");
    }

    internal static void Tick()
    {
        if (_closeRequested)
        {
            _closeRequested = false;
            Mod.EndPlan();
            return;
        }
        if (Site == null || Time.unscaledTime < _nextPush) return;
        _nextPush = Time.unscaledTime + PushInterval;
        string state = State(Site);
        if (state == _lastPushed) return;
        _lastPushed = state;
        _surface?.Emit("plan.changed", state);
    }

    internal static void Close()
    {
        Mod.ClearOutlines();
        if (_root == null) return;
        Surfaces.Unmount(SurfaceId);
        UnityEngine.Object.Destroy(_root);
        _root = null;
        _surface = null;
        Site = null;
        _closeRequested = false;
    }

    private static string Price(int amount) => string.Format(CultureInfo.InvariantCulture, "${0:N0}", amount);

    internal static string State(SiteDefinition site)
    {
        var state = Mod.Engine.GetUpgrade(site.Base.Id, site.UpgradeId);
        var upgrade = site.Upgrade;
        string phase = Mod.Engine.CurrentPhase(state.BaseId, state.UpgradeId) ?? "";
        int nights = Math.Max(0, upgrade.ConstructionCycles - state.CompletedCycles);
        bool ordering = state.Stage is UpgradeStage.RightsMissing or UpgradeStage.RightsOwned;
        bool owned = Mod.Owns(site);
        bool broken = Mod.Failure.Length > 0;
        string paid = Text.T("Paid");
        return JsonConvert.SerializeObject(new
        {
            title = Text.T(site.Title),
            status = Text.T(state.Stage switch
            {
                UpgradeStage.Complete => "Ready to use",
                UpgradeStage.Constructing => site.PhaseTitles.TryGetValue(phase, out string? title) ? title : "Construction confirmed",
                UpgradeStage.RightsOwned => "Rights acquired",
                _ => "Plan your expansion"
            }),
            summary = state.Stage switch
            {
                UpgradeStage.RightsMissing => Text.T("Ming sells the rest of the floor. Construction is a separate order."),
                UpgradeStage.RightsOwned => Text.T("Materials and the crew of three are included."),
                _ => ""
            },
            progress = state.Stage switch
            {
                UpgradeStage.Constructing => nights == 1 ? Text.T("One night left") : Text.T("{0} nights left", nights),
                UpgradeStage.Complete => "",
                _ => Text.T("{0} nights of work", upgrade.ConstructionCycles)
            },
            stage = state.Stage.ToString(),
            ordering,
            canBuy = owned && !broken && ordering,
            buy = Text.T(state.Stage == UpgradeStage.RightsMissing ? "Buy rights" : "Order construction"),
            close = Text.T("Close"),
            rightsLabel = Text.T("Expansion rights"),
            rights = state.Stage == UpgradeStage.RightsMissing ? Price(upgrade.RightsPrice) : paid,
            constructionLabel = Text.T("Construction"),
            construction = state.Stage < UpgradeStage.Constructing ? Price(upgrade.ConstructionPrice) : paid,
            cash = ordering ? Text.T("Paid in cash.") : "",
            restrictions = state.Stage == UpgradeStage.Complete ? Array.Empty<string>() : site.Restrictions.Select(r => Text.T(r)).ToArray(),
            notice = broken ? Text.T("Construction paused because the expansion could not be applied.")
                : !owned ? Text.T("Buy the existing room first.")
                : Mod.HeldBy.Length > 0 ? Text.T("Construction is on hold: move {0} away from the marked walls.", Mod.HeldBy)
                : _notice,
            payment = Payments.StatusText,
            steps = Steps(site, upgrade, state, phase),
            drawing = Drawing(site)
        });
    }

    private static object[] Steps(SiteDefinition site, UpgradeDefinition upgrade, UpgradeState state, string phase)
    {
        bool complete = state.Stage == UpgradeStage.Complete;
        int current = upgrade.Phases.ToList().FindIndex(p => p.Id == phase);
        var steps = upgrade.Phases.Select((p, i) => (object)new
        {
            label = Text.T(site.PhaseTitles[p.Id]),
            state = complete || i < current ? "done" : i == current ? "active" : "todo"
        }).ToList();
        steps.Add(new { label = Text.T("Ready to use"), state = complete ? "done" : "todo" });
        return steps.ToArray();
    }

    // Lays the site's rectangles out in pixels with north at the top, plus the one-metre raster lines.
    private static object Drawing(SiteDefinition site)
    {
        float minX = site.Plan.Min(r => r.MinX), maxX = site.Plan.Max(r => r.MaxX);
        float minZ = site.Plan.Min(r => r.MinZ), maxZ = site.Plan.Max(r => r.MaxZ);
        return new
        {
            width = Mathf.Ceil((maxX - minX) * PixelsPerMetre) + 2,
            height = Mathf.Ceil((maxZ - minZ) * PixelsPerMetre) + 2,
            metre = PixelsPerMetre,
            rooms = site.Plan.Select(r => new
            {
                kind = r.Kind.ToString().ToLowerInvariant(),
                left = (r.MinX - minX) * PixelsPerMetre,
                top = (maxZ - r.MaxZ) * PixelsPerMetre,
                width = (r.MaxX - r.MinX) * PixelsPerMetre,
                height = (r.MaxZ - r.MinZ) * PixelsPerMetre,
                label = r.Label == null ? "" : Text.T(r.Label)
            }).ToArray()
        };
    }
}
