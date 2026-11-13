using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;

namespace PlayroomsNewGamePlus;

[BepInPlugin(Guid, Name, Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "com.playroomsngplus.mod";
    public const string Name = "Playrooms New Game Plus";
    public const string Version = "1.2.4";

    internal static new ManualLogSource Log;

    internal static ConfigEntry<int> BonusPennies;
    internal static ConfigEntry<bool> KeepLexi;
    internal static ConfigEntry<bool> InstallPrograms;
    internal static ConfigEntry<bool> KeepCollectables;
    internal static ConfigEntry<bool> OpenUnlocks;
    internal static ConfigEntry<bool> KitAfterPrologue;
    internal static ConfigEntry<bool> ShowCycleLabel;

    internal static ConfigEntry<float> HandVacPrice, HandVacEffect, HandVacShopPrice, ThroughputPrice, ThroughputEffect;
    internal static ConfigEntry<int> MaxExtraCost;
    internal static ConfigEntry<float> SpookyMultiplier;

    internal static ConfigEntry<int> LastMode;
    internal static ConfigEntry<bool> LastSpooky, LastTokens, LastKit;

    public override void Load()
    {
        Log = base.Log;
        BonusPennies = Config.Bind("NewGamePlus", "BonusPennies", 0,
            "Extra money a New Game+ run starts with, in pennies (1 ball = 1 penny, 400 = $4.00). 0 = none.");
        KeepCollectables = Config.Bind("NewGamePlus", "KeepCollectables", true,
            "Bring the collectables you found into the new run, so the clean room stays decorated.");
        OpenUnlocks = Config.Bind("NewGamePlus", "OpenUnlocks", true,
            "In New Game+, upgrades and shop items ignore room, ball pit and day requirements. Upgrade chains still apply.");
        KeepLexi = Config.Bind("NewGamePlus", "KeepLexi", true,
            "Bring Lexi the cat into the new run's clean room if she reached it in the finished run.");
        InstallPrograms = Config.Bind("NewGamePlus", "InstallPrograms", true,
            "Install the Shop, Upgrades, Stats and Report programs on the terminal from day 1.");
        KitAfterPrologue = Config.Bind("NewGamePlus", "KitAfterPrologue", true,
            "Deliver the starter kit once the opening prologue is done, so the intro plays normally.");
        ShowCycleLabel = Config.Bind("Interface", "ShowCycleLabel", true,
            "Show a small NG+ label in the bottom-right corner while playing a New Game+ run.");

        HandVacPrice = Config.Bind("Balance", "HandVacUpgradePrice", 1.75f,
            "NG+ Balance Patch: price multiplier for hand vacuum upgrades.");
        HandVacEffect = Config.Bind("Balance", "HandVacUpgradeEffect", 0.75f,
            "NG+ Balance Patch: how much of each hand vacuum upgrade's bonus is kept (0.75 = 25% less).");
        HandVacShopPrice = Config.Bind("Balance", "HandVacShopPrice", 1.75f,
            "NG+ Balance Patch: price multiplier for buying the hand vacuum itself.");
        ThroughputPrice = Config.Bind("Balance", "ThroughputUpgradePrice", 1.0f,
            "NG+ Balance Patch: price multiplier for the game's other 'Throughput' upgrades (ball collecting, vacuums, robots, vents).");
        ThroughputEffect = Config.Bind("Balance", "ThroughputUpgradeEffect", 1.0f,
            "NG+ Balance Patch: how much of each other Throughput upgrade's bonus is kept.");
        MaxExtraCost = Config.Bind("Balance", "MaxExtraCost", 15000,
            "NG+ Balance Patch: the most all price increases together may add, in pennies. A 100% run ended with about 18,000 spare, so this keeps every upgrade affordable.");
        MigrateBalance(Config.Bind("Balance", "DefaultsVersion", 1, "Internal: which default Balance numbers this file was last updated to. Leave as is."));
        SpookyMultiplier = Config.Bind("Spooky", "ScareMultiplier", 2.0f,
            "Spooky toggle: scares per day are multiplied by this; gaps and cooldowns between scares are divided by it.");

        LastMode = Config.Bind("Menu", "LastMode", 1, "Remembered choice in the New Game+ window (1 Simple, 2 SpeedRun, 3 Balance Patch).");
        LastSpooky = Config.Bind("Menu", "LastSpooky", false, "Remembered Spooky toggle.");
        LastTokens = Config.Bind("Menu", "LastTokens", false, "Remembered Tokens toggle.");
        LastKit = Config.Bind("Menu", "LastKit", false, "Remembered Starter kit toggle.");

        NewGamePlus.Init();

        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<NgPlusBehaviour>())
            ClassInjector.RegisterTypeInIl2Cpp<NgPlusBehaviour>();
        AddComponent<NgPlusBehaviour>();

        Log.LogInfo((object)$"{Name} {Version} loaded.");
    }

    private static void MigrateBalance(ConfigEntry<int> version)
    {
        if (version.Value >= 2) return;
        static void Move(ConfigEntry<float> e, float oldDefault, float newDefault)
        {
            if (System.Math.Abs(e.Value - oldDefault) < 0.0001f) e.Value = newDefault;
        }
        Move(HandVacPrice, 2.0f, 1.75f);
        Move(HandVacShopPrice, 2.0f, 1.75f);
        Move(ThroughputPrice, 1.25f, 1.0f);
        version.Value = 2;
    }

    private static bool _hooksInstalled;
    private static float _lastHookAttempt = -99f;

    internal static void EnsureHooks(float now)
    {
        if (_hooksInstalled || now - _lastHookAttempt < 3f) return;
        _lastHookAttempt = now;
        if (R.T(Game.MainMenuT) == null || R.T(Game.SkillServiceT) == null) return;
        _hooksInstalled = true;
        R.Patch(R.T(Game.MainMenuT), "StartGame", 3, typeof(Hooks), null, nameof(Hooks.StartGamePostfix));
        R.Patch(R.T(Game.GameManagerT), "ActivateSlot", 3, typeof(Hooks), nameof(Hooks.ActivateSlotPrefix), null);
        Gates.Install();
    }
}
