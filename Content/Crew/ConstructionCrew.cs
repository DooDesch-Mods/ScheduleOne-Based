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

namespace Based.Content.SiteCrews;

internal abstract class ConstructionCrew : ISiteCrew
{
    // Share of the UseHammer clip at which the head lands; "baseddiag hammer" measures it on a live avatar.
    internal static float HammerContact { get; set; } = 0.325f;
#if DEBUG
    public float HammerContactNormalizedTime { get => HammerContact; set => HammerContact = value; }
#endif
    private const float SpawnScanInterval = 1f;
    private const float IdleCheckInterval = 1f;
    // Work comes in bursts with a reach for material between them, and a worker moves on after a few of those.
    private const float SwingMin = 2.5f, SwingMax = 5f, RestMin = 1.5f, RestMax = 3.5f, StationMin = 10f, StationMax = 18f;
    private static readonly int UseHammerParameter = Animator.StringToHash("UseHammer");
    private readonly Dictionary<string, Worker> Workers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (float Since, string Reason, bool Failed)> PendingSpawns = new(StringComparer.Ordinal);
    private Action<string> _info = MelonLoader.MelonLogger.Msg;
    private Action<string> _warn = MelonLoader.MelonLogger.Warning;
    private UpgradeStage _stage;
    private string? _phase;
    private string _updatedUpgrade = "";
    private bool _rainedOut, _updatedRain;
    private string? _audioWarning;
    private AudioClip[]? _clips;
    private bool _initialized;
    private float _nextSpawnScan;

    // What differs per site: who works, how they look, where they stand and what the foreman says.
    protected abstract string Tag { get; }
    // The base the crew builds at; a tip to the foreman goes to its running build.
    protected abstract string BaseId { get; }
    protected abstract string[] Ids { get; }
    protected abstract string[] Names { get; }
    protected abstract Vector3 SpawnPosition { get; }
    protected abstract bool Owns(Personnel.PersonnelNpc npc);
    protected abstract NpcAppearance CreateAppearance(int index);
    protected abstract string Line(int index, string? phase);
    // Index 0 is the foreman, 1 the carpenter, 2 the fitter. Each worker has two stations per phase and walks between them.
    protected abstract Vector3 WorkPoint(int index, bool secondary, string? phase);
    // The spot the worker faces and, with a hammer, strikes.
    protected abstract Vector3 ContactPoint(int index, bool secondary, string? phase);
    protected abstract bool WantsHammer(int index, bool secondary, string? phase);

    // The build the crew follows: the one under construction, otherwise the site's main upgrade.
    protected string Upgrade { get; private set; } = "";
    // The quest phase of the job the crew follows; null on the order day.
    protected string? Phase => _phase;
    // The Personnel region the workers are registered in.
    protected virtual string Region => "Northtown";
    // Whether the crew is on site while the followed build stands at this stage and phase.
    protected virtual bool WorksOn(UpgradeStage stage, string? phase) => stage == UpgradeStage.Constructing && phase != null;
    // Whether this worker takes part in today's job; the others stay out of sight.
    protected virtual bool OnJob(int index, string? phase) => true;
    // The worker who carries the plan, takes tips and gives the build's line; -1 for a crew without one.
    protected virtual int ForemanIndex => 0;
    // What the worker swings at the contact point.
    protected virtual string Tool(int index) => "Avatar/Equippables/Hammer";
    // Whether the worker reaches for material between bursts of work.
    protected virtual bool GrabsBetweenSwings(int index) => index != ForemanIndex;

    protected bool WorkingHours => WorksOn(_stage, _phase) && NetworkSingleton<TimeManager>.InstanceExists &&
        NetworkSingleton<TimeManager>.Instance.CurrentTime >= 800 && NetworkSingleton<TimeManager>.Instance.CurrentTime < 1800;

    protected bool IsVisible(string id) => Workers.TryGetValue(id, out var worker) && worker.Visible;

    public int ActiveCount => Workers.Values.Count(w => w.Visible);

#if DEBUG
    private static float _swingProbeUntil;

    public void ProbeSwing(float seconds) => _swingProbeUntil = Time.time + seconds;

    public void Diagnostics()
    {
        _info(Tag + " diagnostics: attached=" + Workers.Count + "/" + Ids.Length + ", active=" + ActiveCount +
            ", upgrade=" + Upgrade + ", phase=" + (_phase ?? "none") + ", stage=" + _stage + ", pending=" + PendingSpawns.Count);
        foreach (var worker in Workers.Values.OrderBy(w => w.Npc.ID)) worker.Diagnostics();
    }
#endif

    public void Initialize(Action<string>? info, Action<string>? warn)
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
                Spawn = new NpcSpawn { Physical = true, Auto = false, Region = Region, Position = SpawnPosition },
                Contact = new NpcContact { Visible = false, MapMarker = false },
                Behavior = new NpcBehavior { Aggression = 0, Conversation = "none", MaxHealth = 100, Scale = 1 },
                Appearance = CreateAppearance(i)
            };
            if (!Personnel.API.Register(definition)) throw new InvalidOperationException("Based could not register worker definition " + Ids[i]);
        }
        _initialized = true;
        _info(Tag + ": " + Ids.Length + " stable Personnel definitions registered; S1API owns instantiation and networking");
    }

    // Called before Update when the site knows which build the crew follows.
    public void Follow(string upgrade) => Upgrade = upgrade;

    // Called before Update: the build lost last night to rain, so the foreman says that instead of the day's line.
    public void RainedOut(bool rainedOut) => _rainedOut = rainedOut;

    public void Update(UpgradeStage stage, string? phase)
    {
        bool changed = stage != _stage || phase != _phase || Upgrade != _updatedUpgrade || _rainedOut != _updatedRain;
        _stage = stage;
        _phase = phase;
        _updatedUpgrade = Upgrade;
        _updatedRain = _rainedOut;
        if (!changed) return;
        foreach (var worker in Workers.Values)
        {
            worker.EndBreak("the build stage changed");
            worker.ResetWork();
            worker.RefreshDialogue();
        }
        _info(Tag + ": state=" + stage + ", phase=" + (phase ?? "none") + (Upgrade.Length > 0 ? ", upgrade=" + Upgrade : ""));
    }

    public void Tick()
    {
        if (!_initialized) return;
        if (Workers.Count < Ids.Length && Time.unscaledTime >= _nextSpawnScan)
        {
            _nextSpawnScan = Time.unscaledTime + SpawnScanInterval;
            ScanForSpawnedWorkers();
        }
        bool working = WorkingHours;
        foreach (var worker in Workers.Values) worker.Tick(working && OnJob(worker.Index, _phase));
    }

    // S1API can hydrate client wrappers without calling a user constructor or OnCreated, so the list is the only signal.
    private void ScanForSpawnedWorkers()
    {
        foreach (var npc in NPC.All)
        {
            if (npc is not Personnel.PersonnelNpc construction || !Owns(construction) || construction.gameObject == null || Workers.ContainsKey(construction.ID)) continue;
            if (NativeSpawnReady(construction, out string reason))
            {
                if (PendingSpawns.Remove(construction.ID)) _info(Tag + ": " + construction.ID + " native network spawn confirmed");
                Attach(construction);
                continue;
            }
            if (!PendingSpawns.TryGetValue(construction.ID, out var pending))
            {
                PendingSpawns.Add(construction.ID, (Time.realtimeSinceStartup, reason, false));
                _info(Tag + ": " + construction.ID + " awaiting native network spawn: " + reason);
                continue;
            }
            if (pending.Reason != reason)
            {
                _info(Tag + ": " + construction.ID + " native spawn wait changed: " + reason);
                pending = (pending.Since, reason, pending.Failed);
                PendingSpawns[construction.ID] = pending;
            }
            float elapsed = Time.realtimeSinceStartup - pending.Since;
            if (!pending.Failed && elapsed >= 20f)
            {
                PendingSpawns[construction.ID] = (pending.Since, reason, true);
                MelonLoader.MelonLogger.Error(Tag + ": " + construction.ID + " native network spawn unconfirmed after " + elapsed.ToString("F1") +
                    "s: " + reason + "; S1API CustomNpcsReady=" + NPC.CustomNpcsReady + ". Inspect the original S1API spawn error; Based has not spawned a replacement.");
            }
        }
    }

    private static bool NativeSpawnReady(Personnel.PersonnelNpc npc, out string reason)
    {
        if (npc.gameObject == null) { reason = "root missing"; return false; }
        var network = npc.gameObject.GetComponent<NetworkObject>();
        if (network == null) { reason = "NetworkObject missing"; return false; }
        if (!network.IsSpawned) { reason = "NetworkObject.IsSpawned=false, rootActive=" + npc.gameObject.activeInHierarchy; return false; }
        if (!npc.gameObject.activeInHierarchy) { reason = "spawned root inactive"; return false; }
        reason = "";
        return true;
    }

    private void Attach(Personnel.PersonnelNpc npc)
    {
        if (!NativeSpawnReady(npc, out string reason))
            throw new InvalidOperationException("Cannot attach worker " + npc.ID + " before native network spawn: " + reason);
        if (Workers.TryGetValue(npc.ID, out var existing))
        {
            if (existing.Npc.gameObject == npc.gameObject) return;
            if (existing.Npc.gameObject != null)
            {
                _warn(Tag + ": duplicate S1API identity " + npc.ID + "; second instance is hidden");
                npc.gameObject.GetComponent<NativeNpc>()?.SetVisible(false);
                foreach (var collider in npc.gameObject.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                return;
            }
            Workers.Remove(npc.ID);
        }
        int index = Array.IndexOf(Ids, npc.ID);
        if (index < 0) throw new InvalidOperationException("Unknown construction worker identity " + npc.ID);
        var worker = new Worker(this, npc, index);
        Workers.Add(npc.ID, worker);
        if (index == ForemanIndex)
        {
            // The tip conversation depends on the cash and product the player carries, so it is built as the talk starts.
            Based.Runtime.Tips.Bind(BaseId, npc);
            npc.Dialogue.OnConversationStart(worker.RefreshDialogue);
        }
        worker.RefreshDialogue();
        worker.Tick(false);
    }

    public void Cleanup()
    {
        foreach (var worker in Workers.Values) worker.Dispose();
        Workers.Clear();
        PendingSpawns.Clear();
        _clips = null;
        _audioWarning = null;
        _stage = UpgradeStage.RightsMissing;
        _phase = null;
        Upgrade = "";
        _updatedUpgrade = "";
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
            _audioWarning = Tag + ": expected three loaded vanilla hammer impact clips, found " + _clips.Length + "; missing clips are not replaced";
            _warn(_audioWarning);
        }
        return _clips;
    }

    private sealed class Worker
    {
        private readonly ConstructionCrew _crew;
        internal readonly Personnel.PersonnelNpc Npc;
        private readonly NativeNpc _native;
        private readonly int _index;
        internal int Index => _index;
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
        private const int BreakFirstMin = 20, BreakFirstMax = 80, BreakMin = 5, BreakMax = 10, BreakJitter = 15, BreakDelay = 5;
        private int _nextBreak = -1, _breakEnds = -1;
        private Il2CppScheduleOne.Core.Equipping.Framework.IEquippedItemHandler? _cigarette;
        private static Il2CppScheduleOne.Core.Equipping.Framework.EquippableData? _cigaretteData;
        private static bool _cigaretteMissing;
        private string? _failure;
        internal bool Visible;
#if DEBUG
        private int _soundCount;
        private string _lastSoundClip = "";
        private readonly float[] _swingHeights = new float[20];
        private readonly int[] _swingSamples = new int[20];
#endif

        internal Worker(ConstructionCrew crew, Personnel.PersonnelNpc npc, int index)
        {
            _crew = crew;
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
            OpenTalkAngle(crew, npc);
        }

        // Vanilla only talks from the NPC's front half, and a worker faces the job, away from whoever walks in.
        private static void OpenTalkAngle(ConstructionCrew crew, Personnel.PersonnelNpc npc)
        {
            var talk = npc.gameObject.GetComponentInChildren<Il2CppScheduleOne.Dialogue.DialogueController>(true)?._interactable;
            if (talk == null)
            {
                crew._warn(crew.Tag + ": " + npc.ID + " has no talk interaction; the angle limit stays.");
                return;
            }
            talk.LimitInteractionAngle = false;
            talk.Priority = 1;
            crew._info(crew.Tag + ": " + npc.ID + " can be talked to from every side.");
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
            _crew._info(_crew.Tag + ": " + Npc.ID + " appearance applied after native spawn, including saved workers; skin=" + appearance.SkinColor);
        }

        internal void RefreshDialogue()
        {
            try
            {
                bool rain = _crew._rainedOut && _index == _crew.ForemanIndex;
                string line = Based.UI.Text.T(rain ? "Rain soaked the site. One more night." : _crew.Line(_index, _crew._phase));
                Npc.Dialogue.BuildAndRegisterContainer("based_construction", builder =>
                {
                    if (_index == _crew.ForemanIndex) Based.Runtime.Tips.Build(_crew.BaseId, builder, line);
                    else builder.AddNode("ENTRY", line);
                });
                if (!Npc.Dialogue.UseContainerOnInteract("based_construction")) Failure("construction dialogue could not bind to interaction");
            }
            catch (Exception error) { Failure("construction dialogue failed: " + error.Message); }
        }

        // Breaks run on the host in game minutes, so a sleep or a time jump moves them with the clock.
        private static int GameMinute()
        {
            if (!NetworkSingleton<TimeManager>.InstanceExists) return -1;
            var time = NetworkSingleton<TimeManager>.Instance;
            return time.ElapsedDays * 1440 + TimeManager.GetMinSumFrom24HourTime(time.CurrentTime);
        }

        private static string Clock() => NetworkSingleton<TimeManager>.InstanceExists ? NetworkSingleton<TimeManager>.Instance.CurrentTime.ToString("0000") : "?";

        internal bool OnBreak => _breakEnds >= 0;

        // True while the worker is on a break. A break starts about once a game hour, never for the whole crew at once.
        private bool TickBreak()
        {
            int now = GameMinute();
            if (now < 0) return OnBreak;
            // A clock set back (console, another mod) leaves the plan hours ahead; the plan starts over from now.
            if (OnBreak && _breakEnds - now > BreakMax)
            {
                _crew._info(_crew.Tag + ": " + Npc.ID + " break ends " + (_breakEnds - now) + " game minutes ahead; the clock went back.");
                EndBreak("the clock went back");
                _nextBreak = -1;
                return false;
            }
            if (_nextBreak - now > BreakFirstMax + _index * 17 + 60 + BreakJitter)
            {
                _crew._info(_crew.Tag + ": " + Npc.ID + " next break was " + (_nextBreak - now) + " game minutes ahead; the clock went back, the plan starts over.");
                _nextBreak = -1;
            }
            if (_nextBreak < 0)
            {
                _nextBreak = now + UnityEngine.Random.Range(BreakFirstMin, BreakFirstMax) + _index * 17;
                return false;
            }
            if (OnBreak)
            {
                if (now < _breakEnds) return true;
                EndBreak("the break is over");
                return false;
            }
            if (now < _nextBreak || _walking) return false;
            var others = _crew.Workers.Values.Where(w => w != this && w.OnBreak).ToArray();
            if (others.Length > 0)
            {
                _nextBreak = now + BreakDelay;
                _crew._info(_crew.Tag + ": " + Npc.ID + " break waits " + BreakDelay + " game minutes; " + others[0].Npc.ID + " is on a break.");
                return false;
            }
            StartBreak(now);
            return true;
        }

        private void StartBreak(int now)
        {
            ResetWork();
            int minutes = UnityEngine.Random.Range(BreakMin, BreakMax + 1);
            _breakEnds = now + minutes;
            var cigarette = Cigarette(_crew);
            try
            {
                if (cigarette != null)
                {
                    _cigarette = _native.Equip_Networked(cigarette);
                    _native.Avatar?.LookController?.OverrideIKWeight(0.3f);
                }
            }
            catch (Exception error) { _crew._warn(_crew.Tag + ": " + Npc.ID + " break without a cigarette: " + error.Message); }
            _crew._info(_crew.Tag + ": " + Npc.ID + " break starts at " + Clock() + " for " + minutes + " game minutes" + (_cigarette != null ? " with a cigarette" : "") + ".");
        }

        internal void EndBreak(string why)
        {
            if (!OnBreak) return;
            _breakEnds = -1;
            int now = GameMinute();
            _nextBreak = now < 0 ? -1 : now + 60 + UnityEngine.Random.Range(-BreakJitter, BreakJitter + 1);
            try
            {
                if (_cigarette != null && InstanceFinder.IsServer)
                {
                    _native.Unequip(_cigarette);
                    _native.Avatar?.LookController?.OverrideIKWeight(0.2f);
                }
            }
            catch (Exception error) { _crew._warn(_crew.Tag + ": " + Npc.ID + " cigarette not put away: " + error.Message); }
            _cigarette = null;
            _nextAction = Time.time;
            _crew._info(_crew.Tag + ": " + Npc.ID + " break ends at " + Clock() + ": " + why + ".");
        }

        private static Il2CppScheduleOne.Core.Equipping.Framework.EquippableData? Cigarette(ConstructionCrew crew)
        {
            if (_cigaretteData != null) return _cigaretteData;
            _cigaretteData = Resources.Load<Il2CppScheduleOne.Core.Equipping.Framework.EquippableData>("equippables/cigarette/Cigarette");
            if (_cigaretteData == null && !_cigaretteMissing)
            {
                _cigaretteMissing = true;
                crew._warn(crew.Tag + ": the vanilla cigarette is missing; workers take breaks without one.");
            }
            return _cigaretteData;
        }

        internal void Tick(bool working)
        {
            if (Npc.gameObject == null || !Npc.gameObject.activeInHierarchy) return;
            bool host = InstanceFinder.IsServer;
            if (!working)
            {
                if (Time.time < _nextCheck) return;
                _nextCheck = Time.time + IdleCheckInterval;
                if (host) EndBreak("work ended for the day");
                // The next working day starts with a fresh offset instead of a break that fell due overnight.
                _nextBreak = -1;
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
                var anchor = _crew.WorkPoint(_index, _workAtSecondary, _crew._phase);
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
            if (TickBreak()) return;
            if (_index == _crew.ForemanIndex && !_planEquipped)
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
                SetHammer(_crew.WantsHammer(_index, _workAtSecondary, _crew._phase) && ContactInReach());
            }
            PlayContactSound();
        }

        private void TickRhythm()
        {
            if (Time.time < _rhythmSwitch) return;
            _swinging = !_swinging;
            _rhythmSwitch = Time.time + (_swinging ? UnityEngine.Random.Range(SwingMin, SwingMax) : UnityEngine.Random.Range(RestMin, RestMax));
            if (_hammering) _native.SetAnimationBool_Networked(null, "UseHammer", _swinging);
            if (!_swinging && _crew.GrabsBetweenSwings(_index)) _native.SetAnimationTrigger_Networked(null, "GrabItem");
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
            _crew._info(_crew.Tag + ": " + Npc.ID + " on-duty PropertyInterior access acknowledged: mask " + _originalAreaMask + " -> " + allowed + ", named area=" + area);
        }

        private void RestorePropertyAccess()
        {
            if (!_jobPropertyAccess) return;
            if (_agent == null)
            {
                _jobPropertyAccess = false;
                _crew._info(_crew.Tag + ": " + Npc.ID + " PropertyInterior access ended after native agent destruction");
                return;
            }
            _agent.areaMask = _originalAreaMask;
            if (_agent.areaMask != _originalAreaMask)
                throw new InvalidOperationException("Worker " + Npc.ID + " original navigation mask restore was not acknowledged: required=" + _originalAreaMask + ", actual=" + _agent.areaMask);
            _jobPropertyAccess = false;
            _crew._info(_crew.Tag + ": " + Npc.ID + " original navigation access restored and acknowledged: mask " + _originalAreaMask);
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
            var requested = _crew.WorkPoint(_index, secondary, _crew._phase);
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
            _contact = _crew.ContactPoint(_index, secondary, _crew._phase);
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
            Equip(enabled ? _crew.Tool(_index) : "");
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
                float contact = Mathf.Clamp01(HammerContact);
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
            if (Npc.gameObject == null) { _crew._info("worker diagnostics: " + Npc.ID + " root missing"); return; }
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
                upgrade = _crew.Upgrade,
                phase = _crew._phase,
                anchor = _targetValid ? new[] { _resolvedAnchor.x, _resolvedAnchor.y, _resolvedAnchor.z } : null,
                atAnchor = AtAssignedAnchor(),
                contactInReach = _targetValid && ContactInReach(),
                walking = _walking,
                hammering = _hammering,
                onBreak = OnBreak,
                breakEnds = _breakEnds,
                nextBreak = _nextBreak,
                gameMinute = GameMinute(),
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
            _crew._info(_crew.Tag + ": " + Npc.ID + " hammer swing lowest at normalized " + lowest.Time.ToString("F3") + "; heights " +
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
            _crew._warn(_crew.Tag + ": " + Npc.ID + " phase=" + (_crew._phase ?? "none") + ": " + reason);
        }

        private void Recover()
        {
            if (_failure == null) return;
            _crew._info(_crew.Tag + ": " + Npc.ID + " recovered from " + _failure);
            _failure = null;
        }

        internal void Dispose()
        {
            if (InstanceFinder.IsServer) EndBreak("the crew was cleaned up");
            ResetWork();
            RestorePropertyAccess();
            SetVisible(false);
            if (_audio != null) Object.Destroy(_audio.gameObject);
        }
    }
}
