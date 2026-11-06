using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace PlayroomsCoop
{
    internal static class KeyInput
    {
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private static readonly bool[] _down = new bool[256];
        private static readonly bool[] _pressed = new bool[256];

        public const int Escape = 0x1B;
        public const int Key0 = 0x30;
        public const int H = 0x48;
        public const int L = 0x4C;

        public static void Poll(bool focused, int[] keys)
        {
            foreach (int k in keys)
            {
                if (k <= 0 || k > 255) continue;
                bool now = focused && (GetAsyncKeyState(k) & 0x8000) != 0;
                _pressed[k] = now && !_down[k];
                _down[k] = now;
            }
        }

        public static bool IsDown(int vk) => vk > 0 && vk < 256 && (GetAsyncKeyState(vk) & 0x8000) != 0;

        public static bool Pressed(int vk) => vk > 0 && vk < 256 && _pressed[vk];
    }

    internal static class MenuInput
    {
        private static readonly string[] LockNames = { "LookBehaviour", "MoveBehaviour", "InteractBehaviour", "ToolBehaviour", "JumpBehaviour" };
        private static readonly List<(object behaviour, bool wasActive)> _locked = new List<(object, bool)>();
        private static bool _active;
        private static object _prevLock; private static bool _prevVisible; private static bool _savedCursor;
        private static bool _loggedOnce;

        private static string ClassName(object o)
        {
            var il2cpp = R.T("Il2CppInterop.Runtime.IL2CPP");
            var klass = (IntPtr)R.CallStatic(il2cpp, "il2cpp_object_get_class", R.Ptr(o));
            var name = (IntPtr)R.CallStatic(il2cpp, "il2cpp_class_get_name", klass);
            return System.Runtime.InteropServices.Marshal.PtrToStringAnsi(name);
        }

        private static void FindBehaviours()
        {
            _locked.Clear();
            object ctrl = null;
            foreach (var c in R.FindAll(R.T("TwoPixels.Player.PlayerBehaviourController"))) { ctrl = c; break; }
            if (ctrl == null) return;
            var coll = R.Get(ctrl, "Behaviours");
            List<object> items;
            try { items = new List<object>(); foreach (var (k, v) in R.Pairs(coll)) items.Add(v ?? k); }
            catch { items = R.Items(coll); }
            foreach (var it in items)
            {
                if (it == null) continue;
                string name;
                try { name = ClassName(it); } catch { continue; }
                if (Array.IndexOf(LockNames, name) < 0) continue;
                var t = R.T("TwoPixels.Player." + name);
                if (t == null) continue;
                var typed = Activator.CreateInstance(t, R.Ptr(it));
                bool was = true;
                try { was = R.Get<bool>(typed, "IsBehaviourActive"); } catch { }
                _locked.Add((typed, was));
            }
            if (!_loggedOnce) { _loggedOnce = true; Plugin.Log.LogInfo($"Menu: pausing {_locked.Count} player controls while open"); }
        }

        public static object FindPlayerBehaviour(string name)
        {
            object ctrl = null;
            foreach (var c in R.FindAll(R.T("TwoPixels.Player.PlayerBehaviourController"))) { ctrl = c; break; }
            if (ctrl == null) return null;
            var coll = R.Get(ctrl, "Behaviours");
            List<object> items;
            try { items = new List<object>(); foreach (var (k, v) in R.Pairs(coll)) items.Add(v ?? k); }
            catch { items = R.Items(coll); }
            foreach (var it in items)
            {
                if (it == null) continue;
                try
                {
                    if (ClassName(it) != name) continue;
                    var t = R.T("TwoPixels.Player." + name);
                    return t == null ? null : Activator.CreateInstance(t, R.Ptr(it));
                }
                catch { }
            }
            return null;
        }

        private static void ClearInputs()
        {
            foreach (var (b, _) in _locked)
            {
                foreach (var field in new[] { "_lookInput", "_movementInput", "_lerpedMovementInput" })
                {
                    try
                    {
                        if (!R.Has(b, field)) continue;
                        var cur = R.Get(b, field);
                        if (cur != null) R.Set(b, field, Activator.CreateInstance(cur.GetType()));
                    }
                    catch { }
                }
            }
        }

        private static object _inputService;
        private static string _prevInput;
        private static bool _switchedInput;
        private static readonly string[] CameraScripts = { "LookWithMouse", "sCameraControlOrbit", "Unity.Cinemachine.CinemachineInputAxisController", "Cinemachine.CinemachineInputProvider" };
        private static readonly List<object> _pausedCams = new List<object>();
        private static float _openedAt, _lastForce;
        private static bool _forcedCursor;
        private static string _uiName;
        private static bool _worldAtOpen, _worldChanged;
        private static float _worldSince = -1f, _nextTrack;

        private static void SwitchToUi()
        {
            _switchedInput = false;
            if (!World.Alive(_inputService))
            {
                _inputService = null;
                foreach (var s in R.FindAll(R.T("TwoPixels.Services.InputService.InputService"))) { _inputService = s; break; }
            }
            if (_inputService == null) return;
            _prevInput = R.Get<string>(_inputService, "ActivePlayerInputName");
            string ui = null;
            foreach (var b in R.FindAll(R.T("TwoPixels.Whiteboard.Whiteboard")))
            {
                try { ui = R.Get<string>(b, "uiInputName"); } catch { }
                if (!string.IsNullOrEmpty(ui)) break;
            }
            if (string.IsNullOrEmpty(ui)) ui = "UI";
            _uiName = ui;
            if (ui == _prevInput) return;
            _switchedInput = Convert.ToBoolean(R.Call(_inputService, "EnablePlayerInput", ui));
        }

        private static readonly List<object> _pausedEvents = new List<object>();
        private static float _lastEventScan;

        private static void PauseGameUi()
        {
            _lastEventScan = UnityEngine.Time.realtimeSinceStartup;
            foreach (var tn in new[] { "UnityEngine.EventSystems.EventSystem", "UnityEngine.InputSystem.UI.InputSystemUIInputModule" })
            {
                var t = R.T(tn);
                if (t == null) continue;
                foreach (var c in R.FindAll(t))
                {
                    try
                    {
                        var b = (Behaviour)c;
                        if (!b.enabled) continue;
                        b.enabled = false;
                        bool known = false;
                        foreach (var p in _pausedEvents) if (R.Same(p, c)) { known = true; break; }
                        if (!known) _pausedEvents.Add(c);
                    }
                    catch { }
                }
            }
        }

        private static void ResumeGameUi()
        {
            for (int pass = 0; pass < 2; pass++)
                foreach (var c in _pausedEvents)
                {
                    try
                    {
                        bool isSystem = R.Has(c, "currentSelectedGameObject");
                        if ((pass == 0) == isSystem) continue;
                        if (World.Alive(c)) ((Behaviour)c).enabled = true;
                    }
                    catch { }
                }
            _pausedEvents.Clear();
        }

        private static void PauseMenuCameras()
        {
            _pausedCams.Clear();
            foreach (var tn in CameraScripts)
            {
                var t = R.T(tn);
                if (t == null) continue;
                foreach (var c in R.FindAll(t))
                {
                    try { var b = (Behaviour)c; if (b.enabled) { b.enabled = false; _pausedCams.Add(c); } } catch { }
                }
            }
        }

        public static void Begin()
        {
            if (_active) return;
            _active = true;
            _openedAt = UnityEngine.Time.realtimeSinceStartup;
            _forcedCursor = false;
            _worldChanged = false; _worldSince = -1f;
            try { _worldAtOpen = World.InGameWorld(); } catch { _worldAtOpen = false; }
            try { SwitchToUi(); } catch (Exception e) { Plugin.Log.LogWarning("Couldn't switch to menu controls: " + e.Message); }
            try
            {
                FindBehaviours();
                foreach (var (b, was) in _locked)
                    if (was) { try { R.Call(b, "SetBehaviourActive", false, false); } catch { } }
                ClearInputs();
            }
            catch (Exception e) { Plugin.Log.LogWarning("Couldn't pause the player's controls for the menu: " + e.Message); }
            try { PauseMenuCameras(); } catch { }
            try { PauseGameUi(); } catch (Exception e) { Plugin.Log.LogWarning("Couldn't pause the game's menus: " + e.Message); }
            try
            {
                var cursor = R.T("UnityEngine.Cursor");
                if (cursor != null)
                {
                    _prevLock = R.GetStatic(cursor, "lockState");
                    _prevVisible = Convert.ToBoolean(R.GetStatic(cursor, "visible"));
                    _savedCursor = true;
                }
            }
            catch { }
        }

        public static void Hold()
        {
            if (!_active) return;
            ClearInputs();
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now - _lastEventScan > 0.5f) { try { PauseGameUi(); } catch { } }
            if (now >= _nextTrack) { _nextTrack = now + 0.25f; try { TrackWorldChange(now); } catch { } }
            if (now - _openedAt < 0.3f || now - _lastForce < 1f) return;
            try
            {
                var cursor = R.T("UnityEngine.Cursor");
                if (cursor == null) return;
                if (Convert.ToInt32(R.GetStatic(cursor, "lockState")) == 0 && Convert.ToBoolean(R.GetStatic(cursor, "visible"))) return;
                _lastForce = now; _forcedCursor = true;
                R.SetStatic(cursor, "lockState", 0);
                R.SetStatic(cursor, "visible", true);
            }
            catch { }
        }

        private static void TrackWorldChange(float now)
        {
            if (World.Alive(_inputService) && !string.IsNullOrEmpty(_uiName))
            {
                string act = R.Get<string>(_inputService, "ActivePlayerInputName");
                if (!string.IsNullOrEmpty(act) && act != _uiName)
                {
                    _prevInput = act;
                    _switchedInput = Convert.ToBoolean(R.Call(_inputService, "EnablePlayerInput", _uiName));
                }
            }
            bool inWorld = World.InGameWorld();
            if (inWorld == _worldAtOpen) { _worldSince = -1f; return; }
            if (_worldSince < 0f) { _worldSince = now; return; }
            if (now - _worldSince < 1f) return;
            _worldAtOpen = inWorld; _worldChanged = true; _worldSince = -1f;
            FindBehaviours();
            foreach (var (b, was) in _locked)
                if (was) { try { R.Call(b, "SetBehaviourActive", false, false); } catch { } }
            ClearInputs();
        }

        private static readonly int[] MoveKeys = { 0x57, 0x41, 0x53, 0x44, 0x5A, 0x51, 0x25, 0x26, 0x27, 0x28 };
        private static object _move; private static float _nextMoveFind, _nextGuard, _strayFor;
        private static int _cleared;

        public static void StuckKeyGuard(float now)
        {
            if (now < _nextGuard) return;
            _nextGuard = now + 0.1f;
            try
            {
                if (!World.InGameWorld()) { _strayFor = 0f; return; }
                if (!World.Alive(_move))
                {
                    _move = null;
                    if (now < _nextMoveFind) return;
                    _nextMoveFind = now + 3f;
                    bool logged = _loggedOnce; _loggedOnce = true;
                    FindBehaviours();
                    _loggedOnce = logged;
                    foreach (var (b, _) in _locked) if (ClassName(b) == "MoveBehaviour") { _move = b; break; }
                    _locked.Clear();
                    if (_move == null) return;
                }
                if (World.Alive(_inputService) || FindInputService())
                {
                    try { if (Convert.ToBoolean(R.Get(_inputService, "IsUsingGamepad"))) { _strayFor = 0f; return; } } catch { }
                }
                foreach (int k in MoveKeys) if (KeyInput.IsDown(k)) { _strayFor = 0f; return; }
                var v = R.Get(_move, "_movementInput");
                if (v == null) return;
                float x = R.Get<float>(v, "x"), y = R.Get<float>(v, "y");
                if (Math.Abs(x) < 0.01f && Math.Abs(y) < 0.01f) { _strayFor = 0f; return; }
                _strayFor += 0.1f;
                if (_strayFor < 0.25f) return;
                _strayFor = 0f;
                R.Set(_move, "_movementInput", Activator.CreateInstance(v.GetType()));
                if (_cleared++ < 5) Plugin.Log.LogInfo($"Cleared a stuck movement key (the game still had ({x:0.00}, {y:0.00}) with no key held)");
            }
            catch { }
        }

        private static bool FindInputService()
        {
            foreach (var s in R.FindAll(R.T("TwoPixels.Services.InputService.InputService"))) { _inputService = s; return true; }
            return false;
        }

        public static void End()
        {
            if (!_active) return;
            _active = false;
            ClearInputs();
            foreach (var (b, was) in _locked)
                if (was) { try { R.Call(b, "SetBehaviourActive", true, false); } catch { } }
            _locked.Clear();
            foreach (var c in _pausedCams) { try { if (World.Alive(c)) ((Behaviour)c).enabled = true; } catch { } }
            _pausedCams.Clear();
            try { ResumeGameUi(); } catch { }
            try
            {
                if (_switchedInput && World.Alive(_inputService) && !string.IsNullOrEmpty(_prevInput))
                    R.Call(_inputService, "EnablePlayerInput", _prevInput);
            }
            catch { }
            _switchedInput = false;
            try
            {
                var cursor = R.T("UnityEngine.Cursor");
                if (cursor != null && _worldChanged)
                {
                    bool game = World.InGameWorld() && _prevInput != _uiName;
                    R.SetStatic(cursor, "lockState", game ? 1 : 0);
                    R.SetStatic(cursor, "visible", !game);
                }
                else if (cursor != null && _savedCursor && _forcedCursor)
                {
                    R.SetStatic(cursor, "lockState", _prevLock);
                    R.SetStatic(cursor, "visible", _prevVisible);
                }
            }
            catch { }
        }
    }
}
