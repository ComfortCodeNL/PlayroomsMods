using System;
using UnityEngine;

namespace ComfortCode.Playrooms.ShiftHud
{
    public sealed class ShiftHudBehaviour : MonoBehaviour
    {
        public ShiftHudBehaviour(IntPtr ptr) : base(ptr) { }

        private ShiftSnapshot _snapshot = new ShiftSnapshot();
        private float _nextRead, _nextLightAttempt, _lastError;
        private GUIStyle _title, _normal, _small, _large;
        private bool _updateReported, _guiReported;

        private void Update()
        {
            if (!_updateReported)
            {
                _updateReported = true;
                Plugin.Logger?.LogInfo("[ShiftHUD] Unity Update callback active.");
            }
            try
            {
                float now = Time.realtimeSinceStartup;
                if (now >= _nextLightAttempt && !NightLightKeeper.Installed)
                {
                    _nextLightAttempt = now + 5f;
                    NightLightKeeper.TryInstall();
                }
                if (now < _nextRead) return;
                _nextRead = now + Mathf.Clamp(Plugin.RefreshInterval.Value, 0.25f, 5f);
                _snapshot = ShiftSnapshot.Capture();
            }
            catch (Exception ex) { Warn(ex); }
        }

        private void OnGUI()
        {
            if (!_guiReported)
            {
                _guiReported = true;
                Plugin.Logger?.LogInfo("[ShiftHUD] Unity OnGUI callback active.");
            }
            if (Plugin.ShowHud == null || !Plugin.ShowHud.Value) return;
            try { Draw(); }
            catch (Exception ex) { Warn(ex); }
        }

        private void Draw()
        {
            if (_normal == null)
            {
                _title = new GUIStyle(GUI.skin.label) { fontSize = 18, richText = true };
                _normal = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true };
                _small = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true };
                _large = new GUIStyle(GUI.skin.label) { fontSize = 35, richText = true, alignment = TextAnchor.MiddleCenter };
            }

            float width = Mathf.Min(400f, Screen.width - 30f);
            if (width < 230f) return;
            float x = 16f, y = 80f;
            int limit = Math.Max(1, Math.Min(25, Plugin.MaxRoomNames.Value));
            int show = Plugin.ShowCompletedRooms.Value ?
                Math.Min(limit, _snapshot.CompletedNames.Count) : 0;
            float height = 168f + (show > 0 ? 25f + 19f * show : 0f);
            if (Plugin.ShowCompletedRooms.Value && _snapshot.CompletedNames.Count > show) height += 18f;
            height = Mathf.Min(height, Screen.height - 100f);
            if (height < 135f) return;

            var prev = GUI.color;
            GUI.color = new Color(0.04f, 0.05f, 0.09f, 0.91f);
            GUI.DrawTexture(new Rect(x, y, width, height), Texture2D.whiteTexture);
            GUI.color = new Color(0.39f, 0.85f, 0.92f, 1f);
            GUI.DrawTexture(new Rect(x, y, width, 3f), Texture2D.whiteTexture);
            GUI.color = prev;

            float left = x + 14f, inner = width - 28f;
            GUI.Label(new Rect(left, y + 9f, inner, 27f), "<b><color=#8FEAFF>COMFORT SHIFT HUD</color></b>", _title);

            string room = Escape(Short(_snapshot.CurrentRoom, 46));
            string balls = _snapshot.CurrentRemaining < 0 ? "onbekend" :
                _snapshot.CurrentRemaining.ToString("N0") + " / " + _snapshot.CurrentTotal.ToString("N0");
            GUI.Label(new Rect(left, y + 40f, inner, 23f), "Kamer: <b>" + room + "</b>", _normal);
            GUI.Label(new Rect(left, y + 67f, inner, 23f), "Ballen over: <color=#FFD66B><b>" + balls + "</b></color>", _normal);
            GUI.Label(new Rect(left, y + 94f, inner, 23f),
                "Shift eindigt over: <color=#8FEAFF><b>" + _snapshot.ShiftLabel + "</b></color>", _normal);
            GUI.Label(new Rect(left, y + 121f, inner, 23f),
                "Kamers opgeruimd: <color=#A8F0B8><b>" + _snapshot.CompletedCount +
                " / " + _snapshot.RoomCount + "</b></color>", _normal);

            if (show > 0)
            {
                GUI.Label(new Rect(left, y + 152f, inner, 20f), "Voltooide kamers:", _small);
                for (int i = 0; i < show; i++)
                    GUI.Label(new Rect(left + 7f, y + 175f + i * 19f, inner - 7f, 19f),
                        "<color=#A8F0B8>✓</color> " + Escape(Short(_snapshot.CompletedNames[i], 49)), _small);
                if (_snapshot.CompletedNames.Count > show)
                    GUI.Label(new Rect(left + 7f, y + 175f + show * 19f, inner - 7f, 19f),
                        "... en " + (_snapshot.CompletedNames.Count - show) + " meer", _small);
            }

            if (_snapshot.ShiftEnded)
            {
                float w = Mathf.Min(Screen.width - 30f, 520f);
                float bx = (Screen.width - w) * 0.5f, by = Mathf.Max(20f, Screen.height * 0.16f);
                GUI.color = new Color(0.04f, 0.05f, 0.09f, 0.90f);
                GUI.DrawTexture(new Rect(bx, by, w, 78f), Texture2D.whiteTexture);
                GUI.color = prev;
                GUI.Label(new Rect(bx + 8f, by + 8f, w - 16f, 60f),
                    "<b><color=#FFE48C>END DAY</color></b>", _large);
            }
            GUI.color = prev;
        }

        private static string Short(string value, int max)
            => string.IsNullOrEmpty(value) ? "Onbekend" :
               value.Length > max ? value.Substring(0, max - 3) + "..." : value;

        private static string Escape(string value)
            => (value ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        private void Warn(Exception ex)
        {
            float now = Time.realtimeSinceStartup;
            if (now < _lastError) return;
            _lastError = now + 15f;
            Plugin.Logger?.LogWarning("[ShiftHUD] " + ex);
        }
    }
}
