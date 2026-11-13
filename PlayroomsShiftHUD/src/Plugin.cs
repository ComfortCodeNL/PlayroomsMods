using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;

namespace ComfortCode.Playrooms.ShiftHUD
{
    [BepInPlugin(Id, Name, Version)]
    [BepInDependency("com.playroomscoop.mod", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BasePlugin
    {
        public const string Id = "nl.comfortcode.playrooms.shifthud";
        public const string Name = "Comfort Shift HUD";
        public const string Version = "0.1.0";

        internal static ManualLogSource Logger;
        internal static ConfigEntry<bool> ShowHud;
        internal static ConfigEntry<bool> HudRight;
        internal static ConfigEntry<bool> KeepLight;
        internal static ConfigEntry<float> WorkLightRange;
        internal static ConfigEntry<float> WorkLightIntensity;
        internal static ConfigEntry<float> RefreshSeconds;
        internal static ConfigEntry<int> MaxRoomNames;

        public override void Load()
        {
            Logger = base.Log;
            ShowHud = Config.Bind("HUD", "Visible", true,
                "Show ball count, shift countdown and cleaned rooms; always visible while in game, no hotkeys required.");
            HudRight = Config.Bind("HUD", "TopRight", true,
                "Put overlay at the top right; false for top left.");
            MaxRoomNames = Config.Bind("HUD", "MaximumCleanRoomNames", 8,
                "Maximum number of cleaned room names shown at once (1-16).");
            RefreshSeconds = Config.Bind("Tracking", "RefreshSeconds", 3f,
                "Seconds between complete ball-count passes, clamped to 1-15. Ball scanning is chunked.");
            KeepLight = Config.Bind("AfterShift", "EnableLocalWorkLight", true,
                "Keep YOUR nearby area illuminated after the shift (visual only). Does not change the clock or multiplayer state.");
            WorkLightRange = Config.Bind("AfterShift", "WorkLightRange", 45f,
                "Local after-shift light radius, 5-75 metres. Depends on Unity HDRP rendering.");
            WorkLightIntensity = Config.Bind("AfterShift", "WorkLightIntensity", 3f,
                "Local after-shift light intensity (0.5-12). Does not override the game's global lighting.");

            try
            {
                if (!ClassInjector.IsTypeRegisteredInIl2Cpp<ShiftHudBehaviour>())
                    ClassInjector.RegisterTypeInIl2Cpp<ShiftHudBehaviour>();
                AddComponent<ShiftHudBehaviour>();
                Logger.LogInfo(Name + " " + Version +
                    " loaded. Room count / shift timer / cleaned rooms / visual END DAY. No F-key conflict with Co-op.");
            }
            catch (Exception e)
            {
                Logger.LogError("Unable to initialize Comfort Shift HUD: " + e);
                throw;
            }
        }
    }
}
