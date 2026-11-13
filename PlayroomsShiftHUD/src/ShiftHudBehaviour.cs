using System;
using System.Collections.Generic;
using UnityEngine;

namespace ComfortCode.Playrooms.ShiftHUD
{
    public sealed class ShiftHudBehaviour : MonoBehaviour
    {
        public ShiftHudBehaviour(IntPtr ptr) : base(ptr) { }

        private readonly ShiftState _state = new ShiftState();
        private GUIStyle _heading, _line, _minor, _banner;
        private float _nextTick, _nextWarning, _nextLightWarning;
        private bool _updateReported, _guiReported;
        private GameObject _lightObject;
        private Light _light;

        private void Update()
        {
            try
            {
                if (!_updateReported)
                {
                    _updateReported = true;
                    Plugin.Logger?.LogInfo("Unity Update callback active (Comfort Shift HUD).");
                }
                float now = Time.realtimeSinceStartup;
                if (now >= _nextTick)
                {
                    _nextTick = now + 0.15f;
                    _state.Tick(now);
                }
                RefreshWorkLight(now);
            }
            catch (Exception e) { Warn(e); }
        }

        private void RefreshWorkLight(float now)
        {
            bool wanted = _state.ShiftEnded && Plugin.KeepLight.Value;
            if (!wanted)
            {
                RemoveLight();
                return;
            }
            Transform follow = null;
            try
            {
                var camera = Camera.main;
                follow = camera != null ? camera.transform : GameAccess.Player();
            }
            catch { }
            if (follow == null) return;

            try
            {
                if (_lightObject == null)
                {
                    _lightObject = new GameObject("ComfortShiftHUD_AfterShiftLocalLight");
                    _light = _lightObject.AddComponent<Light>();
                    _light.type = LightType.Point;
                    _light.color = Color.white;
                    _light.shadows = LightShadows.None;
                    Plugin.Logger?.LogInfo(
                        "Local after-shift worklight created. No game clock, save, network or scene light was changed.");
                }
                if (_light == null) return;
                _light.range = Mathf.Clamp(Plugin.WorkLightRange.Value, 5f, 75f);
                _light.intensity = Mathf.Clamp(Plugin.WorkLightIntensity.Value, 0.5f, 12f);
                _lightObject.transform.position = follow.position + new Vector3(0f, 0.5f, 0f);
            }
            catch (Exception e)
            {
                if (now >= _nextLightWarning)
                {
                    _nextLightWarning = now + 15f;
                    Plugin.Logger?.LogWarning(
                        "After-shift local worklight unavailable; END DAY banner remains available. " + e.Message);
                }
                RemoveLight();
            }
        }

        private void RemoveLight()
        {
            if (_lightObject == null) return;
            try { UnityEngine.Object.Destroy(_lightObject); }
            catch { }
            _lightObject = null;
            _light = null;
        }

        private void OnDestroy() => RemoveLight();

        private void PrepareStyles()
        {
            if (_heading != null) return;
            _heading = new GUIStyle(GUI.skin.label) { fontSize = 18, richText = true };
            _line = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true };
            _minor = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true };
            _banner = new GUIStyle(GUI.skin.label) {
                fontSize = 32, richText = true, alignment = TextAnchor.MiddleCenter
            };
        }

        private static string Safe(string value)
            => (value ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        private void OnGUI()
        {
            if (!_guiReported)
            {
                _guiReported = true;
                Plugin.Logger?.LogInfo("Unity OnGUI callback active (Comfort Shift HUD).");
            }
            // Do not hide the HUD when rooms have not yet been detected. That made
            // the previous ball tracker appear broken even though BepInEx loaded it.
            if (!Plugin.ShowHud.Value || !_state.InWorld) return;
            try
            {
                PrepareStyles();
                var names = _state.CleanedRoomNames(
                    Math.Max(1, Math.Min(16, Plugin.MaxRoomNames.Value)));
                const float width = 362f;
                float x = Plugin.HudRight.Value ?
                    Mathf.Max(12f, Screen.width - width - 18f) : 18f;
                float height = 185f + names.Count * 20f;
                if (_state.CleanedRooms > names.Count) height += 20f;
                const float y = 70f;
                if (Screen.width < 390f || Screen.height < 220f) return;

                var previous = GUI.color;
                GUI.color = new Color(0.04f, 0.055f, 0.105f, 0.87f);
                GUI.DrawTexture(new Rect(x, y, width, height), Texture2D.whiteTexture);
                GUI.color = new Color(0.41f, 0.91f, 0.80f, 1f);
                GUI.DrawTexture(new Rect(x, y, width, 3f), Texture2D.whiteTexture);
                GUI.color = previous;

                float ix = x + 14f;
                float iw = width - 26f;
                GUI.Label(new Rect(ix, y + 10f, iw, 26f),
                    "<b><color=#8DFFF0>COMFORT SHIFT HUD</color></b>", _heading);
                GUI.Label(new Rect(ix, y + 43f, iw, 24f),
                    "<b>Kamer:</b> " + Safe(_state.CurrentRoom), _line);
                GUI.Label(new Rect(ix, y + 68f, iw, 24f),
                    "<b>Ballen:</b> <color=#FFD777>" + Safe(_state.Balls) + "</color>", _line);
                GUI.Label(new Rect(ix, y + 93f, iw, 24f),
                    "<b>Shift:</b> " + (_state.ShiftEnded ?
                        "<color=#FFB266>AFGELOPEN</color>" :
                        "<color=#D8FFED>" + Safe(_state.ShiftTime) + "</color>"), _line);
                GUI.Label(new Rect(ix, y + 122f, iw, 24f),
                    "<b>Opgeruimd:</b> " + _state.CleanedRooms + " / " +
                    _state.CountedRooms + " kamers met ballen", _line);

                float nameY = y + 149f;
                if (names.Count == 0)
                {
                    GUI.Label(new Rect(ix, nameY, iw, 20f),
                        _state.RoomCount == 0 ? "Kamers zoeken..." :
                        _state.IsCounting ? "Kamers tellen..." : "Nog geen kamer volledig leeg",
                        _minor);
                }
                else
                {
                    foreach (var name in names)
                    {
                        string room = Safe(name);
                        if (room.Length > 45) room = room.Substring(0, 42) + "...";
                        GUI.Label(new Rect(ix, nameY, iw, 20f),
                            "<color=#9AF4B2>\u2713</color> " + room, _minor);
                        nameY += 20f;
                    }
                    if (_state.CleanedRooms > names.Count)
                        GUI.Label(new Rect(ix, nameY, iw, 20f),
                            "+ " + (_state.CleanedRooms - names.Count) + " andere kamers", _minor);
                }

                // Clear central END DAY notice is independent of the room scan.
                if (_state.ShiftEnded)
                {
                    float bannerWidth = Mathf.Min(410f, Screen.width - 40f);
                    float bx = (Screen.width - bannerWidth) / 2f;
                    float by = Mathf.Min(22f, Screen.height * 0.035f);
                    GUI.color = new Color(0.06f, 0.07f, 0.12f, 0.9f);
                    GUI.DrawTexture(new Rect(bx, by, bannerWidth, 65f), Texture2D.whiteTexture);
                    GUI.color = previous;
                    GUI.Label(new Rect(bx, by + 4f, bannerWidth, 55f),
                        "<b><color=#FFC887>END DAY</color></b>", _banner);
                }
            }
            catch (Exception ex) { Warn(ex); }
        }

        private void Warn(Exception e)
        {
            float now = Time.realtimeSinceStartup;
            if (now < _nextWarning) return;
            _nextWarning = now + 10f;
            Plugin.Logger?.LogWarning("Comfort Shift HUD callback: " + e);
        }
    }
}
