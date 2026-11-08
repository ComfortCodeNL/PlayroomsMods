using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;

namespace PlayroomsCoop
{
    public partial class CoopBehaviour
    {
        private void OnGUI()
        {
            try { DrawGui(); }
            catch (Exception e)
            {
                if (Now - _lastErrorLog > 5f) { _lastErrorLog = Now; Plugin.Log.LogError(e.ToString()); }
            }
        }

        private const float PanelX = 30f, PanelW = 600f, Pad = 16f;
        private float _y;
        private float _panelH = 200f;

        private void DrawGui()
        {
            if (!_guiSeen) { _guiSeen = true; Plugin.Log.LogInfo("Co-op UI active (OnGUI running)"); }
            if (_textStyle == null)
            {
                _boxStyle = new GUIStyle(GUI.skin.box);
                _textStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, richText = true };
                _smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true };
                _nameStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true };
                _buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 14, richText = true };
            }

            if (_connected && Plugin.ShowNames.Value && _cam != null)
            {
                foreach (var kv in _avatars)
                {
                    var av = kv.Value; var p = Net.Get(kv.Key);
                    if (p == null || !av.Alive || !av.HasPose) continue;
                    Vector3 sp = _cam.WorldToScreenPoint(av.HeadPosition + Vector3.up * 0.45f);
                    if (sp.z <= 0f || sp.z >= 60f) continue;
                    string hex = ColorHex(av.Color);
                    string tag = $"{av.Label} {p.Name}";
                    float w = 9f * tag.Length;
                    GUI.Label(new Rect(sp.x - w * 0.5f, Screen.height - sp.y - 14f, w + 40f, 28f),
                        $"<b><color=#{hex}>{av.Label}</color></b> {p.Name}", _nameStyle);
                }
            }

            if (Now < _noticeUntil && !string.IsNullOrEmpty(_notice))
                GUI.Label(new Rect(20f, Screen.height * 0.25f, 900f, 30f), "<b><color=#FFD866>" + _notice + "</color></b>", _textStyle);

            if (_inviteFrom != 0 && !_menuOpen)
            {
                const float ix = 30f, iy = 30f;
                var r = new Rect(ix, iy, 520f, 64f);
                GUI.Box(r, "", _boxStyle); GUI.Box(r, "", _boxStyle);
                GUI.Label(new Rect(ix + 14f, iy + 8f, 500f, 24f), $"<b>{SteamNet.NameOf(_inviteFrom)}</b> invited you to play co-op", _textStyle);
                GUI.Label(new Rect(ix + 14f, iy + 34f, 500f, 24f), $"<b>[Y]</b> Accept    <b>[N]</b> Decline    <size=12>({KeyName(Plugin.MenuKey.Value)} to open the menu)</size>", _smallStyle);
            }

            if (!_menuOpen)
            {
                if (Now < _toastUntil && !string.IsNullOrEmpty(_status) && _inviteFrom == 0)
                    GUI.Label(new Rect(20f, 20f, 900f, 28f), "<b>Co-op:</b> " + _status, _textStyle);
                return;
            }

            DrawPanel();
        }

        private void DrawPanel()
        {
            var bg = new Rect(PanelX, 30f, PanelW, _panelH);
            GUI.Box(bg, "", _boxStyle); GUI.Box(bg, "", _boxStyle); GUI.Box(bg, "", _boxStyle);
            _y = 30f + 12f;

            Line("<b><size=20>PLAYROOMS CO-OP</size></b>", 30f);
            if (GUI.Button(new Rect(PanelX + PanelW - Pad - 34f, 40f, 34f, 26f), "X", _buttonStyle)) SetMenu(false, Now);
            if (!SteamNet.Ready) { Line("Waiting for Steam..."); End(); return; }
            Line($"<size=13>Signed in as <b>{SteamNet.MyName}</b>     {KeyName(Plugin.MenuKey.Value)} / Esc closes</size>", 22f);
            if (!string.IsNullOrEmpty(_status)) Line($"<color=#FFD866>{_status}</color>");
            Gap(6f);

            if (_inviteFrom != 0) DrawInvite();

            if (_view == View.Settings) DrawSettings();
            else if (!_connected) DrawBrowser();
            else DrawLobby();
            End();
        }

        private void DrawInvite()
        {
            Line($"<b>{SteamNet.NameOf(_inviteFrom)}</b> invited you to play co-op.");
            if (Btn(0, 170f, "Accept  <b>[Y]</b>")) AcceptInvite();
            if (Btn(180f, 170f, "Decline  <b>[N]</b>")) DeclineInvite();
            NextRow(); Gap(8f);
        }

        private void DrawBrowser()
        {
            if (_role == Role.None)
            {
                if (Btn(0, 220f, "Host a game  <b>[H]</b>")) StartHosting();
                NextRow();
                Line("<size=13>Up to 4 players. The host is Player 1; others get Players 2 to 4 as they join.</size>", 22f);
            }
            else if (_role == Role.Host)
            {
                Line("<b>Lobby open.</b> Up to 3 friends can join from their list, or invite them below.");
                if (_invitedFriend != 0) Line($"<size=13>Waiting for {SteamNet.NameOf(_invitedFriend)} to answer your invite...</size>", 22f);
                if (Btn(0, 220f, "Close lobby  <b>[L]</b>")) Leave();
                NextRow();
            }
            else
            {
                Line($"Joining <b>{SteamNet.NameOf(_joinTarget)}</b>...");
                if (Btn(0, 220f, "Cancel  <b>[L]</b>")) Leave();
                NextRow();
            }
            Gap(10f);
            DrawFriendList();
        }

        private void DrawFriendList()
        {
            Line("<b>Friends playing Playrooms</b>");
            if (_friends.Count == 0)
            {
                Line("<size=13><i>None right now. Your friend needs the mod and has to be in Playrooms.</i></size>", 22f);
                return;
            }
            int pages = (_friends.Count + FriendsPerPage - 1) / FriendsPerPage;
            _friendPage = Math.Max(0, Math.Min(_friendPage, pages - 1));
            int first = _friendPage * FriendsPerPage;
            for (int i = first; i < _friends.Count && i < first + FriendsPerPage; i++)
            {
                var f = _friends[i];
                var (hosting, open) = FriendState(f.Id);
                int mod = ModState(f.Id);
                string state = mod == 2 ? "<color=#888888>no mod</color>"
                             : mod == 1 ? "<color=#FFB066>different mod version</color>"
                             : hosting ? (open ? "<color=#7CFC7C>HOSTING</color>" : "<color=#BBBBBB>in a game</color>")
                             : mod == 3 ? "<color=#BBBBBB>checking...</color>" : "<color=#BBBBBB>has the mod</color>";
                GUI.Label(new Rect(PanelX + Pad, _y + 3f, 330f, 26f), $"{f.Name}   <size=13>{state}</size>", _textStyle);

                string action = null;
                if (mod == 1 || mod == 2 || Net.Peers.ContainsKey(f.Id)) action = null;
                else if (_role == Role.None && hosting && open) action = "Join";
                else if (_role != Role.Client && !(hosting && !open)) action = _invitedFriend == f.Id ? "Invited" : "Invite";
                if (action != null && GUI.Button(new Rect(PanelX + PanelW - Pad - 120f, _y, 120f, 28f), action, _buttonStyle))
                    FriendAction(i);
                _y += 32f;
            }
            if (pages > 1)
            {
                Gap(2f);
                if (_friendPage > 0 && Btn(0, 110f, "< Previous")) _friendPage--;
                GUI.Label(new Rect(PanelX + Pad + 120f, _y + 4f, 130f, 26f), $"<size=13>Page {_friendPage + 1} of {pages}</size>", _textStyle);
                if (_friendPage < pages - 1 && Btn(250f, 110f, "Next >")) _friendPage++;
                _rowH = 34f;
                NextRow();
            }
        }

        private void DrawLobby()
        {
            var rows = new List<(ulong id, int slot, string name, bool ready, Color color)>
            {
                (Net.Me, Net.MySlot, SteamNet.MyName, _meReady, Net.ColorFor(Net.MySlot, Net.MyColor))
            };
            foreach (var p in Net.Peers.Values) rows.Add((p.Id, p.Slot, p.Name, p.Ready, Net.ColorFor(p.Slot, p.Color)));
            rows.Sort((a, b) => a.slot.CompareTo(b.slot));

            Line(_playing ? $"<b><size=17>Playing</size></b>  <size=13>({rows.Count} of {Net.MaxPlayers})</size>" : $"<b><size=17>Lobby</size></b>  <size=13>({rows.Count} of {Net.MaxPlayers})</size>", 26f);
            foreach (var row in rows)
            {
                bool me = row.id == Net.Me;
                string tag = _playing ? (me ? "<color=#BBBBBB>you</color>" : "") : ReadyTag(row.ready) + (me ? "  <color=#BBBBBB>(you)</color>" : "");
                GUI.Label(new Rect(PanelX + Pad, _y + 2f, 400f, 28f),
                    $"<size=22><color=#{ColorHex(row.color)}>■</color></size>  <b>Player {row.slot + 1}</b>  {row.name}   {tag}", _textStyle);
                if (_role == Role.Host && !me)
                {
                    string key = "kick" + row.id;
                    if (GUI.Button(new Rect(PanelX + PanelW - Pad - 130f, _y, 130f, 28f), _confirm == key ? "<color=#FF7070>Confirm kick</color>" : "Kick", _buttonStyle) && Confirmed(key)) Kick(row.id);
                }
                _y += 32f;
            }
            Gap(4f);

            var mine = Net.ColorFor(Net.MySlot, Net.MyColor);
            GUI.Label(new Rect(PanelX + Pad, _y + 4f, 110f, 28f), "Your colour", _textStyle);
            if (GUI.Button(new Rect(PanelX + Pad + 115f, _y, 34f, 30f), "<", _buttonStyle)) CycleColor(-1);
            if (GUI.Button(new Rect(PanelX + Pad + 153f, _y, 46f, 30f), $"<size=26><color=#{ColorHex(mine)}>■</color></size>", _buttonStyle)) CycleColor(1);
            if (GUI.Button(new Rect(PanelX + Pad + 203f, _y, 34f, 30f), ">", _buttonStyle)) CycleColor(1);
            GUI.Label(new Rect(PanelX + Pad + 245f, _y + 6f, 300f, 26f), "<size=13>click the square or press <b>[C]</b></size>", _textStyle);
            _rowH = 34f; NextRow();

            if (!_playing)
            {
                Gap(2f);
                if (HostSave.Enabled)
                {
                    Line("<size=13><color=#9FD8FF>Everyone plays in the host's world. Each client's own save is set aside</color></size>", 20f);
                    Line("<size=13><color=#9FD8FF>and put back after the session, untouched.</color></size>", 22f);
                }
                else
                {
                    Line("<size=13><color=#FFB066>Before you start: back up your saves. Clients' money is replaced by the host's,</color></size>", 20f);
                    Line("<size=13><color=#FFB066>and all worlds' progress is merged (balls, pits, upgrades, unlocks).</color></size>", 22f);
                }
                if (_role == Role.Host && rows.Count < Net.MaxPlayers) Line("<size=13>More friends can still join: invite them from the list below.</size>", 22f);
                Gap(4f);
            }

            float x = 0f;
            if (!_playing) { if (Btn(0, 200f, _meReady ? "Not ready  <b>[R]</b>" : "<b>Ready</b>  <b>[R]</b>")) ToggleReady(); x = 210f; }
            if (Btn(x, 150f, _confirm == "leave" ? "<color=#FF7070>Confirm leave</color>" : "Leave  <b>[L]</b>") && Confirmed("leave")) Leave();
            NextRow();
            if (_confirm != null) Line("<size=13>Press again to confirm (Esc to cancel).</size>", 22f);

            if (_role == Role.Host)
            {
                Gap(4f);
                if (Btn(0, 200f, "Host settings  <b>[S]</b>")) _view = View.Settings;
                NextRow();
                if (rows.Count < Net.MaxPlayers) { Gap(6f); DrawFriendList(); }
            }
        }

        private void DrawSettings()
        {
            Line("<b><size=17>Host settings</size></b>", 26f);
            Line("<color=#FFB066><b>This feature isn't finished yet.</b></color>");
            Line("<size=13>Lobby settings will come in a later update. For now, co-op options</size>", 20f);
            Line("<size=13>(shared wallet, scare amount, what gets shared) are in the config file:</size>", 20f);
            Line("<size=13>BepInEx\\config\\com.playroomscoop.mod.cfg</size>", 24f);
            Gap(6f);
            if (Btn(0, 160f, "Back  <b>[S]</b>")) _view = View.Main;
            NextRow();
        }

        private static string ReadyTag(bool ready) => ready ? "<color=#7CFC7C><b>READY</b></color>" : "<color=#BBBBBB>not ready</color>";

        [HideFromIl2Cpp]
        private void Line(string text, float h = 24f)
        {
            GUI.Label(new Rect(PanelX + Pad, _y, PanelW - Pad * 2f, h + 4f), text, _textStyle);
            _y += h;
        }

        private void Gap(float h) => _y += h;

        private float _rowH;

        [HideFromIl2Cpp]
        private bool Btn(float x, float w, string label)
        {
            _rowH = 34f;
            return GUI.Button(new Rect(PanelX + Pad + x, _y, w, 30f), label, _buttonStyle);
        }

        private void NextRow() { _y += _rowH > 0f ? _rowH : 34f; _rowH = 0f; }

        private void End() { _panelH = _y - 30f + 12f; }

        private static string ColorHex(Color c) =>
            $"{(int)(Mathf.Clamp01(c.r) * 255):X2}{(int)(Mathf.Clamp01(c.g) * 255):X2}{(int)(Mathf.Clamp01(c.b) * 255):X2}";

        private static string KeyName(int vk) => vk >= 0x70 && vk <= 0x7B ? "F" + (vk - 0x6F) : "0x" + vk.ToString("X2");
    }
}
