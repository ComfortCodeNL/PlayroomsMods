using System;

namespace PlayroomsNewGamePlus;

internal enum NgMode
{
    Legacy = 0,
    Simple = 1,
    SpeedRun = 2,
    Balance = 3,
}

internal sealed class RunOptions
{
    public NgMode Mode = NgMode.Simple;
    public bool Spooky;
    public bool Tokens;
    public bool Kit;

    // names contain "unlock" so Co-op syncs them to every player
    public const string ModeStat = "ngplus_unlocked_mode";
    public const string SpookyStat = "ngplus_unlocked_spooky";
    public const string TokensStat = "ngplus_unlocked_tokens";
    public const string TokensUsedStat = "ngplus_unlocked_tokens_used";

    public RunOptions Clone() => (RunOptions)MemberwiseClone();

    public bool KitApplies => Kit && Mode != NgMode.SpeedRun;

    public static RunOptions FromConfig() => new()
    {
        Mode = Enum.IsDefined(typeof(NgMode), Plugin.LastMode.Value) && Plugin.LastMode.Value != 0
            ? (NgMode)Plugin.LastMode.Value
            : NgMode.Simple,
        Spooky = Plugin.LastSpooky.Value,
        Tokens = Plugin.LastTokens.Value,
        Kit = Plugin.LastKit.Value,
    };

    public void Remember()
    {
        Plugin.LastMode.Value = (int)Mode;
        Plugin.LastSpooky.Value = Spooky;
        Plugin.LastTokens.Value = Tokens;
        Plugin.LastKit.Value = Kit;
    }

    public string Serialize() => $"{(int)Mode},{(Spooky ? 1 : 0)},{(Tokens ? 1 : 0)},{(Kit ? 1 : 0)}";

    public static RunOptions Parse(string s)
    {
        var o = new RunOptions();
        if (string.IsNullOrEmpty(s)) return o;
        var a = s.Split(',');
        if (a.Length > 0 && int.TryParse(a[0], out var m) && Enum.IsDefined(typeof(NgMode), m)) o.Mode = (NgMode)m;
        o.Spooky = a.Length > 1 && a[1] == "1";
        o.Tokens = a.Length > 2 && a[2] == "1";
        o.Kit = a.Length > 3 && a[3] == "1";
        return o;
    }

    public static string Name(NgMode m) => m switch
    {
        NgMode.SpeedRun => "NG+ SpeedRun",
        NgMode.Balance => "NG+ Balance Patch",
        _ => "NG+ Simple",
    };

    public static string Short(NgMode m) => m switch
    {
        NgMode.SpeedRun => "SpeedRun",
        NgMode.Balance => "Balance",
        NgMode.Simple => "Simple",
        _ => "",
    };

    public static string Summary(NgMode m) => m switch
    {
        NgMode.SpeedRun =>
            "Start as strong as possible. Every upgrade level and every tool you owned comes\n" +
            "with you: robots, ball vacuums, hand vacuum, broom, flashlight, radar and more.\n" +
            "Money, world purchases like vents, and the story start over.",
        NgMode.Balance =>
            "Like Simple: shop and upgrades open from day 1, chains still apply. The hand\n" +
            $"vacuum is retuned: its upgrades cost {Plugin.HandVacPrice.Value:0.##}x and give " +
            $"{(1f - Plugin.HandVacEffect.Value) * 100f:0}% less bonus, and the hand\n" +
            $"vacuum itself costs {Plugin.HandVacShopPrice.Value:0.##}x." +
            (Math.Abs(Plugin.ThroughputPrice.Value - 1f) > 0.001f ? $" Other throughput upgrades cost {Plugin.ThroughputPrice.Value:0.##}x." : "") +
            "\nExtra cost is capped so every upgrade stays affordable.",
        _ =>
            "A fresh world with the shop and every upgrade open from day 1. Buy upgrades in\n" +
            "any order; upgrades that need another one first still do. You start with\n" +
            "nothing but your collectables, Lexi and the terminal programs.",
    };

    public const string SpookyText = "More entity ambience: twice the scares each day, shorter gaps between them.";
    public static string TokensText(int cycle) =>
        $"{cycle} free purchase{(cycle == 1 ? "" : "s")} this run (one per NG+ cycle), for any upgrade or shop item.";
    public const string KitText = "Starter kit: charging station, robot vacuum, ball vacuum, hand vacuum,\nflashlight, radar and broom as you wake up.";
}
