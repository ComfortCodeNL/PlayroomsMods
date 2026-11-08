using System;
using System.Collections.Generic;

namespace PlayroomsNewGamePlus;

internal static class Modal
{
    private static readonly string[] UiTypes = { "UnityEngine.EventSystems.EventSystem", "UnityEngine.InputSystem.UI.InputSystemUIInputModule" };

    private static readonly List<object> _paused = new();
    private static bool _active, _savedCursor, _prevVisible, _worldAtOpen;
    private static object _prevLock;
    private static float _openedAt, _lastScan, _lastForce;

    public static bool Active => _active;

    public static void Want(bool modal)
    {
        if (modal && !_active) Begin();
        else if (!modal && _active) End();
        else if (_active) Hold();
    }

    private static void Begin()
    {
        _active = true;
        _openedAt = Game.Now;
        try { _worldAtOpen = Game.InWorld(); } catch { _worldAtOpen = false; }
        try
        {
            var c = R.T("UnityEngine.Cursor");
            if (c != null)
            {
                _prevLock = R.GetStatic(c, "lockState");
                _prevVisible = Convert.ToBoolean(R.GetStatic(c, "visible"));
                _savedCursor = true;
            }
        }
        catch { }
        PauseGameUi();
    }

    private static void Hold()
    {
        float now = Game.Now;
        if (now - _lastScan > 0.5f) PauseGameUi();
        if (now - _openedAt < 0.3f || now - _lastForce < 1f) return;
        try
        {
            var c = R.T("UnityEngine.Cursor");
            if (c != null && (Convert.ToInt32(R.GetStatic(c, "lockState")) != 0 || !Convert.ToBoolean(R.GetStatic(c, "visible"))))
            {
                _lastForce = now;
                R.SetStatic(c, "lockState", 0);
                R.SetStatic(c, "visible", true);
            }
        }
        catch { }
    }

    public static void End()
    {
        if (!_active && _paused.Count == 0) return;
        _active = false;
        ResumeGameUi();
        try
        {
            var c = R.T("UnityEngine.Cursor");
            if (c == null) return;
            bool inWorld = false;
            try { inWorld = Game.InWorld(); } catch { }
            if (inWorld != _worldAtOpen)
            {
                bool play = inWorld && !Game.TerminalOpen();
                R.SetStatic(c, "lockState", play ? 1 : 0);
                R.SetStatic(c, "visible", !play);
            }
            else if (_savedCursor)
            {
                R.SetStatic(c, "lockState", _prevLock);
                R.SetStatic(c, "visible", _prevVisible);
            }
        }
        catch { }
        _savedCursor = false;
    }

    private static void PauseGameUi()
    {
        _lastScan = Game.Now;
        foreach (var name in UiTypes)
        {
            var t = R.T(name);
            if (t == null) continue;
            foreach (var b in R.FindAll(t))
            {
                try
                {
                    if (!Convert.ToBoolean(R.Get(b, "enabled"))) continue;
                    R.Set(b, "enabled", false);
                    if (!_paused.Exists(p => R.Ptr(p) == R.Ptr(b))) _paused.Add(b);
                }
                catch { }
            }
        }
    }

    private static void ResumeGameUi()
    {
        // input modules first, then event systems (same order as Co-op)
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (var b in _paused)
            {
                try
                {
                    bool isEventSystem = R.Has(b, "currentSelectedGameObject");
                    if ((pass == 0) != isEventSystem && Game.Alive(b)) R.Set(b, "enabled", true);
                }
                catch { }
            }
        }
        _paused.Clear();
    }
}
