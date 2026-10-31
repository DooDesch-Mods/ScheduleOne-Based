using System;
using System.Linq;
using System.Collections.Generic;
using Based.Content.Ming;
using Based.Core;
using Based.Persistence;
using Based.Runtime;
using Based.UI;
using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.Dialogue;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.Variables;
using MapExpansions;
using MelonLoader;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Events;

[assembly: MelonInfo(typeof(Based.Mod), "Based", DooDesch.ModVersion.Current, "DooDesch", "https://github.com/DooDesch-Mods/ScheduleOne-Based")]
[assembly: MelonGame("TVGS", "Schedule I")]
[assembly: MelonAdditionalDependencies("MapExpansions", "Personnel", "Sideload")]

namespace Based;

public sealed class Mod : MelonMod
{
    private const string Wire = "BASED-STATE1:";
    internal static Mod Instance { get; private set; } = null!;
    internal static UpgradeEngine Engine { get; private set; } = null!;
    internal static string Failure { get; private set; } = "";
    private static bool _main, _requestedSnapshot, _snapshotReceived, _openRequested;
    private static float _nextBind;
    private static S1API.Entities.NPC? _ming;
    private static string _lastWorld = "", _lastUi = "";
    private static bool _worldDirty = true;
    private static bool _initialized;
    private static bool _waitingNetwork;
    private static string _lastAck = "";
    private static readonly Dictionary<int, string> PeerSnapshots = new();

    private sealed class HostAuthority : IAuthority
    {
        public bool CanMutate => InstanceFinder.IsServer;
    }

    public override void OnInitializeMelon()
    {
        Instance = this;
        Text.Register();
        Engine = new UpgradeEngine(new[] { MingDefinition.Create() }, new HostAuthority());
        MingWorld.Register();
#if DEBUG
        DockProbe.Register();
#endif
        Payments.Initialize(Engine, Changed);
        Payments.ValidatePurchase = command => !OwnsMing() ? Text.T("Buy the existing room from Ming first.")
            : command.Kind == PurchaseKind.Construction && !DoorwayClear(out string reason) ? reason : null;
        NativeBus.HostReceived += ReceiveHost;
        NativeBus.ClientReceived += ReceiveClient;
        MapExpansionsApi.PeerWorldStateRequested += SendSnapshot;
        S1API.GameTime.TimeManager.OnSleepEnd += SleepEnded;
        MingWorkers.Initialize(Log, Warn);
        _initialized = true;
        LoggerInstance.Msg("Based initialized; Ming content registered with MapExpansions.");
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        if (!_initialized) return;
        if (sceneName == "Menu")
        {
            _main = false;
#if DEBUG
            DockProbe.ResetForMenu();
            RoomProbe.ResetForMenu();
#endif
            BuildPlan.Close();
            MingWorkers.Cleanup();
            Payments.ResetForMenu();
            NativeBus.Stop();
            Engine.Reset();
            BasedSave.BeginLoad();
            _ming = null;
            _lastWorld = _lastUi = Failure = "";
            _requestedSnapshot = _snapshotReceived = _openRequested = false;
            _lastAck = "";
            PeerSnapshots.Clear();
            _worldDirty = true;
        }
        else if (sceneName == "Main")
        {
            _main = true;
            _nextBind = 0;
            _worldDirty = true;
        }
    }

    public override void OnUpdate()
    {
        if (!_initialized || !_main || !string.IsNullOrEmpty(Failure)) return;
        try
        {
            if (!NativeBus.Start()) return;
            if (!NativeBus.IsConnected)
            {
                if (!_waitingNetwork) { Log("Waiting for the game's network session before syncing construction."); _waitingNetwork = true; }
                return;
            }
            if (_waitingNetwork) { Log("Game network session connected; construction sync resumed."); _waitingNetwork = false; }
            if (NativeBus.IsHost) Payments.SetSession(BasedSave.SaveId);
            Payments.Tick();
            if (!NativeBus.IsHost && !_requestedSnapshot)
            {
                NativeBus.SendToHost(Wire + JsonConvert.SerializeObject(new StatePacket { Kind = "request" }));
                _requestedSnapshot = true;
            }
            if (_worldDirty) ApplyWorld();
            if (!NativeBus.IsHost && _snapshotReceived && WorldApplied())
            {
                string key = BasedSave.SaveId + ":" + Engine.Revision;
                if (_lastAck != key)
                {
                    NativeBus.SendToHost(Wire + JsonConvert.SerializeObject(new StatePacket
                        { Kind = "ack", SaveId = BasedSave.SaveId, Revision = Engine.Revision }));
                    _lastAck = key;
                }
            }
            var load = LoadManager.Instance;
            if (load == null || !load.IsGameLoaded) return;
            if (Time.unscaledTime >= _nextBind && _ming == null)
            {
                _nextBind = Time.unscaledTime + 1f;
                BindMing();
            }
            var state = Engine.GetUpgrade(MingDefinition.BaseId, MingDefinition.UpgradeId);
            MingWorkers.Update(state.Stage, Engine.CurrentPhase(state.BaseId, state.UpgradeId));
            MingWorkers.Tick();
            if (_openRequested && _ming != null && _ming.Dialogue.IsDialogueInProgress)
            {
                _openRequested = false;
                BuildPlan.Open();
            }
            if (BuildPlan.IsOpen && (_ming == null || !_ming.Dialogue.IsDialogueInProgress)) BuildPlan.Close();
            BuildPlan.Tick();
            string ui = BuildPlan.State();
            if (ui != _lastUi)
            {
                _lastUi = ui;
                BuildPlan.Push(ui);
            }
        }
        catch (Exception error) { Fail("runtime update", error); }
    }

    private static void BindMing()
    {
        var ming = S1API.Entities.NPC.Get("ming");
        if (ming == null) return;
        var controller = ming.gameObject.GetComponentInChildren<DialogueController>(true);
        var handler = ming.gameObject.GetComponentInChildren<DialogueHandler>(true);
        if (controller == null || handler == null) throw new InvalidOperationException("Ming has no dialogue controller/handler.");
        const string containerId = "based_ming_plan";
        ming.Dialogue.BuildAndRegisterContainer(containerId,
            b => b.AddNode("ENTRY", Text.T("Here is the plan for the rest of the floor.")));
        Conversation? container = null;
        for (int i = 0; i < handler.dialogueContainers.Count; i++)
            if (handler.dialogueContainers[i].name == containerId) container = handler.dialogueContainers[i];
        if (container == null) throw new InvalidOperationException("S1API did not register the Based conversation.");
        var choice = new DialogueController.DialogueChoice
        {
            Enabled = true,
            ChoiceText = Text.T("I would like more space upstairs."),
            Conversation = container,
            Priority = 55,
            onChoosen = new UnityEvent()
        };
        choice.onChoosen.AddListener((Action)(() => _openRequested = true));
        controller.AddDialogueChoice(choice, 55);
        _ming = ming;
        Log("Ming's existing dialogue extended with the Based plan.");
    }

    internal static bool OwnsMing() => S1API.Property.PropertyManager.GetOwnedProperties()
        .Any(p => p.PropertyCode == "sweatshop");

    internal static string Purchase(PurchaseKind kind)
    {
        if (!string.IsNullOrEmpty(Failure)) return Failure;
        if (!OwnsMing()) return Text.T("Buy the existing room from Ming first.");
        if (!NativeBus.IsHost && !_snapshotReceived) return Text.T("Waiting for the host's construction plan.");
        if (kind == PurchaseKind.Construction && !DoorwayClear(out string reason)) return reason;
        try
        {
            Payments.Request(kind, MingDefinition.BaseId, MingDefinition.UpgradeId);
            return "";
        }
        catch (InvalidOperationException error)
        {
            Warn("Purchase refused: " + error.Message);
            return error.Message;
        }
    }

    internal static bool DoorwayClear(out string reason)
    {
        foreach (var item in DoorwayItems())
        {
            item.ShowOutline(Color.yellow);
            reason = Text.T("Move {0} away from the doorway before ordering construction.", item.GetManagementName());
            return false;
        }
        reason = "";
        return true;
    }

    internal static Il2CppScheduleOne.EntityFramework.BuildableItem[] DoorwayItems()
    {
        var opening = new Bounds(new Vector3(-59.85f, .9f, 136.65f), new Vector3(1.3f, 2.3f, 1.8f));
        var items = new Dictionary<IntPtr, Il2CppScheduleOne.EntityFramework.BuildableItem>();
        var colliders = Physics.OverlapBox(opening.center, opening.extents, Quaternion.identity);
        for (int i = 0; i < colliders.Length; i++)
        {
            var item = colliders[i].GetComponentInParent<Il2CppScheduleOne.EntityFramework.BuildableItem>();
            if (item != null && !item.IsDestroyed) items[item.Pointer] = item;
        }
        return items.Values.ToArray();
    }

    internal static void EndPlan()
    {
        BuildPlan.Close();
        _ming?.Dialogue.End();
    }

#if DEBUG
    internal static void DebugPlan()
    {
        if (_ming == null) throw new InvalidOperationException("Ming dialogue is not ready.");
        _ming.Dialogue.Start("based_ming_plan");
        _openRequested = true;
    }
#endif

    internal static void Restore(BasedState state)
    {
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
        if (!NativeBus.IsHost || !_main || !string.IsNullOrEmpty(Failure)) return;
        try
        {
            long cycle = (long)VariableDatabase.Instance.GetValue<float>("Sleep_Count");
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
        catch (Exception error) { Fail("sleep transition", error); }
    }

    internal static void Changed()
    {
        _worldDirty = true;
        if (!ApplyWorld()) return;
        BasedSave.Capture(Engine.ExportState());
        if (NativeBus.IsHost)
        {
            foreach (int peer in PeerSnapshots.Keys.ToArray())
            {
                if (!InstanceFinder.ServerManager.Clients.ContainsKey(peer)) { PeerSnapshots.Remove(peer); continue; }
                SendSnapshot(peer);
            }
        }
    }

    private static bool ApplyWorld()
    {
        var requested = new List<string>();
        foreach (var definition in Engine.Definitions)
        foreach (var upgrade in definition.Upgrades)
        foreach (var effect in upgrade.Effects.Where(e => e.Kind == "world-state"))
        {
            var state = Engine.GetUpgrade(definition.Id, upgrade.Id);
            string? phaseId = Engine.CurrentPhase(definition.Id, upgrade.Id);
            string desired = state.Stage == UpgradeStage.Complete ? effect.StateId
                : state.Stage == UpgradeStage.Constructing ? upgrade.Phases.FirstOrDefault(p => p.Id == phaseId)?.WorldStateId ?? "" : "";
            if (!MapExpansionsApi.TrySetWorldState(effect.TargetId, desired, out string error))
            {
                Fail("applying world state " + effect.TargetId + "/" + desired, new InvalidOperationException(error));
                return false;
            }
            requested.Add(effect.TargetId + "/" + desired);
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
        { Kind = "snapshot", SaveId = BasedSave.SaveId, Revision = Engine.Revision, State = Engine.ExportState() });

    private static bool WorldApplied() => Engine.Definitions.SelectMany(b => b.Upgrades)
        .SelectMany(u => u.Effects).Where(e => e.Kind == "world-state")
        .All(e => MapExpansionsApi.IsWorldStateApplied(e.TargetId));

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
            if (_snapshotReceived && packet.State.Revision < Engine.Revision) return;
            BasedSave.AdoptSession(packet.SaveId);
            Restore(packet.State);
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
    }

    internal static void Log(string message) => Instance.LoggerInstance.Msg("[Based] " + message);
    internal static void Warn(string message) => Instance.LoggerInstance.Warning("[Based] " + message);
    internal static void Fail(string operation, Exception error)
    {
        string failure = operation + ": " + error.Message;
        if (Failure != failure) MelonLogger.Error("[Based] " + failure + "\n" + error);
        Failure = failure;
    }
}

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.Save), new Type[0])]
internal static class PreventUnsafeSave
{
    private static bool Prefix()
    {
        if (string.IsNullOrEmpty(Mod.Failure)) return true;
        Mod.Warn("Save blocked because the expansion is incomplete: " + Mod.Failure);
        return false;
    }
}
