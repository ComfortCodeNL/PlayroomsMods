using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;

namespace PlayroomsCoop
{
    public partial class CoopBehaviour : MonoBehaviour
    {
        public CoopBehaviour(IntPtr ptr) : base(ptr) { _instance = this; }
        private static CoopBehaviour _instance;

        internal static void OnLocalStartGame(bool freshStart)
        {
            var me = _instance;
            if (me == null || me._role != Role.Host || !me._connected) return;
            HostSave.OnHostStartGame();
            var w = Protocol.W.Begin(Msg.HostStarting); w.U8(freshStart ? (byte)1 : (byte)0);
            w.SendTo(me._peer, SteamNet.SendReliable);
            Plugin.Log.LogInfo("Host is starting the game: telling Player 2 to load in");
        }

        private enum Role { None, Host, Client }
        private enum View { Main, Settings }

        private const float JoinTimeout = 20f;
        private const float PeerTimeout = 10f;
        private const float InviteLife = 30f;

        private const int KeyR = 0x52, KeyK = 0x4B, KeyS = 0x53, KeyY = 0x59, KeyN = 0x4E;

        private Role _role;
        private ulong _peer;
        private string _peerName = "";
        private bool _connected;
        private bool _playing;
        private bool _worldStarted;
        private bool _restoredAtStart;
        private bool _meReady, _peerReady;
        private ulong _joinTarget;
        private uint _joinNonce;

        private ulong _inviteFrom;
        private float _inviteAt;
        private ulong _invitedFriend;
        private float _invitedAt, _lastInviteSend;

        private string _confirm;
        private float _confirmUntil;
        private View _view = View.Main;

        private float _joinStarted, _lastHello, _lastRecv, _lastBeacon, _lastFriendRefresh, _lastAccept, _nextSend;
        private float _lastCamSearch, _lastErrorLog, _toastUntil;
        private bool _menuOpen;
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

        private Avatar _local, _remote;
        private Camera _cam;
        private float _eyeH = 1.6f;
        private uint _seq;
        private bool _remoteInWorld;
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
                _keys = new[] { Plugin.MenuKey.Value, KeyInput.H, KeyInput.L, KeyInput.Escape, KeyR, KeyK, KeyS, KeyY, KeyN };
            }
            KeyInput.Poll(Application.isFocused, _keys);
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
                if (_peer != 0) SteamNet.Accept(_peer);
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
                        foreach (var f in _friends)
                        {
                            var w = Protocol.W.Begin(Msg.Beacon); w.U8(_connected ? (byte)0 : (byte)1);
                            w.SendTo(f.Id, SteamNet.SendUnreliable);
                        }
                    }
                    if (_invitedFriend != 0 && !_connected)
                    {
                        if (now - _invitedAt > InviteLife) { SetStatus($"{SteamNet.NameOf(_invitedFriend)} didn't answer the invite."); _invitedFriend = 0; }
                        else if (now - _lastInviteSend > 3f)
                        {
                            _lastInviteSend = now;
                            Protocol.SendSimple(_invitedFriend, Msg.Invite, SteamNet.SendReliable);
                        }
                    }
                    break;

                case Role.Client:
                    if (!_connected)
                    {
                        if (now - _lastHello > 1f)
                        {
                            _lastHello = now;
                            Protocol.SendHello(_joinTarget, _joinNonce);
                        }
                        if (now - _joinStarted > JoinTimeout)
                        {
                            SetStatus($"No answer from {SteamNet.NameOf(_joinTarget)}. Their lobby may have closed.");
                            ResetSession();
                        }
                    }
                    break;
            }

            if (_connected && now - _lastRecv > PeerTimeout)
            {
                SetStatus($"Lost connection to {_peerName}.");
                DropPeer();
            }

            if (_connected) { Perf.Begin("poses/avatars"); try { UpdatePoses(now, dt); } finally { Perf.End(); } }
            Perf.Begin("host save");
            try { if (_connected && _role == Role.Host) HostSave.HostTick(now, _playing, _peer); } finally { Perf.End(); }
            if (_role != Role.Host) HostSave.ClientTick(now);
            HostSave.SelfCheck(now);
            if (_connected && _role == Role.Client) HostSave.ClientResendTick(now, _peer);
            if (_connected && _playing && !_worldStarted && _role == Role.Client && HostSave.GuestLoaded && World.InGameWorld())
            {
                StartWorld();
                Protocol.SendSimple(_peer, Msg.WorldReady, SteamNet.SendReliable);
            }
            ClientMenuCheck(now);
            ClockGate();
            ClockHold();
            World.Tick(now, dt);
        }

        [HideFromIl2Cpp]
        private void OnMessage(ulong from, byte[] buf, int len)
        {
            if (Protocol.IsModHello(buf, len, out byte theirVersion)) { _modSeen[from] = (Now, theirVersion); return; }
            if (!Protocol.TryReadHeader(buf, len, out Msg type)) return;
            float now = Now;
            _modSeen[from] = (now, Protocol.Version);

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
                    uint nonce = Protocol.ReadHello(buf, len);
                    if (_peer == 0 || _peer == from)
                    {
                        bool isNew = !_connected;
                        Protocol.SendWelcome(from, true, nonce);
                        if (isNew) Connect(from, now);
                    }
                    else Protocol.SendWelcome(from, false, nonce);
                    break;
                }

                case Msg.Welcome:
                {
                    if (_role != Role.Client || from != _joinTarget || _connected) break;
                    bool accepted = Protocol.ReadWelcome(buf, len, out uint nonce);
                    if (nonce != _joinNonce) break;
                    if (accepted) Connect(from, now);
                    else
                    {
                        SetStatus($"{SteamNet.NameOf(from)}'s lobby is full.");
                        ResetSession();
                    }
                    break;
                }

                case Msg.Ready:
                    if (!_connected || from != _peer) break;
                    _lastRecv = now;
                    _peerReady = len > Protocol.HeaderSize && buf[Protocol.HeaderSize] == 1;
                    TryStartPlaying();
                    break;

                case Msg.HostStarting:
                    if (from != _peer || _role != Role.Client) break;
                    _lastRecv = now;
                    if (HostSave.Enabled) Notify("The host is loading: you'll join their world in a moment");
                    else AutoLoad();
                    break;

                case Msg.SaveOffer:
                case Msg.SaveChunk:
                    if (from != _peer || _role != Role.Client) break;
                    _lastRecv = now;
                    HostSave.Handle(type, buf, len);
                    break;

                case Msg.SaveResend:
                    if (from != _peer || _role != Role.Host) break;
                    _lastRecv = now;
                    HostSave.HandleResend(buf, len);
                    break;

                case Msg.WorldReady:
                    if (from != _peer || _role != Role.Host || !_worldStarted) break;
                    _lastRecv = now;
                    World.OnDisconnected();
                    World.OnConnected(_peer, true);
                    Plugin.Log.LogInfo("Player 2 is in the host's world: world sharing re-synced");
                    break;

                case Msg.Kick:
                    if (from != _peer || _role != Role.Client) break;
                    SetStatus($"{_peerName} removed you from the game.");
                    Notify("You were removed from the game");
                    ResetSession();
                    break;

                case Msg.State:
                    if (!_connected || from != _peer) break;
                    if (!Protocol.ReadState(buf, len, out PoseState s)) break;
                    _lastRecv = now;
                    if (s.Seq == 0) break;
                    EnsureAvatars();
                    _remoteInWorld = s.InWorld; World.RemoteInWorld = s.InWorld;
                    if (s.InWorld) { _remote.SetTarget(s); _remote.SetBroom(s.Sweeping && Plugin.ShowBroom.Value); }
                    else _remote.SetVisible(false);
                    break;

                default:
                    if (!_connected || from != _peer) break;
                    _lastRecv = now;
                    if (_worldStarted) World.Handle(type, buf, len);
                    break;

                case Msg.Bye:
                    if (from != _peer) break;
                    SetStatus($"{_peerName} left.");
                    DropPeer();
                    break;
            }
        }

        [HideFromIl2Cpp]
        private void Connect(ulong peer, float now)
        {
            _peer = peer;
            _peerName = SteamNet.NameOf(peer);
            _connected = true;
            _playing = false; _meReady = false; _peerReady = false;
            _invitedFriend = 0; _inviteFrom = 0;
            _lastRecv = now;
            _toastUntil = now + 6f;
            SetStatus($"{_peerName} is in the lobby. Press Ready when you're set.");
            Plugin.Log.LogInfo($"Connected with {_peerName} ({peer}) as {(_role == Role.Host ? "Player 1" : "Player 2")}");
            EnsureAvatars();
            _worldStarted = false;
            HostSave.OnConnected();
            SetMenu(true, now);
        }

        private void DropPeer()
        {
            _connected = false; ClockGateRelease();
            if (_peer != 0) SteamNet.Close(_peer);
            if (_worldStarted) World.OnDisconnected();
            HostSave.OnDisconnected();
            _peer = 0;
            _connected = false; _playing = false; _worldStarted = false; _meReady = false; _peerReady = false;
            DestroyAvatars();
            if (_role == Role.Client) { _role = Role.None; _joinTarget = 0; }
        }

        private void ResetSession()
        {
            _connected = false; ClockGateRelease();
            if (_peer != 0) SteamNet.Close(_peer);
            if (_worldStarted) World.OnDisconnected();
            HostSave.OnDisconnected();
            _role = Role.None;
            _peer = 0;
            _joinTarget = 0;
            _invitedFriend = 0;
            _connected = false; _playing = false; _meReady = false; _peerReady = false;
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
            SetStatus($"Waiting for {_peerName} to start or continue a game.");
        }

        private object _dayService;
        private bool _clockPausedByUs;
        private float _nextClockCheck;

        private void ClockGate()
        {
            float now = Now;
            if (now < _nextClockCheck) return;
            _nextClockCheck = now + 0.5f;
            bool wantPause = _connected && World.InGameWorld() && !(_playing && _remoteInWorld);
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
                Plugin.Log.LogInfo(wantPause ? "Day clock held until both players are in the world" : "Both players in the world: day clock running");
                if (wantPause) Notify("Clock paused: waiting for your partner to load in");
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
