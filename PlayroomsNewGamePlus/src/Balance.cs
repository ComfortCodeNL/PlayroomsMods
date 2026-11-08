using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;

namespace PlayroomsNewGamePlus;

internal static class Balance
{
    public static readonly Feature Feature = new("Balance", true);

    private static readonly Dictionary<IntPtr, (object obj, int amount)> _costs = new();
    private static readonly Dictionary<IntPtr, (object obj, float value)> _effects = new();
    private static bool _applied;

    public static bool Applied => _applied;

    public static float ShopFactor(string rewardStat) =>
        rewardStat == "tool_handvac" ? Math.Max(0.1f, Plugin.HandVacShopPrice.Value) : 1f;

    public static bool Ensure(bool wanted)
    {
        if (wanted == _applied) return false;
        var ss = Game.Skills();
        if (ss == null) return false;
        bool changed = false;
        Feature.Try(() =>
        {
            if (wanted) Apply(ss);
            else Restore();
            changed = true;
        }, wanted ? "apply Balance Patch" : "restore original prices");
        return changed;
    }

    private static (float price, float effect) Rule(string id, string tier)
    {
        if (id.StartsWith("handvac_", StringComparison.Ordinal))
            return (Plugin.HandVacPrice.Value, Plugin.HandVacEffect.Value);
        if (tier == "Throughput")
            return (Plugin.ThroughputPrice.Value, Plugin.ThroughputEffect.Value);
        return (1f, 1f);
    }

    private static void Apply(object ss)
    {
        var plan = new List<(string id, string tier, float price, float effect, object def)>();
        long baseTotal = 0, rawExtra = 0;
        foreach (var (k, def) in R.Pairs(R.Get(ss, "Skills")))
        {
            string id = k?.ToString();
            if (id == null || def == null) continue;
            string tier = "";
            try { tier = R.Get(def, "BalanceTier")?.ToString() ?? ""; } catch { }
            var (price, effect) = Rule(id, tier);
            foreach (var level in R.Items(R.Get(def, "Levels")))
                foreach (var cost in BallCosts(level))
                {
                    int amount = OriginalCost(cost);
                    baseTotal += amount;
                    rawExtra += (long)Math.Round(amount * (price - 1f));
                }
            plan.Add((id, tier, price, effect, def));
        }

        float cap = Math.Max(0, Plugin.MaxExtraCost.Value);
        float scale = rawExtra > cap && rawExtra > 0 ? cap / rawExtra : 1f;

        var report = new StringBuilder();
        report.AppendLine("Playrooms New Game Plus - Balance Patch report");
        report.AppendLine($"Base total upgrade cost: {baseTotal} pennies. Extra before cap: {rawExtra}. Cap: {cap}. Price increase scale: {scale:0.###}");
        report.AppendLine();
        report.AppendLine("upgrade, tier, price factor, effect factor, level costs (original -> new), effect values (original -> new)");

        long newTotal = 0;
        foreach (var (id, tier, price, effect, def) in plan)
        {
            float p = 1f + (price - 1f) * scale;
            var costs = new List<string>();
            var effects = new List<string>();
            foreach (var level in R.Items(R.Get(def, "Levels")))
            {
                foreach (var cost in BallCosts(level))
                {
                    int orig = OriginalCost(cost);
                    int now = Math.Max(1, (int)Math.Round(orig * p));
                    R.Set(cost, "Amount", now);
                    newTotal += now;
                    costs.Add(orig == now ? orig.ToString() : $"{orig}->{now}");
                }
                if (Math.Abs(effect - 1f) > 0.0001f)
                    foreach (var action in EffectActions(level))
                    {
                        float orig = OriginalEffect(action);
                        float now = ScaleBonus(orig, Operation(action), effect);
                        R.Set(action, "BaseValue", now);
                        effects.Add($"{orig:0.###}->{now:0.###}");
                    }
            }
            report.AppendLine($"{id}, {tier}, {p:0.###}, {effect:0.###}, [{string.Join(" ", costs)}], [{string.Join(" ", effects)}]");
        }
        report.AppendLine();
        report.AppendLine($"New total upgrade cost: {newTotal} pennies (+{newTotal - baseTotal}).");
        report.AppendLine($"Hand vacuum shop item price factor: {ShopFactor("tool_handvac"):0.###}");

        _applied = true;
        try { File.WriteAllText(Path.Combine(Paths.ConfigPath, "PlayroomsNewGamePlus.balance-report.txt"), report.ToString()); } catch { }
        Plugin.Log.LogInfo((object)$"Balance Patch on: upgrades cost {newTotal} pennies in total (+{newTotal - baseTotal}, cap {cap})");
    }

    private static void Restore()
    {
        foreach (var (_, (obj, amount)) in _costs) { try { R.Set(obj, "Amount", amount); } catch { } }
        foreach (var (_, (obj, value)) in _effects) { try { R.Set(obj, "BaseValue", value); } catch { } }
        _applied = false;
        Plugin.Log.LogInfo((object)"Balance Patch off: original prices and effects");
    }

    private static IEnumerable<object> BallCosts(object level)
    {
        foreach (var cost in R.Items(R.Get(level, "Costs")))
        {
            var cur = R.Get(cost, "Currency");
            if (cur != null && R.Get<string>(cur, "CurrencyId") == Game.CurrencyId) yield return cost;
        }
    }

    private static IEnumerable<object> EffectActions(object level)
    {
        foreach (var t in R.Items(R.Get(level, "Templates")))
        {
            var st = R.Cast(t, "TwoPixels.Services.Skill.StatusEffectSkillTemplate");
            if (st == null) continue;
            var data = R.Get(st, "StatusEffect");
            if (data == null) continue;
            foreach (var action in R.Items(R.Get(data, "EffectActions"))) yield return action;
        }
    }

    private static int OriginalCost(object cost)
    {
        var p = R.Ptr(cost);
        if (!_costs.TryGetValue(p, out var v))
        {
            v = (cost, Convert.ToInt32(R.Get(cost, "Amount")));
            _costs[p] = v;
        }
        return v.amount;
    }

    private static float OriginalEffect(object action)
    {
        var p = R.Ptr(action);
        if (!_effects.TryGetValue(p, out var v))
        {
            v = (action, Convert.ToSingle(R.Get(action, "BaseValue")));
            _effects[p] = v;
        }
        return v.value;
    }

    private static string Operation(object action)
    {
        try
        {
            var ops = R.Get(action, "Operations");
            if (ops != null && R.Count(ops) > 0) return R.Get(R.At(ops, 0), "OperationType")?.ToString() ?? "";
        }
        catch { }
        return "";
    }

    public static float ScaleBonus(float value, string op, float k) => op switch
    {
        "Multiplication" or "Division" => 1f + (value - 1f) * k,
        "SetValue" => value,
        _ => value * k,
    };
}
