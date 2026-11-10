using System;
using System.Collections.Generic;

namespace PlayroomsNewGamePlus;

internal static class Gates
{
    public static readonly Feature Feature = new("Unlocks", true);

    public static bool Replaying;

    private static readonly HashSet<IntPtr> _rules = new();
    private static float _rulesAt = -99, _checkedAt = -99;
    private static int _registryCount = -1;

    public static bool On => NewGamePlus.IsNgWorld && Plugin.OpenUnlocks.Value && Feature.Enabled;

    public static void Invalidate() => _rulesAt = _checkedAt = -99;

    public static void Install()
    {
        var skill = R.T(Game.SkillServiceT);
        R.Patch(skill, "IsCategoryUnlocked", 1, typeof(Hooks), null, nameof(Hooks.TruePostfix));
        R.Patch(skill, "GetLockingRoomGate", 1, typeof(Hooks), null, nameof(Hooks.NoGatePostfix));
        R.Patch(skill, "GetRoomGate", 2, typeof(Hooks), null, nameof(Hooks.NoGatePostfix));

        var rule = R.T("TwoPixels.Balancing.UnlockRule");
        R.Patch(rule, "IsUnlocked", 0, typeof(Hooks), null, nameof(Hooks.RulePostfix));
        R.Patch(rule, "IsUnlockedExceptRoomGate", 0, typeof(Hooks), null, nameof(Hooks.RulePostfix));

        var shop = R.T(Game.PurchaseT);
        R.Patch(shop, "get_IsWaitingForRoom", 0, typeof(Hooks), null, nameof(Hooks.FalsePostfix));
        R.Patch(shop, "get_IsWaitingForBallPit", 0, typeof(Hooks), null, nameof(Hooks.FalsePostfix));
        R.Patch(shop, "get_IsCopyCapped", 0, typeof(Hooks), null, nameof(Hooks.FalsePostfix));
        R.Patch(shop, "get_CurrentRoomGate", 0, typeof(Hooks), null, nameof(Hooks.NoGatePostfix));
        R.Patch(shop, "get_IsAvailable", 0, typeof(Hooks), null, nameof(Hooks.ShopAvailablePostfix));

        R.Patch(shop, "get_CurrentPrice", 0, typeof(Hooks), null, nameof(Hooks.ShopPricePostfix));
        R.Patch(shop, "CompletePurchase", 0, typeof(Hooks), null, nameof(Hooks.ShopBoughtPostfix));
        R.Patch(skill, "CanAfford", 1, typeof(Hooks), null, nameof(Hooks.CanAffordPostfix));
        R.Patch(skill, "CanUnlock", 1, typeof(Hooks), null, nameof(Hooks.CanUnlockPostfix));
        R.Patch(skill, "UnlockSkill", 1, typeof(Hooks), nameof(Hooks.UnlockSkillPrefix), nameof(Hooks.UnlockSkillPostfix));

        R.Patch(R.T("TwoPixels.Windows95.Windows95Desktop"), "IsInstalled", 1, typeof(Hooks), null, nameof(Hooks.AppInstalledPostfix));
        R.Patch(R.T("TwoPixels.Windows95.SupervisorMailbox"), "HasArrived", 1, typeof(Hooks), null, nameof(Hooks.MailArrivedPostfix));
    }

    public static bool IsOurRule(object rule)
    {
        Refresh();
        return _rules.Contains(R.Ptr(rule));
    }

    private static void Refresh()
    {
        float now = Game.Now;
        if (_rules.Count > 0 && now - _checkedAt < 2f) return;
        _checkedAt = now;
        List<object> shopItems = null;
        try { shopItems = Game.ShopItems(); } catch { }
        int regCount = shopItems?.Count ?? -1;
        if (now - _rulesAt < 15f && regCount == _registryCount && _rules.Count > 0) return;
        _rulesAt = now;
        _registryCount = regCount;
        _rules.Clear();
        Feature.Try(() =>
        {
            var ss = Game.Skills();
            if (ss != null)
            {
                foreach (var (_, def) in R.Pairs(R.Get(ss, "Skills")))
                {
                    Add(R.Get(def, "Unlock"));
                    var cat = R.Get(def, "Category");
                    if (cat != null) Add(R.Get(cat, "Unlock"));
                }
            }
            if (shopItems != null)
                foreach (var item in shopItems) Add(R.Get(item, "Unlock"));
        }, "collect unlock rules");
    }

    private static void Add(object rule)
    {
        var p = R.Ptr(rule);
        if (p != IntPtr.Zero) _rules.Add(p);
    }
}
