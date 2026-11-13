using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;

namespace ComfortCode.Playrooms.ShiftHud
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency("com.playroomscoop.mod", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BasePlugin
    {
        public const string Guid = "nl.comfortcode.playrooms.shifthud";
        public const string Name = "Comfort Shift HUD";
        public const string Version = "0.1.0";

        internal static ManualLogSource Logger;
        internal static ConfigEntry<bool> ShowHud;
        internal static ConfigEntry<bool> KeepLightsOn;
        internal static ConfigEntry<bool> ShowCompletedRooms;
        internal static ConfigEntry<int> MaxRoomNames;
        internal static ConfigEntry<float> RefreshInterval;

        public override void Load()
        {
            Logger = base.Log;
            ShowHud = Config.Bind("Display", "ShowHud", true, "Toon de HUD, ook als nog geen kamer is geladen.");
            ShowCompletedRooms = Config.Bind("Display", "ShowCompletedRooms", true, "Toon de namen/ID's van voltooide kamers.");
            MaxRoomNames = Config.Bind("Display", "MaxRoomNames", 10, "Maximaal getoonde voltooide kamers (1-25).");
            RefreshInterval = Config.Bind("Display", "RefreshInterval", 0.75f, "Interval van de HUD-uitlezing (0.25-5 seconden).");
            KeepLightsOn = Config.Bind("Gameplay", "KeepLightsAfterShift", true,
                "Blokkeer uitsluitend de nachtlicht-uitschakeling bij de volgende shiftgrens; verandert de dagklok en saves niet.");

            try
            {
                if (!ClassInjector.IsTypeRegisteredInIl2Cpp<ShiftHudBehaviour>())
                    ClassInjector.RegisterTypeInIl2Cpp<ShiftHudBehaviour>();
                AddComponent<ShiftHudBehaviour>();
                Logger.LogInfo(Name + " " + Version + " loaded. HUD always on; no F8/F9/F10 shortcut conflicts.");
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to register HUD: " + ex);
                throw;
            }
        }
    }
}
