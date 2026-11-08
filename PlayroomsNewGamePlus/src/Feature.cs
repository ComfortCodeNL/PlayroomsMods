using System;
using System.Reflection;

namespace PlayroomsNewGamePlus;

internal sealed class Feature
{
    public readonly string Name;
    public bool Enabled;
    private int _errors;

    public Feature(string name, bool enabled)
    {
        Name = name;
        Enabled = enabled;
    }

    public bool Try(Action a, string what)
    {
        if (!Enabled) return false;
        try
        {
            a();
            return true;
        }
        catch (Exception ex)
        {
            var inner = (ex as TargetInvocationException)?.InnerException ?? ex;
            _errors++;
            if (_errors <= 5) Plugin.Log.LogWarning((object)"[" + Name + "] " + what + ": " + inner.GetType().Name + ": " + inner.Message);
            if (_errors >= 40)
            {
                Enabled = false;
                Plugin.Log.LogError((object)"[" + Name + "] turned off after repeated errors. The rest of New Game+ keeps working.");
            }
            return false;
        }
    }
}
