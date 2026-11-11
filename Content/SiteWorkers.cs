using Based.Core;
using Il2CppFishNet;
using Il2CppFishNet.Object;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.AvatarFramework;
using Personnel.Model;
using S1API.Entities;
using UnityEngine;
using UnityEngine.AI;
using NativeNpc = Il2CppScheduleOne.NPCs.NPC;
using Object = UnityEngine.Object;

namespace Based.Content;

// What one site's crew is: who they are, where each of them works on which build, what they hold and say.
internal abstract class CrewPlan
{
    // Prefix of every log line of this crew.
    public abstract string LogName { get; }
    public abstract string[] Ids { get; }
    public abstract string[] Names { get; }
    public abstract string Region { get; }
    public abstract Vector3 SpawnPosition { get; }
    public abstract NpcAppearance Appearance(int index);
    // True for the S1API wrapper classes of this crew.
    public abstract bool Owns(NPC npc);
    // Whether the crew is on site while this upgrade stands at this stage and phase.
    public abstract bool WorksOn(string upgrade, UpgradeStage stage, string? phase);
    // Whether this worker takes part in the job; the others stay out of sight.
    public virtual bool OnJob(int index, string upgrade) => true;
    public abstract Vector3 WorkPoint(int index, bool secondary, string upgrade, string? phase);
    // Where the tool lands, seen from the work point; the worker faces it.
    public abstract Vector3 ContactPoint(int index, bool secondary, string upgrade, string? phase);
    public abstract bool UsesTool(int index, bool secondary, string upgrade, string? phase);
    // The equippable the worker swings with the hammer animation.
    public virtual string Tool(int index) => "Avatar/Equippables/Hammer";
    // What the worker holds while not swinging, such as the foreman's clipboard.
    public virtual string? Held(int index) => null;
    // Reaching for material between bursts of swings.
    public virtual bool GrabsBetweenSwings(int index) => true;
    // The English line the worker says; the crew translates it.
    public abstract string Line(int index, string upgrade, string? phase);
}

// The construction workers of one site: Personnel NPCs that S1API spawns and networks. On the host they walk between
// the plan's work points during work hours and swing their tool; clients see the swing through the animator.
internal sealed class SiteWorkers : ISiteCrew
{
    private const float SpawnScanInterval = 1f;
    private const float IdleCheckInterval = 1f;
    // Work comes in bursts with a reach for material between them, and a worker moves on after a few of those.
    private const float SwingMin = 2.5f, SwingMax = 5f, RestMin = 1.5f, RestMax = 3.5f, StationMin = 10f, StationMax = 18f;
    private static readonly int UseHammerParameter = Animator.StringToHash("UseHammer");
    private static AudioClip[]? _clips;
    private static string? _audioWarning;

    private readonly CrewPlan _plan;
    private readonly Dictionary<string, Worker> _workers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (float Since, string Reason, bool Failed)> _pendingSpawns = new(StringComparer.Ordinal);
    private Action<string> _info = MelonLoader.MelonLogger.Msg;
    private Action<string> _warn = MelonLoader.MelonLogger.Warning;
    private UpgradeStage _stage;
    private string _upgrade = "";
    private string? _phase;
    private bool _initialized;
    private float _nextSpawnScan;

    public SiteWorkers(CrewPlan plan) => _plan = plan;

    // Share of the UseHammer clip at which the head lands; "baseddiag hammer" measures it on a live avatar.
    public float HammerContactNormalizedTime { get; set; } = 0.325f;

    public int ActiveCount => _workers.Values.Count(w => w.Visible);

#if DEBUG
    private float _swingProbeUntil;

    public void ProbeSwing(float seconds) => _swingProbeUntil = Time.time + seconds;

    public void Diagnostics()
    {
        _info(_plan.LogName + " diagnostics: attached=" + _workers.Count + "/" + _plan.Ids.Length + ", active=" + ActiveCount +
            ", upgrade=" + _upgrade + ", phase=" + (_phase ?? "none") + ", stage=" + _stage + ", pending=" + _pendingSpawns.Count);
        foreach (var worker in _workers.Values.OrderBy(w => w.Npc.ID)) worker.Diagnostics();
    }
#endif

    public void Initialize(Action<string> info, Action<string> warn)
    {
        _info = info;
        _warn = warn;
        if (_initialized) return;
        for (int i = 0; i < _plan.Ids.Length; i++)
        {
            if (Personnel.API.TryGet(_plan.Ids[i], out _)) continue;
            var definition = new NpcDef
            {
                Id = _plan.Ids[i], SaveId = _plan.Ids[i], Source = "Based", DisplayName = _plan.Names[i],
                Spawn = new NpcSpawn { Physical = true, Auto = false, Region = _plan.Region, Position = _plan.SpawnPosition },
                Contact = new NpcContact { Visible = false, MapMarker = false },
                Behavior = new NpcBehavior { Aggression = 0, Conversation = "none", MaxHealth = 100, Scale = 1 },
                Appearance = _plan.Appearance(i)
            };
            if (!Personnel.API.Register(definition)) throw new InvalidOperationException("Based could not register worker definition " + _plan.Ids[i]);
        }
        _initialized = true;
        _info(_plan.LogName + ": " + _plan.Ids.Length + " stable Personnel definitions registered; S1API owns instantiation and networking");
    }

    public void Update(string upgrade, UpgradeStage stage, string? phase)
    {
        bool changed = stage != _stage || phase != _phase || upgrade != _upgrade;
        _stage = stage;
        _upgrade = upgrade;
        _phase = phase;
        if (!changed) return;
        foreach (var worker in _workers.Values)
        {
            worker.ResetWork();
            worker.RefreshDialogue();
        }
        _info(_plan.LogName + ": upgrade=" + upgrade + ", state=" + stage + ", phase=" + (phase ?? "none"));
    }

    public void Tick()
    {
        if (!_initialized) return;
        if (_workers.Count < _plan.Ids.Length && Time.unscaledTime >= _nextSpawnScan)
        {
            _nextSpawnScan = Time.unscaledTime + SpawnScanInterval;
            ScanForSpawnedWorkers();
        }
        bool working = _plan.WorksOn(_upgrade, _stage, _phase) && NetworkSingleton<TimeManager>.InstanceExists &&
            NetworkSingleton<TimeManager>.Instance.CurrentTime >= 800 && NetworkSingleton<TimeManager>.Instance.CurrentTime < 1800;
        foreach (var worker in _workers.Values) worker.Tick(working && _plan.OnJob(worker.Index, _upgrade));
    }

    // S1API can hydrate client wrappers without calling a user constructor or OnCreated, so the list is the only signal.
    private void ScanForSpawnedWorkers()
    {
        foreach (var npc in NPC.All)
        {
            if (!_plan.Owns(npc) || npc.gameObject == null || _workers.ContainsKey(npc.ID)) continue;
            if (NativeSpawnReady(npc, out string reason))
            {
                if (_pendingSpawns.Remove(npc.ID)) _info(_plan.LogName + ": " + npc.ID + " native network spawn confirmed");
                Attach(npc);
                continue;
            }
            if (!_pendingSpawns.TryGetValue(npc.ID, out var pending))
            {
                _pendingSpawns.Add(npc.ID, (Time.realtimeSinceStartup, reason, false));
                _info(_plan.LogName + ": " + npc.ID + " awaiting native network spawn: " + reason);
                continue;
            }
            if (pending.Reason != reason)
            {
                _info(_plan.LogName + ": " + npc.ID + " native spawn wait changed: " + reason);
                pending = (pending.Since, reason, pending.Failed);
                _pendingSpawns[npc.ID] = pending;
            }
            float elapsed = Time.realtimeSinceStartup - pending.Since;
            if (!pending.Failed && elapsed >= 20f)
            {
                _pendingSpawns[npc.ID] = (pending.Since, reason, true);
                MelonLoader.MelonLogger.Error(_plan.LogName + ": " + npc.ID + " native network spawn unconfirmed after " + elapsed.ToString("F1") +
                    "s: " + reason + "; S1API CustomNpcsReady=" + NPC.CustomNpcsReady + ". Inspect the original S1API spawn error; Based has not spawned a replacement.");
            }
        }
    }

    private static bool NativeSpawnReady(NPC npc, out string reason)
    {
        if (npc.gameObject == null) { reason = "root missing"; return false; }
        var network = npc.gameObject.GetComponent<NetworkObject>();
        if (network == null) { reason = "NetworkObject missing"; return false; }
        if (!network.IsSpawned) { reason = "NetworkObject.IsSpawned=false, rootActive=" + npc.gameObject.activeInHierarchy; return false; }
        if (!npc.gameObject.activeInHierarchy) { reason = "spawned root inactive"; return false; }
        reason = "";
        return true;
    }

    private void Attach(NPC npc)
    {
        if (!NativeSpawnReady(npc, out string reason))
            throw new InvalidOperationException("Cannot attach worker " + npc.ID + " before native network spawn: " + reason);
        if (_workers.TryGetValue(npc.ID, out var existing))
        {
            if (existing.Npc.gameObject == npc.gameObject) return;
            if (existing.Npc.gameObject != null)
            {
                _warn(_plan.LogName + ": duplicate S1API identity " + npc.ID + "; second instance is hidden");
                npc.gameObject.GetComponent<NativeNpc>()?.SetVisible(false);
                foreach (var collider in npc.gameObject.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                return;
            }
            _workers.Remove(npc.ID);
        }
        int index = Array.IndexOf(_plan.Ids, npc.ID);
        if (index < 0) throw new InvalidOperationException("Unknown construction worker identity " + npc.ID);
        var worker = new Worker(this, npc, index);
        _workers.Add(npc.ID, worker);
        worker.RefreshDialogue();
        worker.Tick(false);
    }

    public void Cleanup()
    {
        foreach (var worker in _workers.Values) worker.Dispose();
        _workers.Clear();
        _pendingSpawns.Clear();
        _clips = null;
        _audioWarning = null;
        _stage = UpgradeStage.RightsMissing;
        _upgrade = "";
        _phase = null;
    }

    private AudioClip[] Clips()
    {
        if (_clips != null) return _clips;
        var loaded = Resources.FindObjectsOfTypeAll<AudioClip>();
        var result = new List<AudioClip>();
        for (int n = 1; n <= 3; n++)
        {
            string name = "Generic smooth hammer impact " + n;
            var clip = loaded.FirstOrDefault(c => c != null && c.name == name);
            if (clip != null) result.Add(clip);
        }
        _clips = result.ToArray();
        if (_clips.Length < 3 && _audioWarning == null)
        {
            _audioWarning = _plan.LogName + ": expected three loaded vanilla hammer impact clips, found " + _clips.Length + "; missing clips are not replaced";
            _warn(_audioWarning);
        }
        return _clips;
    }

    private sealed class Worker
    {
        internal readonly NPC Npc;
        internal readonly int Index;
        private readonly SiteWorkers _crew;
        private readonly CrewPlan _plan;
        private readonly NativeNpc _native;
        private readonly List<(Collider Collider, bool Enabled)> _colliders = new();
        private readonly AudioSource _audio;
        private readonly Animator? _animator;
        private readonly NavMeshAgent _agent;
        private readonly float _originalStoppingDistance;
        private readonly int _originalAreaMask;
        private bool _jobPropertyAccess;
        private bool _placed, _hammering, _swinging, _walking, _workAtSecondary, _heldEquipped, _stationWorking, _targetValid;
        private bool? _pendingSecondary;
        private Vector3 _requestedAnchor, _resolvedAnchor, _contact;
        private float _nextAction, _nextCheck, _workStartsAt, _rhythmSwitch, _previousNormalized = -1;
        private const float WorkStoppingDistance = 0.15f;
        private const float ArrivalHorizontal = 0.22f;
        private const float ArrivalVertical = 0.2f;
        private const float LeaveHorizontal = 0.45f;
        private const float FloorHeightTolerance = 0.2f;
        private int _hammerLayer = -1, _soundIndex;
        private string? _failure;
        internal bool Visible;
#if DEBUG
        private int _soundCount;
        private string _lastSoundClip = "";
        private readonly float[] _swingHeights = new float[20];
        private readonly int[] _swingSamples = new int[20];
#endif

        private string Upgrade => _crew._upgrade;
        private string? Phase => _crew._phase;

        internal Worker(SiteWorkers crew, NPC npc, int index)
        {
            _crew = crew;
            _plan = crew._plan;
            Npc = npc;
            Index = index;
            _native = npc.gameObject.GetComponent<NativeNpc>();
            if (_native == null) throw new InvalidOperationException("Worker " + npc.ID + " has no vanilla NPC component");
            _agent = npc.gameObject.GetComponent<NavMeshAgent>();
            if (_agent == null) throw new InvalidOperationException("Worker " + npc.ID + " has no native navigation agent");
            _originalStoppingDistance = _agent.stoppingDistance;
            _originalAreaMask = _agent.areaMask;
            ApplyAppearance();
            foreach (var collider in npc.gameObject.GetComponentsInChildren<Collider>(true)) _colliders.Add((collider, collider.enabled));
            var sound = new GameObject("Based.HammerContact");
            sound.transform.SetParent(npc.gameObject.transform, false);
            sound.transform.localPosition = new Vector3(0, 0.9f, 0.55f);
            _audio = sound.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.loop = false;
            _audio.spatialBlend = 1;
            _audio.minDistance = 1;
            _audio.maxDistance = 12;
            _audio.volume = 0.14f;
            _audio.dopplerLevel = 0;
            _animator = npc.gameObject.GetComponentInChildren<Animator>(true);
            _nextAction = Time.time + index * 2.35f;
            OpenTalkAngle();
        }

        private void Info(string line) => _crew._info(_plan.LogName + ": " + line);

        // Vanilla only talks from the NPC's front half, and a worker faces the job, away from whoever walks in.
        private void OpenTalkAngle()
        {
            var talk = Npc.gameObject.GetComponentInChildren<Il2CppScheduleOne.Dialogue.DialogueController>(true)?._interactable;
            if (talk == null)
            {
                _crew._warn(_plan.LogName + ": " + Npc.ID + " has no talk interaction; the angle limit stays.");
                return;
            }
            talk.LimitInteractionAngle = false;
            talk.Priority = 1;
            Info(Npc.ID + " can be talked to from every side.");
        }

        private void ApplyAppearance()
        {
            if (!Personnel.API.TryGet(Npc.ID, out var definition) || definition == null)
                throw new InvalidOperationException("Worker appearance definition missing: " + Npc.ID);
            var appearance = definition.Appearance;
            if (Resources.Load<GameObject>(appearance.HairPath) == null)
                throw new InvalidOperationException("Worker hair resource missing: " + appearance.HairPath);
            foreach (var face in appearance.FaceLayers)
                if (Resources.Load<AvatarLayer>(face.Path) == null)
                    throw new InvalidOperationException("Worker face resource missing: " + face.Path);
            foreach (var accessory in appearance.Accessories)
                if (Resources.Load<GameObject>(accessory.Path) == null)
                    throw new InvalidOperationException("Worker accessory resource missing: " + accessory.Path);
            if (!Personnel.API.ApplyAppearance(_native.Avatar, definition))
                throw new InvalidOperationException("Worker appearance could not be applied: " + Npc.ID);
            Info(Npc.ID + " appearance applied after native spawn, including saved workers; skin=" + appearance.SkinColor);
        }

        internal void RefreshDialogue()
        {
            Npc.Dialogue.BuildAndRegisterContainer("based_construction", builder => builder.AddNode("ENTRY",
                Based.UI.Text.T(_plan.Line(Index, Upgrade, Phase))));
            if (!Npc.Dialogue.UseContainerOnInteract("based_construction")) Failure("construction dialogue could not bind to interaction");
        }

        internal void Tick(bool working)
        {
            if (Npc.gameObject == null || !Npc.gameObject.activeInHierarchy) return;
            bool host = InstanceFinder.IsServer;
            if (!working)
            {
                if (Time.time < _nextCheck) return;
                _nextCheck = Time.time + IdleCheckInterval;
                if (_hammering || _walking || _stationWorking || _targetValid) ResetWork();
                RestorePropertyAccess();
                SetVisible(false);
                _placed = false;
                return;
            }
            if (!host)
            {
                SetVisible(true);
                PlayContactSound();
                return;
            }
            if (Time.time < _nextCheck) { PlayContactSound(); return; }
            _nextCheck = Time.time + 0.12f;
            if (!Npc.IsConscious || Npc.Dialogue.IsDialogueInProgress)
            {
                ResetWork();
                _nextAction = Time.time + 2f;
                return;
            }
            GrantPropertyAccess();
            if (!_placed)
            {
                var anchor = WorkPoint(_workAtSecondary);
                if (!TryResolveAnchor(anchor, out var resolved, out string reason))
                {
                    Failure(reason);
                    SetVisible(false);
                    return;
                }
                Npc.Movement.Warp(resolved);
                CommitAnchor(_workAtSecondary, anchor, resolved);
                _placed = true;
                _nextAction = Time.time + Index * 2.35f;
            }
            SetVisible(true);
            string? held = _plan.Held(Index);
            if (held != null && !_heldEquipped)
            {
                Equip(held);
                _heldEquipped = true;
            }
            if (_walking)
            {
                if (!AtAssignedAnchor())
                {
                    if (Time.time > _nextAction)
                    {
                        Npc.Movement.Stop();
                        _walking = false;
                        _pendingSecondary = _workAtSecondary;
                        Failure("work anchor path did not arrive within 15 seconds");
                        _nextAction = Time.time + 8f;
                    }
                    return;
                }
                _walking = false;
                Npc.Movement.Stop();
                Npc.Movement.FacePoint(_contact);
                _stationWorking = true;
                _workStartsAt = Time.time + 0.55f;
                _nextAction = _workStartsAt + UnityEngine.Random.Range(StationMin, StationMax);
                _swinging = true;
                _rhythmSwitch = _workStartsAt + UnityEngine.Random.Range(SwingMin, SwingMax);
                Recover();
            }
            else if (_stationWorking && LeftAssignedAnchor())
            {
                SetHammer(false);
                _stationWorking = false;
                _pendingSecondary = _workAtSecondary;
                _nextAction = Time.time;
                Failure("worker left its assigned work anchor; activity stopped");
            }
            else if (Time.time >= _nextAction)
            {
                SetHammer(false);
                _stationWorking = false;
                BeginWalk(_pendingSecondary ?? !_workAtSecondary);
            }
            if (_stationWorking && Time.time >= _workStartsAt)
            {
                TickRhythm();
                SetHammer(_plan.UsesTool(Index, _workAtSecondary, Upgrade, Phase) && ContactInReach());
            }
            PlayContactSound();
        }

        private void TickRhythm()
        {
            if (Time.time < _rhythmSwitch) return;
            _swinging = !_swinging;
            _rhythmSwitch = Time.time + (_swinging ? UnityEngine.Random.Range(SwingMin, SwingMax) : UnityEngine.Random.Range(RestMin, RestMax));
            if (_hammering) _native.SetAnimationBool_Networked(null, "UseHammer", _swinging);
            if (!_swinging && _plan.GrabsBetweenSwings(Index)) _native.SetAnimationTrigger_Networked(null, "GrabItem");
        }

        private void GrantPropertyAccess()
        {
            if (_jobPropertyAccess) return;
            int area = NavMesh.GetAreaFromName("PropertyInterior");
            if (area < 0 || area > 31)
                throw new InvalidOperationException("Worker " + Npc.ID + " cannot start its property job: named PropertyInterior navigation area is missing");
            // Plain S1API civilians exclude private interiors. This owned construction job grants that named area only.
            int allowed = _originalAreaMask | (1 << area);
            _agent.areaMask = allowed;
            if (_agent.areaMask != allowed)
                throw new InvalidOperationException("Worker " + Npc.ID + " PropertyInterior access write was not acknowledged: required=" + allowed + ", actual=" + _agent.areaMask);
            _jobPropertyAccess = true;
            Info(Npc.ID + " on-duty PropertyInterior access acknowledged: mask " + _originalAreaMask + " -> " + allowed + ", named area=" + area);
        }

        private void RestorePropertyAccess()
        {
            if (!_jobPropertyAccess) return;
            if (_agent == null)
            {
                _jobPropertyAccess = false;
                Info(Npc.ID + " PropertyInterior access ended after native agent destruction");
                return;
            }
            _agent.areaMask = _originalAreaMask;
            if (_agent.areaMask != _originalAreaMask)
                throw new InvalidOperationException("Worker " + Npc.ID + " original navigation mask restore was not acknowledged: required=" + _originalAreaMask + ", actual=" + _agent.areaMask);
            _jobPropertyAccess = false;
            Info(Npc.ID + " original navigation access restored and acknowledged: mask " + _originalAreaMask);
        }

        private bool TryResolveAnchor(Vector3 requested, out Vector3 resolved, out string reason)
        {
            var filter = new NavMeshQueryFilter { agentTypeID = _agent.agentTypeID, areaMask = _agent.areaMask };
            if (!NavMesh.SamplePosition(requested, out NavMeshHit hit, 0.6f, filter))
            {
                resolved = default;
                reason = "no navigation surface within 0.6m of work anchor " + requested + " for worker agent/mask";
                return false;
            }
            resolved = hit.position;
            if (HorizontalDistance(requested, resolved) > 0.2f || Mathf.Abs(requested.y - resolved.y) > FloorHeightTolerance)
            {
                reason = "work anchor projection exceeds 0.2m horizontally or " + FloorHeightTolerance + "m in height: " + requested + " -> " + resolved;
                return false;
            }
            reason = "";
            return true;
        }

        private void BeginWalk(bool secondary)
        {
            _pendingSecondary = secondary;
            var requested = WorkPoint(secondary);
            if (!TryResolveAnchor(requested, out var resolved, out string reason))
            {
                Failure(reason);
                _nextAction = Time.time + 8f;
                return;
            }
            var filter = new NavMeshQueryFilter { agentTypeID = _agent.agentTypeID, areaMask = _agent.areaMask };
            var path = new NavMeshPath();
            if (!_agent.isOnNavMesh || !NavMesh.CalculatePath(Npc.Position, resolved, filter, path) || path.status != NavMeshPathStatus.PathComplete)
            {
                // A rebuilt site can leave a working worker outside the walls. The next check puts them back at work.
                Failure("no complete path to work anchor " + requested + " for worker agent/mask; placing the worker again");
                _placed = false;
                _nextAction = Time.time + 8f;
                return;
            }
            CommitAnchor(secondary, requested, resolved);
            _pendingSecondary = null;
            _agent.stoppingDistance = WorkStoppingDistance;
            // The S1API convenience overload uses a one-metre success distance, too far for tool contact.
            _native.Movement.SetDestination(resolved, null, ArrivalHorizontal, 0.01f);
            // The agent's own request waits in a queue shared with every walking NPC, which after a load can outlast the whole walk. The checked path is ready now.
            if (!_agent.SetPath(path))
            {
                _native.Movement.Stop();
                Failure("work anchor path to " + requested + " was refused by the agent");
                _nextAction = Time.time + 8f;
                return;
            }
            _walking = true;
            _nextAction = Time.time + 15f;
        }

        private void CommitAnchor(bool secondary, Vector3 requested, Vector3 resolved)
        {
            _workAtSecondary = secondary;
            _requestedAnchor = requested;
            _resolvedAnchor = resolved;
            _contact = _plan.ContactPoint(Index, secondary, Upgrade, Phase);
            _targetValid = true;
        }

        private Vector3 WorkPoint(bool secondary) => _plan.WorkPoint(Index, secondary, Upgrade, Phase);

        private bool AtAssignedAnchor() => _targetValid &&
            HorizontalDistance(Npc.Position, _resolvedAnchor) <= ArrivalHorizontal &&
            Mathf.Abs(Npc.Position.y - _resolvedAnchor.y) <= ArrivalVertical &&
            Mathf.Abs(Npc.Position.y - _requestedAnchor.y) <= FloorHeightTolerance;

        // A standing agent drifts a little, so leaving takes clearly more distance than arriving. Otherwise each nudge restarts the job.
        private bool LeftAssignedAnchor() => !_targetValid ||
            HorizontalDistance(Npc.Position, _resolvedAnchor) > LeaveHorizontal ||
            Mathf.Abs(Npc.Position.y - _resolvedAnchor.y) > ArrivalVertical;

        private static float HorizontalDistance(Vector3 a, Vector3 b) =>
            Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        private bool ContactInReach()
        {
            Vector3 direction = _contact - Npc.Position;
            direction.y = 0;
            return HorizontalDistance(Npc.Position, _contact) <= 0.85f &&
                Mathf.Abs(Npc.Position.y + 0.9f - _contact.y) <= 0.35f &&
                Vector3.Angle(Npc.Transform.forward, direction) <= 20f;
        }

        private void SetVisible(bool visible)
        {
            if (_native == null) return;
            if (_native.isVisible != visible) _native.SetVisible(visible, false);
            if (Visible == visible) return;
            Visible = visible;
            foreach (var state in _colliders) if (state.Collider != null) state.Collider.enabled = visible && state.Enabled;
        }

        private void SetHammer(bool enabled)
        {
            if (_hammering == enabled || !InstanceFinder.IsServer) return;
            _hammering = enabled;
            Equip(enabled ? _plan.Tool(Index) : _plan.Held(Index) ?? "");
            _native.SetAnimationBool_Networked(null, "UseHammer", enabled && _swinging);
            _previousNormalized = -1;
            _hammerLayer = -1;
            if (!enabled) _audio.Stop();
        }

        // The S1API call changes only the local avatar. This RPC also reaches clients, and the NPC resends it to later joiners.
        private void Equip(string path) => _native.SetEquippable_Client(null, path);

        private bool PlaysHammerClip(int layer)
        {
            var clips = _animator!.GetCurrentAnimatorClipInfo(layer);
            for (int i = 0; i < clips.Length; i++)
                if (clips[i].clip != null && clips[i].clip.name == "UseHammer" && clips[i].weight > 0.5f) return true;
            return false;
        }

        private void PlayContactSound()
        {
            if (!Visible || _animator == null) return;
            // Clients only see the swing through the animator, so the cheap flag decides before any clip lookup.
            if (!_animator.GetBool(UseHammerParameter)) { _hammerLayer = -1; _previousNormalized = -1; return; }
            if (_hammerLayer < 0)
            {
                for (int layer = 0; layer < _animator.layerCount; layer++)
                    if (PlaysHammerClip(layer)) { _hammerLayer = layer; break; }
                if (_hammerLayer < 0) return;
            }
            if (!PlaysHammerClip(_hammerLayer)) { _hammerLayer = -1; _previousNormalized = -1; return; }
            float normalized = _animator.GetCurrentAnimatorStateInfo(_hammerLayer).normalizedTime;
#if DEBUG
            SampleSwing(normalized);
#endif
            if (_previousNormalized >= 0)
            {
                float contact = Mathf.Clamp01(_crew.HammerContactNormalizedTime);
                int before = Mathf.FloorToInt(_previousNormalized - contact);
                int now = Mathf.FloorToInt(normalized - contact);
                if (now > before)
                {
                    var clips = _crew.Clips();
                    if (clips.Length > 0)
                    {
                        var clip = clips[_soundIndex++ % clips.Length];
                        _audio.pitch = 0.95f + ((_soundIndex - 1) % 3) * 0.05f;
                        _audio.PlayOneShot(clip);
#if DEBUG
                        _soundCount++;
                        _lastSoundClip = clip.name;
#endif
                    }
                }
            }
            _previousNormalized = normalized;
        }

#if DEBUG
        internal void Diagnostics()
        {
            if (Npc.gameObject == null) { Info("worker diagnostics: " + Npc.ID + " root missing"); return; }
            Vector3 position = Npc.gameObject.transform.position;
            var movement = _native.Movement;
            var movementAgent = movement._agent;
            _crew._info("worker diagnostics: " + Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                id = Npc.ID,
                agent = new
                {
                    sameAsMovement = movementAgent != null && movementAgent.Pointer == _agent.Pointer,
                    enabled = _agent.enabled,
                    onNavMesh = _agent.isOnNavMesh,
                    isStopped = _agent.isOnNavMesh && _agent.isStopped,
                    hasPath = _agent.hasPath,
                    pathPending = _agent.pathPending,
                    pathStatus = _agent.pathStatus.ToString(),
                    remaining = _agent.isOnNavMesh ? _agent.remainingDistance : -1f,
                    stopping = _agent.stoppingDistance,
                    speed = _agent.speed,
                    velocity = _agent.velocity.magnitude,
                    desired = _agent.desiredVelocity.magnitude,
                    updatePosition = _agent.updatePosition,
                    areaMask = _agent.areaMask,
                    obstacleAvoidance = _agent.obstacleAvoidanceType.ToString(),
                    canMove = movement.CanMove,
                    hasDestination = movement._hasDestination,
                    destination = new[] { movement.CurrentDestination.x, movement.CurrentDestination.y, movement.CurrentDestination.z },
                    inBuilding = _native.isInBuilding,
                    ragdolled = _native.Avatar != null && _native.Avatar.Ragdolled,
                    moveSpeed = movement.SpeedController != null ? movement.SpeedController.MoveSpeed : -1f
                },
                position = new[] { position.x, position.y, position.z },
                visible = Visible,
                upgrade = Upgrade,
                phase = Phase,
                anchor = _targetValid ? new[] { _resolvedAnchor.x, _resolvedAnchor.y, _resolvedAnchor.z } : null,
                atAnchor = AtAssignedAnchor(),
                contactInReach = _targetValid && ContactInReach(),
                walking = _walking,
                hammering = _hammering,
                soundCount = _soundCount,
                lastSoundClip = _lastSoundClip,
                failure = _failure
            }));
        }

        // Averages the right hand's height per slice of the swing; the lowest slice is where the hammer lands.
        private void SampleSwing(float normalized)
        {
            if (Time.time < _crew._swingProbeUntil)
            {
                var hand = _animator != null && _animator.isHuman ? _animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
                if (hand == null) return;
                int slice = Mathf.FloorToInt((normalized - Mathf.Floor(normalized)) * _swingHeights.Length) % _swingHeights.Length;
                _swingHeights[slice] += hand.position.y;
                _swingSamples[slice]++;
                return;
            }
            if (_crew._swingProbeUntil <= 0 || _swingSamples.Sum() == 0) return;
            var report = _swingHeights.Select((h, i) => (Time: (i + .5f) / _swingHeights.Length, Height: _swingSamples[i] > 0 ? h / _swingSamples[i] : float.NaN)).ToArray();
            var lowest = report.Where(r => !float.IsNaN(r.Height)).OrderBy(r => r.Height).First();
            Info(Npc.ID + " hammer swing lowest at normalized " + lowest.Time.ToString("F3") + "; heights " +
                string.Join(" ", report.Select(r => r.Time.ToString("F2") + "=" + r.Height.ToString("F3"))));
            Array.Clear(_swingHeights, 0, _swingHeights.Length);
            Array.Clear(_swingSamples, 0, _swingSamples.Length);
        }
#endif

        internal void ResetWork()
        {
            if (Npc.gameObject == null) return;
            if (InstanceFinder.IsServer)
            {
                SetHammer(false);
                if (_heldEquipped) { Equip(""); _heldEquipped = false; }
                Npc.Movement.Stop();
            }
            _walking = false;
            _stationWorking = false;
            _targetValid = false;
            _pendingSecondary = null;
            // The access stays: without it the agent standing inside jumps to the nearest floor it may use, outside the room.
            if (_agent != null) _agent.stoppingDistance = _originalStoppingDistance;
            _audio.Stop();
            _nextAction = Time.time + 1f + Index * 2.35f;
        }

        private void Failure(string reason)
        {
            if (_failure == reason) return;
            _failure = reason;
            _crew._warn(_plan.LogName + ": " + Npc.ID + " upgrade=" + Upgrade + " phase=" + (Phase ?? "none") + ": " + reason);
        }

        private void Recover()
        {
            if (_failure == null) return;
            Info(Npc.ID + " recovered from " + _failure);
            _failure = null;
        }

        internal void Dispose()
        {
            ResetWork();
            RestorePropertyAccess();
            SetVisible(false);
            if (_audio != null) Object.Destroy(_audio.gameObject);
        }
    }
}
