using System;

namespace PlayroomsNewGamePlus;

internal static class UpgradeUnlock
{
    public static readonly Feature Feature = new("Upgrades", true);

    private static bool _askingGame;

    public static bool DeferRebuild;
    private static bool _rebuildPending;

    private static bool On => Gates.On && Feature.Enabled;

    public static bool Allowed(string id)
    {
        var def = Game.SkillDef(id);
        if (def == null) return false;
        int lvl = Game.SkillLevel(id);
        if (lvl >= Convert.ToInt32(R.Get(def, "MaxLevel"))) return false;
        return PrerequisitesMet(def);
    }

    private static bool PrerequisitesMet(object def)
    {
        var list = R.Items(R.Get(def, "Prerequisites"));
        if (list.Count == 0) return true;
        bool all = Convert.ToBoolean(R.Get(def, "RequireAllPrerequisites"));
        int met = 0;
        foreach (var p in list)
        {
            var other = R.Get(p, "Skill");
            if (other == null) { met++; continue; }
            string otherId = R.Get<string>(other, "SkillId");
            if (Game.SkillLevel(otherId) >= Convert.ToInt32(R.Get(p, "RequiredLevel"))) met++;
        }
        return all ? met == list.Count : met > 0;
    }

    private static bool GameAllows(object ss, string id)
    {
        _askingGame = true;
        try { return Convert.ToBoolean(R.Call(ss, "CanUnlock", id)); }
        catch { return false; }
        finally { _askingGame = false; }
    }

    public static void RebuildEffects()
    {
        _rebuildPending = false;
        var ss = Game.Skills();
        if (ss == null) return;
        int n = 0;
        foreach (var (k, def) in R.Pairs(R.Get(ss, "Skills")))
        {
            if (Game.SkillLevel(k.ToString()) <= 0) continue;
            R.Call(ss, "RemoveSkillTemplates", def);
            n++;
        }
        R.Call(ss, "ReplayUnlockedSkills");
        Plugin.Log.LogInfo((object)$"Upgrade effects rebuilt from {n} upgrades");
    }

    public static void FinishBatch()
    {
        DeferRebuild = false;
        if (_rebuildPending) RebuildEffects();
    }

    public static void CanUnlockPostfix(string skillId, ref bool result)
    {
        if (result || _askingGame || !On) return;
        try { if (Allowed(skillId)) result = true; } catch { }
    }

    public static bool UnlockSkillPrefix(string skillId)
    {
        if (!On) return true;
        try
        {
            var ss = Game.Skills();
            if (ss == null || GameAllows(ss, skillId) || !Allowed(skillId)) return true;
            ManualUnlock(ss, skillId);
            return false;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning((object)("New Game+ unlock of " + skillId + " failed: " + ((ex as System.Reflection.TargetInvocationException)?.InnerException ?? ex).Message));
            return false; // never let the game throw "Cannot unlock" at the player
        }
    }

    private static void ManualUnlock(object ss, string id)
    {
        var def = Game.SkillDef(id);
        int lvl = Game.SkillLevel(id);
        var level = R.At(R.Get(def, "Levels"), lvl);

        var costs = R.Items(R.Get(level, "Costs"));
        foreach (var cost in costs)
        {
            string cur = R.Get<string>(R.Get(cost, "Currency"), "CurrencyId");
            int amount = Convert.ToInt32(R.Get(cost, "Amount"));
            if (Convert.ToInt32(R.Call(ss, "GetCurrencyBalance", cur)) < amount) return;
        }
        foreach (var cost in costs)
        {
            string cur = R.Get<string>(R.Get(cost, "Currency"), "CurrencyId");
            int amount = Convert.ToInt32(R.Get(cost, "Amount"));
            if (amount > 0) R.Call(ss, "SpendCurrency", cur, amount, 1); // CurrencySink.Skill
        }

        var levels = R.Get(R.Get(ss, "SaveData"), "SkillLevels");
        R.Call(levels, "set_Item", id, lvl + 1);
        if (DeferRebuild) _rebuildPending = true;
        else RebuildEffects();
        try
        {
            var ev = R.Get(ss, "OnSkillUnlocked");
            if (ev != null) R.Call(ev, "Invoke", def, lvl + 1);
        }
        catch (Exception ex) { Plugin.Log.LogWarning((object)("OnSkillUnlocked: " + ex.Message)); }
        try { R.Call(ss, "NotifyIfSkillListChanged", def); } catch { }
        Plugin.Log.LogInfo((object)$"Unlocked {id} level {lvl + 1} (New Game+ open upgrades)");
    }
}
