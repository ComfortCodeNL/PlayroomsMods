using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;

namespace PlayroomsCoop
{
    public partial class CoopBehaviour : MonoBehaviour
    {
        public CoopBehaviour(IntPtr ptr) : base(ptr) { _instance = this; }
        private static CoopBehaviour _instance;

        internal static bool ApiConnected => _instance != null && _instance._connected;
        internal static bool ApiIsHost => ApiConnected && _instance._role == Role.Host;
        internal static bool ApiIsGuest => ApiConnected && _instance._role == Role.Client;

        internal static void OnLocalStartGame(bool freshStart)
        {
            var me = _instance;
            if (me != null && me._menuOpen) me.SetMenu(false, Time.realtimeSinceStartup);
            if (me == null || me._role != Role.Host || !me._connected) return;
            HostSave.OnHostStartGame();
            var w = Protocol.W.Begin(Msg.HostStarting); w.U8(freshStart ? (byte)1 : (byte)0);
            w.SendAll(SteamNet.SendReliable);
            Plugin.Log.LogInfo("Host is starting the game: telling the other players to load in");
        }

        private enum Role { None, Host, Client }
        private enum View { Main, Settings }

        private const float JoinTimeout = 20f;
        private const float PeerTimeout = 10f;
        private const float InviteLife = 30f;

        private const int KeyR = 0x52, KeyK = 0x4B, KeyS = 0x53, KeyY = 0x59, KeyN = 0x4E, KeyC = 0x43;

        private Role _role;
        private bool _connected;
        private bool _playing;
        private bool _worldStarted;
        private bool _restoredAtStart;
        private bool _meReady;
        private ulong _joinTarget;
        private uint _joinNonce;

        private ulong _inviteFrom;
        private float _inviteAt;
        private ulong _invitedFriend;
        private float _invitedAt, _lastInviteSend;

        private string _confirm;
        private float _confirmUntil;
        private View _view = View.Main;

        private float _joinStarted, _lastHello, _lastBeacon, _lastFriendRefresh, _lastAccept, _nextSend;
        private float _lastCamSearch, _lastErrorLog, _toastUntil;
        private bool _menuOpen, _wasInWorld;
        private bool _guiSeen, _guiWarned, _updateSeen;
        private float _menuOpenedAt;
        private static string _notice;
        private static float _noticeUntil;

        internal static void Notify(string text)
        {
            _notice = text;
            _noticeUntil = UnityEngine.Time.realtimeSinceStartup + 5f;
        }
        private string _status = "";

        private List<SteamNet.Friend> _friends = new List<SteamNet.Friend>();
        private readonly Dictionary<ulong, (float time, bool open)> _beacons = new Dictionary<ulong, (float, bool)>();
        private readonly Dictionary<ulong, (float time, byte version)> _modSeen = new Dictionary<ulong, (float, byte)>();
        private float _lastModHello, _steamReadyAt = -1f;

        private Avatar _local;
        private readonly Dictionary<ulong, Avatar> _avatars = new Dictionary<ulong, Avatar>();
        private Camera _cam;
        private float _eyeH = 1.6f;
        private uint _seq;
        private float _bodyEyeOffset, _attachedYaw, _attachedPitch;
        private bool _hasBodyOffset;
        private PoseState _lastPose = new PoseState { EyeHeight = 1.6f };

        private GUIStyle _boxStyle, _textStyle, _nameStyle, _smallStyle, _buttonStyle;
        private int[] _keys;

        private float Now => Time.realtimeSinceStartup;

        private void FixedUpdate()
        {
            if (_worldStarted) World.FixedTick();
        }

        private void Update()
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            try { Tick(); }
            catch (Exception e)
            {
                if (Now - _lastErrorLog > 5f) { _lastErrorLog = Now; Plugin.Log.LogError(e.ToString()); }
            }
            Perf.Frame((System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency, Now);
        }

        private void Tick()
        {
            float dt = Time.unscaledDeltaTime;
            float now = Now;

            if (_keys == null)
            {
                _keys = new[] { Plugin.MenuKey.Value, KeyInput.H, KeyInput.L, KeyInput.Escape, KeyR, KeyK, KeyS, KeyY, KeyN, KeyC };
            }
            KeyInput.Poll(Application.isFocused, _keys);
            bool inWorld = World.InGameWorld();
            if (inWorld && !_wasInWorld && _menuOpen && now - _menuOpenedAt > 1f) SetMenu(false, now);
            _wasInWorld = inWorld;
            if (KeyInput.Pressed(Plugin.MenuKey.Value)) SetMenu(!_menuOpen, now);
            if (_menuOpen && KeyInput.Pressed(KeyInput.Escape))
            {
                if (_view != View.Main) _view = View.Main;
                else if (_confirm != null) _confirm = null;
                else SetMenu(false, now);
            }
            if (_menuOpen && !_guiSeen && now - _menuOpenedAt > 3f && !_guiWarned)
            {
                _guiWarned = true;
                Plugin.Log.LogWarning("Menu is open but Unity never called OnGUI, so nothing can be drawn. Please send this log.");
            }
            if (!_updateSeen) { _updateSeen = true; Plugin.Log.LogInfo("Co-op update loop running"); }
            if (_menuOpen) MenuInput.Hold();
            else MenuInput.StuckKeyGuard(now);

            if (!_restoredAtStart) { _restoredAtStart = true; HostSave.RestoreAll(); }

            if (!SteamNet.Ready && !SteamNet.TryInit())
            {
                _status = "Waiting for Steam...";
                return;
            }

            if (now - _lastFriendRefresh > (_menuOpen || _role != Role.None ? 2f : 4f))
            {
                _lastFriendRefresh = now;
                _friends = SteamNet.GetFriendsInThisGame();
                _friends.Sort((a, b) =>
                {
                    int ra = FriendRank(a.Id), rb = FriendRank(b.Id);
                    return ra != rb ? ra.CompareTo(rb) : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                });
            }
            if (now - _lastAccept > 1f)
            {
                _lastAccept = now;
                foreach (var f in _friends) SteamNet.Accept(f.Id);
                if (_joinTarget != 0) SteamNet.Accept(_joinTarget);
                foreach (var id in Net.Peers.Keys) SteamNet.Accept(id);
                NetCheck.Tick(_friends);
            }

            if (_steamReadyAt < 0f) _steamReadyAt = now;
            if (now - _lastModHello > 8f)
            {
                _lastModHello = now;
                foreach (var f in _friends) Protocol.SendSimple(f.Id, Msg.ModHello, SteamNet.SendUnreliable);
            }

            Perf.Begin("network receive"); try { SteamNet.Receive(OnMessage); } finally { Perf.End(); }
            HandleKeys(now);

            if (_inviteFrom != 0 && now - _inviteAt > InviteLife) _inviteFrom = 0;
            if (_confirm != null && now > _confirmUntil) _confirm = null;

            switch (_role)
            {
                case Role.Host:
                    if (now - _lastBeacon > 2f)
                    {
                        _lastBeacon = now;
                        bool open = Net.Peers.Count < Net.MaxPlayers - 1;
                        foreach (var f in _friends)
                        {
                            var w = Protocol.W.Begin(Msg.Beacon); w.U8(open ? (byte)1 : (byte)0);
                            w.SendTo(f.Id, SteamNet.SendUnreliable);
                        }
                    }
                    if (_invitedFriend != 0)
                    {
                        if (Net.Peers.ContainsKey(_invitedFriend)) _invitedFriend = 0;
                        else if (now - _invitedAt > InviteLife) { SetStatus($"{SteamNet.NameOf(_invitedFriend)} didn't answer the invite."); _invitedFriend = 0; }
                        else if (now - _lastInviteSend > 3f)
                        {
                            _lastInviteSend = now;
                            Protocol.SendSimple(_invitedFriend, Msg.Invite, SteamNet.SendReliable);
                        }
                    }
                    foreach (var p in new List<Net.Peer>(Net.Peers.Values))
                        if (now - p.LastRecv > PeerTimeout) { SetStatus($"Lost connection to {p.Name}."); RemovePeer(p.Id, true); }
                    break;

                case Role.Client:
                    if (!_connected)
                    {
                        if (now - _lastHello > 1f)
                        {
                            _lastHello = now;
                            SendHello();
                        }
                        if (now - _joinStarted > JoinTimeout)
                        {
                            SetStatus($"No answer from {SteamNet.NameOf(_joinTarget)}. Their lobby may have closed.");
                            ResetSession();
                        }
                    }
                    else
                    {
                        var host = Net.Get(Net.HostId);
                        if (host == null || now - host.LastRecv > PeerTimeout)
                        {
                            SetStatus($"Lost connection to {(host != null ? host.Name : "the host")}.");
                            ResetSession();
                        }
                    }
                    break;
            }

            if (_connected) { Perf.Begin("poses/avatars"); try { UpdatePoses(now, dt); } finally { Perf.End(); } }
            Perf.Begin("host save");
            try { if (_connected && _role == Role.Host) HostSave.HostTick(now, _playing, new List<ulong>(Net.Peers.Keys)); } finally { Perf.End(); }
            if (_role != Role.Host) HostSave.ClientTick(now);
            HostSave.SelfCheck(now);
            if (_connected && _role == Role.Client) HostSave.ClientResendTick(now, Net.HostId);
            if (_connected && _playing && !_worldStarted && _role == Role.Client && (HostSave.GuestLoaded || !HostSave.Enabled) && World.InGameWorld())
            {
                StartWorld();
                Protocol.SendSimple(Net.HostId, Msg.WorldReady, SteamNet.SendReliable);
            }
            ClientMenuCheck(now);
            ClockGate();
            ClockHold();
            World.Tick(now, dt);
            LagGuard.Tick(now, dt, _worldStarted && Net.Peers.Count > 0 && World.InGameWorld());
            CoopApi.Tick(now);
        }

        private void SendHello()
        {
            var w = Protocol.W.Begin(Msg.Hello); w.U32(_joinNonce); w.Str(SteamNet.MyName); w.U8(ColorByte(Net.MyColor));
            w.SendTo(_joinTarget, SteamNet.SendReliable);
        }

        private static byte ColorByte(int c) => c < 0 || c > 254 ? (byte)255 : (byte)c;
        private static int ColorFromByte(byte b) => b == 255 ? -1 : b;

        [HideFromIl2Cpp]
        private void OnMessage(ulong from, byte[] buf, int len)
        {
            if (Protocol.IsModHello(buf, len, out byte theirVersion)) { _modSeen[from] = (Now, theirVersion); return; }
            if (!Protocol.TryReadHeader(buf, len, out Msg type)) return;
            float now = Now;
            _modSeen[from] = (now, Protocol.Version);
            var peer = Net.Get(from);
            if (peer != null) peer.LastRecv = now;
            Net.Sender = from;

            switch (type)
            {
                case Msg.Beacon:
                {
                    bool open = len > Protocol.HeaderSize ? buf[Protocol.HeaderSize] == 1 : true;
                    _beacons[from] = (now, open);
                    break;
                }

                case Msg.Invite:
                    if (_connected || _role == Role.Client) break;
                    if (_inviteFrom != from) Plugin.Log.LogInfo("Invite from " + SteamNet.NameOf(from));
                    _inviteFrom = from; _inviteAt = now;
                    break;

                case Msg.InviteDecline:
                    if (from == _invitedFriend) { SetStatus($"{SteamNet.NameOf(from)} declined the invite."); _invitedFriend = 0; }
                    break;

                case Msg.Hello:
                {
                    if (_role != Role.Host) break;
                    var r = new Reader(buf, len, Protocol.HeaderSize);
                    uint nonce = r.U32();
                    string name = r.Remaining > 2 ? r.Str() : SteamNet.NameOf(from);
                    int color = r.Remaining > 0 ? ColorFromByte(r.U8()) : -1;
                    if (peer == null && Net.Peers.Count >= Net.MaxPlayers - 1) { Protocol.SendWelcome(from, false, nonce, 0); break; }
                    if (peer == null) peer = AddPeer(from, name, color, now);
                    Protocol.SendWelcome(from, true, nonce, peer.Slot);
                    SendRoster();
                    break;
                }

                case Msg.Welcome:
                {
                    if (_role != Role.Client || from != _joinTarget || _connected) break;
                    bool accepted = Protocol.ReadWelcome(buf, len, out uint nonce, out int slot);
                    if (nonce != _joinNonce) break;
                    if (accepted) ConnectAsClient(from, slot, now);
                    else
                    {
                        SetStatus($"{SteamNet.NameOf(from)}'s lobby is full.");
                        ResetSession();
                    }
                    break;
                }

                case Msg.Roster:
                    if (_role != Role.Client || from != Net.HostId || !_connected) break;
                    ApplyRoster(buf, len, now);
                    break;

                case Msg.LobbyState:
                {
                    if (_role != Role.Host || peer == null) break;
                    var r = new Reader(buf, len, Protocol.HeaderSize);
                    peer.Ready = r.U8() == 1;
                    peer.Color = ColorFromByte(r.U8());
                    SendRoster();
                    TryStartPlaying();
                    break;
                }

                case Msg.HostStarting:
                    if (from != Net.HostId || _role != Role.Client) break;
                    if (HostSave.Enabled) Notify("The host is loading: you'll join their world in a moment");
                    else AutoLoad();
                    break;

                case Msg.SaveOffer:
                case Msg.SaveChunk:
                    if (from != Net.HostId || _role != Role.Client) break;
                    HostSave.Handle(type, buf, len);
                    break;

                case Msg.SaveResend:
                    if (peer == null || _role != Role.Host) break;
                    HostSave.HandleResend(from, buf, len);
                    break;

                case Msg.WorldReady:
                    if (peer == null || _role != Role.Host || !_worldStarted) break;
                    World.OnDisconnected();
                    World.OnConnected(true);
                    Plugin.Log.LogInfo($"{peer.Name} is in the host's world: world sharing re-synced");
                    break;

                case Msg.Kick:
                    if (from != Net.HostId || _role != Role.Client) break;
                    SetStatus($"{Net.NameOf(from)} removed you from the game.");
                    Notify("You were removed from the game");
                    ResetSession();
                    break;

                case Msg.State:
                {
                    if (!_connected || peer == null) break;
                    if (!Protocol.ReadState(buf, len, out PoseState s)) break;
                    if (s.Seq == 0) break;
                    if (s.Seq <= peer.StateSeq && peer.StateSeq - s.Seq < 1000) break;
                    peer.StateSeq = s.Seq; peer.StateAt = now;
                    var av = AvatarFor(peer);
                    if (peer.InWorld != s.InWorld) Plugin.Log.LogInfo($"[Avatars] {peer.Name} {(s.InWorld ? "is in the world" : "left the world (menu or loading)")}");
                    peer.InWorld = s.InWorld;
                    if (s.InWorld) { av.SetTarget(s); av.SetBroom(s.Sweeping && Plugin.ShowBroom.Value); }
                    else av.SetVisible(false);
                    break;
                }

                case Msg.ModCounterTake:
                case Msg.ModCounterResult:
                    if (!_connected || peer == null) break;
                    CoopApi.Handle(type, from, buf, len);
                    break;

                default:
                    if (!_connected || peer == null) break;
                    if (_worldStarted) World.Handle(type, buf, len);
                    break;

                case Msg.Bye:
                    if (peer == null) break;
                    if (_role == Role.Client && from == Net.HostId) { SetStatus($"{peer.Name} closed the game."); ResetSession(); }
                    else if (_role == Role.Host) { SetStatus($"{peer.Name} left."); RemovePeer(from, true); }
                    break;
            }
            Net.Sender = 0;
        }

        [HideFromIl2Cpp]
        private Net.Peer AddPeer(ulong id, string name, int color, float now)
        {
            int slot = 1;
            while (slot < Net.MaxPlayers && Net.Peers.Values.Any(p => p.Slot == slot)) slot++;
            var peer = new Net.Peer { Id = id, Slot = slot, Name = string.IsNullOrEmpty(name) ? SteamNet.NameOf(id) : name, Color = color, LastRecv = now };
            Net.Peers[id] = peer;
            bool first = !_connected;
            _connected = true;
            if (_invitedFriend == id) _invitedFriend = 0;
            _inviteFrom = 0;
            _toastUntil = now + 6f;
            SetStatus(_playing ? $"{peer.Name} is joining your game." : $"{peer.Name} joined the lobby. Press Ready when everyone's set.");
            Plugin.Log.LogInfo($"{peer.Name} ({id}) joined as Player {slot + 1}");
            if (first) { EnsureAvatars(); HostSave.OnConnected(); SetMenu(true, now); }
            if (_worldStarted) World.OnPeerJoined();
            return peer;
        }

        [HideFromIl2Cpp]
        private void RemovePeer(ulong id, bool announce)
        {
            if (!Net.Peers.Remove(id)) return;
            SteamNet.Close(id);
            if (_avatars.TryGetValue(id, out var av)) { av.Destroy(); _avatars.Remove(id); }
            HostSave.OnPeerLeft(id);
            if (_worldStarted) World.OnPeerLeft(id);
            if (_role == Role.Host)
            {
                if (announce) SendRoster();
                if (Net.Peers.Count == 0) EndHostSession();
            }
        }

        private void EndHostSession()
        {
            _connected = false; ClockGateRelease();
            if (_worldStarted) World.OnDisconnected();
            HostSave.OnDisconnected();
            _playing = false; _worldStarted = false; _meReady = false;
            DestroyAvatars();
        }

        private void SendRoster()
        {
            if (_role != Role.Host) return;
            var w = Protocol.W.Begin(Msg.Roster);
            w.U8(_playing ? (byte)1 : (byte)0);
            w.U8((byte)(Net.Peers.Count + 1));
            w.U64(Net.Me); w.U8(0); w.U8(_meReady ? (byte)1 : (byte)0); w.U8(ColorByte(Net.MyColor)); w.Str(SteamNet.MyName);
            foreach (var p in Net.Peers.Values) { w.U64(p.Id); w.U8((byte)p.Slot); w.U8(p.Ready ? (byte)1 : (byte)0); w.U8(ColorByte(p.Color)); w.Str(p.Name); }
            w.SendAll(SteamNet.SendReliable);
            RefreshAvatarColors();
        }

        [HideFromIl2Cpp]
        private void ApplyRoster(byte[] buf, int len, float now)
        {
            var r = new Reader(buf, len, Protocol.HeaderSize);
            bool playing = r.U8() == 1;
            int n = r.U8();
            var seen = new HashSet<ulong>();
            bool added = false;
            for (int i = 0; i < n; i++)
            {
                ulong id = r.U64(); int slot = r.U8(); bool ready = r.U8() == 1; int color = ColorFromByte(r.U8()); string name = r.Str();
                if (id == Net.Me) { Net.MySlot = slot; continue; }
                seen.Add(id);
                var p = Net.Get(id);
                if (p == null)
                {
                    p = new Net.Peer { Id = id, LastRecv = now };
                    Net.Peers[id] = p; added = true;
                    SteamNet.Accept(id);
                    if (id != Net.HostId) Plugin.Log.LogInfo($"{name} is in the game as Player {slot + 1}");
                }
                p.Slot = slot; p.Ready = ready; p.Color = color; p.Name = name;
                if (id != Net.HostId && p.LastRecv < now - PeerTimeout) p.LastRecv = now;
            }
            foreach (var id in new List<ulong>(Net.Peers.Keys)) if (!seen.Contains(id)) RemovePeer(id, false);
            if (added && _worldStarted) World.OnPeerJoined();
            if (playing && !_playing)
            {
                _playing = true;
                Plugin.Log.LogInfo("The host started the game");
                SetStatus(HostSave.Enabled ? "Starting. Waiting for the host's world..." : "Starting. Load your game to join.");
            }
            RefreshAvatarColors();
        }

        [HideFromIl2Cpp]
        private void ConnectAsClient(ulong host, int slot, float now)
        {
            _connected = true;
            Net.HostId = host; Net.IsHost = false; Net.MySlot = slot;
            Net.Peers[host] = new Net.Peer { Id = host, Slot = 0, Name = SteamNet.NameOf(host), LastRecv = now };
            _playing = false; _meReady = false;
            _invitedFriend = 0; _inviteFrom = 0;
            _toastUntil = now + 6f;
            SetStatus($"In {SteamNet.NameOf(host)}'s lobby. Press Ready when you're set.");
            Plugin.Log.LogInfo($"Connected to {SteamNet.NameOf(host)} ({host}) as Player {slot + 1}");
            EnsureAvatars();
            _worldStarted = false;
            HostSave.OnConnected();
            SendLobbyState();
            SetMenu(true, now);
        }

        private void ResetSession()
        {
            _connected = false; ClockGateRelease();
            foreach (var id in Net.Peers.Keys) SteamNet.Close(id);
            if (_joinTarget != 0) SteamNet.Close(_joinTarget);
            if (_worldStarted) World.OnDisconnected();
            HostSave.OnDisconnected();
            _role = Role.None;
            Net.Reset();
            _joinTarget = 0;
            _invitedFriend = 0;
            _playing = false; _worldStarted = false; _meReady = false;
            DestroyAvatars();
        }

        private void AutoLoad()
        {
            if (World.InGameWorld()) return;
            try
            {
                object gm = null;
                foreach (var g in R.FindAll(R.T("TwoPixels.BackroomCleaner.GameManager"))) { gm = g; break; }
                object canvas = null;
                foreach (var c in R.FindAll(R.T("TwoPixels.UserInterfaces.MainMenuCanvas"))) { canvas = c; break; }
                if (gm == null || canvas == null) { Notify("Host started: load your game to join them"); return; }
                bool any = Convert.ToBoolean(R.Call(gm, "HasAnySave"));
                int slot = any ? Convert.ToInt32(R.Call(gm, "GetContinueSlot")) : -1;
                if (slot < 0)
                {
                    Notify("Host started: press Play to start your game and join them");
                    SetStatus("The host started. You have no save yet: press Play (New Game) to join them.");
                    return;
                }
                SetMenu(false, Now);
                Notify("Host started: loading your game...");
                Plugin.Log.LogInfo($"Host started: continuing save slot {slot}");
                try { R.Call(canvas, "StartGame", slot, false, null); }
                catch { R.Call(gm, "ActivateSlot", slot, false, null); }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Couldn't load automatically: " + ((e as System.Reflection.TargetInvocationException)?.InnerException ?? e).Message);
                Notify("Host started: load your game to join them");
            }
        }

        private void ClockGateRelease()
        {
            if (!_clockPausedByUs) return;
            _clockPausedByUs = false;
        }

        private float _clientOutSince = -1f;

        private void ClientMenuCheck(float now)
        {
            if (_role != Role.Client || !_connected || !_worldStarted || !HostSave.Enabled) { _clientOutSince = -1f; return; }
            if (World.InGameWorld()) { _clientOutSince = -1f; return; }
            if (_clientOutSince < 0f) { _clientOutSince = now; return; }
            if (now - _clientOutSince < 3f) return;
            _clientOutSince = -1f;
            _worldStarted = false;
            World.OnDisconnected();
            HostSave.OnClientBackInMenu();
            Plugin.Log.LogInfo("Back in the main menu: waiting for the host's next game");
            SetStatus($"Waiting for {Net.NameOf(Net.HostId)} to start or continue a game.");
        }

        private object _dayService;
        private bool _clockPausedByUs;
        private float _nextClockCheck;

        private void ClockGate()
        {
            float now = Now;
            if (now < _nextClockCheck) return;
            _nextClockCheck = now + 0.5f;
            bool wantPause = _connected && World.InGameWorld() && !(_playing && Net.Peers.Values.All(p => p.InWorld));
            try
            {
                if (!World.Alive(_dayService))
                {
                    _dayService = null; _clockPausedByUs = false;
                    foreach (var d in R.FindAll(R.T("TwoPixels.DayTime.DayTimeService"))) { _dayService = d; break; }
                }
                if (_dayService == null) { _clockPausedByUs = false; return; }
                if (wantPause && !World.DayRunning(_dayService)) wantPause = false;
                if (wantPause == _clockPausedByUs) return;
                _clockPausedByUs = wantPause;
                if (wantPause)
                {
                    _pinPhase = Convert.ToInt32(R.Get(_dayService, "_phase"));
                    _pinTimer = R.Get<float>(_dayService, "_timer");
                }
                Plugin.Log.LogInfo(wantPause ? "Day clock held until everyone is in the world" : "Everyone is in the world: day clock running");
                if (wantPause) Notify("Clock paused: waiting for everyone to load in");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Day clock hold: " + e.Message); _clockPausedByUs = false; }
        }

        private static int _pinPhase;
        private static float _pinTimer;

        private void ClockHold()
        {
            if (!_clockPausedByUs || !World.Alive(_dayService)) return;
            try
            {
                if (Convert.ToInt32(R.Get(_dayService, "_phase")) != _pinPhase) { _clockPausedByUs = false; return; }
                R.Set(_dayService, "_timer", _pinTimer);
            }
            catch { _clockPausedByUs = false; }
        }

        [HideFromIl2Cpp]
        private void SetStatus(string s)
        {
            _status = s;
            _toastUntil = Math.Max(_toastUntil, Now + 5f);
        }

        private void OnDestroy()
        {
            try { DestroyAvatars(); MenuInput.End(); }
            catch { /* shutting down */ }
        }
    }
}
