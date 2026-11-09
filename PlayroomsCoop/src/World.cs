using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlayroomsCoop
{
    internal static partial class World
    {
        private const string BallServiceT = "TwoPixels.Ball.BallService";
        private const string BallSpotT = "TwoPixels.Ball.BallSpot";
        private const string BallPitT = "TwoPixels.Ball.BallPit";
        private const string RoomObjectT = "TwoPixels.Room.RoomObject";
        private const string EnemyControllerT = "TwoPixels.Enemy.EnemyController";
        private const string DayTimeServiceT = "TwoPixels.DayTime.DayTimeService";

        public static Feature Balls, Pits, Entity, Clock, Doors, Scares;

        private static bool _connected, _isHost;
        public static Vector3? LocalHead;

        private static bool _applying;
        private static bool _hooksInstalled;
        private static float _lastHookAttempt;

        private static object _ballService;
        private static readonly Dictionary<string, object> _rooms = new Dictionary<string, object>();
        private static readonly Dictionary<string, object> _pits = new Dictionary<string, object>();
        private static readonly Dictionary<IntPtr, Dictionary<IntPtr, int>> _spotIndex = new Dictionary<IntPtr, Dictionary<IntPtr, int>>();
        private static float _lastWorldRefresh = -99f;
        private static IntPtr _snapshotSentFor;
        private static float _lastSnapshotCheck;

        private static readonly List<(string room, int idx)> _outTaken = new List<(string, int)>();
        private static readonly Dictionary<string, int> _outPits = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> _pitLast = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> _pitTarget = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> _pitPending = new Dictionary<string, int>();
        private static float _nextPitPoll;

        private sealed class Mover { public object Spot, Ball; public string Room; public int Idx; public Vector3 Start, LastSent, Anchor; public float StillSince, Since, AnchorAt; public bool Quiet; }
        private static readonly Dictionary<IntPtr, Mover> _moving = new Dictionary<IntPtr, Mover>();
        private sealed class Puppet
        {
            public object Spot, Ball, Body; public bool OrigKinematic, Shown; public float LastUpdate, Radius = 0.1f;
            public Vector3 Target, LastShown;
            public readonly List<(float t, Vector3 p)> Samples = new List<(float, Vector3)>();
        }
        private const float InterpDelay = 0.1f;
        private static readonly Dictionary<IntPtr, float> _recentPuppet = new Dictionary<IntPtr, float>();
        private static readonly List<object> _disabledVents = new List<object>();
        private static readonly List<Vector3> _ventIntakes = new List<Vector3>();
        private static float _nextVentScan;
        private static readonly Dictionary<IntPtr, Puppet> _puppets = new Dictionary<IntPtr, Puppet>();
        private static readonly List<(string room, int idx, Vector3 pos)> _outLive = new List<(string, int, Vector3)>();
        private static float _nextActiveScan, _lastMoveLog;
        private static int _liveSentCount, _liveRecvCount;
        private static readonly Dictionary<IntPtr, (object spot, string room, int idx, float time)> _recentTaken = new Dictionary<IntPtr, (object, string, int, float)>();
        private static readonly List<(string room, int idx, Vector3 pos)> _outMoved = new List<(string, int, Vector3)>();
        private static float _nextMovePoll;
        private static readonly Queue<(string room, int idx)> _pendingTakes = new Queue<(string, int)>();
        private const int MaxPendingTakes = 400000;
        private static readonly HashSet<string> _warnedRooms = new HashSet<string>();

        private static object _enemy;
        private static float _lastEnemySearch = -99f, _nextEnemySend;
        private static object _hostPlayerTransform;
        private static GameObject _proxy;
        private static bool _targetingProxy;
        private static bool _enemyFrozen;
        private static IntPtr _frozenEnemyPtr;
        private static (bool active, Vector3 pos, float yaw, float speed, int state, float time) _enemyNet;
        private static Vector3 _enemyShownPos;
        private static float _enemyShownYaw;

        private static float _nextClockSend;

        public static void Init()
        {
            Balls = new Feature("Balls", Plugin.SyncBalls.Value);
            Pits = new Feature("Pits", Plugin.SyncPits.Value);
            Entity = new Feature("Entity", Plugin.SyncEntity.Value);
            Clock = new Feature("DayTime", Plugin.SyncDayTime.Value);
            Doors = new Feature("Doors", Plugin.SyncDoors.Value);
            Scares = new Feature("Scares", true);
            BroomInit();
            SusanneInit();
            StrayInit();
            RespawnInit();
            UnstuckInit();
            Upgrades.Init();
            Unlocks.Init();
            Boards.Init();
            Props.Init();
            Shared.Init();
        }

        public static void OnConnected(bool isHost)
        {
            Shared.NewWorld();
            _connected = true; _isHost = isHost;
            _snapshotSentFor = IntPtr.Zero;
            _pendingTakes.Clear(); _outTaken.Clear(); _outPits.Clear(); _warnedRooms.Clear();
            _pitLast.Clear(); _pitTarget.Clear(); _pitPending.Clear();
            _doorLast.Clear(); _doorIgnoreUntil.Clear(); _doorCloseWait.Clear(); _dayLastPhase = -1;
            _moving.Clear(); _recentTaken.Clear(); _outMoved.Clear(); _outLive.Clear(); _recentPuppet.Clear(); _settleCool.Clear();
            _restQueue.Clear(); BroomReset(); SusanneReset(); RespawnReset();
            foreach (var v in _disabledVents) { try { if (Alive(v)) ((Behaviour)v).enabled = true; } catch { } }
            _disabledVents.Clear(); _ventIntakes.Clear();
            foreach (var pp in _puppets.Values) ReleasePuppet(pp);
            _puppets.Clear();
            _enemyNet = default;
            Upgrades.OnConnected(isHost);
            Unlocks.OnConnected(isHost);
            Boards.OnConnected(isHost);
            Props.OnConnected(isHost);
            Shared.OnConnected(isHost);
        }

        public static void OnDisconnected()
        {
            _connected = false; _pendDay = -1; _bedCheckAt = -1f; _keptPairs.Clear();
            Entity.Try(RestoreEnemy, "restore entity");
            if (_proxy != null) { UnityEngine.Object.Destroy(_proxy); _proxy = null; }
            _pendingTakes.Clear(); _outTaken.Clear(); _outPits.Clear();
            _restQueue.Clear(); BroomReset(); SusanneReset(); RespawnReset();
            Scares.Try(RestoreDirector, "restore scares");
            Upgrades.OnDisconnected();
            Unlocks.OnDisconnected();
            Boards.OnDisconnected();
            Props.OnDisconnected();
            Shared.OnDisconnected();
        }

        public static void OnPeerLeft(ulong id)
        {
            if (_remoteBrooms.TryGetValue(id, out var rb)) { if (rb.Proxy != null) UnityEngine.Object.Destroy(rb.Proxy); _remoteBrooms.Remove(id); }
            _catchCool.Remove(id); _sees.Remove(id);
            Plugin.Log.LogInfo($"[World] {Net.NameOf(id)} left: their robots, Susanne and entity target go back to the others");
        }

        private static void T(string name, Action a)
        {
            Perf.Begin(name);
            try { a(); } finally { Perf.End(); }
        }

        public static void Tick(float now, float dt)
        {
            if (!_hooksInstalled && now - _lastHookAttempt > 3f) { _lastHookAttempt = now; InstallHooks(); }
            if (!_connected && now - _lastWorldRefresh > 5f) T("find world", () => RefreshWorld(now));
            T("stray balls", () => Strays.Try(() => StrayTick(now), "stray balls"));
            if (!_connected) return;

            if (now - _lastWorldRefresh > 5f) T("find world", () => RefreshWorld(now));

            if (now - _lastSnapshotCheck > 2f)
            {
                _lastSnapshotCheck = now;
                if (Alive(_ballService) && _rooms.Count > 0 && R.Ptr(_ballService) != _snapshotSentFor)
                {
                    _snapshotSentFor = R.Ptr(_ballService);
                    T("ball snapshot", () => Balls.Try(SendSnapshot, "send snapshot"));
                }
            }

            T("pickups", () => ProcessPendingTakes(1500));
            if (now >= _nextPitPoll) { _nextPitPoll = now + 0.25f; T("pits", () => Pits.Try(PollPits, "poll pits")); }
            if (now >= _nextMovePoll) { _nextMovePoll = now + 0.05f; T("moving balls", () => Balls.Try(() => PollMovingBalls(now), "poll moving balls")); }
            T("puppet balls", () => Balls.Try(() => TickPuppets(now, dt), "move other players' balls"));
            if (now >= _nextVentScan) { _nextVentScan = now + 3f; T("vents", () => Balls.Try(ScanVents, "vents")); }
            T("pit drops", () => Pits.Try(() => DeliverPending(40), "deliver to pits"));
            T("broom", () => Broom.Try(() => BroomCapsTick(now), "broom caps"));
            if (Plugin.ShowBroom.Value) T("broom model", () => Broom.Try(() => CaptureBroomHold(now), "broom model"));
            T("ball corrections", () => Balls.Try(() => ApplyRests(now, 120), "apply ball rests"));
            T("sending", () => { FlushOutgoing(); FlushSweeps(); FlushVentFeed(); });

            if (_isHost) T("entity (host)", () => { HostEntity(now); HostClock(now); });
            else T("entity (client)", () => ClientEntity(now, dt));
            T("robots/vacuums", () => Shared.Tick(now, dt));
            T("wallet/upgrades", () => Upgrades.Tick(now));
            T("world purchases", () => Unlocks.Tick(now));
            T("whiteboards", () => Boards.Tick(now));
            T("loose objects", () => Props.Tick(now, dt));
            if (now >= _nextDoorPoll) { _nextDoorPoll = now + 0.2f; T("doors", () => Doors.Try(() => PollDoors(now), "poll doors")); }
            if (now >= _nextDayPoll) { _nextDayPoll = now + 0.5f; T("day", () => Clock.Try(() => PollDay(now), "poll day")); }
            T("day", () => Clock.Try(() => { DayPendingTick(now); BedCheckTick(now); }, "day change"));
            T("scares", () => Scares.Try(() => TuneDirector(now), "ambient scares"));
            T("Susanne", () => Susanne.Try(() => SusanneTick(now, dt), "Susanne"));
            T("respawn", () => Respawn.Try(() => RespawnTick(now), "respawn"));
        }

        private static void InstallHooks()
        {
            var spotT = R.T(BallSpotT);
            if (spotT == null) return;
            _hooksInstalled = true;
            R.Patch(spotT, "OnBallCollected", 0, typeof(Hooks), null, nameof(Hooks.SpotCollectedPostfix));
            R.Patch(R.T(EnemyControllerT), "IsInCatchRange", 0, typeof(Hooks), nameof(Hooks.CatchRangePrefix), null);
            R.Patch(R.T("TwoPixels.UserInterfaces.MainMenuCanvas"), "StartGame", 3, typeof(Hooks), null, nameof(Hooks.StartGamePostfix));
            R.Patch(R.T(ShovelT), "Sweep", 2, typeof(Hooks), nameof(Hooks.SweepPrefix), null);
            R.Patch(R.T("TwoPixels.Ball.BallVent"), "ConsumeIntoPit", 1, typeof(Hooks), nameof(Hooks.VentConsumePrefix), null);
            R.Patch(R.T("TwoPixels.Enemy.EnemyService"), "TriggerDeathFlow", 2, typeof(Hooks), nameof(Hooks.DeathPrefix), nameof(Hooks.DeathPostfix));
            UnstuckHooks();
            HelpMenuHooks();
            Shared.Hooks();
            R.Patch(R.T("TwoPixels.Interactables.PurchaseInteractable"), "EquipPurchasedTool", 0, typeof(Hooks), nameof(Hooks.EquipToolPrefix), null);
            R.Patch(R.T("TwoPixels.Room.RoomService"), "OnHubDoorStateChanged", 1, typeof(Hooks), nameof(Hooks.HubDoorPrefix), nameof(Hooks.HubDoorPostfix));
            R.Patch(R.T(DoorT), "RequestClose", 0, typeof(Hooks), nameof(Hooks.DoorClosePrefix), null);
            PortalHooks();
        }

        internal static void OnLocalSpotCollected(object spot)
        {
            if (_applying || !_connected) return;
            Balls.Try(() =>
            {
                var room = R.Get(spot, "_room");
                if (room == null) return;
                string id = R.Get<string>(room, "RoomId");
                int idx = IndexOf(room, spot);
                if (idx < 0) return;
                _outTaken.Add((id, idx));
                _recentTaken[R.Ptr(spot)] = (spot, id, idx, Time.realtimeSinceStartup);
            }, "local pickup");
        }

        internal static bool CatchRangePrefix(object controller, ref bool result)
        {
            if (!_connected || !_isHost || !_targetingProxy || _proxy == null || !Entity.Enabled) return true;
            try
            {
                var t = ((Component)controller).transform.position;
                var p = _proxy.transform.position;
                float dx = t.x - p.x, dz = t.z - p.z;
                float catchDist = R.Get<float>(controller, "catchDistance");
                if (dx * dx + dz * dz <= catchDist * catchDist && _targetPeer != 0)
                {
                    ulong caught = _targetPeer;
                    _catchCool[caught] = Time.realtimeSinceStartup + 15f;
                    Net.SendTo(caught, Protocol.W.Begin(Msg.EnemyCaught), SteamNet.SendReliable);
                    Plugin.Log.LogInfo($"Entity caught {Net.NameOf(caught)}");
                    _targetPeer = 0;
                    SetEnemyTarget(controller, _hostPlayerTransform, false);
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Catch check: " + e.Message); }
            result = false;
            return false;
        }

        private static Transform _playerBody;

        internal static Transform PlayerBody()
        {
            if (_playerBody != null && Alive(_playerBody)) return _playerBody;
            _playerBody = null;
            try { if (Alive(_ballService)) _playerBody = R.Get(_ballService, "playerTransform") as Transform; } catch { }
            return _playerBody;
        }

        internal static IntPtr WorldId()
        {
            if (!Alive(_ballService))
            {
                _ballService = null; _playerBody = null;
                foreach (var bs in R.FindAll(R.T(BallServiceT))) { _ballService = bs; break; }
            }
            return _ballService != null ? R.Ptr(_ballService) : IntPtr.Zero;
        }

        internal static bool InGameWorld()
        {
            float now = Time.realtimeSinceStartup;
            if (now - _lastWorldRefresh > 5f) RefreshWorld(now);
            return _rooms.Count > 0 && PlayerBody() != null && !MainMenuShowing(now);
        }

        private static bool _menuShowing;
        private static float _nextMenuCheck;

        internal static bool MainMenuShowing(float now)
        {
            if (now < _nextMenuCheck) return _menuShowing;
            _nextMenuCheck = now + 0.5f;
            _menuShowing = false;
            try
            {
                foreach (var c in R.FindAll(R.T("TwoPixels.UserInterfaces.MainMenuCanvas")))
                {
                    bool showing = true;
                    try { var ui = R.Get(c, "userInterfaceCanvas"); if (ui != null) showing = Convert.ToBoolean(R.Get(ui, "IsShowing")); } catch { }
                    if (showing) { _menuShowing = true; break; }
                }
            }
            catch { }
            return _menuShowing;
        }

        internal static object BallServiceObj => Alive(_ballService) ? _ballService : null;

        internal static bool Alive(object o) => o is UnityEngine.Object u && u != null;

        private static void RefreshWorld(float now)
        {
            _lastWorldRefresh = now;
            try
            {
                if (!Alive(_ballService))
                {
                    _ballService = null; _playerBody = null;
                    foreach (var bs in R.FindAll(R.T(BallServiceT))) { _ballService = bs; break; }
                }
                _rooms.Clear();
                foreach (var room in R.FindAll(R.T(RoomObjectT)))
                {
                    var id = R.Get<string>(room, "RoomId");
                    if (!string.IsNullOrEmpty(id)) _rooms[id] = room;
                }
                _pits.Clear();
                foreach (var pit in R.FindAll(R.T(BallPitT)))
                {
                    var id = R.Get<string>(pit, "BallPitId");
                    if (!string.IsNullOrEmpty(id)) _pits[id] = pit;
                }
            }
            catch (Exception e) { if (_stepWarned.Add("find rooms")) Plugin.Log.LogWarning("Finding rooms failed: " + e.Message); }
        }

        private static int IndexOf(object room, object spot)
        {
            IntPtr rp = R.Ptr(room), sp = R.Ptr(spot);
            if (!_spotIndex.TryGetValue(rp, out var map) || !map.TryGetValue(sp, out int idx))
            {
                map = BuildIndex(room);
                _spotIndex[rp] = map;
                if (!map.TryGetValue(sp, out idx)) return -1;
            }
            return idx;
        }

        private static Dictionary<IntPtr, int> BuildIndex(object room)
        {
            var map = new Dictionary<IntPtr, int>();
            var spots = R.Get(room, "_ballSpots");
            int n = R.Count(spots);
            for (int i = 0; i < n; i++) map[R.Ptr(R.At(spots, i))] = i;
            return map;
        }

        // guests can message each other, so only take these from the host
        private static bool HostOnly(Msg type) =>
            type == Msg.WalletState || type == Msg.DayTime || type == Msg.Enemy || type == Msg.EnemyCaught ||
            type == Msg.EnemyNotice || type == Msg.ObjOwner;

        private static readonly HashSet<ulong> _warnedHostOnly = new HashSet<ulong>();

        public static void Handle(Msg type, byte[] buf, int len)
        {
            if (HostOnly(type) && (_isHost || Net.Sender != Net.HostId))
            {
                if (_warnedHostOnly.Add(Net.Sender)) Plugin.Log.LogWarning($"[Net] ignored a host-only message ({type}) from {SteamNet.Plain(Net.NameOf(Net.Sender))}, who isn't the host");
                return;
            }
            switch (type)
            {
                case Msg.BallsTaken:
                {
                    if (!Balls.Enabled) return;
                    var r = new Reader(buf, len, Protocol.HeaderSize);
                    int n = r.U16();
                    for (int i = 0; i < n; i++) { var t = (r.Str(), r.I32()); if (_pendingTakes.Count < MaxPendingTakes) _pendingTakes.Enqueue(t); }
                    break;
                }
                case Msg.BallsLive:
                {
                    if (!Balls.Enabled) return;
                    var r = new Reader(buf, len, Protocol.HeaderSize);
                    float sentAt = r.F32();
                    sentAt += Net.Offset(Net.Sender, sentAt);
                    int rc = r.U8();
                    var rooms = new string[rc];
                    for (int i = 0; i < rc; i++) rooms[i] = r.Str();
                    int n = r.U16();
                    for (int i = 0; i < n; i++)
                    {
                        int ri = r.U8(); int idx = r.I32();
                        var pos = new Vector3(r.F32(), r.F32(), r.F32());
                        if (ri >= rc) continue;
                        string room = rooms[ri];
                        Balls.Try(() => ApplyLive(room, idx, pos, sentAt), "apply live ball");
                    }
                    _liveRecvCount += n;
                    break;
                }
                case Msg.BallsMoved:
                {
                    if (!Balls.Enabled) return;
                    var r = new Reader(buf, len, Protocol.HeaderSize);
                    int n = r.U16();
                    float now = Time.realtimeSinceStartup;
                    for (int i = 0; i < n; i++)
                    {
                        string room = r.Str(); int idx = r.I32();
                        var pos = new Vector3(r.F32(), r.F32(), r.F32());
                        _restQueue.Enqueue((room, idx, pos, now, 0));
                    }
                    break;
                }
                case Msg.SusanneClaim:
                case Msg.SusanneRelease:
                case Msg.SusanneState:
                    Susanne.Try(() => OnSusanneMessage(type, buf, len), "other player's Susanne");
                    break;
                case Msg.PlayerDied:
                case Msg.PlayerRespawned:
                case Msg.SafeState:
                    Respawn.Try(() => OnRespawnMessage(type, buf, len), "other player respawn");
                    break;
                case Msg.VentFeed:
                    if (Balls.Enabled) OnVentFeed(buf, len);
                    break;
                case Msg.BroomSweep:
                    if (Balls.Enabled) Broom.Try(() => OnSweepMessage(buf, len), "other player sweep");
                    break;
                case Msg.BroomCaps:
                {
                    var r = new Reader(buf, len, Protocol.HeaderSize);
                    var pr = Net.Get(Net.Sender);
                    if (pr == null) break;
                    pr.BroomReplay = r.U8() == 1 && Broom.Enabled;
                    Plugin.Log.LogInfo(pr.BroomReplay ? $"[Broom] {pr.Name} replays your broom" : $"[Broom] {pr.Name} can't replay your broom; sending live ball positions");
                    break;
                }
                case Msg.PitAmount:
                {
                    if (!Pits.Enabled) return;
                    var r = new Reader(buf, len, Protocol.HeaderSize);
                    string id = r.Str(); int amount = r.I32();
                    Pits.Try(() => QueuePit(id, amount), "apply pit");
                    break;
                }
                case Msg.Snapshot:
                    ApplySnapshot(buf, len);
                    break;
                case Msg.Enemy:
                {
                    var r = new Reader(buf, len, Protocol.HeaderSize);
                    bool active = r.U8() == 1;
                    var pos = new Vector3(r.F32(), r.F32(), r.F32());
                    _enemyNet = (active, pos, r.F32(), r.F32(), r.U8(), Time.realtimeSinceStartup);
                    break;
                }
                case Msg.EnemyCaught:
                    Entity.Try(ClientCaught, "jumpscare");
                    break;
                case Msg.EnemyNotice:
                    Entity.Try(() => PlayOn("TwoPixels.Enemy.EnemyAudio", "PlayNotice"), "notice sound");
                    break;
                case Msg.SkillUp:
                case Msg.ShopBuy:
                case Msg.WalletDelta:
                case Msg.WalletState:
                case Msg.UpgradeSnapshot:
                    Upgrades.Handle(type, buf, len);
                    break;
                case Msg.ObjState:
                case Msg.ObjClaim:
                case Msg.ObjRelease:
                case Msg.ObjFlag:
                case Msg.ObjDelta:
                case Msg.RobotSay:
                case Msg.ObjOwner:
                case Msg.ObjFinal:
                    Shared.Handle(type, buf, len);
                    break;
                case Msg.DevicesHome:
                    OnDevicesHome();
                    break;
                case Msg.StatSet:
                    Unlocks.Handle(buf, len);
                    break;
                case Msg.Whiteboard:
                    Boards.Handle(buf, len);
                    break;
                case Msg.PropsLive:
                case Msg.PropsRest:
                    Props.Handle(type, buf, len);
                    break;
                case Msg.DoorState:
                {
                    var r = new Reader(buf, len, Protocol.HeaderSize);
                    string key = r.Str(); bool open = r.U8() == 1;
                    Doors.Try(() => ApplyDoor(key, open), "apply door");
                    break;
                }
                case Msg.DayState:
                {
                    var r = new Reader(buf, len, Protocol.HeaderSize);
                    int phase = r.I32(), day = r.I32();
                    Clock.Try(() => ApplyDay(phase, day), "apply day");
                    break;
                }
                case Msg.DayTime:
                {
                    var r = new Reader(buf, len, Protocol.HeaderSize);
                    int phase = r.I32(); float timer = r.F32();
                    Clock.Try(() => ApplyClock(phase, timer), "day clock");
                    break;
                }
            }
        }

        private static void ApplySnapshot(byte[] buf, int len)
        {
            var r = new Reader(buf, len, Protocol.HeaderSize);
            int rooms = r.U16(), queued = 0;
            for (int k = 0; k < rooms; k++)
            {
                string id = r.Str(); int n = r.I32();
                if (n < 0 || n > 200000) return;
                var bits = r.Bytes((n + 7) / 8);
                if (!Balls.Enabled) continue;
                for (int i = 0; i < n; i++)
                    if ((bits[i >> 3] & (1 << (i & 7))) != 0 && _pendingTakes.Count < MaxPendingTakes) { _pendingTakes.Enqueue((id, i)); queued++; }
            }
            int pits = r.U16();
            for (int k = 0; k < pits; k++)
            {
                string id = r.Str(); int amount = r.I32();
                if (Pits.Enabled) Pits.Try(() => QueuePit(id, amount), "snapshot pit");
            }
            Plugin.Log.LogInfo($"Received world snapshot: {rooms} rooms, {queued} collected balls to merge, {pits} pits");
        }
    }

    internal static class Hooks
    {
        public static void SpotCollectedPostfix(object __instance) => World.OnLocalSpotCollected(__instance);
        public static bool CatchRangePrefix(object __instance, ref bool __result) => World.CatchRangePrefix(__instance, ref __result);
        public static void StartGamePostfix(bool freshStart) => CoopBehaviour.OnLocalStartGame(freshStart);
        public static void SweepPrefix(object __instance, Vector3 pushDirection, float pushSpeed) => World.OnLocalSweep(__instance, pushDirection, pushSpeed);
        public static bool VentConsumePrefix(object __instance, object ball, ref bool __result) => World.OnVentConsume(__instance, ball, ref __result);
        public static bool DeathPrefix(object playJumpscare, ref bool __result) => World.OnDeathFlow(playJumpscare, ref __result);
        public static void DeathPostfix(bool __result) => World.OnDeathFlowDone(__result);
        public static void HubDoorPrefix() => World.OnHubDoorChange(true);
        public static void HubDoorPostfix() => World.OnHubDoorChange(false);
        public static bool DoorClosePrefix() => World.OnDoorRequestClose();
        public static void ClaimOpenPairPrefix(object __instance) => World.OnClaimOpenPair(__instance);
        public static bool EquipToolPrefix() => Upgrades.OnEquipPurchasedTool();
        public static bool RobotSayPrefix(object __instance, ref bool __result) => Shared.OnSayPrefix(__instance, ref __result);
        public static void RobotSayPostfix(object __instance, object clip, bool interrupt, bool __result) => Shared.OnSaid(__instance, clip, interrupt, __result);
        public static bool UnstuckPrefix(object __instance) => World.OnUnstuckButton(__instance);
        public static void ResetBallsPrefix(object __0, out List<(int idx, Vector3 pos)> __state) => __state = World.BallSpotsBefore(__0);
        public static void ResetBallsPostfix(object __0, List<(int idx, Vector3 pos)> __state) => World.SendResetBalls(__0, __state);
        public static void DevicesHomePostfix(int __result) => World.SendDevicesHome(__result);
        public static bool RestartDayPrefix() => World.AllowRestartDay();
        public static void UnstuckCooldownPostfix(ref float __result) => World.OnUnstuckCooldown(ref __result);
    }
}
