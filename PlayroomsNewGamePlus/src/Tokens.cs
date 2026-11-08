using System;

namespace PlayroomsNewGamePlus;

internal static class Tokens
{
    public static bool Armed { get; private set; }

    private static string _skill;
    private static int _levelBefore, _granted;

    public static bool Enabled => NewGamePlus.IsNgWorld && NewGamePlus.WorldOptions.Tokens;

    public static int Total => Enabled ? Math.Max(0, NewGamePlus.WorldCycle) : 0;

    public static int Used
    {
        get { try { return Math.Max(0, Game.Stat(RunOptions.TokensUsedStat)); } catch { return 0; } }
    }

    public static int Left => Math.Max(0, Total - Used);

    public static void Arm(bool on)
    {
        Armed = on && Left > 0;
        if (Armed) Ui.Notify($"Token ready: your next upgrade or shop purchase is free ({Left} left).");
    }

    public static void Tick()
    {
        if (Armed && (!Enabled || Left <= 0)) Armed = false;
    }

    private static bool Active => Armed && Enabled && Left > 0 && !Gates.Replaying && !Coop.ReplayingPurchase;

    private static void Consume(string what, int price)
    {
        Armed = false;
        int total = Total;
        bool asked = Coop.TryTakeCounter(RunOptions.TokensUsedStat, total, granted =>
        {
            if (granted)
            {
                Ui.Notify($"Token used on {what}. {Math.Max(0, Left)} left.");
                return;
            }
            // someone else took the last token first: this one is paid for normally
            var ss = Game.Skills();
            bool paid = price <= 0 || (ss != null && Convert.ToBoolean(R.Call(ss, "SpendCurrency", Game.CurrencyId, price, 0)));
            Ui.Notify(paid
                ? $"No tokens left (another player used the last one), so {what} cost ${price / 100f:0.00}."
                : $"No tokens left (another player used the last one), and there wasn't enough money to pay for {what}.");
        });
        if (asked) return;

        Game.SetStat(RunOptions.TokensUsedStat, Used + 1);
        Ui.Notify($"Token used on {what}. {Left} token{(Left == 1 ? "" : "s")} left.");
    }

    public static void CanAfford(ref bool result)
    {
        if (!result && Active) result = true;
    }

    public static void BeforeUnlockSkill(string skillId)
    {
        _skill = null;
        if (!Active) return;
        var def = Game.SkillDef(skillId);
        if (def == null) return;
        _levelBefore = Game.SkillLevel(skillId);
        _granted = Game.NextLevelCost(def, _levelBefore);
        Game.Grant(_granted);
        _skill = skillId;
    }

    public static void AfterUnlockSkill(string skillId)
    {
        if (_skill == null || _skill != skillId) return;
        _skill = null;
        if (Game.SkillLevel(skillId) > _levelBefore)
        {
            Consume("an upgrade", _granted);
        }
        else if (_granted > 0)
        {
            var ss = Game.Skills();
            if (ss != null) R.Call(ss, "SpendCurrency", Game.CurrencyId, _granted, 0);
        }
    }

    private static int _lastShopPrice;

    public static void ShopPrice(ref int price)
    {
        if (price > 0 && Active)
        {
            _lastShopPrice = price;
            price = 0;
        }
    }

    public static void AfterShopPurchase()
    {
        if (Active) Consume("a shop item", _lastShopPrice);
    }
}
