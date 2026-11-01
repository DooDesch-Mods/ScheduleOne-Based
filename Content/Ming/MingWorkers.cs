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

namespace Based.Content.Ming;

public static class MingWorkers
{
    public const string ForemanId = "based_ming_foreman";
    public const string CarpenterId = "based_ming_carpenter";
    public const string FitterId = "based_ming_fitter";
    // Share of the UseHammer clip at which the head lands; "baseddiag hammer" measures it on a live avatar.
    public static float HammerContactNormalizedTime { get; set; } = 0.325f;
    private const float SpawnScanInterval = 1f;
    private const float IdleCheckInterval = 1f;
    // Work comes in bursts with a reach for material between them, and a worker moves on after a few of those.
    private const float SwingMin = 2.5f, SwingMax = 5f, RestMin = 1.5f, RestMax = 3.5f, StationMin = 10f, StationMax = 18f;
    private static readonly int UseHammerParameter = Animator.StringToHash("UseHammer");
    private static readonly Dictionary<string, Worker> Workers = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, (float Since, string Reason, bool Failed)> PendingSpawns = new(StringComparer.Ordinal);
    private static readonly string[] Ids = { ForemanId, CarpenterId, FitterId };
    private static readonly string[] Names = { "Frank Mercer", "Joe Ellis", "Luis Moreno" };
    private static Action<string> _info = MelonLoader.MelonLogger.Msg;
    private static Action<string> _warn = MelonLoader.MelonLogger.Warning;
    private static UpgradeStage _stage;
    private static string? _phase;
    private static string? _audioWarning;
    private static AudioClip[]? _clips;
    private static bool _initialized;
    private static float _nextSpawnScan;

    public static int ActiveCount => Workers.Values.Count(w => w.Visible);

#if DEBUG
    private static float _swingProbeUntil;

    public static void ProbeSwing(float seconds) => _swingProbeUntil = Time.time + seconds;

    public static void Diagnostics()
    {
        _info("workers diagnostics: attached=" + Workers.Count + "/3, active=" + ActiveCount +
            ", phase=" + (_phase ?? "none") + ", stage=" + _stage + ", pending=" + PendingSpawns.Count);
        foreach (var worker in Workers.Values.OrderBy(w => w.Npc.ID)) worker.Diagnostics();
    }
#endif

    public static void Initialize(Action<string>? info = null, Action<string>? warn = null)
    {
        if (info != null) _info = info;
        if (warn != null) _warn = warn;
        if (_initialized) return;
        for (int i = 0; i < Ids.Length; i++)
        {
            if (Personnel.API.TryGet(Ids[i], out _)) continue;
            var definition = new NpcDef
            {
                Id = Ids[i], SaveId = Ids[i], Source = "Based", DisplayName = Names[i],
                Spawn = new NpcSpawn { Physical = true, Auto = false, Region = "Northtown", Position = new Vector3(-63.949f, MingWorld.FloorY, 140.19f) },
                Contact = new NpcContact { Visible = false, MapMarker = false },
                Behavior = new NpcBehavior { Aggression = 0, Conversation = "none", MaxHealth = 100, Scale = 1 },
                Appearance = CreateAppearance(i)
            };
            if (!Personnel.API.Register(definition)) throw new InvalidOperationException("Based could not register worker definition " + Ids[i]);
        }
        _initialized = true;
        _info("workers: three stable Personnel definitions registered; S1API owns instantiation and networking");
    }

    private static NpcAppearance CreateAppearance(int index)
    {
        var skin = index == 0 ? new Color(0.72f, 0.53f, 0.40f, 1f) : index == 1
            ? new Color(0.79f, 0.59f, 0.43f, 1f) : new Color(0.56f, 0.39f, 0.28f, 1f);
        return new NpcAppearance
        {
            Gender = 0,
            Height = index == 0 ? 1.02f : index == 1 ? 0.98f : 1.04f,
            Weight = index == 0 ? 0.63f : 0.5f,
            SkinColor = skin,
            LeftEyeLidColor = skin,
            RightEyeLidColor = skin,
            HairPath = index == 0 ? "Avatar/Hair/Balding/Balding" : "Avatar/Hair/BuzzCut/BuzzCut",
            HairColor = index == 0 ? new Color(0.24f, 0.20f, 0.17f, 1f) : new Color(0.15f, 0.11f, 0.08f, 1f),
            EyebrowScale = 1.05f,
            EyebrowThickness = 1.1f,
            LeftEyeTop = 0.55f,
            RightEyeTop = 0.55f,
            LeftEyeBottom = 0.08f,
            RightEyeBottom = 0.08f,
            PupilDilation = 0.6f,
            FaceLayers = new List<NpcLayer>
            {
                new() { Path = "Avatar/Layers/Face/Face_Neutral", Tint = Color.black },
                new() { Path = "Avatar/Layers/Face/FacialHair_Stubble", Tint = Color.black }
            },
            BodyLayers = new List<NpcLayer>
            {
                new() { Path = index == 1 ? "Avatar/Layers/Top/Overalls" : "Avatar/Layers/Top/T-Shirt", Tint = index == 0 ? new Color(0.62f, 0.39f, 0.14f, 1f) : index == 1 ? new Color(0.21f, 0.30f, 0.44f, 1f) : new Color(0.51f, 0.23f, 0.17f, 1f) },
                new() { Path = "Avatar/Layers/Bottom/Jeans", Tint = new Color(0.25f, 0.31f, 0.38f, 1f) }
            },
            Accessories = new List<NpcLayer>
            {
                new() { Path = "Avatar/Accessories/Feet/CombatBoots/CombatBoots", Tint = new Color(0.22f, 0.16f, 0.11f, 1f) }
            }
        };
    }

    public static void Update(UpgradeStage stage, string? phase)
    {
        bool changed = stage != _stage || phase != _phase;
        _stage = stage;
        _phase = phase;
        if (!changed) return;
        foreach (var worker in Workers.Values)
        {
            worker.ResetWork();
            worker.RefreshDialogue();
        }
        _info("workers: state=" + stage + ", phase=" + (phase ?? "none"));
    }

    public static void Tick()
    {
        if (!_initialized) return;
        if (Workers.Count < Ids.Length && Time.unscaledTime >= _nextSpawnScan)
        {
            _nextSpawnScan = Time.unscaledTime + SpawnScanInterval;
            ScanForSpawnedWorkers();
        }
        bool working = _stage == UpgradeStage.Constructing && _phase != null && NetworkSingleton<TimeManager>.InstanceExists &&
            NetworkSingleton<TimeManager>.Instance.CurrentTime >= 800 && NetworkSingleton<TimeManager>.Instance.CurrentTime < 1800;
        foreach (var worker in Workers.Values) worker.Tick(working);
    }

    // S1API can hydrate client wrappers without calling a user constructor or OnCreated, so the list is the only signal.
    private static void ScanForSpawnedWorkers()
    {
        foreach (var npc in NPC.All)
        {
            if (npc is not MingWorkerNpc construction || construction.gameObject == null || Workers.ContainsKey(construction.ID)) continue;
            if (NativeSpawnReady(construction, out string reason))
            {
                if (PendingSpawns.Remove(construction.ID)) _info("workers: " + construction.ID + " native network spawn confirmed");
                Attach(construction);
                continue;
            }
            if (!PendingSpawns.TryGetValue(construction.ID, out var pending))
            {
                PendingSpawns.Add(construction.ID, (Time.realtimeSinceStartup, reason, false));
                _info("workers: " + construction.ID + " awaiting native network spawn: " + reason);
                continue;
            }
            if (pending.Reason != reason)
            {
                _info("workers: " + construction.ID + " native spawn wait changed: " + reason);
                pending = (pending.Since, reason, pending.Failed);
                PendingSpawns[construction.ID] = pending;
            }
            float elapsed = Time.realtimeSinceStartup - pending.Since;
            if (!pending.Failed && elapsed >= 20f)
            {
                PendingSpawns[construction.ID] = (pending.Since, reason, true);
                MelonLoader.MelonLogger.Error("workers: " + construction.ID + " native network spawn unconfirmed after " + elapsed.ToString("F1") +
                    "s: " + reason + "; S1API CustomNpcsReady=" + NPC.CustomNpcsReady + ". Inspect the original S1API spawn error; Based has not spawned a replacement.");
            }
        }
    }

    private static bool NativeSpawnReady(MingWorkerNpc npc, out string reason)
    {
        if (npc.gameObject == null) { reason = "root missing"; return false; }
        var network = npc.gameObject.GetComponent<NetworkObject>();
        if (network == null) { reason = "NetworkObject missing"; return false; }
        if (!network.IsSpawned) { reason = "NetworkObject.IsSpawned=false, rootActive=" + npc.gameObject.activeInHierarchy; return false; }
        if (!npc.gameObject.activeInHierarchy) { reason = "spawned root inactive"; return false; }
        reason = "";
        return true;
    }

    private static void Attach(MingWorkerNpc npc)
    {
        if (!NativeSpawnReady(npc, out string reason))
            throw new InvalidOperationException("Cannot attach worker " + npc.ID + " before native network spawn: " + reason);
        if (Workers.TryGetValue(npc.ID, out var existing))
        {
            if (existing.Npc.gameObject == npc.gameObject) return;
            if (existing.Npc.gameObject != null)
            {
                _warn("workers: duplicate S1API identity " + npc.ID + "; second instance is hidden");
                npc.gameObject.GetComponent<NativeNpc>()?.SetVisible(false);
                foreach (var collider in npc.gameObject.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                return;
            }
            Workers.Remove(npc.ID);
        }
        int index = Array.IndexOf(Ids, npc.ID);
        if (index < 0) throw new InvalidOperationException("Unknown construction worker identity " + npc.ID);
        var worker = new Worker(npc, index);
        Workers.Add(npc.ID, worker);
        worker.RefreshDialogue();
        worker.Tick(false);
    }

    public static void Cleanup()
    {
        foreach (var worker in Workers.Values) worker.Dispose();
        Workers.Clear();
        PendingSpawns.Clear();
        _clips = null;
        _audioWarning = null;
        _stage = UpgradeStage.RightsMissing;
        _phase = null;
    }

    private static string ForemanLine => Based.UI.Text.T(_phase switch
    {
        "breakthrough" => "We're opening the wall today. Stay behind the barrier.",
        "frame" => "Floor's in. Walls go up today.",
        "roof" => "Roof and finishing today. You can move in tomorrow.",
        _ => "Talk to Mrs. Ming about the expansion."
    });

    private static AudioClip[] Clips()
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
            _audioWarning = "workers: expected three loaded vanilla hammer impact clips, found " + _clips.Length + "; missing clips are not replaced";
            _warn(_audioWarning);
        }
        return _clips;
    }

    private sealed class Worker
    {
        internal readonly MingWorkerNpc Npc;
        private readonly NativeNpc _native;
        private readonly int _index;
        private readonly List<(Collider Collider, bool Enabled)> _colliders = new();
        private readonly AudioSource _audio;
        private readonly Animator? _animator;
        private readonly NavMeshAgent _agent;
        private readonly float _originalStoppingDistance;
        private readonly int _originalAreaMask;
        private bool _jobPropertyAccess;
        private bool _placed, _hammering, _swinging, _walking, _workAtSecondary, _planEquipped, _stationWorking, _targetValid;
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

        internal Worker(MingWorkerNpc npc, int index)
        {
            Npc = npc;
            _index = index;
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
            OpenTalkAngle(npc);
        }

        // Vanilla only talks from the NPC's front half, and a worker faces the job, away from whoever walks in.
        private static void OpenTalkAngle(MingWorkerNpc npc)
        {
            var talk = npc.gameObject.GetComponentInChildren<Il2CppScheduleOne.Dialogue.DialogueController>(true)?._interactable;
            if (talk == null)
            {
                _warn("workers: " + npc.ID + " has no talk interaction; the angle limit stays.");
                return;
            }
            talk.LimitInteractionAngle = false;
            talk.Priority = 1;
            _info("workers: " + npc.ID + " can be talked to from every side.");
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
            _info("workers: " + Npc.ID + " appearance applied after native spawn, including saved workers; skin=" + appearance.SkinColor);
        }

        internal void RefreshDialogue()
        {
            Npc.Dialogue.BuildAndRegisterContainer("based_construction", builder => builder.AddNode("ENTRY",
                _index == 0 ? ForemanLine : Based.UI.Text.T(_phase == "roof" ? "Just the last few fittings. Mind the tools." : "Keep the site clear, please.")));
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
                _nextAction = Time.time + _index * 2.35f;
            }
            SetVisible(true);
            if (_index == 0 && !_planEquipped)
            {
                Equip("tools/managementclipboard/ManagementClipboard_AvatarEquippable");
                _planEquipped = true;
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
                SetHammer(WantsHammer() && ContactInReach());
            }
            PlayContactSound();
        }

        private void TickRhythm()
        {
            if (Time.time < _rhythmSwitch) return;
            _swinging = !_swinging;
            _rhythmSwitch = Time.time + (_swinging ? UnityEngine.Random.Range(SwingMin, SwingMax) : UnityEngine.Random.Range(RestMin, RestMax));
            if (_hammering) _native.SetAnimationBool_Networked(null, "UseHammer", _swinging);
            if (!_swinging && _index != 0) _native.SetAnimationTrigger_Networked(null, "GrabItem");
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
            _info("workers: " + Npc.ID + " on-duty PropertyInterior access acknowledged: mask " + _originalAreaMask + " -> " + allowed + ", named area=" + area);
        }

        private void RestorePropertyAccess()
        {
            if (!_jobPropertyAccess) return;
            if (_agent == null)
            {
                _jobPropertyAccess = false;
                _info("workers: " + Npc.ID + " PropertyInterior access ended after native agent destruction");
                return;
            }
            _agent.areaMask = _originalAreaMask;
            if (_agent.areaMask != _originalAreaMask)
                throw new InvalidOperationException("Worker " + Npc.ID + " original navigation mask restore was not acknowledged: required=" + _originalAreaMask + ", actual=" + _agent.areaMask);
            _jobPropertyAccess = false;
            _info("workers: " + Npc.ID + " original navigation access restored and acknowledged: mask " + _originalAreaMask);
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
                reason = "work anchor projection exceeds 0.2m horizontally or " + FloorHeightTolerance + "m in height: " + requested;
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
            _contact = ContactPoint(secondary);
            _targetValid = true;
        }

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

        // On the first day the far spot has no table yet, so the carpenter only looks at the plan there.
        private bool WantsHammer() => _index == 1 && !(_phase == "breakthrough" && _workAtSecondary) || _index == 2 && _phase != "breakthrough";

        private bool ContactInReach()
        {
            Vector3 direction = _contact - Npc.Position;
            direction.y = 0;
            return HorizontalDistance(Npc.Position, _contact) <= 0.85f &&
                Mathf.Abs(Npc.Position.y + 0.9f - _contact.y) <= 0.35f &&
                Vector3.Angle(Npc.Transform.forward, direction) <= 20f;
        }

        private Vector3 WorkPoint(bool secondary)
        {
            float y = MingWorld.FloorY;
            // The foreman stays put and only turns from the plan to the timber, so nobody crosses the carpenter between the tables.
            if (_index == 0) return new Vector3(-57.5f, y, 143.2f);
            if (_index == 1) return !secondary ? new Vector3(-57.8f, y, 137.55f) : new Vector3(-56.4f, y, 143.45f);
            return secondary ? new Vector3(-54.7f, y, 138.4f) : new Vector3(-54.7f, y, 140.1f);
        }

        private Vector3 ContactPoint(bool secondary)
        {
            var point = WorkPoint(secondary);
            if (_index == 0) return secondary ? new Vector3(-58.85f, 0.1f, 144f) : new Vector3(-57.5f, 0.45f, 142.4f);
            if (_index == 1)
            {
                if (point.z < 140) return new Vector3(-57.8f, 0.45f, 136.95f);
                return _phase == "breakthrough" ? new Vector3(-57.2f, 0.45f, 142.05f) : new Vector3(-56.4f, 0.45f, 144.05f);
            }
            return new Vector3(-54.1f, 0.9f, point.z);
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
            Equip(enabled ? "Avatar/Equippables/Hammer" : "");
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
                float contact = Mathf.Clamp01(HammerContactNormalizedTime);
                int before = Mathf.FloorToInt(_previousNormalized - contact);
                int now = Mathf.FloorToInt(normalized - contact);
                if (now > before)
                {
                    var clips = Clips();
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
            if (Npc.gameObject == null) { _info("worker diagnostics: " + Npc.ID + " root missing"); return; }
            Vector3 position = Npc.gameObject.transform.position;
            var movement = _native.Movement;
            var movementAgent = movement._agent;
            _info("worker diagnostics: " + Newtonsoft.Json.JsonConvert.SerializeObject(new
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
                phase = _phase,
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
            if (Time.time < _swingProbeUntil)
            {
                var hand = _animator != null && _animator.isHuman ? _animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
                if (hand == null) return;
                int slice = Mathf.FloorToInt((normalized - Mathf.Floor(normalized)) * _swingHeights.Length) % _swingHeights.Length;
                _swingHeights[slice] += hand.position.y;
                _swingSamples[slice]++;
                return;
            }
            if (_swingProbeUntil <= 0 || _swingSamples.Sum() == 0) return;
            var report = _swingHeights.Select((h, i) => (Time: (i + .5f) / _swingHeights.Length, Height: _swingSamples[i] > 0 ? h / _swingSamples[i] : float.NaN)).ToArray();
            var lowest = report.Where(r => !float.IsNaN(r.Height)).OrderBy(r => r.Height).First();
            _info("workers: " + Npc.ID + " hammer swing lowest at normalized " + lowest.Time.ToString("F3") + "; heights " +
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
                if (_planEquipped) { Equip(""); _planEquipped = false; }
                Npc.Movement.Stop();
            }
            _walking = false;
            _stationWorking = false;
            _targetValid = false;
            _pendingSecondary = null;
            // The access stays: without it the agent standing inside jumps to the nearest floor it may use, outside the room.
            if (_agent != null) _agent.stoppingDistance = _originalStoppingDistance;
            _audio.Stop();
            _nextAction = Time.time + 1f + _index * 2.35f;
        }

        private void Failure(string reason)
        {
            if (_failure == reason) return;
            _failure = reason;
            _warn("workers: " + Npc.ID + " phase=" + (_phase ?? "none") + ": " + reason);
        }

        private void Recover()
        {
            if (_failure == null) return;
            _info("workers: " + Npc.ID + " recovered from " + _failure);
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

public abstract class MingWorkerNpc : Personnel.PersonnelNpc { }

public sealed class MingForeman : MingWorkerNpc { protected override string DefId => MingWorkers.ForemanId; }
public sealed class MingCarpenter : MingWorkerNpc { protected override string DefId => MingWorkers.CarpenterId; }
public sealed class MingFitter : MingWorkerNpc { protected override string DefId => MingWorkers.FitterId; }
