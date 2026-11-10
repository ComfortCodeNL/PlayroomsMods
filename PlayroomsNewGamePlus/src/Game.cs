using System;
using System.Collections.Generic;
using System.Reflection;

namespace PlayroomsNewGamePlus;

internal static class Game
{
    public const string GameManagerT = "TwoPixels.BackroomCleaner.GameManager";
    public const string MainMenuT = "TwoPixels.UserInterfaces.MainMenuCanvas";
    public const string SkillServiceT = "TwoPixels.Services.Skill.SkillService";
    public const string StatServiceT = "TwoPixels.Services.Statistic.StatisticService";
    public const string PlayerT = "TwoPixels.Player.PersistentPlayer";
    public const string EndSequenceT = "TwoPixels.Ending.GameEndSequence";
    public const string PurchaseT = "TwoPixels.Interactables.PurchaseInteractable";

    public const string CurrencyId = "balls";
    public const string CompletedStat = "game_completed";
    public const string PrologueStat = "prologue_done";
    public const string CollectablePrefix = "collectable_";

    private static object _skills, _stats, _menu;
    private static float _skillsAt = -99, _statsAt = -99, _menuAt = -99;
    private static MethodInfo _opInequality;

    public static float Now => UnityEngine.Time.realtimeSinceStartup;

    public static bool Alive(object o)
    {
        if (o == null) return false;
        try
        {
            _opInequality ??= typeof(UnityEngine.Object).GetMethod("op_Inequality", BindingFlags.Static | BindingFlags.Public);
            if (_opInequality == null) return R.Ptr(o) != IntPtr.Zero;
            return (bool)_opInequality.Invoke(null, new[] { o, null });
        }
        catch
        {
            return false;
        }
    }

    public static object GameManager()
    {
        var t = R.T(GameManagerT);
        if (t == null) return null;
        try
        {
            var gm = R.GetStatic(t, "Instance");
            return Alive(gm) ? gm : null;
        }
        catch
        {
            return null;
        }
    }

    private static object Cached(ref object slot, ref float at, string typeName)
    {
        if (Alive(slot)) return slot;
        slot = null;
        if (Now - at < 1f) return null;
        at = Now;
        slot = R.FindFirst(typeName);
        return slot;
    }

    public static object Skills() => Cached(ref _skills, ref _skillsAt, SkillServiceT);
    public static object Stats() => Cached(ref _stats, ref _statsAt, StatServiceT);
    public static object MainMenu() => Cached(ref _menu, ref _menuAt, MainMenuT);

    public static bool MainMenuShowing()
    {
        var menu = MainMenu();
        if (menu == null) return false;
        try
        {
            object canvas = R.Get(menu, "userInterfaceCanvas");
            return canvas == null || Convert.ToBoolean(R.Get(canvas, "IsShowing"));
        }
        catch
        {
            return true;
        }
    }

    public static bool InWorld()
    {
        if (MainMenuShowing()) return false;
        var skills = Skills();
        if (skills == null || Stats() == null) return false;
        try
        {
            if (!Convert.ToBoolean(R.Get(skills, "IsInitialized"))) return false;
        }
        catch { }
        return R.FindAll(R.T(PlayerT)).Count > 0;
    }

    public static List<object> ShopItems()
    {
        var t = R.T(PurchaseT);
        var list = new List<object>();
        if (t == null) return list;
        object reg = null;
        try { reg = R.GetStatic(t, "_registry"); } catch { }
        try { reg ??= R.GetStatic(t, "Registry"); } catch { }
        foreach (var it in R.Items(reg)) if (Alive(it)) list.Add(it);
        return list;
    }

    public static object StatDict()
    {
        var s = Stats();
        return s == null ? null : R.Get(s, "Statistics");
    }

    public static int Stat(string key, int fallback = 0)
    {
        var s = Stats();
        if (s == null) return fallback;
        return R.DictInt(R.Get(s, "Statistics"), key, fallback);
    }

    public static void SetStat(string key, int value)
    {
        var s = Stats() ?? throw new InvalidOperationException("statistics service not found");
        R.Call(s, "SetStatistic", key, value);
    }

    public static int Balance()
    {
        var ss = Skills();
        return ss == null ? 0 : Convert.ToInt32(R.Call(ss, "GetCurrencyBalance", CurrencyId));
    }

    public static void SetBalance(int target)
    {
        var ss = Skills() ?? throw new InvalidOperationException("skill service not found");
        int diff = target - Balance();
        if (diff > 0) R.Call(ss, "GrantCurrency", CurrencyId, diff, 0);
        else if (diff < 0) R.Call(ss, "SpendCurrency", CurrencyId, -diff, 0);
    }

    public static void Grant(int amount)
    {
        if (amount <= 0) return;
        var ss = Skills() ?? throw new InvalidOperationException("skill service not found");
        R.Call(ss, "GrantCurrency", CurrencyId, amount, 0);
    }

    public static int SlotCount()
    {
        var gm = GameManager();
        return gm == null ? 0 : Convert.ToInt32(R.Get(gm, "SaveSlotAmount"));
    }

    public static bool SlotExists(int slot)
    {
        var gm = GameManager();
        return gm != null && Convert.ToBoolean(R.Call(gm, "SlotExists", slot));
    }

    public static object SlotStats(int slot)
    {
        var gm = GameManager();
        return gm == null ? null : R.Call(gm, "GetSlotStatistics", slot);
    }

    public static int SlotMode(int slot)
    {
        var gm = GameManager();
        return gm == null ? 0 : Convert.ToInt32(R.Call(gm, "GetSlotMode", slot));
    }

    public static int ContinueSlot()
    {
        var gm = GameManager();
        return gm == null ? -1 : Convert.ToInt32(R.Call(gm, "GetContinueSlot"));
    }

    public static int NextEmptySlot(int from)
    {
        int n = SlotCount();
        for (int i = 1; i <= n; i++)
        {
            int s = ((from < 0 ? -1 : from) + i) % n;
            if (s >= 0 && !SlotExists(s)) return s;
        }
        return -1;
    }

    public static object SkillDef(string id)
    {
        var ss = Skills();
        if (ss == null) return null;
        var dict = R.Get(ss, "Skills");
        if (!Convert.ToBoolean(R.Call(dict, "ContainsKey", id))) return null;
        return R.Call(dict, "get_Item", id);
    }

    public static int SkillLevel(string id)
    {
        var ss = Skills();
        return ss == null ? 0 : Convert.ToInt32(R.Call(ss, "GetSkillLevel", id));
    }

    public static int NextLevelCost(object def, int level)
    {
        if (def == null) return 0;
        var levels = R.Get(def, "Levels");
        if (level < 0 || level >= R.Count(levels)) return 0;
        int sum = 0;
        foreach (var cost in R.Items(R.Get(R.At(levels, level), "Costs")))
        {
            var cur = R.Get(cost, "Currency");
            if (cur != null && R.Get<string>(cur, "CurrencyId") == CurrencyId) sum += Convert.ToInt32(R.Get(cost, "Amount"));
        }
        return sum;
    }

    public static Dictionary<string, int> LiveSkillLevels()
    {
        var res = new Dictionary<string, int>();
        var ss = Skills();
        if (ss == null) return res;
        foreach (var (k, _) in R.Pairs(R.Get(ss, "Skills")))
        {
            string id = k?.ToString();
            if (id == null) continue;
            int lvl = SkillLevel(id);
            if (lvl > 0) res[id] = lvl;
        }
        return res;
    }

    private static readonly System.Text.RegularExpressions.Regex SkillBlock =
        new("\"SkillLevels\"\\s*:\\s*\\{([^}]*)\\}", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex SkillPair =
        new("\"([^\"]+)\"\\s*:\\s*(\\d+)", System.Text.RegularExpressions.RegexOptions.Compiled);

    public static Dictionary<string, int> SlotSkillLevels(int slot)
    {
        var res = new Dictionary<string, int>();
        var helper = R.T("TwoPixels.Utilities.TwoPixelsEasyAutoSaveHelper");
        if (helper == null) return res;
        string path = R.Get<string>(R.CallStatic(helper, "GetSlotSettings", slot), "FullPath");
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return res;

        byte[] bytes = System.IO.File.ReadAllBytes(path);
        if (bytes.Length > 2 && bytes[0] == 0x1F && bytes[1] == 0x8B)
        {
            using var src = new System.IO.MemoryStream(bytes);
            using var gz = new System.IO.Compression.GZipStream(src, System.IO.Compression.CompressionMode.Decompress);
            using var dst = new System.IO.MemoryStream();
            gz.CopyTo(dst);
            bytes = dst.ToArray();
        }
        string text = System.Text.Encoding.UTF8.GetString(bytes);
        var block = SkillBlock.Match(text);
        if (!block.Success) return res;
        foreach (System.Text.RegularExpressions.Match m in SkillPair.Matches(block.Groups[1].Value))
        {
            int lvl = int.Parse(m.Groups[2].Value);
            if (lvl > 0) res[m.Groups[1].Value] = lvl;
        }
        return res;
    }

    private static object _terminal;
    private static float _terminalAt = -99;

    public static bool TerminalOpen()
    {
        if (!Alive(_terminal) && Now - _terminalAt > 1f)
        {
            _terminalAt = Now;
            _terminal = R.FindFirst("TwoPixels.Windows95.Windows95Terminal");
        }
        if (!Alive(_terminal)) return false;
        try { return Convert.ToBoolean(R.Get(_terminal, "IsOpen")); } catch { return false; }
    }

    public static string ModeName(int mode) => mode switch { 1 => "Cozy", 2 => "Hardcore", _ => "Normal" };

    public static List<string> CollectablesIn(object statDict)
    {
        var list = new List<string>();
        foreach (var (k, v) in R.Pairs(statDict))
        {
            string key = k?.ToString();
            if (key != null && key.StartsWith(CollectablePrefix, StringComparison.Ordinal) && Convert.ToInt32(v) > 0)
                list.Add(key);
        }
        return list;
    }
}
