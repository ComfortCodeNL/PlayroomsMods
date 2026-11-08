using System;
using System.Reflection;

namespace PlayroomsNewGamePlus;

internal static class Coop
{
    private static float _checkedAt = -99f;
    private static bool _present, _connected, _isClient, _isHost;
    private static Type _api;
    private static bool _apiLooked;

    private static Type Api
    {
        get
        {
            if (!_apiLooked || _api == null)
            {
                _apiLooked = true;
                _api = R.T("PlayroomsCoop.CoopApi");
            }
            return _api;
        }
    }

    public static bool HasApi => Api != null;

    public static bool Present { get { Refresh(); return _present; } }

    public static bool Connected { get { Refresh(); return _connected; } }

    public static bool IsClient { get { Refresh(); return _isClient; } }

    public static bool IsHost { get { Refresh(); return _isHost; } }

    public static bool? EveryoneInMenu
    {
        get
        {
            if (Api == null) return null;
            try { return Convert.ToBoolean(R.GetStatic(Api, "EveryoneInMenu")); } catch { return null; }
        }
    }

    public static bool ReplayingPurchase
    {
        get
        {
            try
            {
                if (Api != null) return Convert.ToBoolean(R.GetStatic(Api, "ReplayingPurchase"));
                var t = R.T("PlayroomsCoop.Upgrades");
                return t != null && Convert.ToBoolean(R.GetStatic(t, "_replayingPurchase"));
            }
            catch { return false; }
        }
    }

    public static bool Notify(string text)
    {
        try
        {
            if (Api != null) { R.CallStatic(Api, "Notify", text); return true; }
            var t = R.T("PlayroomsCoop.CoopBehaviour");
            if (t == null) return false;
            R.CallStatic(t, "Notify", text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryTakeCounter(string key, int limit, Action<bool> done)
    {
        if (Api == null || !Connected) return false;
        try
        {
            var m = Api.GetMethod("TakeCounter", BindingFlags.Public | BindingFlags.Static);
            if (m == null) return false;
            m.Invoke(null, new object[] { key, limit, done });
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning((object)("Co-op counter unavailable: " + ((ex as TargetInvocationException)?.InnerException ?? ex).Message));
            return false;
        }
    }

    private static void Refresh()
    {
        float now = Game.Now;
        if (now - _checkedAt < 0.5f) return;
        _checkedAt = now;
        _connected = _isClient = _isHost = false;

        if (Api != null)
        {
            _present = true;
            try
            {
                _connected = Convert.ToBoolean(R.GetStatic(Api, "Connected"));
                _isHost = Convert.ToBoolean(R.GetStatic(Api, "IsHost"));
                _isClient = Convert.ToBoolean(R.GetStatic(Api, "IsGuest"));
            }
            catch { }
        }
        else
        {
            var behaviourT = R.T("PlayroomsCoop.CoopBehaviour");
            _present = behaviourT != null;
            if (!_present) return;
            try
            {
                var inst = R.GetStatic(behaviourT, "_instance");
                if (inst != null)
                {
                    _connected = Convert.ToBoolean(R.Get(inst, "_connected"));
                    string role = R.Get(inst, "_role")?.ToString();
                    _isClient = _connected && role == "Client";
                    _isHost = _connected && role == "Host";
                }
            }
            catch { }
        }
        try
        {
            var hostSaveT = R.T("PlayroomsCoop.HostSave");
            if (hostSaveT != null && Convert.ToBoolean(R.GetStatic(hostSaveT, "GuestLoaded"))) _isClient = true;
        }
        catch { }
    }
}
