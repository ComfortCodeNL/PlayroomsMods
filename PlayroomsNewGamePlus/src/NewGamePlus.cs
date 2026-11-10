using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;

namespace PlayroomsNewGamePlus;

internal static class NewGamePlus
{
    public const string CycleStat = "ngplus_unlocked_cycle";
    public const string KitStat = "ngplus_kit_granted";
    public const string OldCycleStat = "ngplus_cycle", OldKitStat = "ngplus_kit";

    public static int CycleIn(object stats) => Math.Max(R.DictInt(stats, CycleStat), R.DictInt(stats, OldCycleStat));

    public const string LexiStat = "lexi_escaped";
    public const string LexiMarker = "ngplus_lexi_granted";
    public const string LexiElevatorStat = "elevator_OfficeElevator_Lexi";

    public static readonly string[] ProgramMails = { "install_skills", "install_shop", "install_stats", "report_update" };

    public static readonly (string stat, string name)[] Kit =
    {
        ("tool_robot_station", "charging station"),
        ("tool_robot", "robot vacuum"),
        ("tool_vacuum", "ball vacuum"),
        ("tool_handvac", "hand vacuum"),
        ("tool_flashlight", "flashlight"),
        ("tool_scanner", "radar"),
        ("tool_shovel", "broom"),
    };

    private sealed class Pending
    {
        public int Slot, Cycle, Mode, Source;
        public List<string> Collectables = new();
        public RunOptions Options = new();
        public Dictionary<string, int> Skills = new();
        public Dictionary<string, int> Tools = new();
    }

    private static readonly HashSet<string> NotTools = new() { "tool_ballvent" };

    public static RunOptions WorldOptions { get; private set; } = new();

    public static int WorldCycle => ActiveCycle > 0 ? ActiveCycle : (PendingHere ? _pending.Cycle : 0);

    private static bool PendingHere => _pending != null && !_awaitMenu && CurrentSlot == _pending.Slot && !Coop.IsClient;

    public static readonly Feature Setup = new("Setup", true);
    private static readonly Feature Marker = new("Marker", true);

    public static int CurrentSlot = -1;

    public static int ActiveCycle { get; private set; }

    public static bool IsNgWorld => ActiveCycle > 0 || PendingHere;

    private static float _nextExtras;

    private static Pending _pending;
    private static bool _ourStart;
    private static int _ourStartSlot = -1;
    private static float _ourStartAt = -999f;
    private static bool _awaitMenu;
    private static float _menuSince = -1, _worldSince = -1, _kitWaitSince = -1, _nextActiveCheck;
    private static bool _stageADone;
    private static int _kitIndex;

    private static string PendingPath => Path.Combine(Paths.ConfigPath, "PlayroomsNewGamePlus.pending.txt");

    public static void Init() => LoadPending();

    public static int TargetFor(int sourceSlot) => Game.NextEmptySlot(sourceSlot);

    public static bool Begin(int sourceSlot, object stats, bool fromEndScreen, RunOptions options)
    {
        if (Coop.IsClient)
        {
            Ui.Notify("In co-op, only the host can start New Game+.", 6f);
            return false;
        }
        int target = TargetFor(sourceSlot);
        if (target < 0)
        {
            Ui.Notify("No empty save slot for New Game+. Delete a slot in the main menu first.", 8f);
            return false;
        }
        var p = new Pending
        {
            Slot = target,
            Source = sourceSlot,
            Cycle = CycleIn(stats) + 1,
            Mode = sourceSlot >= 0 ? Game.SlotMode(sourceSlot) : 0,
            Collectables = Kept(stats),
            Options = options.Clone(),
        };
        if (options.Mode == NgMode.SpeedRun)
        {
            try { p.Skills = fromEndScreen ? Game.LiveSkillLevels() : Game.SlotSkillLevels(sourceSlot); }
            catch (Exception ex) { Plugin.Log.LogWarning((object)("Reading upgrade levels failed: " + ex.Message)); }
            if (p.Skills.Count == 0)
            {
                Ui.Notify("Couldn't read that save's upgrades for SpeedRun. Try starting it from the end screen.", 8f);
                return false;
            }
            foreach (var (k, v) in R.Pairs(stats))
            {
                string key = k?.ToString();
                int n = Convert.ToInt32(v);
                if (key != null && key.StartsWith("tool_", StringComparison.Ordinal) && n > 0 && !NotTools.Contains(key)) p.Tools[key] = n;
            }
        }
        _pending = p;
        SavePending();
        Plugin.Log.LogInfo((object)$"New Game+ {p.Cycle}: from slot {sourceSlot + 1} into slot {target + 1} ({Game.ModeName(p.Mode)}, {RunOptions.Name(p.Options.Mode)}, spooky {p.Options.Spooky}, tokens {p.Options.Tokens}, kit {p.Options.KitApplies}, {p.Collectables.Count} kept, {p.Skills.Count} upgrades, {p.Tools.Count} tools)");

        if (fromEndScreen)
        {
            _awaitMenu = true;
            _menuSince = -1;
            if (!LeaveToMenu()) Ui.Notify("Couldn't return to the menu. Use the Menu button; New Game+ will start from there.", 8f);
        }
        else
        {
            StartPendingGame();
        }
        return true;
    }

    private static List<string> Kept(object stats)
    {
        var list = Plugin.KeepCollectables.Value ? Game.CollectablesIn(stats) : new List<string>();
        if (Plugin.KeepLexi.Value && R.DictInt(stats, LexiStat) > 0) list.Add(LexiStat);
        return list;
    }

    private static bool LeaveToMenu()
    {
        try
        {
            var end = R.FindFirst(Game.EndSequenceT);
            if (end != null)
            {
                R.Call(end, "OnClickMenu");
                return true;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning((object)"End screen menu button failed: " + ex.Message);
        }
        try
        {
            var gm = Game.GameManager();
            if (gm == null) return false;
            R.Call(gm, "ReturnToMainMenu");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning((object)"ReturnToMainMenu failed: " + ex.Message);
            return false;
        }
    }

    private static void StartPendingGame()
    {
        var menu = Game.MainMenu() ?? throw new InvalidOperationException("main menu not found");
        _ourStart = true;
        _ourStartSlot = _pending.Slot;
        _ourStartAt = Game.Now;
        try
        {
            R.Call(menu, "StartGame", _pending.Slot, true, _pending.Mode);
        }
        finally
        {
            _ourStart = false;
        }
        CurrentSlot = _pending.Slot;
        _stageADone = false;
        _kitIndex = 0;
        _worldSince = _kitWaitSince = -1;
        Ui.Notify($"Starting {RunOptions.Name(_pending.Options.Mode)} ({Ui.Label(_pending.Cycle)}) in slot {_pending.Slot + 1}..." + (Coop.IsHost ? " Everyone else joins automatically." : ""), 6f);
    }

    public static void OnGameStarting(int slot, bool freshStart, bool fromMenu)
    {
        CurrentSlot = slot;
        ActiveCycle = 0;
        _worldSince = _kitWaitSince = -1;
        _stageADone = false;
        _kitIndex = 0;
        if (!fromMenu) PrepareDataFor(slot);
        bool justOurs = slot == _ourStartSlot && Game.Now - _ourStartAt < 120f;
        if (_pending != null && fromMenu && freshStart && !_ourStart && !justOurs && slot == _pending.Slot)
        {
            Plugin.Log.LogInfo((object)$"A normal new game was started in slot {slot + 1}; dropping the waiting New Game+ setup for it.");
            ClearPending();
        }
    }

    public static void Tick()
    {
        float now = Game.Now;
        Plugin.EnsureHooks(now);

        if (Modal.Active && (now - Ui.LastDraw > 1f || !Ui.Feature.Enabled)) Modal.End();

        if (now >= _nextActiveCheck)
        {
            _nextActiveCheck = now + 1f;
            ActiveCycle = 0;
            if (Game.InWorld() && !Marker.Try(() => ActiveCycle = CurrentCycle(), "read NG+ marker")) ActiveCycle = 0;
            Marker.Try(RefreshWorldOptions, "read NG+ options");
            if (Game.InWorld()) Modes();
        }
        Tokens.Tick();

        if (_awaitMenu && _pending != null)
        {
            if (Game.MainMenuShowing())
            {
                if (_menuSince < 0) _menuSince = now;
                if (now - _menuSince > 1.5f && ReadyToStart(now - _menuSince))
                {
                    _awaitMenu = false;
                    Setup.Try(StartPendingGame, "start New Game+");
                }
            }
            else _menuSince = -1;
        }

        if (ActiveCycle > 0 && now >= _nextExtras)
        {
            _nextExtras = now + 2f;
            Setup.Try(EnsureWorldExtras, "terminal programs and Lexi");
        }

        if (_pending != null && !_awaitMenu && CurrentSlot == _pending.Slot && !Coop.IsClient)
            Setup.Try(() => SetupTick(now), "set up New Game+");
    }

    private static void RefreshWorldOptions()
    {
        if (PendingHere && ActiveCycle <= 0)
        {
            WorldOptions = _pending.Options;
            return;
        }
        if (ActiveCycle <= 0)
        {
            WorldOptions = new RunOptions { Mode = NgMode.Legacy };
            return;
        }
        int mode = Game.Stat(RunOptions.ModeStat);
        WorldOptions = new RunOptions
        {
            Mode = Enum.IsDefined(typeof(NgMode), mode) ? (NgMode)mode : NgMode.Simple,
            Spooky = Game.Stat(RunOptions.SpookyStat) > 0,
            Tokens = Game.Stat(RunOptions.TokensStat) > 0,
        };
        if (PendingHere) WorldOptions = _pending.Options;
    }

    private static void Modes()
    {
        bool ng = IsNgWorld;
        bool wantBalance = ng && WorldOptions.Mode == NgMode.Balance;
        if (Balance.Ensure(wantBalance)) ReplayUpgradeEffects();
        Spooky.Ensure(ng && WorldOptions.Spooky);
    }

    private static void PrepareDataFor(int slot)
    {
        Setup.Try(() =>
        {
            bool balance;
            if (_pending != null && _pending.Slot == slot && !Coop.IsClient) balance = _pending.Options.Mode == NgMode.Balance;
            else
            {
                var stats = Game.SlotStats(slot);
                balance = CycleIn(stats) > 0 && R.DictInt(stats, RunOptions.ModeStat) == (int)NgMode.Balance;
            }
            Balance.Ensure(balance);
        }, "prepare upgrade data");
    }

    private static void ReplayUpgradeEffects()
    {
        if (!Game.InWorld()) return;
        Setup.Try(() =>
        {
            if (Game.LiveSkillLevels().Count == 0) return;
            UpgradeUnlock.RebuildEffects();
        }, "re-apply upgrade effects");
    }

    private static bool ReadyToStart(float waited)
    {
        if (!Coop.Connected) return true;
        var all = Coop.EveryoneInMenu;
        if (all == null) return waited > 5f;
        if (all.Value) return true;
        if (waited > 30f)
        {
            Plugin.Log.LogWarning((object)"Not every player reached the menu within 30 seconds; starting New Game+ anyway");
            return true;
        }
        return false;
    }

    private static int CurrentCycle()
    {
        int cycle = Game.Stat(CycleStat), old = Game.Stat(OldCycleStat);
        if (old > cycle && !Coop.IsClient)
        {
            Game.SetStat(CycleStat, old);
            int oldKit = Game.Stat(OldKitStat);
            if (oldKit > Game.Stat(KitStat)) Game.SetStat(KitStat, oldKit);
            Plugin.Log.LogInfo((object)("Moved New Game+ markers to the co-op synced names (cycle " + old + ")"));
            cycle = old;
        }
        return Math.Max(0, Math.Max(cycle, old));
    }

    private static void EnsureWorldExtras()
    {
        if (!Game.InWorld()) return;
        int day = 1;
        try
        {
            var dt = R.FindFirst("TwoPixels.DayTime.DayTimeService");
            if (dt != null) day = Math.Max(1, Convert.ToInt32(R.Get(dt, "CurrentDay")));
        }
        catch { }

        int added = 0;
        if (Plugin.InstallPrograms.Value)
        {
            foreach (var id in ProgramMails)
            {
                if (Game.Stat("mail_day_" + id) <= 0) { Game.SetStat("mail_day_" + id, day); added++; }
                if (Game.Stat("mail_read_" + id) <= 0) Game.SetStat("mail_read_" + id, 1);
            }
        }
        if (Game.Stat(LexiMarker) > 0) LexiRescuedOnce();
        if (added > 0) Plugin.Log.LogInfo((object)$"Terminal programs installed by mail on day {day} ({added} mails)");
    }

    private static void LexiRescuedOnce()
    {
        if (Game.Stat(LexiStat) <= 0)
        {
            Game.SetStat(LexiStat, 1);
            Plugin.Log.LogInfo((object)"Lexi is back in the clean room");
        }

        foreach (var elevator in R.FindAll(R.T("TwoPixels.Interactables.ElevatorInteractable")))
        {
            object ride = null;
            try { ride = R.Get(elevator, "chairRide"); } catch { }
            if (!Game.Alive(ride)) continue;

            string openStat = null;
            try { openStat = R.Get<string>(elevator, "OpenStatistic"); } catch { }
            if (string.IsNullOrEmpty(openStat)) openStat = LexiElevatorStat;
            if (Game.Stat(openStat) <= 0)
            {
                Game.SetStat(openStat, 1);
                try { R.Call(elevator, "RefreshOpenState"); } catch { }
            }

            bool aboard = false;
            try { aboard = Convert.ToBoolean(R.Get(ride, "IsAboard")); } catch { }
            if (!aboard) continue;
            try { R.Call(ride, "Restore"); } catch { }
            try { aboard = Convert.ToBoolean(R.Get(ride, "IsAboard")); } catch { }
            if (aboard)
            {
                try { R.Call(ride, "Despawn"); } catch { }
            }
            Plugin.Log.LogInfo((object)"Lexi's office elevator is empty (she was rescued in the finished run)");
        }
        if (Game.Stat(LexiElevatorStat) <= 0) Game.SetStat(LexiElevatorStat, 1);
    }

    private static void SetupTick(float now)
    {
        if (!Game.InWorld())
        {
            _worldSince = -1;
            return;
        }
        if (_worldSince < 0) _worldSince = now;
        if (now - _worldSince < 1f) return;

        var p = _pending;

        if (!_stageADone)
        {
            bool first = Game.Stat(CycleStat) != p.Cycle;
            Game.SetStat(CycleStat, p.Cycle);
            int kept = 0;
            foreach (var c in p.Collectables)
            {
                if (Game.Stat(c) > 0) continue;
                Game.SetStat(c, 1);
                kept++;
            }
            if (p.Collectables.Contains(LexiStat))
            {
                Game.SetStat(LexiMarker, 1);
                LexiRescuedOnce();
            }
            Game.SetStat(RunOptions.ModeStat, (int)p.Options.Mode);
            if (p.Options.Spooky) Game.SetStat(RunOptions.SpookyStat, 1);
            if (p.Options.Tokens) Game.SetStat(RunOptions.TokensStat, 1);
            if (first && Game.Stat(KitStat) != p.Cycle && Plugin.BonusPennies.Value > 0) Game.SetBalance(Plugin.BonusPennies.Value);
            _stageADone = true;
            ActiveCycle = p.Cycle;
            Gates.Invalidate();
            _nextExtras = 0f;
            Plugin.Log.LogInfo((object)$"New Game+ {p.Cycle} marked; {kept} kept items restored; wallet {Game.Balance()}");
            string bonus = Plugin.BonusPennies.Value > 0 ? $"  -  ${Plugin.BonusPennies.Value / 100f:0.00} bonus" : "";
            string extras = (p.Options.Spooky ? ", Spooky" : "") + (p.Options.Tokens ? $", {p.Cycle} token{(p.Cycle == 1 ? "" : "s")}" : "");
            Ui.Notify($"{RunOptions.Name(p.Options.Mode).ToUpperInvariant()} {p.Cycle}  -  slot {p.Slot + 1}{extras}{bonus}", 8f);
        }

        if (Game.Stat(KitStat) == p.Cycle)
        {
            Finish();
            return;
        }
        if (Plugin.KitAfterPrologue.Value && Game.Stat(Game.PrologueStat) <= 0)
        {
            if (_kitWaitSince < 0) _kitWaitSince = now;
            if (now - _kitWaitSince < 600f) return;
            Plugin.Log.LogWarning((object)"Prologue flag not seen after 10 minutes; delivering the starter kit anyway");
        }
        if (p.Options.Mode == NgMode.SpeedRun) SpeedRun(p);
        else if (p.Options.KitApplies) GiveKit();
        Game.SetStat(KitStat, p.Cycle);
        Finish();
    }

    private static void GiveKit()
    {
        for (; _kitIndex < Kit.Length; _kitIndex++)
        {
            var (stat, name) = Kit[_kitIndex];
            if (Game.Stat(stat) > 0) continue;
            if (BuyFree(stat))
            {
                Plugin.Log.LogInfo((object)$"Starter kit: {name} bought");
                continue;
            }
            Game.SetStat(stat, 1);
            Plugin.Log.LogInfo((object)$"Starter kit: {name} granted through its statistic");
        }
    }

    private static void SpeedRun(Pending p)
    {
        foreach (var (stat, count) in p.Tools.OrderBy(t => t.Key == "tool_shovel" ? 1 : 0))
        {
            int tries = 0;
            while (Game.Stat(stat) < count && tries++ < count + 2)
                if (!BuyFree(stat)) break;
            if (Game.Stat(stat) < count) Game.SetStat(stat, count);
        }

        int bought = 0, wanted = p.Skills.Values.Sum();
        Gates.Replaying = true;
        UpgradeUnlock.DeferRebuild = true;
        try
        {
            for (int pass = 0; pass < 12; pass++)
            {
                bool progress = false;
                foreach (var (id, target) in p.Skills)
                {
                    var def = Game.SkillDef(id);
                    if (def == null) continue;
                    int lvl = Game.SkillLevel(id);
                    while (lvl < target)
                    {
                        int cost = Game.NextLevelCost(def, lvl);
                        Game.Grant(cost);
                        try { R.Call(Game.Skills(), "UnlockSkill", id); } catch { }
                        int now = Game.SkillLevel(id);
                        if (now <= lvl)
                        {
                            if (cost > 0) R.Call(Game.Skills(), "SpendCurrency", Game.CurrencyId, cost, 0);
                            break;
                        }
                        bought += now - lvl;
                        lvl = now;
                        progress = true;
                    }
                }
                if (!progress) break;
            }
        }
        finally
        {
            Gates.Replaying = false;
            UpgradeUnlock.FinishBatch();
        }
        Plugin.Log.LogInfo((object)$"SpeedRun: {bought} of {wanted} upgrade levels and {p.Tools.Count} kinds of tools carried over");
        if (bought < wanted) Ui.Notify($"SpeedRun: {bought} of {wanted} upgrade levels carried over (see the log).", 8f);
    }

    private static bool BuyFree(string stat)
    {
        object item = null;
        foreach (var it in Game.ShopItems())
        {
            string reward = null;
            try { reward = R.Get<string>(it, "RewardStatisticName"); } catch { }
            if (reward == stat) { item = it; break; }
        }
        if (item == null) return false;

        int owned = Game.Stat(stat);
        int price = 0;
        try { price = Convert.ToInt32(R.Get(item, "CurrentPrice")); } catch { }
        int before = Game.Balance();
        Game.Grant(price);
        try
        {
            Gates.Replaying = true;
            R.Call(item, "CompletePurchase");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning((object)$"Starter kit purchase of {stat} failed: {ex.InnerException?.Message ?? ex.Message}");
        }
        finally
        {
            Gates.Replaying = false;
        }
        int after = Game.Balance();
        if (after > before)
        {
            var ss = Game.Skills();
            if (ss != null) R.Call(ss, "SpendCurrency", Game.CurrencyId, after - before, 0);
        }
        return Game.Stat(stat) > owned;
    }

    private static void Finish()
    {
        Plugin.Log.LogInfo((object)$"New Game+ {_pending?.Cycle} set up");
        ClearPending();
    }

    private static void SavePending()
    {
        try
        {
            var p = _pending;
            File.WriteAllLines(PendingPath, new[]
            {
                "slot=" + p.Slot, "cycle=" + p.Cycle, "mode=" + p.Mode, "source=" + p.Source,
                "collectables=" + string.Join(",", p.Collectables),
                "options=" + p.Options.Serialize(),
                "skills=" + string.Join(",", p.Skills.Select(s => s.Key + ":" + s.Value)),
                "tools=" + string.Join(",", p.Tools.Select(s => s.Key + ":" + s.Value)),
            });
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning((object)"Couldn't write " + PendingPath + ": " + ex.Message);
        }
    }

    private static void LoadPending()
    {
        try
        {
            if (!File.Exists(PendingPath)) return;
            var kv = File.ReadAllLines(PendingPath)
                         .Select(l => l.Split(new[] { '=' }, 2))
                         .Where(a => a.Length == 2)
                         .ToDictionary(a => a[0].Trim(), a => a[1].Trim());
            _pending = new Pending
            {
                Slot = int.Parse(kv["slot"]),
                Cycle = int.Parse(kv["cycle"]),
                Mode = kv.TryGetValue("mode", out var m) ? int.Parse(m) : 0,
                Source = kv.TryGetValue("source", out var s) ? int.Parse(s) : -1,
                Collectables = kv.TryGetValue("collectables", out var c)
                    ? c.Split(',').Select(x => x.Trim()).Where(x => x.StartsWith(Game.CollectablePrefix, StringComparison.Ordinal) || x == LexiStat).ToList()
                    : new List<string>(),
                Options = RunOptions.Parse(kv.TryGetValue("options", out var o) ? o : null),
                Skills = ParseCounts(kv.TryGetValue("skills", out var sk) ? sk : null),
                Tools = ParseCounts(kv.TryGetValue("tools", out var tl) ? tl : null),
            };
            if (!kv.ContainsKey("options")) _pending.Options.Kit = true;
            Plugin.Log.LogInfo((object)$"New Game+ {_pending.Cycle} setup is waiting for slot {_pending.Slot + 1}");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning((object)"Ignoring unreadable " + PendingPath + ": " + ex.Message);
            _pending = null;
        }
    }

    private static Dictionary<string, int> ParseCounts(string s)
    {
        var d = new Dictionary<string, int>();
        if (string.IsNullOrEmpty(s)) return d;
        foreach (var part in s.Split(','))
        {
            var kv = part.Split(':');
            if (kv.Length == 2 && int.TryParse(kv[1], out var n) && n > 0) d[kv[0].Trim()] = n;
        }
        return d;
    }

    private static void ClearPending()
    {
        _pending = null;
        try { if (File.Exists(PendingPath)) File.Delete(PendingPath); } catch { }
    }
}
