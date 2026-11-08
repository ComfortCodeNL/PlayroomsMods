using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlayroomsNewGamePlus;

internal static class Ui
{
    private sealed class SlotInfo
    {
        public int Slot, Cycle, Mode, Target;
        public bool Completed;
        public object Stats;
        public string Tags;
    }

    private sealed class Pick
    {
        public int Slot, Cycle, Target;
        public object Stats;
        public bool FromEnd;
    }

    private static string _notice;
    private static float _noticeUntil;

    private static GUIStyle _box, _text, _small, _big, _button, _panel, _backdrop;
    private static bool _solid;

    private static bool _menuOpen;
    private static float _slotsAt = -99;
    private static readonly List<SlotInfo> _slots = new();

    private static bool _endShown;
    private static float _endAt = -99;
    private static bool _busy;

    private static Pick _pick;
    private static RunOptions _opts;

    public static readonly Feature Feature = new("Interface", true);

    public static float LastDraw { get; private set; } = -99f;

    private const string Gold = "#FFD866", Dim = "#9A9A9A";

    public static void Notify(string text, float seconds = 5f)
    {
        Plugin.Log.LogInfo((object)("[Notice] " + text));
        if (Coop.Notify(text)) return;
        _notice = text;
        _noticeUntil = Game.Now + seconds;
    }

    public static void Draw()
    {
        if (_text == null)
        {
            _box = new GUIStyle(GUI.skin.box);
            _text = new GUIStyle(GUI.skin.label) { fontSize = 16, richText = true };
            _small = new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true };
            _big = new GUIStyle(GUI.skin.label) { fontSize = 24, richText = true };
            _button = new GUIStyle(GUI.skin.button) { fontSize = 15, richText = true };
            _panel = Solid(0.10f, 0.10f, 0.12f, 0.98f);
            _backdrop = Solid(0f, 0f, 0f, 0.6f);
            _solid = _panel != null && _backdrop != null;
            if (!_solid) { _panel = _box; _backdrop = _box; }
        }

        float now = Game.Now;
        LastDraw = now;
        int w = Screen.width, h = Screen.height;

        if (_notice != null && now < _noticeUntil)
        {
            // same rect and style as Co-op's notice
            GUI.Label(new Rect(20f, h * 0.25f, 900f, 30f), "<b><color=" + Gold + ">" + _notice + "</color></b>", _text);
        }

        bool menu = Game.MainMenuShowing();
        if (_pick != null && !(menu || (_pick.FromEnd && EndScreenShowing(now)))) _pick = null;
        Modal.Want(_pick != null || (_menuOpen && menu && _slots.Count > 0));

        if (_pick != null)
        {
            DrawOptions(w, h);
            return;
        }

        if (Game.MainMenuShowing())
        {
            DrawMainMenu(now, w);
            return;
        }

        if (EndScreenShowing(now))
        {
            DrawEndOffer(w);
            return;
        }

        if (NewGamePlus.ActiveCycle > 0)
        {
            if (Plugin.ShowCycleLabel.Value)
            {
                var o = NewGamePlus.WorldOptions;
                string label = Label(NewGamePlus.ActiveCycle) + (o.Mode is NgMode.SpeedRun or NgMode.Balance ? " " + RunOptions.Short(o.Mode) : "") + (o.Spooky ? " Spooky" : "");
                float lw = 30f + label.Length * 9f;
                GUI.Label(new Rect(w - lw, h - 34f, lw, 28f), "<b><color=" + Gold + ">" + label + "</color></b>", _text);
            }
            if (Tokens.Enabled && Game.TerminalOpen()) DrawTokens(w, h);
        }
    }

    public static string Label(int cycle) => cycle <= 0 ? "" : cycle == 1 ? "NG+" : "NG+" + cycle;

    private static GUIStyle Solid(float r, float g, float b, float a)
    {
        try
        {
            var tex = Activator.CreateInstance(R.T("UnityEngine.Texture2D"), 1, 1);
            var color = Activator.CreateInstance(R.T("UnityEngine.Color"), r, g, b, a);
            R.Call(tex, "SetPixel", 0, 0, color);
            R.Call(tex, "Apply");
            try { R.Set(tex, "hideFlags", 61); } catch { } // HideAndDontSave: survives scene loads
            var style = new GUIStyle(GUI.skin.box);
            R.Set(R.Get(style, "normal"), "background", tex);
            return style;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning((object)("Solid window background unavailable, using the default box: " + ex.Message));
            return null;
        }
    }

    private static void Panel(Rect r)
    {
        int layers = _solid ? 1 : 6;
        for (int i = 0; i < layers; i++) GUI.Box(r, "", _panel);
    }

    private static string Check(bool on) => on ? "<b><color=" + Gold + ">ON</color></b>" : "OFF";

    private static void OpenOptions(Pick pick)
    {
        _pick = pick;
        _opts = RunOptions.FromConfig();
    }

    private static void DrawOptions(int w, int h)
    {
        const float W = 720f, H = 500f;
        float left = Math.Max(10f, (w - W) * 0.5f), top = Math.Max(10f, (h - H) * 0.5f);
        GUI.Box(new Rect(0f, 0f, w, h), "", _backdrop);
        Panel(new Rect(left, top, W, H));

        float x = left + 20f, y = top + 14f;
        GUI.Label(new Rect(x, y, 600f, 34f), "<b><color=" + Gold + ">" + Label(_pick.Cycle).Replace("NG+", "NEW GAME+") + "</color></b>", _big);
        GUI.Label(new Rect(x, y + 32f, 680f, 22f), $"From slot {_pick.Slot + 1} into slot {_pick.Target + 1}. Your collectables, Lexi and the terminal programs always come along.", _small);

        y += 66f;
        GUI.Label(new Rect(x, y, 600f, 24f), "<b>How do you want to replay?</b>", _text);
        y += 30f;
        var modes = new[] { NgMode.Simple, NgMode.SpeedRun, NgMode.Balance };
        float bw = (W - 40f - 2 * 10f) / 3f;
        for (int i = 0; i < modes.Length; i++)
        {
            bool sel = _opts.Mode == modes[i];
            string txt = sel ? "<b><color=" + Gold + ">> " + RunOptions.Name(modes[i]) + " <</color></b>" : RunOptions.Name(modes[i]);
            if (GUI.Button(new Rect(x + i * (bw + 10f), y, bw, 40f), txt, _button)) _opts.Mode = modes[i];
        }

        y += 50f;
        GUI.Box(new Rect(x, y, W - 40f, 96f), "", _box);
        GUI.Box(new Rect(x, y, W - 40f, 96f), "", _box);
        GUI.Label(new Rect(x + 12f, y + 8f, W - 64f, 84f), RunOptions.Summary(_opts.Mode), _small);

        y += 110f;
        GUI.Label(new Rect(x, y, 600f, 24f), "<b>Options</b>", _text);
        y += 30f;
        if (GUI.Button(new Rect(x, y, 70f, 30f), Check(_opts.Spooky), _button)) _opts.Spooky = !_opts.Spooky;
        GUI.Label(new Rect(x + 84f, y + 5f, W - 124f, 24f), "<b>Spooky</b>   " + RunOptions.SpookyText, _small);
        y += 38f;
        if (GUI.Button(new Rect(x, y, 70f, 30f), Check(_opts.Tokens), _button)) _opts.Tokens = !_opts.Tokens;
        GUI.Label(new Rect(x + 84f, y + 5f, W - 124f, 24f), "<b>Tokens</b>   " + RunOptions.TokensText(_pick.Cycle), _small);
        y += 38f;
        if (_opts.Mode == NgMode.SpeedRun)
        {
            GUI.Label(new Rect(x + 84f, y + 5f, W - 124f, 24f), "<color=" + Dim + "><b>Starter kit</b>   Not needed: SpeedRun brings every tool you owned.</color>", _small);
        }
        else
        {
            if (GUI.Button(new Rect(x, y, 70f, 30f), Check(_opts.Kit), _button)) _opts.Kit = !_opts.Kit;
            GUI.Label(new Rect(x + 84f, y + 2f, W - 124f, 36f), "<b>Starter kit</b>   " + RunOptions.KitText, _small);
        }

        float by = top + H - 56f;
        if (GUI.Button(new Rect(x, by, 160f, 40f), "Back", _button))
        {
            _pick = null;
            if (!_menuOpen) Modal.End();
            return;
        }
        if (!_busy && GUI.Button(new Rect(left + W - 20f - 300f, by, 300f, 40f), "<b>Start " + RunOptions.Name(_opts.Mode) + "</b>", _button))
        {
            _busy = true;
            var pick = _pick;
            var opts = _opts.Clone();
            Feature.Try(() =>
            {
                opts.Remember();
                _pick = null;
                _menuOpen = false;
                Modal.End();
                if (!NewGamePlus.Begin(pick.Slot, pick.Stats, pick.FromEnd, opts))
                {
                    _pick = pick;
                    _busy = false;
                }
            }, "start New Game+");
        }
    }

    private static void DrawTokens(int w, int h)
    {
        Panel(new Rect(w - 360f, h - 132f, 340f, 90f));
        int left = Tokens.Left;
        GUI.Label(new Rect(w - 346f, h - 126f, 320f, 24f), $"<b><color={Gold}>Tokens: {left} of {Tokens.Total} left</color></b>", _text);
        if (left <= 0)
        {
            GUI.Label(new Rect(w - 346f, h - 96f, 320f, 40f), "All of this run's free purchases are used.", _small);
            return;
        }
        if (Tokens.Armed)
        {
            GUI.Label(new Rect(w - 346f, h - 98f, 210f, 44f), "Your next upgrade or shop\npurchase is free.", _small);
            if (GUI.Button(new Rect(w - 126f, h - 92f, 96f, 34f), "Cancel", _button)) Tokens.Arm(false);
        }
        else if (GUI.Button(new Rect(w - 346f, h - 94f, 316f, 38f), "<b>Use a token on my next purchase</b>", _button))
        {
            Tokens.Arm(true);
        }
    }

    private static bool EndScreenShowing(float now)
    {
        if (now - _endAt < 0.5f) return _endShown;
        _endAt = now;
        _endShown = false;
        try
        {
            var end = R.FindFirst(Game.EndSequenceT);
            if (end == null) return false;
            var panel = R.Get(end, "_panel");
            if (!Game.Alive(panel)) return false;
            var go = R.Get(panel, "gameObject");
            if (!Convert.ToBoolean(R.Get(go, "activeInHierarchy"))) return false;
            _endShown = Game.Stat(Game.CompletedStat) > 0;
        }
        catch { }
        return _endShown;
    }

    private static void DrawEndOffer(int w)
    {
        int next = NewGamePlus.ActiveCycle + 1;
        float left = w - 440f;
        Panel(new Rect(left, 24f, 410f, 150f));
        GUI.Label(new Rect(left + 16f, 34f, 380f, 30f), "<b><color=" + Gold + ">" + Label(next).Replace("NG+", "NEW GAME+") + " unlocked</color></b>", _big);
        GUI.Label(new Rect(left + 16f, 66f, 380f, 44f),
            "Replay in a new world in the next empty slot:\nSimple, SpeedRun or Balance Patch.", _small);
        if (Coop.IsClient)
        {
            GUI.Label(new Rect(left + 16f, 126f, 378f, 30f), "<b>In co-op, the host starts New Game+ for both of you.</b>", _small);
            return;
        }
        if (!_busy && GUI.Button(new Rect(left + 16f, 120f, 378f, 40f), "<b>New Game+ options</b>", _button))
        {
            Feature.Try(() =>
            {
                int src = NewGamePlus.CurrentSlot >= 0 ? NewGamePlus.CurrentSlot : Game.ContinueSlot();
                int target = NewGamePlus.TargetFor(src);
                if (target < 0)
                {
                    Notify("No empty save slot for New Game+. Delete a slot in the main menu first.", 8f);
                    return;
                }
                OpenOptions(new Pick { Slot = src, Cycle = next, Target = target, Stats = Game.StatDict(), FromEnd = true });
            }, "open options from end screen");
        }
    }

    private static void RefreshSlots(float now)
    {
        if (now - _slotsAt < 2f) return;
        _slotsAt = now;
        _slots.Clear();
        _busy = false;
        Feature.Try(() =>
        {
            int n = Game.SlotCount();
            for (int i = 0; i < n; i++)
            {
                if (!Game.SlotExists(i)) continue;
                var stats = Game.SlotStats(i);
                var info = new SlotInfo
                {
                    Slot = i,
                    Stats = stats,
                    Completed = R.DictInt(stats, Game.CompletedStat) > 0,
                    Cycle = NewGamePlus.CycleIn(stats),
                    Mode = Game.SlotMode(i),
                };
                if (info.Cycle > 0)
                {
                    var mode = (NgMode)R.DictInt(stats, RunOptions.ModeStat);
                    info.Tags = Label(info.Cycle)
                        + (mode is NgMode.SpeedRun or NgMode.Balance ? " " + RunOptions.Short(mode) : "")
                        + (R.DictInt(stats, RunOptions.SpookyStat) > 0 ? " Spooky" : "")
                        + (R.DictInt(stats, RunOptions.TokensStat) > 0 ? " Tokens" : "");
                }
                if (info.Completed || info.Cycle > 0)
                {
                    info.Target = NewGamePlus.TargetFor(i);
                    _slots.Add(info);
                }
            }
        }, "read save slots");
    }

    private static void DrawMainMenu(float now, int w)
    {
        RefreshSlots(now);
        if (_slots.Count == 0) return;

        if (!_menuOpen)
        {
            bool any = _slots.Exists(s => s.Completed);
            string label = any ? "<b>New Game+ available</b>" : "<b>New Game+</b>";
            if (GUI.Button(new Rect(w - 250f, 20f, 230f, 36f), label, _button)) _menuOpen = true;
            return;
        }

        float h = 84f + _slots.Count * 56f;
        float left = w - 600f;
        Panel(new Rect(left, 20f, 580f, h));
        GUI.Label(new Rect(left + 16f, 28f, 400f, 32f), "<b><color=" + Gold + ">NEW GAME+</color></b>", _big);
        if (GUI.Button(new Rect(left + 534f, 28f, 32f, 28f), "X", _button))
        {
            _menuOpen = false;
            Modal.End();
        }

        float y = 68f;
        foreach (var s in _slots)
        {
            string tag = s.Tags != null ? "  <color=" + Gold + "><b>" + s.Tags + "</b></color>" : "";
            string state = s.Completed ? "finished" : "in progress";
            GUI.Label(new Rect(left + 16f, y, 380f, 26f), "<b>Slot " + (s.Slot + 1) + "</b>" + tag + "   <size=13>" + Game.ModeName(s.Mode) + ", " + state + "</size>", _text);
            if (s.Completed && Coop.IsClient)
            {
                GUI.Label(new Rect(left + 16f, y + 24f, 540f, 22f), "<size=12>Joined as a guest: the host starts New Game+ for everyone.</size>", _small);
            }
            else if (s.Completed)
            {
                string into = s.Target >= 0 ? "into slot " + (s.Target + 1) : "no empty slot";
                GUI.Label(new Rect(left + 16f, y + 24f, 370f, 22f), "<size=12>Next: " + Label(s.Cycle + 1) + ", " + into + "</size>", _small);
                if (s.Target >= 0 && !_busy && GUI.Button(new Rect(left + 390f, y + 6f, 176f, 36f), "<b>Start " + Label(s.Cycle + 1) + "</b>", _button))
                {
                    var pick = s;
                    OpenOptions(new Pick { Slot = pick.Slot, Cycle = pick.Cycle + 1, Target = pick.Target, Stats = pick.Stats, FromEnd = false });
                }
            }
            else
            {
                GUI.Label(new Rect(left + 16f, y + 24f, 540f, 22f), "<size=12>Finish this run to unlock " + Label(s.Cycle + 1) + ".</size>", _small);
            }
            y += 56f;
        }
    }
}
