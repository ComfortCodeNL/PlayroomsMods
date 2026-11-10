using System;
using System.Collections.Generic;

namespace PlayroomsNewGamePlus;

internal static class Spooky
{
    public static readonly Feature Feature = new("Spooky", true);

    private static readonly Dictionary<IntPtr, (object director, int min, int max, float perScare, float cooldown)> _orig = new();
    private static IntPtr _boosted = IntPtr.Zero;

    public static void Ensure(bool wanted)
    {
        Feature.Try(() =>
        {
            var t = R.T("TwoPixels.EasterEgg.HorrorDirector");
            if (t == null) return;
            var d = R.GetStatic(t, "Instance");
            if (!Game.Alive(d)) return;
            var ptr = R.Ptr(d);

            if (wanted && _boosted != ptr)
            {
                if (!_orig.ContainsKey(ptr))
                    _orig[ptr] = (d, Convert.ToInt32(R.Get(d, "minScaresPerDay")), Convert.ToInt32(R.Get(d, "maxScaresPerDay")),
                                  Convert.ToSingle(R.Get(d, "dayMinutesPerScare")), Convert.ToSingle(R.Get(d, "cooldownAfterScare")));
                var o = _orig[ptr];
                float m = Math.Max(1f, Plugin.SpookyMultiplier.Value);
                R.Set(d, "minScaresPerDay", (int)Math.Ceiling(o.min * m));
                R.Set(d, "maxScaresPerDay", (int)Math.Ceiling(o.max * m));
                R.Set(d, "dayMinutesPerScare", o.perScare / m);
                R.Set(d, "cooldownAfterScare", o.cooldown / m);
                try { R.Call(d, "ResetDay"); } catch { }
                _boosted = ptr;
                Plugin.Log.LogInfo((object)$"Spooky on: {o.min}-{o.max} scares a day become {Math.Ceiling(o.min * m)}-{Math.Ceiling(o.max * m)}");
            }
            else if (!wanted && _boosted == ptr && _orig.TryGetValue(ptr, out var o))
            {
                R.Set(d, "minScaresPerDay", o.min);
                R.Set(d, "maxScaresPerDay", o.max);
                R.Set(d, "dayMinutesPerScare", o.perScare);
                R.Set(d, "cooldownAfterScare", o.cooldown);
                _boosted = IntPtr.Zero;
                Plugin.Log.LogInfo((object)"Spooky off: normal scares");
            }
        }, "adjust scares");
    }
}
