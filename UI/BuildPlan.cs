using System;
using System.Collections.Generic;
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
    private const float PixelsPerMetre = 30f;
    private const float WallPixels = 4f;
    private static GameObject? _root;
    private static SurfaceHandle? _surface;
    private static bool _closeRequested;
    private static string _notice = "", _lastPushed = "", _noticeUpgrade = "";
    private static UpgradeStage _noticeStage;
    private static float _nextPush;
    private static Il2CppScheduleOne.State.State? _gameState;
    private static IntPtr _gameStateParent;
    private static bool _gameStatePushed;
    private static volatile bool _gameStateLeft;
    internal static SiteDefinition? Site { get; private set; }
    // Opened from the folder rather than in the vendor's conversation, so nothing else holds the mouse or ends the plan.
    internal static bool Standalone { get; private set; }

    internal static void Open(SiteDefinition site, bool standalone = false)
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
        Standalone = standalone;
        _notice = _lastPushed = "";
        Payments.ClearSettledStatus();
        Mod.PlanOpened(site);
        if (standalone) PushGameState();
        _surface = Surfaces.Mount(rect, SurfaceId, "Based.Assets.based", designShortSide: 540)
            .OnCall("plan.state", _ => State(site))
            .OnCall("plan.take", _ =>
            {
                RememberNoticeStage(site);
                _notice = ProjectFolder.Give();
                return _notice;
            })
            .OnCall("plan.buy", kind =>
            {
                if (kind != "rights" && kind != "construction")
                {
                    Mod.Warn("Construction plan sent an unknown purchase: " + kind);
                    return "invalid-purchase";
                }
                Payments.ClearSettledStatus();
                string upgradeId = RememberNoticeStage(site);
                _notice = Mod.Purchase(site, upgradeId, kind == "rights" ? PurchaseKind.Rights : PurchaseKind.Construction);
                if (kind == "rights" && upgradeId == site.UpgradeId && _notice.Length == 0) ProjectFolder.GiveAfterPayment(site);
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
        if (_gameStateLeft)
        {
            _gameStateLeft = false;
            Close("the game left its UI state (Escape)");
            return;
        }
        if (Site == null || Time.unscaledTime < _nextPush) return;
        _nextPush = Time.unscaledTime + PushInterval;
        string state = State(Site);
        if (state == _lastPushed) return;
        _lastPushed = state;
        _surface?.Emit("plan.changed", state);
    }

    internal static void Close(string reason)
    {
        Mod.ClearOutlines();
        if (_root == null) return;
        Mod.Log("Construction plan closed because " + reason + ".");
        PopGameState();
        Surfaces.Unmount(SurfaceId);
        UnityEngine.Object.Destroy(_root);
        _root = null;
        _surface = null;
        Site = null;
        Standalone = false;
        _closeRequested = false;
        ProjectFolder.NoteClosed();
    }

    // The game's own UI state frees the mouse, stops the player and answers Escape.
    private static void PushGameState()
    {
        _gameStateLeft = false;
        var inGame = Il2CppScheduleOne.SceneState.Current?.InGame;
        if (inGame == null) throw new InvalidOperationException("The game has no in-game state machine.");
        if (_gameState == null || _gameStateParent != inGame.Pointer)
        {
            var parent = inGame.TryCast<Il2CppScheduleOne.State.IStateMachine>() ?? new Il2CppScheduleOne.State.IStateMachine(inGame.Pointer);
            _gameState = new Il2CppScheduleOne.State.State("Based_ConstructionPlan", parent, Il2CppScheduleOne.State.StateProperties.EPreset.UINoInventory, null);
            _gameState.EnableAutoExitListener(1);
            _gameState.add_OnRemovedFromStack(Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action>((Action)GameStateRemoved));
            _gameStateParent = inGame.Pointer;
        }
        _gameState.PushToDefaultParent();
        _gameStatePushed = true;
        Mod.Log("Construction plan pushed the game's UI state.");
    }

    // Called from inside the state machine's pop, so the plan closes on the next frame.
    private static void GameStateRemoved()
    {
        if (_gameStatePushed) _gameStateLeft = true;
    }

    private static void PopGameState()
    {
        if (!_gameStatePushed || _gameState == null) return;
        _gameStatePushed = false;
        _gameStateLeft = false;
        try { _gameState.RemoveFromDefaultParent(); }
        catch (Exception error) { Mod.Warn("Leaving the game's UI state failed: " + error.Message); }
    }

    private static string Price(int amount) => string.Format(CultureInfo.InvariantCulture, "${0:N0}", amount);

    internal static string State(SiteDefinition site) => State(site, Standalone, wall: false);

    // The page on the folder: everything the plan reports, nothing to press.
    internal static string WallState(SiteDefinition site) => State(site, readOnly: true, wall: true);

    // A refusal belongs to the stage and upgrade it was given for, so it ends when either moves on.
    private static string RememberNoticeStage(SiteDefinition site)
    {
        string upgradeId = Mod.ActiveUpgradeId(site);
        _noticeUpgrade = upgradeId;
        _noticeStage = Mod.Engine.GetUpgrade(site.Base.Id, upgradeId).Stage;
        return upgradeId;
    }

    private static string State(SiteDefinition site, bool readOnly, bool wall)
    {
        string upgradeId = Mod.ActiveUpgradeId(site);
        var followUp = site.FollowUp(upgradeId);
        var state = Mod.Engine.GetUpgrade(site.Base.Id, upgradeId);
        var upgrade = site.UpgradeOf(upgradeId);
        var expansion = Mod.Engine.GetUpgrade(site.Base.Id, site.UpgradeId);
        string phase = Mod.Engine.CurrentPhase(state.BaseId, state.UpgradeId) ?? "";
        int nights = Math.Max(0, upgrade.ConstructionCycles - state.CompletedCycles);
        bool ordering = state.Stage is UpgradeStage.RightsMissing or UpgradeStage.RightsOwned;
        // The vendor sells the first expansion; the folder orders everything after it. The wall page only reports.
        bool locked = wall || readOnly && followUp == null;
        bool rightsSold = upgrade.RightsPrice > 0;
        bool buyRights = rightsSold && state.Stage == UpgradeStage.RightsMissing;
        string done = followUp?.Done ?? "Ready to move in";
        bool owned = Mod.Owns(site);
        bool broken = Mod.Failure.Length > 0;
        return JsonConvert.SerializeObject(new
        {
            title = Text.T(followUp?.Title ?? site.Title),
            status = state.Stage switch
            {
                UpgradeStage.Complete => Text.T(done),
                UpgradeStage.Constructing => Text.T(site.PhaseTitles.TryGetValue(phase, out string? title) ? title : "Construction confirmed"),
                _ => ""
            },
            progress = state.Stage == UpgradeStage.Constructing ? ConstructionJournal.NightsLeft(nights) : "",
            stage = state.Stage.ToString(),
            buyKind = buyRights ? "rights" : "construction",
            ordering,
            wall,
            canOrder = ordering && !locked,
            canBuy = owned && !broken && ordering && !locked,
            take = !locked && followUp == null && ProjectFolder.Offered(state.Stage) ? Text.T("Take the project folder") : "",
            buy = buyRights
                ? Text.T("Buy rights") + " - " + Price(upgrade.RightsPrice)
                : Text.T("Order construction") + " - " + Price(upgrade.ConstructionPrice),
            rightsShown = rightsSold,
            rightsLabel = Text.T("Expansion rights"),
            rights = Price(upgrade.RightsPrice),
            rightsPaid = state.Stage != UpgradeStage.RightsMissing,
            constructionLabel = Text.T("Construction"),
            construction = Price(upgrade.ConstructionPrice),
            constructionPaid = state.Stage >= UpgradeStage.Constructing,
            duration = upgrade.ConstructionCycles == 1 ? Text.T("One night of work") : Text.T("{0} nights of work", upgrade.ConstructionCycles),
            cash = Text.T("Cash only"),
            notes = Notes(site, followUp, state.Stage),
            notice = broken ? Text.T("Construction paused because the expansion could not be applied.")
                : !owned ? Text.T("Buy the Sweatshop first.")
                : Mod.HeldBy.Length > 0 ? Text.T("On hold: move {0} off the red walls.", Mod.HeldBy)
                : locked && !wall && ordering ? Text.T("Order the construction from Mrs. Ming")
                : state.Stage == _noticeStage && upgradeId == _noticeUpgrade ? _notice : "",
            // The confirmation answers a purchase, so it ends with the ordering. Every other status stays until it clears.
            payment = locked || (!ordering && Payments.StatusText == Text.T("Payment confirmed.")) ? "" : Payments.StatusText,
            steps = Steps(site, upgrade, state, phase, done),
            drawing = Drawing(site, expansion.Stage)
        });
    }

    private static string[] Notes(SiteDefinition site, SiteFollowUp? followUp, UpgradeStage stage)
    {
        if (stage == UpgradeStage.Complete) return Array.Empty<string>();
        var notes = (followUp?.Notes ?? site.Restrictions).Select(r => Text.T(r)).ToList();
        if (stage == UpgradeStage.Constructing)
            notes.Add(Text.T(followUp == null ? "The crew finishes one stage each night you sleep." : "The crew builds it the night you sleep."));
        return notes.ToArray();
    }

    private static object[] Steps(SiteDefinition site, UpgradeDefinition upgrade, UpgradeState state, string phase, string done)
    {
        bool complete = state.Stage == UpgradeStage.Complete;
        int current = upgrade.Phases.ToList().FindIndex(p => p.Id == phase);
        var steps = upgrade.Phases.Select((p, i) => (object)new
        {
            label = Text.T(site.PhaseTitles[p.Id]),
            state = complete || i < current ? "done" : i == current ? "active" : "todo"
        }).ToList();
        steps.Add(new { label = Text.T(done), state = complete ? "done" : "todo" });
        return steps.ToArray();
    }

    // Lays the site's rectangles out in pixels with north at the top. The marks on the walls end with the construction.
    private static object Drawing(SiteDefinition site, UpgradeStage stage)
    {
        var solid = site.Plan.Where(r => r.Kind != PlanRoomKind.Opening).ToArray();
        float minX = solid.Min(r => r.MinX), maxX = solid.Max(r => r.MaxX);
        float minZ = solid.Min(r => r.MinZ), maxZ = solid.Max(r => r.MaxZ);
        Box Place(float x0, float z0, float x1, float z1) => new(
            (x0 - minX) * PixelsPerMetre,
            (maxZ - z1) * PixelsPerMetre,
            (x1 - x0) * PixelsPerMetre,
            (z1 - z0) * PixelsPerMetre);
        var cuts = stage == UpgradeStage.Complete ? Array.Empty<PlanRoom>() : site.Plan.Where(r => r.Kind == PlanRoomKind.Opening).ToArray();
        return new
        {
            width = Mathf.Ceil((maxX - minX) * PixelsPerMetre),
            height = Mathf.Ceil((maxZ - minZ) * PixelsPerMetre),
            fills = solid.Select(r =>
            {
                // A pixel of overlap keeps the paper from showing between two rooms of one colour.
                var box = Place(r.MinX, r.MinZ, r.MaxX, r.MaxZ);
                return new { kind = r.Kind.ToString().ToLowerInvariant(), left = box.left - .5f, top = box.top - .5f, width = box.width + 1f, height = box.height + 1f };
            }).ToArray(),
            walls = Outline(solid).Select(w => Thick(Place(w.x0, w.z0, w.x1, w.z1))).ToArray(),
            cuts = cuts.Select(r => Place(r.MinX, r.MinZ, r.MaxX, r.MaxZ)).ToArray(),
            labels = solid.Where(r => r.Label != null).Select(r =>
            {
                var box = Place(r.MinX, r.MinZ, r.MaxX, r.MaxZ);
                float area = solid.Where(o => o.Kind == r.Kind).Sum(o => (o.MaxX - o.MinX) * (o.MaxZ - o.MinZ));
                return new
                {
                    box.left,
                    box.top,
                    box.width,
                    box.height,
                    area = Mathf.RoundToInt(area).ToString(CultureInfo.InvariantCulture) + " m²",
                    name = Text.T(r.Label!)
                };
            }).ToArray()
        };
    }

    private readonly record struct Box(float left, float top, float width, float height);

    private static Box Thick(Box line) =>
        new(line.left - WallPixels / 2, line.top - WallPixels / 2, line.width + WallPixels, line.height + WallPixels);

    // The rooms' edges cut the plan into cells; a wall runs wherever a covered cell meets an empty one.
    private static List<(float x0, float z0, float x1, float z1)> Outline(PlanRoom[] rooms)
    {
        var xs = rooms.SelectMany(r => new[] { r.MinX, r.MaxX }).Distinct().OrderBy(v => v).ToArray();
        var zs = rooms.SelectMany(r => new[] { r.MinZ, r.MaxZ }).Distinct().OrderBy(v => v).ToArray();
        bool Covered(int i, int j) =>
            i >= 0 && j >= 0 && i < xs.Length - 1 && j < zs.Length - 1
            && rooms.Any(r => r.MinX <= xs[i] && r.MaxX >= xs[i + 1] && r.MinZ <= zs[j] && r.MaxZ >= zs[j + 1]);
        var walls = new List<(float x0, float z0, float x1, float z1)>();
        // Pieces in a line become one wall, so rounding cannot leave a step where two of them meet.
        void Add(float x0, float z0, float x1, float z1)
        {
            int joined = walls.FindIndex(w => x0 == x1 ? w.x0 == x0 && w.x1 == x0 && w.z1 == z0 : w.z0 == z0 && w.z1 == z0 && w.x1 == x0);
            if (joined < 0) walls.Add((x0, z0, x1, z1));
            else walls[joined] = (walls[joined].x0, walls[joined].z0, x1, z1);
        }
        for (int i = 0; i < xs.Length - 1; i++)
        for (int j = 0; j < zs.Length - 1; j++)
        {
            if (!Covered(i, j)) continue;
            if (!Covered(i - 1, j)) Add(xs[i], zs[j], xs[i], zs[j + 1]);
            if (!Covered(i + 1, j)) Add(xs[i + 1], zs[j], xs[i + 1], zs[j + 1]);
        }
        for (int j = 0; j < zs.Length - 1; j++)
        for (int i = 0; i < xs.Length - 1; i++)
        {
            if (!Covered(i, j)) continue;
            if (!Covered(i, j - 1)) Add(xs[i], zs[j], xs[i + 1], zs[j]);
            if (!Covered(i, j + 1)) Add(xs[i], zs[j + 1], xs[i + 1], zs[j + 1]);
        }
        return walls;
    }
}
