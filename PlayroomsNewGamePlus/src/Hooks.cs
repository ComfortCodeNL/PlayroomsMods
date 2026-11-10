using System;

namespace PlayroomsNewGamePlus;

internal static class Hooks
{
    public static void StartGamePostfix(int slot, bool freshStart)
    {
        try { NewGamePlus.OnGameStarting(slot, freshStart, true); }
        catch (Exception ex) { Plugin.Log.LogWarning((object)"StartGame hook: " + ex.Message); }
    }

    public static void ActivateSlotPrefix(int slot, bool freshStart)
    {
        try { NewGamePlus.OnGameStarting(slot, freshStart, false); }
        catch (Exception ex) { Plugin.Log.LogWarning((object)"ActivateSlot hook: " + ex.Message); }
    }

    public static void TruePostfix(ref bool __result)
    {
        if (!__result && Gates.On) __result = true;
    }

    public static void FalsePostfix(ref bool __result)
    {
        if (__result && Gates.On) __result = false;
    }

    public static void NoGatePostfix(ref string __result)
    {
        if (__result != null && Gates.On) __result = null;
    }

    public static void RulePostfix(object __instance, ref bool __result)
    {
        if (__result || !Gates.On) return;
        try
        {
            if (Gates.IsOurRule(__instance)) __result = true;
        }
        catch { }
    }

    public static void ShopAvailablePostfix(object __instance, ref bool __result)
    {
        if (__result || !Gates.On) return;
        try
        {
            bool soldOut = Convert.ToBoolean(R.Get(__instance, "IsSoldOut"));
            bool demo = Convert.ToBoolean(R.Get(__instance, "IsDemoLocked"));
            if (!soldOut && !demo) __result = true;
        }
        catch { }
    }

    public static void ShopPricePostfix(object __instance, ref int __result)
    {
        try
        {
            if (__result > 0 && Balance.Applied && NewGamePlus.IsNgWorld && !Gates.Replaying)
            {
                float f = Balance.ShopFactor(R.Get<string>(__instance, "RewardStatisticName"));
                if (f != 1f) __result = Math.Max(1, (int)Math.Round(__result * f));
            }
            Tokens.ShopPrice(ref __result);
        }
        catch { }
    }

    public static void ShopBoughtPostfix()
    {
        try { Tokens.AfterShopPurchase(); } catch { }
    }

    public static void CanAffordPostfix(ref bool __result)
    {
        try { Tokens.CanAfford(ref __result); } catch { }
    }

    public static bool UnlockSkillPrefix(string skillId)
    {
        try { Tokens.BeforeUnlockSkill(skillId); } catch { }
        return UpgradeUnlock.UnlockSkillPrefix(skillId);
    }

    public static void CanUnlockPostfix(string skillId, ref bool __result)
    {
        UpgradeUnlock.CanUnlockPostfix(skillId, ref __result);
    }

    public static void UnlockSkillPostfix(string skillId)
    {
        try { Tokens.AfterUnlockSkill(skillId); } catch { }
    }

    public static void AppInstalledPostfix(string mailId, ref bool __result)
    {
        if (__result || !Gates.On) return;
        if (mailId == "install_shop" || mailId == "install_skills") __result = true;
    }

    public static void MailArrivedPostfix(string mailId, ref bool __result)
    {
        if (__result || !NewGamePlus.IsNgWorld || !Plugin.InstallPrograms.Value) return;
        if (Array.IndexOf(NewGamePlus.ProgramMails, mailId) >= 0) __result = true;
    }
}
