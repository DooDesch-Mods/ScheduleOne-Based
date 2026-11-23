using Based.Runtime;
using Based.UI;
using Il2CppScheduleOne.Doors;
using Il2CppScheduleOne.Interaction;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.PlayerScripts;
using MapExpansions;
using MelonLoader;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace Based.Content.Bungalow;

// The Bungalow's copied doors: the sunroom's west door, the upper storey's sliding door onto its railing, and the upper
// storey's two wooden doors, from the landing into the west room and between the north rooms.
internal static class BungalowDoors
{
    private const string House = "@Properties/Bungalow/bungalow/";
    private static readonly Vector3 Up = new(0f, BungalowStorey.Lift, 0f);

    // Where each sliding copy's root stands: the house door's root at (-176.08, -3.70, 111.93), moved into the sunroom's
    // west front or lifted with the upper storey.
    internal static readonly CopiedDoor Sunroom = new("Sunroom door", "BASED-DOOR1:", BungalowDefinition.SunroomExpansionId,
        House + "Sliding Glass Door", new Vector3(-183.05f - BungalowSunroom.DoorOut, -3.70f, 111.93f), false, "complete");
    internal static readonly CopiedDoor Storey = new("Upper storey door", "BASED-DOOR2:", BungalowDefinition.StoreyExpansionId,
        House + "Sliding Glass Door", new Vector3(-176.08f, -3.70f, 111.93f) + Up, false, "complete");
    // The wooden doors stand in the copied walls from the night the walls go up; their roots are found from the house's.
    internal static readonly CopiedDoor Landing = new("Upper landing door", "BASED-DOOR3:", BungalowDefinition.StoreyExpansionId,
        House + "Classical Wooden door", null, true, BungalowStorey.WallStates);
    internal static readonly CopiedDoor Hall = new("Upper hall door", "BASED-DOOR4:", BungalowDefinition.StoreyExpansionId,
        House + "Classical Wooden door (1)", null, true, BungalowStorey.WallStates);

    private static readonly CopiedDoor[] All = { Sunroom, Storey, Landing, Hall };
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
        foreach (var door in All)
        {
            try { door.Tick(); }
            catch (Exception error) { MelonLogger.Error("[Based] " + door.Name + " tick failed: " + error); }
        }
    }

    // Dev console: "door [sunroom|storey|landing|hall] open|close|state"; without a door name the sunroom's door answers.
    internal static string Debug(List<string> args)
    {
        var door = Sunroom;
        int at = 1;
        if (args.Count > 1)
        {
            var named = args[1] switch { "sunroom" => Sunroom, "storey" => Storey, "landing" => Landing, "hall" => Hall, _ => null };
            if (named != null) { door = named; at = 2; }
        }
        return door.Name.ToLowerInvariant() + " " + door.Debug(args.Count > at ? args[at] : "state");
    }

    private static void OnHost(int peer, string payload)
    {
        foreach (var door in All)
            if (door.OnHost(peer, payload)) return;
    }

    private static void OnClient(string payload)
    {
        foreach (var door in All)
            if (door.OnClient(payload)) return;
    }
}

// A world state builds every peer's world on its own and may hold no networked part, so a door in it is a visual copy of
// one of the house's doors and this class makes it work: it finds the copy's leaf, gives it a collider and an
// interaction, and moves it the way the house's door moves, sliding or swinging on its hinge. The host owns the open
// state; a client asks the host, and the host tells every peer, a late joiner included when it finds its copy. A
// swinging door also opens on each peer by itself while an NPC stands in it, as the game's doors do for NPCs.
internal sealed class CopiedDoor
{
    // Used only when the house door has no slide script to read: the leaf slides over the fixed light to the south.
    private static readonly Vector3 FallbackSlide = new(0f, 0f, -1.05f);
    private const float FallbackSwing = 100f, SwingSpeed = 220f;
    private const float SearchInterval = 5f, InteractionRange = 2.5f;
    private const float NpcCheckInterval = .25f, NpcReach = 1.3f, NpcHeight = 1.6f;

    internal readonly string Name;
    private readonly string _wire;
    private readonly string _expansion;
    private readonly string _housePath;
    private readonly string _copyName;
    private readonly Vector3? _copyRoot;
    private readonly bool _swing;
    private readonly string[] _states;

    private Transform? _leaf;
    private InteractableObject? _interaction;
    // How far the prompt stands from the middle of the leaf's collider, across the floor; -1 until a leaf is bound.
    private float _promptOffset = -1f;
    private Vector3 _closed, _open;
    private Quaternion _closedTurn;
    private Vector3 _hinge, _across, _doorway;
    private float _swingAngle = FallbackSwing, _angle;
    private float _duration = 1f;
    private AnimationCurve? _curve;
    // 0 closed; for a sliding door 1 open; for a swinging door 1 or -1, the side the leaf swings to.
    private int _side;
    private int _npcSide;
    private float _progress;
    private float _nextSearch, _nextNpcCheck;
    private bool _queried;
    private string _lastMiss = "";
    private bool? _shownOpen;

    internal CopiedDoor(string name, string wire, string expansion, string housePath, Vector3? copyRoot, bool swing, params string[] states)
    {
        Name = name;
        _wire = wire;
        _expansion = expansion;
        _housePath = housePath;
        _copyName = housePath.Substring(housePath.LastIndexOf('/') + 1);
        _copyRoot = copyRoot;
        _swing = swing;
        _states = states;
    }

    internal void Tick()
    {
        string? state = MapExpansionsApi.IsWorldStateApplied(_expansion) ? MapExpansionsApi.GetWorldState(_expansion) : null;
        if (state == null || Array.IndexOf(_states, state) < 0)
        {
            if (_leaf != null || _interaction != null) Mod.Log(Name + " released: its build stage is " + (state ?? "none") + " on this peer.");
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
        if (_swing) WatchNpcs();
        Move();
    }

    private void Release()
    {
        _leaf = null;
        _interaction = null;
        _shownOpen = null;
        _queried = false;
        _npcSide = 0;
        _progress = _side != 0 ? 1f : 0f;
        _angle = _side * _swingAngle;
    }

    private bool Bind()
    {
        var house = GameObject.Find(_housePath);
        if (house == null) { Miss("the house door '" + _housePath + "' is not in the scene"); return false; }
        var root = _copyRoot ?? house.transform.position + new Vector3(0f, BungalowStorey.Lift, 0f);
        var copy = FindCopy(root);
        if (copy == null) { Miss("no copy named '" + _copyName + "' stands at " + root); return false; }

        string leafPath = "Container";
        Vector3 slide = FallbackSlide;
        if (_swing)
        {
            var pivot = house.GetComponentInChildren<PivotDoor>(true);
            if (pivot != null && pivot.DoorTransform != null)
            {
                leafPath = PathBelow(house.transform, pivot.DoorTransform);
                _swingAngle = Mathf.Abs(pivot.OpenOutwardsAngle) > 1f ? Mathf.Abs(pivot.OpenOutwardsAngle) : FallbackSwing;
            }
            else
                Mod.Warn(Name + ": the house door at '" + _housePath + "' has no readable pivot script; the leaf '" + leafPath + "' swings by " + FallbackSwing + " degrees.");
        }
        else
        {
            var sliding = house.GetComponentInChildren<SlidingDoor>(true);
            if (sliding != null && sliding.DoorTransform != null && sliding.OpenPosition != null && sliding.ClosedPosition != null)
            {
                slide = sliding.OpenPosition.position - sliding.ClosedPosition.position;
                leafPath = PathBelow(house.transform, sliding.DoorTransform);
                _duration = Mathf.Max(.2f, sliding.SlideDuration);
                _curve = sliding.SlideCurve;
            }
            else
                Mod.Warn(Name + ": the house door at '" + _housePath + "' has no readable slide script; the leaf slides by " + FallbackSlide + " over 1 s.");
        }

        var leaf = copy.Find(leafPath);
        if (leaf == null) { Miss("the copy has no leaf at '" + leafPath + "'"); return false; }
        // A copy is taken in whatever pose the house's door had then; a swinging leaf starts closed.
        if (_swing) leaf.localRotation = Quaternion.identity;

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
        interaction.onInteractStart.AddListener((UnityAction)(() => Request(_side == 0)));
        // Without a display point the game draws the prompt in the middle of the collider, on the leaf's centre.
        var knob = new GameObject("Based " + Name + " knob").transform;
        knob.SetParent(leaf, false);
        knob.position = Knob(house, leaf, leafPath, bounds, slide, out string knobFrom);
        interaction.displayLocationPoint = knob;
        var offset = knob.position - bounds.center;
        offset.y = 0f;
        _promptOffset = offset.magnitude;

        _leaf = leaf;
        _interaction = interaction;
        _closed = leaf.position;
        _open = _closed + slide;
        _closedTurn = leaf.localRotation;
        // The hinge is the leaf's pivot; the leaf reaches from it across the doorway. Turning by a positive angle moves
        // the leaf to the side Cross(up, across) points to.
        _hinge = leaf.position;
        var reach = bounds.center - _hinge;
        reach.y = 0f;
        _across = reach.sqrMagnitude > .0001f ? reach.normalized : Vector3.forward;
        _doorway = _hinge + reach;
        _progress = _side != 0 ? 1f : 0f;
        _angle = _side * _swingAngle;
        _lastMiss = "";
        Place();
        Mod.Log(Name + " bound: leaf '" + leafPath + "' at " + _closed + (_swing ? ", swings " + _swingAngle.ToString("0") + " degrees"
                : ", slides by " + slide + " in " + _duration.ToString("0.0") + " s") + ", collider " + box.size + " on layer "
                + LayerMask.LayerToName(blocker.layer) + ", " + (_side != 0 ? "open" : "closed") + ", prompt at " + knob.position + " (" + knobFrom + ").");
        return true;
    }

    // Where the door's prompt shows: the house door's own display point, carried over to the copy's leaf, or else the
    // closing edge at handle height: the edge away from the hinge for a swinging leaf, the edge against the slide for a
    // sliding one.
    private Vector3 Knob(GameObject house, Transform leaf, string leafPath, Bounds bounds, Vector3 slide, out string from)
    {
        var houseLeaf = house.transform.Find(leafPath);
        foreach (var vanilla in house.GetComponentsInChildren<InteractableObject>(true))
        {
            var point = vanilla.displayLocationPoint;
            if (point == null || houseLeaf == null) continue;
            from = "the house door's display point";
            return leaf.TransformPoint(houseLeaf.InverseTransformPoint(point.position));
        }
        from = houseLeaf == null ? "the closing edge; the house door has no leaf at '" + leafPath + "'" : "the closing edge; the house door has no display point";
        var centre = bounds.center;
        var flat = _swing ? centre - leaf.position : -slide;
        flat.y = 0f;
        if (flat.sqrMagnitude < .0001f) return centre;
        var direction = flat.normalized;
        float half = Mathf.Abs(Vector3.Dot(bounds.extents, new Vector3(Mathf.Abs(direction.x), 0f, Mathf.Abs(direction.z))));
        var edge = centre + direction * Mathf.Max(0f, half - .08f);
        edge.y = bounds.min.y + 1f;
        return edge;
    }

    private Transform? FindCopy(Vector3 root)
    {
        foreach (var t in Object.FindObjectsOfType<Transform>(true))
            if (t.name == _copyName && (t.position - root).sqrMagnitude < .25f && t.gameObject.activeInHierarchy) return t;
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

    // The side of the doorway a point stands on: 1 where Cross(up, across) points, else -1.
    private int SideOf(Vector3 point) => Vector3.Dot(point - _hinge, Vector3.Cross(Vector3.up, _across)) >= 0f ? 1 : -1;

    // An NPC in the doorway opens a closed leaf away from itself on this peer only; it closes again once the NPC is through.
    private void WatchNpcs()
    {
        if (Time.unscaledTime < _nextNpcCheck) return;
        _nextNpcCheck = Time.unscaledTime + NpcCheckInterval;
        int side = 0;
        var npcs = NPCManager.NPCRegistry;
        for (int i = 0; npcs != null && i < npcs.Count; i++)
        {
            var npc = npcs[i];
            if (npc == null || !npc.gameObject.activeInHierarchy) continue;
            var offset = npc.transform.position - _doorway;
            if (Mathf.Abs(offset.y + 1f) > NpcHeight || new Vector2(offset.x, offset.z).sqrMagnitude > NpcReach * NpcReach) continue;
            side = -SideOf(npc.transform.position);
            break;
        }
        if (side != 0 && _npcSide == 0) Mod.Log(Name + " opens for an NPC on this peer.");
        _npcSide = side;
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
        string where = _leaf == null ? "no leaf bound" : _swing
            ? "leaf at " + _leaf.position.ToString("F2") + ", angle " + _angle.ToString("0") + (_npcSide != 0 ? ", held open for an NPC" : "")
            : "leaf at " + _leaf.position.ToString("F2") + ", progress " + _progress.ToString("0.00");
        string hit = _interaction == null ? "no interaction" : "interaction '" + _interaction.message + "' in " + InteractionRange + " m"
            + ", prompt " + _promptOffset.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " m from the leaf's middle";
        return (_side != 0 ? "open" : "closed") + "; " + where + "; " + hit + ".";
    }

    // A swinging leaf opens away from the local player.
    private void Request(bool open)
    {
        try
        {
            int side = !open ? 0 : !_swing ? 1 : Player.Local != null && _leaf != null ? -SideOf(Player.Local.transform.position) : 1;
            if (NativeBus.IsHost) Broadcast(side, "the host");
            else
            {
                NativeBus.SendToHost(_wire + "set:" + side);
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
            if (body == "query") NativeBus.SendToPeer(peer, _wire + "state:" + _side);
            else if (body.StartsWith("set:", StringComparison.Ordinal) && int.TryParse(body.Substring(4), out int side)) Broadcast(Math.Sign(side), "peer " + peer);
            else Mod.Warn(Name + ": unknown message from peer " + peer + ": " + body);
        }
        catch (Exception error) { MelonLogger.Error("[Based] " + Name + " message from peer " + peer + " failed: " + error); }
        return true;
    }

    private void Broadcast(int side, string by)
    {
        var players = Player.PlayerList;
        int sent = 0;
        for (int i = 0; players != null && i < players.Count; i++)
        {
            var connection = players[i]?.Connection;
            if (connection == null) continue;
            try { NativeBus.SendToPeer(connection.ClientId, _wire + "state:" + side); sent++; }
            catch (Exception error) { MelonLogger.Error("[Based] " + Name + " state to peer " + connection.ClientId + " failed: " + error.Message); }
        }
        Mod.Log(Name + " " + (side != 0 ? "opened" : "closed") + " by " + by + "; sent to " + sent + " peers.");
    }

    // True when the message belongs to this door.
    internal bool OnClient(string payload)
    {
        if (!payload.StartsWith(_wire, StringComparison.Ordinal)) return false;
        string body = payload.Substring(_wire.Length);
        if (!body.StartsWith("state:", StringComparison.Ordinal) || !int.TryParse(body.Substring(6), out int side))
        {
            Mod.Warn(Name + ": unknown message from the host: " + body);
            return true;
        }
        side = Math.Sign(side);
        if (side != _side) Mod.Log(Name + " is now " + (side != 0 ? "open" : "closed") + (_leaf == null ? " (no copy bound on this peer yet)" : "") + ".");
        _side = side;
        return true;
    }

    private void Move()
    {
        if (_interaction != null && _shownOpen != (_side != 0))
        {
            _interaction.SetMessage(Text.T(_side != 0 ? "Close" : "Open"));
            _shownOpen = _side != 0;
        }
        if (_swing)
        {
            float target = (_side != 0 ? _side : _npcSide) * _swingAngle;
            if (Mathf.Approximately(_angle, target)) return;
            _angle = Mathf.MoveTowards(_angle, target, Time.deltaTime * SwingSpeed);
        }
        else
        {
            float target = _side != 0 ? 1f : 0f;
            if (Mathf.Approximately(_progress, target)) return;
            _progress = Mathf.MoveTowards(_progress, target, Time.deltaTime / _duration);
        }
        Place();
    }

    private void Place()
    {
        if (_leaf == null) return;
        if (_swing)
        {
            _leaf.localRotation = _closedTurn * Quaternion.Euler(0f, _angle, 0f);
            return;
        }
        float eased = _curve != null && _curve.length > 0 ? _curve.Evaluate(_progress) : Mathf.SmoothStep(0f, 1f, _progress);
        _leaf.position = Vector3.LerpUnclamped(_closed, _open, eased);
    }
}
