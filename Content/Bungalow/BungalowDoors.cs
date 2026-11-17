using Based.Runtime;
using Based.UI;
using Il2CppScheduleOne.Doors;
using Il2CppScheduleOne.Interaction;
using Il2CppScheduleOne.PlayerScripts;
using MapExpansions;
using MelonLoader;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace Based.Content.Bungalow;

// The Bungalow's copied sliding doors: the sunroom's west door and the upper storey's west door onto its railing.
internal static class BungalowDoors
{
    // Where each copy's root stands: the house door's root at (-176.08, -3.70, 111.93), moved into the sunroom's west front
    // or lifted with the upper storey.
    internal static readonly CopiedSlidingDoor Sunroom = new("Sunroom door", "BASED-DOOR1:", BungalowDefinition.SunroomExpansionId,
        new Vector3(-183.05f - BungalowSunroom.DoorOut, -3.70f, 111.93f));
    internal static readonly CopiedSlidingDoor Storey = new("Upper storey door", "BASED-DOOR2:", BungalowDefinition.StoreyExpansionId,
        new Vector3(-176.08f, -3.70f + BungalowStorey.Lift, 111.93f));

    private static bool _subscribed;

    internal static void Init()
    {
        if (_subscribed) return;
        NativeBus.HostReceived += OnHost;
        NativeBus.ClientReceived += OnClient;
        _subscribed = true;
    }

    internal static void Tick()
    {
        try { Sunroom.Tick(); }
        catch (Exception error) { MelonLogger.Error("[Based] Sunroom door tick failed: " + error); }
        try { Storey.Tick(); }
        catch (Exception error) { MelonLogger.Error("[Based] Upper storey door tick failed: " + error); }
    }

    // Dev console: "door [sunroom|storey] open|close|state"; without a door name the sunroom's door answers.
    internal static string Debug(List<string> args)
    {
        var door = Sunroom;
        int at = 1;
        if (args.Count > 1 && args[1] == "storey") { door = Storey; at = 2; }
        else if (args.Count > 1 && args[1] == "sunroom") at = 2;
        return door.Name.ToLowerInvariant() + " " + door.Debug(args.Count > at ? args[at] : "state");
    }

    private static void OnHost(int peer, string payload)
    {
        if (!Sunroom.OnHost(peer, payload)) Storey.OnHost(peer, payload);
    }

    private static void OnClient(string payload)
    {
        if (!Sunroom.OnClient(payload)) Storey.OnClient(payload);
    }
}

// A world state builds every peer's world on its own and may hold no networked part, so a door in it is a visual copy of
// the house's sliding door and this class makes it work: it finds the copy's sliding leaf, gives it a collider and an
// interaction, and slides it the way the house's door slides. The host owns the open state; a client asks the host, and
// the host tells every peer, a late joiner included when it finds its copy.
internal sealed class CopiedSlidingDoor
{
    private const string HouseDoorPath = "@Properties/Bungalow/bungalow/Sliding Glass Door";
    private const string CopyName = "Sliding Glass Door";
    // Used only when the house door has no slide script to read: the leaf slides over the fixed light to the south.
    private static readonly Vector3 FallbackSlide = new(0f, 0f, -1.05f);
    private const float SearchInterval = 5f;
    private const float InteractionRange = 2.5f;

    internal readonly string Name;
    private readonly string _wire;
    private readonly string _expansion;
    private readonly Vector3 _copyRoot;

    private Transform? _leaf;
    private InteractableObject? _interaction;
    private Vector3 _closed, _open;
    private float _duration = 1f;
    private AnimationCurve? _curve;
    private bool _isOpen;
    private float _progress;
    private float _nextSearch;
    private bool _queried;
    private string _lastMiss = "";
    private bool? _shownOpen;

    internal CopiedSlidingDoor(string name, string wire, string expansion, Vector3 copyRoot)
    {
        Name = name;
        _wire = wire;
        _expansion = expansion;
        _copyRoot = copyRoot;
    }

    internal void Tick()
    {
        bool built = MapExpansionsApi.GetWorldState(_expansion) == "complete" && MapExpansionsApi.IsWorldStateApplied(_expansion);
        if (!built)
        {
            if (_leaf != null || _interaction != null) Mod.Log(Name + " released: its build is not complete on this peer.");
            Release();
            return;
        }
        if (_leaf == null)
        {
            if (_interaction != null) { Mod.Log(Name + " lost its copy; searching again."); Release(); }
            if (Time.unscaledTime < _nextSearch) return;
            _nextSearch = Time.unscaledTime + SearchInterval;
            if (!Bind()) return;
        }
        if (!_queried && !NativeBus.IsHost && NativeBus.IsConnected)
        {
            NativeBus.SendToHost(_wire + "query");
            _queried = true;
            Mod.Log(Name + " asked the host for its state.");
        }
        Slide();
    }

    private void Release()
    {
        _leaf = null;
        _interaction = null;
        _shownOpen = null;
        _queried = false;
        _progress = _isOpen ? 1f : 0f;
    }

    private bool Bind()
    {
        var copy = FindCopy();
        if (copy == null) { Miss("no copy named '" + CopyName + "' stands at " + _copyRoot); return false; }

        Vector3 slide = FallbackSlide;
        string leafPath = "Container";
        var house = GameObject.Find(HouseDoorPath);
        var sliding = house != null ? house.GetComponentInChildren<SlidingDoor>(true) : null;
        if (sliding != null && sliding.DoorTransform != null && sliding.OpenPosition != null && sliding.ClosedPosition != null)
        {
            slide = sliding.OpenPosition.position - sliding.ClosedPosition.position;
            leafPath = PathBelow(house!.transform, sliding.DoorTransform);
            _duration = Mathf.Max(.2f, sliding.SlideDuration);
            _curve = sliding.SlideCurve;
        }
        else
            Mod.Warn(Name + ": the house door at '" + HouseDoorPath + "' has no readable slide script; the leaf slides by " + FallbackSlide + " over 1 s.");

        var leaf = copy.Find(leafPath);
        if (leaf == null) { Miss("the copy has no leaf at '" + leafPath + "'"); return false; }

        var bounds = new Bounds(leaf.position, Vector3.zero);
        int renderers = 0;
        foreach (var renderer in leaf.GetComponentsInChildren<Renderer>(true))
        {
            if (renderers++ == 0) bounds = renderer.bounds;
            else bounds.Encapsulate(renderer.bounds);
        }
        if (renderers == 0) { Miss("the leaf at '" + leafPath + "' has no renderers to size its collider"); return false; }

        var blocker = new GameObject("Based " + Name);
        blocker.transform.SetParent(leaf, false);
        int doorLayer = LayerMask.NameToLayer("Door");
        blocker.layer = doorLayer >= 0 ? doorLayer : 0;
        var box = blocker.AddComponent<BoxCollider>();
        box.center = leaf.InverseTransformPoint(bounds.center);
        var size = leaf.InverseTransformVector(bounds.size);
        box.size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
        var interaction = blocker.AddComponent<InteractableObject>();
        interaction.MaxInteractionRange = InteractionRange;
        interaction.RequiresUniqueClick = true;
        interaction.onInteractStart.AddListener((UnityAction)(() => Request(!_isOpen)));

        _leaf = leaf;
        _interaction = interaction;
        _closed = leaf.position;
        _open = _closed + slide;
        _progress = _isOpen ? 1f : 0f;
        _lastMiss = "";
        Place();
        Mod.Log(Name + " bound: leaf '" + leafPath + "' at " + _closed + ", slides by " + slide + " in " + _duration.ToString("0.0") +
                " s, collider " + box.size + " on layer " + LayerMask.LayerToName(blocker.layer) + ", " + (_isOpen ? "open" : "closed") + ".");
        return true;
    }

    private Transform? FindCopy()
    {
        foreach (var t in Object.FindObjectsOfType<Transform>(true))
            if (t.name == CopyName && (t.position - _copyRoot).sqrMagnitude < .25f && t.gameObject.activeInHierarchy) return t;
        return null;
    }

    private static string PathBelow(Transform root, Transform child)
    {
        string path = child.name;
        for (var t = child.parent; t != null && t != root; t = t.parent) path = t.name + "/" + path;
        return path;
    }

    private void Miss(string reason)
    {
        if (reason == _lastMiss) return;
        _lastMiss = reason;
        Mod.Warn(Name + " not bound yet: " + reason + "; retrying every " + SearchInterval + " s.");
    }

    internal string Debug(string action)
    {
        switch (action)
        {
            case "open": Request(true); break;
            case "close": Request(false); break;
            case "state": break;
            default: return "unknown action '" + action + "'; use open, close or state";
        }
        string where = _leaf == null ? "no leaf bound" : "leaf at " + _leaf.position.ToString("F2") + ", progress " + _progress.ToString("0.00");
        string hit = _interaction == null ? "no interaction" : "interaction '" + _interaction.message + "' in " + InteractionRange + " m";
        return (_isOpen ? "open" : "closed") + "; " + where + "; " + hit + ".";
    }

    private void Request(bool open)
    {
        try
        {
            if (NativeBus.IsHost) Broadcast(open, "the host");
            else
            {
                NativeBus.SendToHost(_wire + "set:" + (open ? 1 : 0));
                Mod.Log(Name + ": asked the host to " + (open ? "open" : "close") + " it.");
            }
        }
        catch (Exception error) { MelonLogger.Error("[Based] " + Name + " request failed: " + error); }
    }

    // True when the message belongs to this door.
    internal bool OnHost(int peer, string payload)
    {
        if (!payload.StartsWith(_wire, StringComparison.Ordinal)) return false;
        try
        {
            string body = payload.Substring(_wire.Length);
            if (body == "query") NativeBus.SendToPeer(peer, _wire + "state:" + (_isOpen ? 1 : 0));
            else if (body.StartsWith("set:", StringComparison.Ordinal)) Broadcast(body == "set:1", "peer " + peer);
            else Mod.Warn(Name + ": unknown message from peer " + peer + ": " + body);
        }
        catch (Exception error) { MelonLogger.Error("[Based] " + Name + " message from peer " + peer + " failed: " + error); }
        return true;
    }

    private void Broadcast(bool open, string by)
    {
        var players = Player.PlayerList;
        int sent = 0;
        for (int i = 0; players != null && i < players.Count; i++)
        {
            var connection = players[i]?.Connection;
            if (connection == null) continue;
            try { NativeBus.SendToPeer(connection.ClientId, _wire + "state:" + (open ? 1 : 0)); sent++; }
            catch (Exception error) { MelonLogger.Error("[Based] " + Name + " state to peer " + connection.ClientId + " failed: " + error.Message); }
        }
        Mod.Log(Name + " " + (open ? "opened" : "closed") + " by " + by + "; sent to " + sent + " peers.");
    }

    // True when the message belongs to this door.
    internal bool OnClient(string payload)
    {
        if (!payload.StartsWith(_wire, StringComparison.Ordinal)) return false;
        string body = payload.Substring(_wire.Length);
        if (!body.StartsWith("state:", StringComparison.Ordinal)) { Mod.Warn(Name + ": unknown message from the host: " + body); return true; }
        bool open = body == "state:1";
        if (open != _isOpen) Mod.Log(Name + " is now " + (open ? "open" : "closed") + (_leaf == null ? " (no copy bound on this peer yet)" : "") + ".");
        _isOpen = open;
        return true;
    }

    private void Slide()
    {
        float target = _isOpen ? 1f : 0f;
        if (_interaction != null && _shownOpen != _isOpen)
        {
            _interaction.SetMessage(Text.T(_isOpen ? "Close" : "Open"));
            _shownOpen = _isOpen;
        }
        if (Mathf.Approximately(_progress, target)) return;
        _progress = Mathf.MoveTowards(_progress, target, Time.deltaTime / _duration);
        Place();
    }

    private void Place()
    {
        if (_leaf == null) return;
        float eased = _curve != null && _curve.length > 0 ? _curve.Evaluate(_progress) : Mathf.SmoothStep(0f, 1f, _progress);
        _leaf.position = Vector3.LerpUnclamped(_closed, _open, eased);
    }
}
