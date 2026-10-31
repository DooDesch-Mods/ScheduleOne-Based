using System;
using System.Linq;
using Based.Content.Ming;
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
    private static GameObject? _root;
    private static SurfaceHandle? _surface;
    private static bool _closeRequested;
    private static string _notice = "";
    internal static bool IsOpen => _root != null;

    internal static void Open()
    {
        if (IsOpen) return;
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
        _surface = Surfaces.Mount(rect, SurfaceId, "Based.Assets.based", designShortSide: 540)
            .OnCall("plan.state", _ => State())
            .OnCall("plan.buy", kind =>
            {
                if (kind != "rights" && kind != "construction") return "invalid-purchase";
                _notice = Mod.Purchase(kind == "rights" ? PurchaseKind.Rights : PurchaseKind.Construction);
                return _notice;
            })
            .OnCall("plan.close", _ => { _closeRequested = true; return "ok"; });
        _notice = "";
        Mod.Log("Construction plan opened from Ming dialogue.");
    }

    internal static void Push(string state)
    {
        _surface?.Emit("plan.changed", state);
    }

    internal static void Tick()
    {
        if (_closeRequested)
        {
            _closeRequested = false;
            Mod.EndPlan();
            return;
        }
    }

    internal static void Close()
    {
        if (_root == null) return;
        Surfaces.Unmount(SurfaceId);
        UnityEngine.Object.Destroy(_root);
        _root = null;
        _surface = null;
        _closeRequested = false;
    }

    internal static string State()
    {
        var state = Mod.Engine.GetUpgrade(MingDefinition.BaseId, MingDefinition.UpgradeId);
        var definition = Mod.Engine.Definitions.First(b => b.Id == MingDefinition.BaseId).Upgrades.First(u => u.Id == MingDefinition.UpgradeId);
        string phase = Mod.Engine.CurrentPhase(state.BaseId, state.UpgradeId) ?? "";
        string title = state.Stage switch
        {
            UpgradeStage.Complete => "Ready to use",
            UpgradeStage.Constructing => phase switch { "breakthrough" => "Breakthrough", "frame" => "Frame", "roof" => "Roof and interior", _ => "Construction confirmed" },
            UpgradeStage.RightsOwned => "Rights acquired",
            _ => "Plan your expansion"
        };
        bool owned = Mod.OwnsMing();
        bool blocked = Mod.Failure.Length > 0;
        return JsonConvert.SerializeObject(new
        {
            title = Text.T("More room above Ming"), status = Text.T(title), phase,
            stage = state.Stage.ToString(), nights = Math.Max(0, definition.ConstructionCycles - state.CompletedCycles),
            rights = definition.RightsPrice, construction = definition.ConstructionPrice,
            canBuy = owned && !blocked && state.Stage is UpgradeStage.RightsMissing or UpgradeStage.RightsOwned,
            notice = blocked ? Text.T("Construction paused because the expansion could not be applied.")
                : !owned ? Text.T("Purchase the existing room first") : _notice,
            payment = Payments.StatusText,
            labels = new
            {
                rights = Text.T("Expansion rights"), construction = Text.T("Order construction"),
                nights = Text.T("Four nights"), existing = Text.T("Existing room"), expansion = Text.T("New production space"),
                details = Text.T("Your current room stays usable. Keep the marked doorway clear during construction."),
                rightsDetail = Text.T("Ming sells the rest of the floor and the expansion rights. Construction is a separate order."),
                buildDetail = Text.T("Materials and the three-person crew are included. Sleep anywhere to advance the work."),
                delivery = Text.T("No delivery dock is blocked at this property."),
                buyRights = Text.T("Buy rights"), buyConstruction = Text.T("Confirm construction"), close = Text.T("Close"),
                working = Text.T("Work in progress"), complete = Text.T("Complete")
            }
        });
    }
}
