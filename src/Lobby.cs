using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;

namespace PlayroomsCoop
{
    public partial class CoopBehaviour
    {
        [HideFromIl2Cpp]
        private void SetMenu(bool open, float now)
        {
            if (open == _menuOpen) return;
            _menuOpen = open;
            _view = View.Main; _confirm = null;
            if (open) { _menuOpenedAt = now; MenuInput.Begin(); } else MenuInput.End();
            Plugin.Log.LogInfo(open ? "Menu opened" : "Menu closed");
        }

        private void StartHosting()
        {
            if (_role != Role.None) return;
            _role = Role.Host;
            Net.Reset();
            Net.IsHost = true; Net.HostId = Net.Me; Net.MySlot = 0; Net.MyColor = SavedColor();
            _lastBeacon = 0;
            _lastFriendRefresh = 0;
            SetStatus("Lobby open. Up to 3 friends can join from their list, or invite them.");
            Plugin.Log.LogInfo("Hosting");
        }

        [HideFromIl2Cpp]
        private void Join(ulong host)
        {
            if (_role != Role.None) return;
            _role = Role.Client;
            Net.Reset();
            Net.MyColor = SavedColor();
            _joinTarget = host;
            _joinNonce = (uint)new System.Random().Next(1, int.MaxValue);
            _joinStarted = Now;
            _lastHello = 0;
            SetStatus($"Joining {SteamNet.NameOf(host)}...");
            Plugin.Log.LogInfo("Joining " + SteamNet.NameOf(host));
        }

        [HideFromIl2Cpp]
        private void Invite(ulong friend)
        {
            if (_role == Role.Client) return;
            if (_role == Role.None) StartHosting();
            if (Net.Peers.Count >= Net.MaxPlayers - 1) { SetStatus("Your lobby is full (4 players)."); return; }
            _invitedFriend = friend;
            _invitedAt = Now; _lastInviteSend = Now;
            Protocol.SendSimple(friend, Msg.Invite, SteamNet.SendReliable);
            SetStatus($"Invite sent to {SteamNet.NameOf(friend)}.");
            Plugin.Log.LogInfo("Invited " + SteamNet.NameOf(friend));
        }

        private void AcceptInvite()
        {
            if (_inviteFrom == 0) return;
            ulong host = _inviteFrom;
            _inviteFrom = 0;
            if (_role != Role.None && !(_role == Role.Host && !_connected)) { SetStatus("Leave your current game first."); return; }
            if (_role == Role.Host) ResetSession();
            Join(host);
        }

        private void DeclineInvite()
        {
            if (_inviteFrom == 0) return;
            Protocol.SendSimple(_inviteFrom, Msg.InviteDecline, SteamNet.SendReliable);
            _inviteFrom = 0;
        }

        private void ToggleReady()
        {
            if (!_connected || _playing) return;
            _meReady = !_meReady;
            if (_role == Role.Host) { SendRoster(); TryStartPlaying(); }
            else SendLobbyState();
        }

        private void SendLobbyState()
        {
            if (_role != Role.Client || !_connected) return;
            var w = Protocol.W.Begin(Msg.LobbyState); w.U8(_meReady ? (byte)1 : (byte)0); w.U8(ColorByte(Net.MyColor));
            w.SendTo(Net.HostId, SteamNet.SendReliable);
        }

        private static int SavedColor()
        {
            int c = Plugin.MyColor.Value;
            return c >= 0 && c < Net.Palette.Length ? c : -1;
        }

        private void CycleColor(int dir)
        {
            int n = Net.Palette.Length;
            int cur = Net.MyColor >= 0 ? Net.MyColor : Net.DefaultIndex(Net.MySlot);
            Net.MyColor = ((cur + dir) % n + n) % n;
            Plugin.MyColor.Value = Net.MyColor;
            if (_role == Role.Host) SendRoster(); else SendLobbyState();
            RefreshAvatarColors();
        }

        private void TryStartPlaying()
        {
            if (_role != Role.Host || _playing || !_connected || !_meReady) return;
            foreach (var p in Net.Peers.Values) if (!p.Ready) return;
            _playing = true;
            Plugin.Log.LogInfo($"Everyone ready ({Net.Count} players)");
            SendRoster();
            StartWorld();
            SetStatus($"Everyone's ready. Playing with {Net.Peers.Count} other player{(Net.Peers.Count == 1 ? "" : "s")}!");
        }

        private void StartWorld()
        {
            if (_worldStarted) return;
            _worldStarted = true;
            World.OnConnected(_role == Role.Host);
            Plugin.Log.LogInfo("World sharing started");
        }

        [HideFromIl2Cpp]
        private bool Confirmed(string action)
        {
            if (_confirm == action && Now <= _confirmUntil) { _confirm = null; return true; }
            _confirm = action; _confirmUntil = Now + 4f;
            return false;
        }

        [HideFromIl2Cpp]
        private void Kick(ulong id)
        {
            var p = Net.Get(id);
            if (_role != Role.Host || p == null) return;
            Protocol.SendSimple(id, Msg.Kick, SteamNet.SendReliable);
            SetStatus($"Removed {p.Name} from the game.");
            RemovePeer(id, true);
        }

        private void Leave()
        {
            foreach (var id in Net.Peers.Keys) Protocol.SendSimple(id, Msg.Bye, SteamNet.SendReliable);
            SetStatus(_role == Role.Host ? "Lobby closed." : "Left the game.");
            ResetSession();
        }

        [HideFromIl2Cpp]
        private int FriendRank(ulong id)
        {
            var (hosting, open) = FriendState(id);
            int mod = ModState(id);
            if (mod == 0 && hosting && open) return 0;
            if (mod == 0) return 1;
            if (mod == 3) return 2;
            return mod == 1 ? 3 : 4;
        }

        private const int FriendsPerPage = 8;
        private int _friendPage;

        private (bool hosting, bool open) FriendState(ulong id)
        {
            if (_beacons.TryGetValue(id, out var b) && Now - b.time < 5f) return (true, b.open);
            return (false, false);
        }

        [HideFromIl2Cpp]
        private int ModState(ulong id)
        {
            if (_modSeen.TryGetValue(id, out var m) && Now - m.time < 25f) return m.version == Protocol.Version ? 0 : 1;
            return Now - _steamReadyAt < 20f ? 3 : 2;
        }

        [HideFromIl2Cpp]
        private void FriendAction(int i)
        {
            if (i < 0 || i >= _friends.Count) return;
            var f = _friends[i];
            if (_role == Role.Client) return;
            if (Net.Peers.ContainsKey(f.Id)) return;
            var (hosting, open) = FriendState(f.Id);
            if (ModState(f.Id) == 2 || ModState(f.Id) == 1) { SetStatus(ModState(f.Id) == 1 ? $"{f.Name} has a different version of the mod." : $"{f.Name} doesn't have the co-op mod."); return; }
            if (_role == Role.None && hosting && open) Join(f.Id);
            else if (_role != Role.Client) Invite(f.Id);
        }

        [HideFromIl2Cpp]
        private void HandleKeys(float now)
        {
            if (_inviteFrom != 0)
            {
                if (KeyInput.Pressed(KeyY)) { AcceptInvite(); SetMenu(true, now); return; }
                if (KeyInput.Pressed(KeyN)) { DeclineInvite(); return; }
            }
            if (!_menuOpen) return;

            if (_view == View.Settings) { if (KeyInput.Pressed(KeyS)) _view = View.Main; return; }

            if (!_connected)
            {
                if (_role == Role.None && KeyInput.Pressed(KeyInput.H)) StartHosting();
                else if (_role != Role.None && KeyInput.Pressed(KeyInput.L)) Leave();
                return;
            }

            if (!_playing && KeyInput.Pressed(KeyR)) ToggleReady();
            if (KeyInput.Pressed(KeyC)) CycleColor(1);
            if (KeyInput.Pressed(KeyInput.L) && Confirmed("leave")) Leave();
            if (_role == Role.Host && KeyInput.Pressed(KeyS)) _view = View.Settings;
        }
    }
}
