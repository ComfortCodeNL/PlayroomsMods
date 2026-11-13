using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace ComfortCode.Playrooms.ShiftHud
{
    // Narrow visual intervention: intercept the light SYSTEM's switch to Night,
    // NOT DayTimeService's clock, day number, game mode or any save hook.
    // We deliberately don't forge LightSystemState, change _phase or enable every
    // Light globally (that can interfere with baked-lighting and game visibility).
    internal static class NightLightKeeper
    {
        private static Harmony _harmony;
        private static bool _installed, _warned, _firstSuppressionLogged;
        internal static bool Installed => _installed;

        internal static void TryInstall()
        {
            if (_installed || Plugin.KeepLightsOn == null || !Plugin.KeepLightsOn.Value) return;
            try
            {
                Type service = GameBridge.Type("TwoPixels.LightSystem.LightSystemService");
                if (service == null) return;
                MethodInfo original = service.GetMethods(GameBridge.AnyMethod)
                    .FirstOrDefault(m => m.Name == "OnDayTimeChanged" &&
                                         m.GetParameters().Length == 1 &&
                                         m.GetParameters()[0].ParameterType.Name.EndsWith("DayTimeType", StringComparison.Ordinal));
                if (original == null)
                {
                    WarnOnce("Expected LightSystemService.OnDayTimeChanged(DayTimeType) missing. Lighting will remain vanilla.");
                    return;
                }

                var prefix = typeof(NightLightKeeper).GetMethod(nameof(OnDayTimeChangedPrefix),
                    BindingFlags.NonPublic | BindingFlags.Static);
                _harmony = new Harmony(Plugin.Guid + ".nightlights");
                _harmony.Patch(original, prefix: new HarmonyMethod(prefix));
                _installed = true;
                Plugin.Logger?.LogInfo("[ShiftHUD] Lighting interception installed; daytime lighting can be kept at shift end.");
            }
            catch (Exception e)
            {
                WarnOnce("Lighting hook unavailable. Vanilla lighting preserved. " + e.Message);
            }
        }

        // Harmony passes the game's native DayTimeType enum as argument 0.
        // A conversion error MUST fall through to original behaviour.
        private static bool OnDayTimeChangedPrefix(object __0)
        {
            if (Plugin.KeepLightsOn == null || !Plugin.KeepLightsOn.Value) return true;
            try
            {
                int phase = Convert.ToInt32(__0);
                // DayTimeType.Day=0, Night=1 (confirmed in Playrooms IL2CPP metadata).
                if (phase != 1) return true;
                if (!_firstSuppressionLogged)
                {
                    _firstSuppressionLogged = true;
                    Plugin.Logger?.LogInfo("[ShiftHUD] Night lighting transition suppressed. The day/shift clock keeps running normally.");
                }
                return false;
            }
            catch (Exception e)
            {
                WarnOnce("Unexpected DayTimeType during light transition; allowing vanilla behaviour. " + e.Message);
                return true;
            }
        }

        private static void WarnOnce(string message)
        {
            if (_warned) return;
            _warned = true;
            Plugin.Logger?.LogWarning("[ShiftHUD] " + message);
        }
    }
}
