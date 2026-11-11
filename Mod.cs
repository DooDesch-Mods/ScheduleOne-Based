using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Based.Content;
using Based.Content.Ming;
using Based.Core;
using Based.Persistence;
using Based.Runtime;
using Based.UI;
using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Dialogue;
using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.Variables;
using MapExpansions;
using MelonLoader;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Events;
#if SNITCH
using Snitch.Api;
#endif

[assembly: MelonInfo(typeof(Based.Mod), "Based", DooDesch.ModVersion.Current, "DooDesch", "https://github.com/DooDesch-Mods/ScheduleOne-Based")]
[assembly: MelonGame("TVGS", "Schedule I")]
[assembly: MelonAdditionalDependencies("MapExpansions", "Personnel", "Sideload")]

namespace Based;

public sealed class Mod : MelonMod
{
    private const string Wire = "BASED-STATE1:";
    private const float VendorWarnAfter = 30f;
    private const float RequestInterval = 5f;
    private const float PlanRequestTimeout = 3f;
    private const float HoldCheckInterval = 1f;

    // Built on first use, not in the type initializer: the site types need S1API, and the missing-mods check has to
    // run first so a missing S1API still reaches the player.
    private static IReadOnlyList<SiteDefinition>? _sites;
    internal static IReadOnlyList<SiteDefinition> Sites => _sites ??= new[] { MingSite.Create(), Content.StorageUnit.StorageSite.Create() };
    // The vendors that name missing mods, without building the sites.
    private static readonly (string NpcId, string ContainerId, string Choice)[] NoticeVendors =
        { (MingSite.VendorNpcId, MingSite.VendorContainerId, MingSite.RequestChoice) };
    internal static Mod Instance { get; private set; } = null!;
    internal static UpgradeEngine Engine { get; private set; } = null!;
    // Set when the world and the saved state no longer match; only this blocks saving.
    internal static string Failure { get; private set; } = "";
    // Name of the item that held construction back last night, or empty.
    internal static string HeldBy { get; private set; } = "";

    private sealed class Vendor
    {
        public S1API.Entities.NPC Npc = null!;
        // Empty for menu sites, whose vendor offers one entry for all of them.
        public DialogueController.DialogueChoice? Choice;
        public DialogueHandler Handler = null!;
    }

    private static readonly Dictionary<SiteDefinition, Vendor> Vendors = new();
    private static readonly HashSet<SiteDefinition> MissingVendors = new();
    private static readonly Dictionary<int, string> PeerSnapshots = new();
    private static readonly List<BuildableItem> Outlined = new();
    private static readonly HashSet<string> ContainedErrors = new();
    private static readonly HashSet<string> NoticeBound = new(), NoticeMissing = new();
    private static SiteDefinition? _planRequest;
    private static float _planRequestedAt, _nextBind, _nextRequest, _nextHoldCheck, _loadedAt = -1;
    private static bool _main, _snapshotReceived, _worldDirty = true, _initialized, _waitingNetwork, _saveWarningShown;
    private static string _lastWorld = "", _lastAck = "";

    private sealed class HostAuthority : IAuthority
    {
        public bool CanMutate => InstanceFinder.IsServer;
    }

    public override void OnInitializeMelon()
    {
        Instance = this;
        Text.Register();
        MissingMods.Check();
        if (MissingMods.Any)
        {
            LoggerInstance.Error("Based stays off: " + MissingMods.Line + " " + string.Join(", ", NoticeVendors.Select(v => v.NpcId).Distinct()) + " names them in the dialogue.");
            // The patches serve the expansion and its save, and without S1API neither exists; several of them reach
            // S1API types and would throw on every call.
            if (!MissingMods.S1ApiLoaded)
            {
                HarmonyInstance.UnpatchSelf();
                LoggerInstance.Msg("Removed Based's patches; the game runs unchanged apart from the notice.");
            }
            return;
        }
        InitializeWithRequiredMods();
    }

    // Holds every first use of a required mod. The runtime resolves those types when it compiles this method, so
    // OnInitializeMelon must not contain them, and inlining would put them back there.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void InitializeWithRequiredMods()
    {
        SiteDefinition.ValidateAll(Sites);
        var foreignMenu = Sites.FirstOrDefault(s => s.Menu && s.VendorNpcId != PlanCopies.RayId);
        if (foreignMenu != null) throw new InvalidOperationException(foreignMenu.Base.Id + ": only Ray offers a menu of builds.");
        Engine = new UpgradeEngine(Sites.Select(s => s.Base), new HostAuthority());
        foreach (var site in Sites) site.RegisterWorld();
#if DEBUG
        DockProbe.Register();
#endif
        Payments.Initialize(Engine, Changed);
        Payments.ValidatePurchase = ValidatePurchase;
        NativeBus.HostReceived += ReceiveHost;
        NativeBus.ClientReceived += ReceiveClient;
        MapExpansionsApi.PeerWorldStateRequested += SendSnapshot;
        S1API.GameTime.TimeManager.OnSleepEnd += SleepEnded;
        foreach (var site in Sites) site.Crew?.Initialize(Log, Warn);
        _initialized = true;
        LoggerInstance.Msg("Based initialized; " + Sites.Count + " site(s) registered with MapExpansions.");
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        // The folder item is an S1API item; without S1API there is nothing to register it with.
        if (sceneName == "Main" && MissingMods.S1ApiLoaded) RegisterItems();
        if (MissingMods.Any)
        {
            MissingModsSceneLoaded(sceneName);
            return;
        }
        if (_initialized) InitializedSceneLoaded(sceneName);
    }

    // The runtime compiles a method as a whole, so the S1API types of the normal path stay out of OnSceneWasLoaded.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InitializedSceneLoaded(string sceneName)
    {
        if (sceneName == "Menu")
        {
            _main = false;
#if DEBUG
            DockProbe.ResetForMenu();
            RoomProbe.ResetForMenu();
#endif
            BuildPlan.Close("the game returned to the menu");
            ConstructionJournal.Reset();
            ProjectFolder.Reset();
            PlanCopies.Reset();
            RayMenu.Reset();
            StaffRoom.Reset();
            foreach (var site in Sites) site.Crew?.Cleanup();
            Payments.ResetForMenu();
            NativeBus.Stop();
            Engine.Reset();
            BasedSave.BeginLoad();
            Vendors.Clear();
            MissingVendors.Clear();
            PeerSnapshots.Clear();
            ContainedErrors.Clear();
            Outlined.Clear();
            _planRequest = null;
            _lastWorld = _lastAck = Failure = HeldBy = "";
            _snapshotReceived = _saveWarningShown = false;
            _nextRequest = 0;
            _loadedAt = -1;
            _worldDirty = true;
        }
        else if (sceneName == "Main")
        {
            _main = true;
            _nextBind = 0;
            _worldDirty = true;
        }
    }

    // The game drops every runtime item before a scene change, so the folder comes back with each Main scene. A joining
    // client reads items from the host's spawn data before the scene's network objects run Start, so this cannot wait.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RegisterItems()
    {
        try { ProjectFolder.Register(); }
        catch (Exception error) { MelonLogger.Error("[Based] Registering the project folder failed: " + error); }
    }

    // The save state of the last save must not carry over into the next one, with or without the required mods.
    private static void MissingModsSceneLoaded(string sceneName)
    {
        if (sceneName == "Menu")
        {
            _main = false;
            if (MissingMods.S1ApiLoaded) BeginSaveLoad();
            NoticeBound.Clear();
            ContainedErrors.Clear();
            Failure = "";
            _saveWarningShown = false;
        }
        else if (sceneName == "Main")
        {
            _main = true;
            _nextBind = 0;
        }
    }

    // BasedSave is an S1API saveable; this method keeps it out of callers that run without S1API.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void BeginSaveLoad() => BasedSave.BeginLoad();

    public override void OnUpdate()
    {
        if (MissingMods.Any)
        {
            if (_main) TickMissingMods();
            return;
        }
        if (!_initialized || !_main) return;
#if SNITCH
        using (Profiler.Sample("Based.Frame")) Frame();
#else
        Frame();
#endif
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Frame()
    {
        try
        {
            // The plan has to stay closable after a failure, so it runs before the failure check.
            TickPlan();
            // The folder reports a failed expansion too, so it runs before the failure check as well.
            if (LoadManager.Instance?.IsGameLoaded == true) TickFolder();
            if (Failure.Length > 0) return;
            if (!NativeBus.Start()) return;
            if (!NativeBus.IsConnected)
            {
                if (!_waitingNetwork) { Log("Waiting for the game's network session before syncing construction."); _waitingNetwork = true; }
                return;
            }
            if (_waitingNetwork) { Log("Game network session connected; construction sync resumed."); _waitingNetwork = false; }
            if (NativeBus.IsHost && BasedSave.SaveIdKnown) Payments.SetSession(BasedSave.SaveId);
            Payments.Tick();
            TickClientSync();
            if (_worldDirty) ApplyWorld();
            if (Failure.Length > 0) return;
            var load = LoadManager.Instance;
            if (load == null || !load.IsGameLoaded) return;
            if (_loadedAt < 0) _loadedAt = Time.unscaledTime;
            if (Time.unscaledTime >= _nextBind)
            {
                _nextBind = Time.unscaledTime + 1f;
                foreach (var site in Sites.Where(s => !s.Menu)) BindVendor(site);
                try { PlanCopies.Tick(Time.unscaledTime - _loadedAt); }
                catch (Exception error) { Contain("plan copies at Ray", error); }
                try { RayMenu.Tick(Time.unscaledTime - _loadedAt); }
                catch (Exception error) { Contain("expansion menu at Ray", error); }
            }
            foreach (var site in Sites) TickCrew(site);
            TickHold();
            TickJournal();
        }
        catch (Exception error) { Contain("runtime update", error); }
    }

    // The quest names what still stands on the walls, so the list shrinks while the player clears them.
    private static void TickHold()
    {
        if (!NativeBus.IsHost || HeldBy.Length == 0 || Time.unscaledTime < _nextHoldCheck) return;
        _nextHoldCheck = Time.unscaledTime + HoldCheckInterval;
        var site = Sites.FirstOrDefault(s => Constructing(s) != null);
        SetHeld(site == null ? "" : BlockerNames(BlockingItems(site, Constructing(site)!)));
    }

    internal static UpgradeStage StageOf(SiteDefinition site, string upgradeId) => Engine.GetUpgrade(site.Base.Id, upgradeId).Stage;

    internal static string? Constructing(SiteDefinition site) => site.ConstructingId(id => StageOf(site, id));

    private static void TickFolder()
    {
        try { ProjectFolder.Tick(); }
        catch (Exception error) { Contain("project folder", error); }
    }

    private static void TickJournal()
    {
        if (!NativeBus.IsHost && !_snapshotReceived) return;
        try { ConstructionJournal.Tick(Sites); }
        catch (Exception error) { Contain("construction quest", error); }
    }

    private static void TickClientSync()
    {
        if (NativeBus.IsHost) return;
        // A request can arrive before the host is ready to answer, so it repeats until a snapshot came back.
        if (!_snapshotReceived && Time.unscaledTime >= _nextRequest)
        {
            _nextRequest = Time.unscaledTime + RequestInterval;
            NativeBus.SendToHost(Wire + JsonConvert.SerializeObject(new StatePacket { Kind = "request" }));
        }
        if (!_snapshotReceived || !WorldApplied()) return;
        string key = BasedSave.SaveId + ":" + Engine.Revision;
        if (_lastAck == key) return;
        NativeBus.SendToHost(Wire + JsonConvert.SerializeObject(new StatePacket
            { Kind = "ack", SaveId = BasedSave.SaveId, Revision = Engine.Revision }));
        _lastAck = key;
    }

    private static void TickCrew(SiteDefinition site)
    {
        if (site.Crew == null) return;
        try
        {
            // The crew works the stages of a build; one without stages goes up in a night without it.
            string? building = Constructing(site);
            string followed = building != null && site.UpgradeOf(building).Phases.Count > 0 ? building : site.UpgradeId;
            var state = Engine.GetUpgrade(site.Base.Id, followed);
            site.Crew.Update(state.Stage, Engine.CurrentPhase(state.BaseId, state.UpgradeId));
            site.Crew.Tick();
        }
        catch (Exception error) { Contain("crew of " + site.Base.Id, error); }
    }

    private static void TickPlan()
    {
        try
        {
            if (_planRequest != null)
            {
                if (VendorTalking(_planRequest))
                {
                    var site = _planRequest;
                    _planRequest = null;
                    BuildPlan.Open(site);
                }
                else if (Time.unscaledTime > _planRequestedAt + PlanRequestTimeout)
                {
                    Warn("Construction plan not opened: the conversation with " + _planRequest.VendorNpcId + " ended first.");
                    _planRequest = null;
                }
            }
            if (BuildPlan.Site != null && !BuildPlan.Standalone && !VendorTalking(BuildPlan.Site)) BuildPlan.Close("the conversation ended");
            BuildPlan.Tick();
        }
        catch (Exception error)
        {
            Contain("construction plan", error);
            BuildPlan.Close("of an error");
        }
    }

    private static bool VendorTalking(SiteDefinition site) =>
        Vendors.TryGetValue(site, out var vendor) && vendor.Npc.Dialogue.IsDialogueInProgress;

    // A menu site has no choice of its own: the vendor's menu entry serves it, and the plan only needs his conversation.
    internal static void RegisterMenuVendor(SiteDefinition site, S1API.Entities.NPC npc, DialogueHandler handler)
    {
        if (Vendors.ContainsKey(site)) return;
        Vendors[site] = new Vendor { Npc = npc, Handler = handler };
        Log(site.VendorNpcId + "'s menu entry serves the plan of " + site.Base.Id + ".");
    }

    private static void BindVendor(SiteDefinition site)
    {
        if (Vendors.TryGetValue(site, out var bound))
        {
            RefreshChoice(site, bound);
            return;
        }
        var vendor = AddVendorChoice(site, Text.T(site.VendorLine), () => RequestPlan(site));
        if (vendor == null) return;
        RefreshChoice(site, vendor);
        Log(site.VendorNpcId + "'s dialogue extended with the construction plan.");
    }

    // Without the required mods the entry stays, owned room or not, and its line says what is missing.
    private static void TickMissingMods()
    {
        try
        {
            if (LoadManager.Instance?.IsGameLoaded != true || Time.unscaledTime < _nextBind) return;
            if (_loadedAt < 0) _loadedAt = Time.unscaledTime;
            _nextBind = Time.unscaledTime + 1f;
            foreach (var vendor in NoticeVendors)
            {
                if (NoticeBound.Contains(vendor.NpcId)) continue;
                if (!MissingModsNotice.Bind(vendor.NpcId, vendor.ContainerId, Text.T(vendor.Choice), MissingMods.Line,
                        line => Log("The player asked for the expansion: " + line)))
                {
                    if (Time.unscaledTime - _loadedAt > VendorWarnAfter && NoticeMissing.Add(vendor.NpcId))
                        Warn(vendor.NpcId + " is missing from this save; nobody names the missing mods.");
                    continue;
                }
                NoticeBound.Add(vendor.NpcId);
                Log(vendor.NpcId + "'s dialogue names the missing mods.");
            }
        }
        catch (Exception error) { Contain("missing mods notice", error); }
    }

    private static Vendor? AddVendorChoice(SiteDefinition site, string line, Action chosen)
    {
        var npc = S1API.Entities.NPC.Get(site.VendorNpcId);
        if (npc == null)
        {
            if (Time.unscaledTime - _loadedAt > VendorWarnAfter && MissingVendors.Add(site))
                Warn(site.VendorNpcId + " is missing from this save; the plan for " + site.Base.Id + " stays unavailable.");
            return null;
        }
        var controller = npc.gameObject.GetComponentInChildren<DialogueController>(true);
        var handler = npc.gameObject.GetComponentInChildren<DialogueHandler>(true);
        if (controller == null || handler == null) throw new InvalidOperationException(site.VendorNpcId + " has no dialogue controller or handler.");
        npc.Dialogue.BuildAndRegisterContainer(site.VendorContainerId, b => b.AddNode("ENTRY", line));
        Conversation? container = null;
        for (int i = 0; i < handler.dialogueContainers.Count; i++)
            if (handler.dialogueContainers[i].name == site.VendorContainerId) container = handler.dialogueContainers[i];
        if (container == null) throw new InvalidOperationException("S1API did not register the conversation " + site.VendorContainerId + ".");
        var choice = new DialogueController.DialogueChoice
        {
            Conversation = container,
            Priority = 55,
            onChoosen = new UnityEvent()
        };
        choice.onChoosen.AddListener(chosen);
        controller.AddDialogueChoice(choice, 55);
        var vendor = new Vendor { Npc = npc, Choice = choice, Handler = handler };
        Vendors[site] = vendor;
        return vendor;
    }

    // The choice offers what is possible right now: nothing before the room is owned or once the work is done.
    private static void RefreshChoice(SiteDefinition site, Vendor vendor)
    {
        if (vendor.Choice == null) return;
        var stage = Engine.GetUpgrade(site.Base.Id, site.UpgradeId).Stage;
        vendor.Choice.Enabled = Owns(site) && stage != UpgradeStage.Complete;
        vendor.Choice.ChoiceText = Text.T(stage == UpgradeStage.Constructing ? site.ProgressChoice : site.RequestChoice);
    }

    // The plan hangs on the vendor's conversation, whose last line ends on any click or key. The plan ends it instead.
    internal static bool HoldsConversation(DialogueHandler handler)
    {
#if DEBUG
        if (DebugFreeConversation) return false;
#endif
        var site = BuildPlan.Standalone ? _planRequest : BuildPlan.Site ?? _planRequest;
        return site != null && Vendors.TryGetValue(site, out var vendor) && vendor.Handler.Pointer == handler.Pointer;
    }

    internal static void RequestPlan(SiteDefinition site)
    {
        _planRequest = site;
        _planRequestedAt = Time.unscaledTime;
    }

    internal static bool Owns(SiteDefinition site) => S1API.Property.PropertyManager.GetOwnedProperties()
        .Any(p => p.PropertyCode == site.Base.PropertyId);

    // A site by base id, property code or index; without a key the first site, so old commands keep their meaning.
    internal static SiteDefinition FindSite(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return Sites[0];
        var site = Sites.FirstOrDefault(s => string.Equals(s.Base.Id, key, StringComparison.OrdinalIgnoreCase)
            || string.Equals(s.Base.PropertyId, key, StringComparison.OrdinalIgnoreCase));
        if (site != null) return site;
        if (int.TryParse(key, out int index) && index >= 0 && index < Sites.Count) return Sites[index];
        throw new ArgumentException("Unknown site '" + key + "'. Known: "
            + string.Join(", ", Sites.Select((s, i) => i + "=" + s.Base.Id + " (" + s.Base.PropertyId + ")")) + ".");
    }

    private static string? ValidatePurchase(PurchaseCommand command)
    {
        var site = Sites.FirstOrDefault(s => s.Base.Id == command.BaseId);
        if (site == null) return "unknown-base";
        if (!Owns(site)) return Text.T(site.BuyFirst);
        // Only a build with marked walls needs them free; the Sweatshop's dock stands outside the room.
        if (command.Kind != PurchaseKind.Construction) return null;
        var items = BlockingItems(site, command.UpgradeId);
        return items.Length > 0 ? BlockedText(items) : null;
    }

    internal static string ActiveUpgradeId(SiteDefinition site) =>
        site.ActiveUpgradeId(id => Engine.GetUpgrade(site.Base.Id, id).Stage);

    internal static string Purchase(SiteDefinition site, string upgradeId, PurchaseKind kind)
    {
        if (Failure.Length > 0) return Text.T("Construction paused because the expansion could not be applied.");
        if (!Owns(site)) return Text.T(site.BuyFirst);
        if (!NativeBus.IsHost && !_snapshotReceived) return Text.T("Waiting for the host's construction plan.");
        if (kind == PurchaseKind.Construction)
        {
            var items = OutlineBlocking(site, upgradeId);
            if (items.Length > 0)
            {
                Log("Construction order refused, on the marked walls: " + BlockerNames(items) + ".");
                return BlockedText(items);
            }
        }
        try
        {
            Payments.Request(kind, site.Base.Id, upgradeId);
            Log("Purchase requested: " + kind + " of " + site.Base.Id + "/" + upgradeId + ".");
            return "";
        }
        catch (InvalidOperationException error)
        {
            Warn("Purchase refused: " + error.Message);
            return error.Message;
        }
    }

    // The outlines end with the plan, so the text names every blocker at once.
    private static string BlockedText(BuildableItem[] items) => Text.T("Move {0} off the red walls.", BlockerNames(items));

    private static string BlockerNames(BuildableItem[] items) => string.Join(", ", items.Select(i => i.GetManagementName()).Distinct());

    internal static BuildableItem[] BlockingItems(SiteDefinition site) => BlockingItems(site, site.UpgradeId);

    internal static BuildableItem[] BlockingItems(SiteDefinition site, string upgradeId)
    {
        var items = new Dictionary<IntPtr, BuildableItem>();
        foreach (var area in site.KeepClearOf(upgradeId))
        {
            var colliders = Physics.OverlapBox(area.center, area.extents, Quaternion.identity);
            for (int i = 0; i < colliders.Length; i++)
            {
                var item = colliders[i].GetComponentInParent<BuildableItem>();
                if (item != null && !item.IsDestroyed) items[item.Pointer] = item;
            }
        }
        return items.Values.ToArray();
    }

    // Only for the player looking at the plan: the host must not light up items for a request from elsewhere.
    internal static BuildableItem[] OutlineBlocking(SiteDefinition site, string upgradeId)
    {
        ClearOutlines();
        var items = BlockingItems(site, upgradeId);
        foreach (var item in items)
        {
            item.ShowOutline(Color.yellow);
            Outlined.Add(item);
        }
        return items;
    }

    internal static void ClearOutlines()
    {
        foreach (var item in Outlined)
            if (item != null && !item.IsDestroyed) item.HideOutline();
        Outlined.Clear();
    }

    // Marks what stands in the way of the build the plan shows: once its rights are bought, or right away when it
    // sells no rights. An empty id is the list of builds, which marks nothing.
    internal static void PlanShows(SiteDefinition site, string upgradeId)
    {
        if (upgradeId.Length == 0) { ClearOutlines(); return; }
        var state = Engine.GetUpgrade(site.Base.Id, upgradeId);
        bool orderable = state.Stage is UpgradeStage.RightsOwned or UpgradeStage.Constructing
            || state.Stage == UpgradeStage.RightsMissing && upgradeId != site.UpgradeId && site.UpgradeOf(upgradeId).RightsPrice == 0;
        if (orderable) OutlineBlocking(site, upgradeId);
        else ClearOutlines();
    }

    internal static void PlanOpened(SiteDefinition site, string upgradeId)
    {
        PlanShows(site, upgradeId);
        // The hold only names last night's blocker, so it ends as soon as the player has moved the item.
        if (NativeBus.IsHost && HeldBy.Length > 0 && Outlined.Count == 0) SetHeld("");
    }

    private static void SetHeld(string item)
    {
        if (HeldBy == item) return;
        HeldBy = item;
        Log(item.Length > 0 ? "Construction hold names: " + item + "." : "Construction hold cleared.");
        BroadcastSnapshot();
    }

    internal static void EndPlan()
    {
        var site = BuildPlan.Site;
        bool conversation = !BuildPlan.Standalone;
        BuildPlan.Close("the player closed it");
        if (conversation && site != null && Vendors.TryGetValue(site, out var vendor)) vendor.Npc.Dialogue.End();
    }

#if DEBUG
    // Lets a test see the conversation end the plan, as it does without the hold.
    internal static bool DebugFreeConversation;

    internal static void DebugBlockers(SiteDefinition site)
    {
        var property = UnityEngine.Object.FindObjectsOfType<Il2CppScheduleOne.Property.Property>().FirstOrDefault(p => p.PropertyCode == site.Base.PropertyId);
        Log("baseddiag: blockers=" + BlockerNames(BlockingItems(site)) + "; culled=" + (property?.IsContentCulled.ToString() ?? "no property")
            + "; placed items=" + (property?.BuildableItems.Count.ToString() ?? "?"));
    }

    internal static void DebugPlan(SiteDefinition site)
    {
        if (!Vendors.TryGetValue(site, out var vendor)) throw new InvalidOperationException(site.VendorNpcId + " dialogue is not ready.");
        vendor.Npc.Dialogue.Start(site.VendorContainerId);
        RequestPlan(site);
    }

    internal static void DebugAdvance() => AdvanceCycle(Engine.ExportState().LastSleepCycle + 1);

    // A jump to another construction state starts over, so one bad state does not need a restart of the game.
    internal static void DebugClearFailure()
    {
        Failure = "";
        ContainedErrors.Clear();
    }
#endif

    internal static void Restore(BasedState state)
    {
        if (MissingMods.Any)
        {
            // Started work has world that cannot be built now, and saving would drop what stands in that room.
            if (state.Upgrades.Any(u => u.Stage is UpgradeStage.Constructing or UpgradeStage.Complete))
                Fail("loading Based save", new InvalidOperationException("Construction has started and required mods are missing. " + MissingMods.Line));
            else Log("Saved Based state has no construction; it stays as it is until the required mods are back.");
            return;
        }
        Engine.RestoreState(state);
        Payments.SetSession(BasedSave.SaveId);
        _worldDirty = true;
        if (!ApplyWorld()) return;
        Log(WorldApplied()
            ? "Saved Based state and world restored before property objects."
            : "Saved Based state restored; world placement is pending.");
    }

    private static void SleepEnded(int skippedMinutes)
    {
        if (!NativeBus.IsHost) { Log("Sleep ended; the host advances construction."); return; }
        if (!_main) { Log("Sleep ended outside the main scene; construction unchanged."); return; }
        if (Failure.Length > 0) { Warn("Sleep ended; construction unchanged because of an earlier failure: " + Failure); return; }
        try { AdvanceCycle((long)VariableDatabase.Instance.GetValue<float>("Sleep_Count")); }
        catch (Exception error) { Fail("sleep transition", error); }
    }

    private static void AdvanceCycle(long cycle)
    {
        foreach (var site in Sites.Where(s => Constructing(s) != null))
        {
            var items = BlockingItems(site, Constructing(site)!);
            if (items.Length == 0) continue;
            string name = BlockerNames(items);
            Warn("Construction of " + site.Base.Id + " held in sleep cycle " + cycle + ": " + name + " stands on a marked wall.");
            SetHeld(name);
            return;
        }
        SetHeld("");
        var before = Engine.ExportState();
        var result = Engine.AdvanceSleep(cycle);
        if (!result.Applied) { Log("Sleep skipped: " + result.Reason); return; }
        _worldDirty = true;
        if (!ApplyWorld())
        {
            Engine.RestoreState(before);
            return;
        }
        Changed();
        Log("Construction sleep cycle " + cycle + " applied; revision " + Engine.Revision + ".");
    }

    internal static void Changed()
    {
        _worldDirty = true;
        if (!ApplyWorld()) return;
        BasedSave.Capture(Engine.ExportState());
        BroadcastSnapshot();
    }

    private static void BroadcastSnapshot()
    {
        if (!NativeBus.IsHost) return;
        foreach (int peer in PeerSnapshots.Keys.ToArray())
        {
            // One unreachable client must not stop the others from getting the new state.
            try
            {
                if (!InstanceFinder.ServerManager.Clients.ContainsKey(peer)) { PeerSnapshots.Remove(peer); continue; }
                SendSnapshot(peer);
            }
            catch (Exception error) { Warn("Snapshot for peer " + peer + " failed: " + error.Message); }
        }
    }

    // Each target ends up with one state: the phase under construction wins over finished upgrades on the same target.
    private static Dictionary<string, string> DesiredWorld()
    {
        var desired = new Dictionary<string, string>(StringComparer.Ordinal);
        var underConstruction = new HashSet<string>(StringComparer.Ordinal);
        foreach (var definition in Engine.Definitions)
        foreach (var upgrade in definition.Upgrades)
        foreach (var effect in upgrade.Effects.Where(e => e.Kind == UpgradeEffectKinds.WorldState))
        {
            var state = Engine.GetUpgrade(definition.Id, upgrade.Id);
            string? phaseId = Engine.CurrentPhase(definition.Id, upgrade.Id);
            string wanted = state.Stage == UpgradeStage.Complete ? effect.StateId
                : state.Stage == UpgradeStage.Constructing ? upgrade.Phases.FirstOrDefault(p => p.Id == phaseId)?.WorldStateId ?? "" : "";
            if (underConstruction.Contains(effect.TargetId)) continue;
            if (state.Stage == UpgradeStage.Constructing && wanted.Length > 0) underConstruction.Add(effect.TargetId);
            if (wanted.Length > 0 || !desired.ContainsKey(effect.TargetId)) desired[effect.TargetId] = wanted;
        }
        return desired;
    }

    private static bool ApplyWorld()
    {
        var requested = new List<string>();
        foreach (var target in DesiredWorld())
        {
            if (!MapExpansionsApi.TrySetWorldState(target.Key, target.Value, out string error))
            {
                Fail("applying world state " + target.Key + "/" + target.Value, new InvalidOperationException(error));
                return false;
            }
            requested.Add(target.Key + "/" + target.Value);
        }
        // Employees above the game's limit would be dropped on load, and the next save would lose them for good.
        try
        {
            foreach (var site in Sites) StaffRoom.Apply(site);
        }
        catch (Exception error)
        {
            Fail("raising the employee limit", error);
            return false;
        }
        _worldDirty = false;
        string desiredStates = string.Join(",", requested);
        if (_lastWorld != desiredStates)
        {
            _lastWorld = desiredStates;
            Log("World states requested: " + desiredStates);
        }
        return true;
    }

    private static string Snapshot() => Wire + JsonConvert.SerializeObject(new StatePacket
        { Kind = "snapshot", SaveId = BasedSave.SaveId, Revision = Engine.Revision, State = Engine.ExportSnapshot(), HeldBy = HeldBy });

    private static bool WorldApplied() => DesiredWorld().Keys.All(MapExpansionsApi.IsWorldStateApplied);

    private static void SendSnapshot(int peer)
    {
        if (!MapExpansionsApi.RequirePeerWorldStateAck(peer, Engine.Revision))
            throw new InvalidOperationException("Could not guard world-state replication for peer " + peer);
        PeerSnapshots[peer] = BasedSave.SaveId + ":" + Engine.Revision;
        NativeBus.SendToPeer(peer, Snapshot());
    }

    private static void ReceiveHost(int peer, string payload)
    {
        if (!payload.StartsWith(Wire, StringComparison.Ordinal)) return;
        try
        {
            var packet = JsonConvert.DeserializeObject<StatePacket>(payload.Substring(Wire.Length))
                ?? throw new InvalidOperationException("Empty state packet.");
            if (packet.Kind == "request") SendSnapshot(peer);
            else if (packet.Kind == "ack")
            {
                // One purchase sends several revisions, so an answer to an earlier one can arrive after the next went out.
                if (packet.SaveId == BasedSave.SaveId && packet.Revision < Engine.Revision)
                {
                    Log("Older snapshot acknowledgement ignored: revision " + packet.Revision + " after " + Engine.Revision + ", peer " + peer + ".");
                    return;
                }
                string key = packet.SaveId + ":" + packet.Revision;
                if (!PeerSnapshots.TryGetValue(peer, out string? expected) || expected != key)
                    throw new InvalidOperationException("Snapshot acknowledgement does not match peer/session/revision.");
                if (!MapExpansionsApi.ConfirmPeerWorldStateAck(peer, packet.Revision))
                    throw new InvalidOperationException("MapExpansions rejected snapshot acknowledgement.");
            }
        }
        catch (Exception error) { Warn("Rejected snapshot message from peer " + peer + ": " + error); }
    }

    private static void ReceiveClient(string payload)
    {
        if (NativeBus.IsHost || !payload.StartsWith(Wire, StringComparison.Ordinal)) return;
        try
        {
            var packet = JsonConvert.DeserializeObject<StatePacket>(payload.Substring(Wire.Length))
                ?? throw new InvalidOperationException("Empty state packet.");
            if (packet.Kind != "snapshot" || packet.State == null || string.IsNullOrEmpty(packet.SaveId))
                throw new InvalidOperationException("Invalid host snapshot.");
            if (_snapshotReceived && packet.State.Revision < Engine.Revision)
            {
                Log("Older host snapshot ignored: revision " + packet.State.Revision + " after " + Engine.Revision + ".");
                return;
            }
            BasedSave.AdoptSession(packet.SaveId);
            Restore(packet.State);
            HeldBy = packet.HeldBy;
            _snapshotReceived = true;
            _lastAck = "";
        }
        catch (Exception error) { Fail("host snapshot", error); }
    }

    internal sealed class StatePacket
    {
        public string Kind { get; set; } = "";
        public string SaveId { get; set; } = "";
        public long Revision { get; set; }
        public BasedState? State { get; set; }
        public string HeldBy { get; set; } = "";
    }

    // Saving was refused, so the player gets told once instead of finding out after a quit.
    internal static void WarnSaveBlocked()
    {
        Warn("Save blocked because the expansion is incomplete: " + Failure);
        if (_saveWarningShown) return;
        _saveWarningShown = true;
        try
        {
            var notifications = Singleton<NotificationsManager>.Instance;
            if (notifications != null)
                notifications.SendNotification("Based", Text.T("Saving is off: the expansion could not be applied."), null, 8f, false);
            else Warn("No notification shown: the notification manager is not ready.");
        }
        catch (Exception error) { Warn("Save warning could not be shown: " + error.Message); }
    }

    internal static void Log(string message) => Instance.LoggerInstance.Msg("[Based] " + message);
    internal static void Warn(string message) => Instance.LoggerInstance.Warning("[Based] " + message);

    // An error in the crew or the plan costs that feature, not the player's ability to save.
    private static void Contain(string operation, Exception error)
    {
        if (ContainedErrors.Add(operation + ":" + error.Message))
            MelonLogger.Error("[Based] " + operation + " failed; saving stays on: " + error);
    }

    internal static void Fail(string operation, Exception error)
    {
        string failure = operation + ": " + error.Message;
        if (Failure != failure) MelonLogger.Error("[Based] " + failure + "\n" + error);
        Failure = failure;
    }
}

[HarmonyPatch(typeof(DialogueHandler), nameof(DialogueHandler.ContinueSubmitted))]
internal static class KeepConversationUnderPlan
{
    private static bool Prefix(DialogueHandler __instance)
    {
        if (!Mod.HoldsConversation(__instance)) return true;
        Mod.Log("Click under the construction plan ignored; the conversation stays open.");
        return false;
    }
}

// The game saves through both overloads, and the short one can be inlined into its callers.
[HarmonyPatch]
internal static class PreventUnsafeSave
{
    private static IEnumerable<MethodBase> TargetMethods() => new MethodBase[]
    {
        AccessTools.Method(typeof(SaveManager), nameof(SaveManager.Save), Type.EmptyTypes),
        AccessTools.Method(typeof(SaveManager), nameof(SaveManager.Save), new[] { typeof(string) })
    };

    private static bool Prefix()
    {
        if (Mod.Failure.Length == 0) return true;
        Mod.WarnSaveBlocked();
        return false;
    }
}
