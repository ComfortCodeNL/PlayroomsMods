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
        public const string Version = "0.1.1";

        internal static ManualLogSource Logger;
        internal static ConfigEntry<bool> ShowHud;
        internal static ConfigEntry<bool> EnableAltCheats;
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
            EnableAltCheats = Config.Bind("Cheats", "EnableAltHotkeys", true,
                "ALT+4: handstofzuiger + bijbehorende upgrades; ALT+5: alle skills naar maximaal niveau. Let op: verandert je huidige save en kan achievements beïnvloeden.");

            try
            {
                if (!ClassInjector.IsTypeRegisteredInIl2Cpp<ShiftHudBehaviour>())
                    ClassInjector.RegisterTypeInIl2Cpp<ShiftHudBehaviour>();
                AddComponent<ShiftHudBehaviour>();
                Logger.LogInfo(Name + " " + Version + " loaded. END GAME only in final 10 seconds; normal darkness restored; ALT+4 hand vacuum, ALT+5 max skills.");
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to register HUD: " + ex);
                throw;
            }
        }
    }
}
