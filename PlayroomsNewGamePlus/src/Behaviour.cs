using System;
using UnityEngine;

namespace PlayroomsNewGamePlus;

public class NgPlusBehaviour : MonoBehaviour
{
    private static float _lastErrorLog = -99f;
    private bool _guiSeen;

    public NgPlusBehaviour(IntPtr ptr) : base(ptr) { }

    private void Update()
    {
        try
        {
            NewGamePlus.Tick();
        }
        catch (Exception ex)
        {
            LogError(ex);
        }
    }

    private void OnGUI()
    {
        try
        {
            if (!_guiSeen)
            {
                _guiSeen = true;
                Plugin.Log.LogInfo((object)"New Game+ interface active");
            }
            if (Ui.Feature.Enabled) Ui.Draw();
        }
        catch (Exception ex)
        {
            LogError(ex);
        }
    }

    private static void LogError(Exception ex)
    {
        float now = Time.realtimeSinceStartup;
        if (now - _lastErrorLog < 5f) return;
        _lastErrorLog = now;
        Plugin.Log.LogError((object)ex.ToString());
    }
}
