using System;
using Based.Core;
using S1API.Internal.Abstraction;
using S1API.Saveables;

namespace Based.Persistence;

public sealed class BasedSave : Saveable
{
    internal static BasedSave? Instance { get; private set; }
    private static string _saveId = Guid.NewGuid().ToString("N");
    internal static string SaveId => _saveId;
    [SaveableField("based_state")] private SavedWorld _world = new();
    private static bool _loaded;
    public override SaveableLoadOrder LoadOrder => SaveableLoadOrder.BeforeBaseGame;

    public BasedSave() { Instance = this; }

    internal static void BeginLoad()
    {
        _saveId = Guid.NewGuid().ToString("N");
        _loaded = false;
        if (Instance != null) Instance._world = new SavedWorld { SaveId = _saveId };
    }

    internal static void AdoptSession(string id)
    {
        if (string.IsNullOrEmpty(id)) throw new ArgumentException("Missing Based save identity.", nameof(id));
        _saveId = id;
    }

    internal static void Capture(BasedState state)
    {
        if (Instance == null) throw new InvalidOperationException("S1API has not registered BasedSave.");
        Instance._world = new SavedWorld { SaveId = _saveId, State = state };
    }

    protected override void OnLoaded()
    {
        try
        {
            if (_world == null || _world.State == null || string.IsNullOrEmpty(_world.SaveId))
                throw new InvalidOperationException("Based save data is incomplete.");
            Instance = this;
            _saveId = _world.SaveId;
            _loaded = true;
            Mod.Restore(_world.State);
        }
        catch (Exception error) { Mod.Fail("loading Based save", error); }
    }

    protected override void OnCreated()
    {
        if (_loaded) return;
        Instance = this;
        _world = new SavedWorld { SaveId = _saveId, State = Mod.Engine.ExportState() };
        _loaded = true;
        Mod.Log("Created Based state for this save.");
    }

    protected override void OnSaved() => Mod.Log("Based state saved at revision " + _world.State.Revision + ".");

    private sealed class SavedWorld
    {
        public string SaveId { get; set; } = _saveId;
        public BasedState State { get; set; } = new();
    }
}
