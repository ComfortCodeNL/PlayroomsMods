using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace PlayroomsCoop
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BasePlugin
    {
        public const string Guid = "com.playroomscoop.mod";
        public const string Name = "Playrooms Co-op";
        public const string Version = "1.0.4";

        internal static new ManualLogSource Log;

        internal static ConfigEntry<int> MenuKey;
        internal static ConfigEntry<string> Player1Color;
        internal static ConfigEntry<string> Player2Color;
        internal static ConfigEntry<float> Glow;
        internal static ConfigEntry<float> HdrpGlowScale;
        internal static ConfigEntry<bool> ShowNames;
        internal static ConfigEntry<int> SendRate;
        internal static ConfigEntry<float> EntitySenseRadius, AmbientScareMultiplier;
        internal static ConfigEntry<bool> SyncBalls, SyncPits, SyncEntity, SyncDayTime, SyncUpgrades, SyncObjects, SharedWallet, SyncDoors, KeepDoorsOpen, SyncUnlocks, SyncBoards, SyncProps, UseHostSave;
        internal static ConfigEntry<string> PitDropStyle;
        internal static ConfigEntry<bool> ReplayBroom, SyncSusanne, ClientVents, FixStrayBalls, CoopRespawn, BetterUnstuck, ShowBroom, CheckHostSave;
        internal static ConfigEntry<float> BallCorrection;
        internal static ConfigEntry<bool> PerfLog;

        public override void Load()
        {
            Log = base.Log;

            MenuKey = Config.Bind("Controls", "MenuKey", 0x77,
                "Windows virtual-key code that opens the co-op menu. 0x77 = F8, 0x76 = F7, 0x78 = F9.");
            Player1Color = Config.Bind("Avatars", "Player1Color", "#D63A3A", "Host (Player 1) colour, hex.");
            Player2Color = Config.Bind("Avatars", "Player2Color", "#3A6BE8", "Client (Player 2) colour, hex.");
            Glow = Config.Bind("Avatars", "BodyGlow", 0f, "Avatar self-glow (0 = lit by the game only). Small values like 0.1 make players easier to spot in the dark.");
            HdrpGlowScale = Config.Bind("Avatars", "GlowBrightness", 1f, "Multiplier for the glow brightness. Raise it if the glow is invisible, lower it if avatars look blown out.");
            ShowNames = Config.Bind("Avatars", "ShowNames", true, "Show the other player's name above their avatar.");
            SendRate = Config.Bind("Network", "SendRate", 20, "Position updates per second (10-60).");
            SyncBalls = Config.Bind("World", "SyncBalls", true, "Balls picked up by one player disappear for the other.");
            SyncPits = Config.Bind("World", "SyncPits", true, "Ball pit fill levels are shared.");
            SyncEntity = Config.Bind("World", "SyncEntity", true, "The host's entity hunts both players; the client's copy follows it.");
            SyncDayTime = Config.Bind("World", "SyncDayTime", true, "The client's day/night clock follows the host's.");
            SyncUpgrades = Config.Bind("World", "SyncUpgrades", true, "Upgrades and shop items bought by either player apply to both.");
            SharedWallet = Config.Bind("World", "SharedWallet", true, "One shared wallet: the host's balance is used, and earning/spending by either player changes it for both.");
            SyncObjects = Config.Bind("World", "SyncRobotsAndVacuums", true, "Robots and vacuum devices are shared: one copy, both players can use them.");
            SyncDoors = Config.Bind("World", "SyncDoors", true, "Doors opened or closed by one player open or close for the other.");
            KeepDoorsOpen = Config.Bind("World", "KeepDoorsOpen", true, "In co-op, opening a hallway door no longer closes the other open hallway doors (the game normally keeps only one open).");
            SyncUnlocks = Config.Bind("World", "SyncWorldPurchases", true, "Things bought out in the world (vents, TVs, robots...) count for both players.");
            SyncBoards = Config.Bind("World", "SyncWhiteboards", true, "Drawings on whiteboards show up for both players.");
            EntitySenseRadius = Config.Bind("World", "EntitySenseRadius", 3f, "Meters. Any player this close to the entity is noticed even without being seen, so nobody can sneak up behind it. 0 = off.");
            SyncProps = Config.Bind("World", "SyncLooseObjects", true, "Tables, chairs and other loose objects moved by one player move for the other too.");
            AmbientScareMultiplier = Config.Bind("World", "AmbientScareMultiplier", 1.5f, "How many more ambient scares (flickering lamps, footsteps, peeks...) happen in co-op. 1 = same as solo, max 3. The entity itself is not affected.");
            UseHostSave = Config.Bind("World", "PlayOnHostSave", true, "Player 2 plays a copy of the host's save; their own save is set aside and restored after the session. Off = each player loads their own save and they're merged.");
            CheckHostSave = Config.Bind("World", "CheckHostSave", true, "Check the host's save before loading it and refuse anything that isn't a normal Playrooms save. Leave this on.");
            PitDropStyle = Config.Bind("World", "PitDropStyle", "vacuum", "How the partner's balls arrive in your pit: vacuum, handvacuum, flight (arc from the partner) or robot (old behaviour).");
            ReplayBroom = Config.Bind("World", "ReplayBroom", true, "Replay the partner's broom on your side so large piles move smoothly. Off = send live ball positions instead.");
            SyncSusanne = Config.Bind("World", "SyncSusanne", true, "One shared Susanne (hall monitor and office): she goes after whoever sets her off, and both players see and hear her.");
            BallCorrection = Config.Bind("World", "BallCorrection", 0.10f, "Meters. After a push, balls that settled further than this from where they settled for the other player slide into place. Lower = closer match, more sliding.");
            ClientVents = Config.Bind("World", "ClientVents", true, "Player 2's vents swallow balls on their own screen too (the host still does the counting). Off = only the host's vents run.");
            FixStrayBalls = Config.Bind("World", "FixStrayBalls", true, "Move balls stuck outside the playable area (inside walls, behind them, under the floor) back to the nearest walkable spot. Works in solo too.");
            CoopRespawn = Config.Bind("World", "CoopRespawn", true, "Caught while your partner is safe (daytime, or in the clean room at night): you wake up in the clean room instead of dying.");
            BetterUnstuck = Config.Bind("World", "BetterUnstuck", true, "Unstuck (pause menu) moves you next to your partner if they stand somewhere walkable, otherwise back to the clean room. 10 s cooldown. Works in solo too (clean room).");
            ShowBroom = Config.Bind("Players", "ShowBroom", true, "Show a broom in the other player's hands while they push with it.");
            PerfLog = Config.Bind("Debug", "PerfLog", true, "Write a line to the log every 30 s showing how much frame time each co-op feature uses.");
            World.Init();

            if (!ClassInjector.IsTypeRegisteredInIl2Cpp<CoopBehaviour>())
                ClassInjector.RegisterTypeInIl2Cpp<CoopBehaviour>();
            AddComponent<CoopBehaviour>();

            Log.LogInfo($"{Name} {Version} loaded. Press F8 in game for the co-op menu.");
        }

        internal static Color ParseColor(string hex, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            hex = hex.Trim().TrimStart('#');
            if (hex.Length != 6 || !uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint v))
                return fallback;
            return new Color(((v >> 16) & 0xFF) / 255f, ((v >> 8) & 0xFF) / 255f, (v & 0xFF) / 255f, 1f);
        }
    }
}
