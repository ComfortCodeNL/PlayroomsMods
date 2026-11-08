using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace PlayroomsCoop
{
    internal static class Perf
    {
        private static readonly Dictionary<string, double> _ms = new Dictionary<string, double>();
        private static readonly Stack<(string name, long start, long child)> _stack = new Stack<(string, long, long)>();
        private static readonly Stopwatch _clock = Stopwatch.StartNew();
        private static double _frameMs, _frameMax;
        private static int _frames;
        private static float _nextLog = 30f;

        public static bool On => Plugin.PerfLog.Value;

        public static void Begin(string name)
        {
            if (On) _stack.Push((name, _clock.ElapsedTicks, 0));
        }

        public static bool BeginTop(string name)
        {
            if (!On || _stack.Count > 0) return false;
            _stack.Push((name, _clock.ElapsedTicks, 0));
            return true;
        }

        public static void End()
        {
            if (!On || _stack.Count == 0) return;
            var (name, start, child) = _stack.Pop();
            long total = _clock.ElapsedTicks - start;
            _ms.TryGetValue(name, out double v);
            _ms[name] = v + (total - child) * 1000.0 / Stopwatch.Frequency;
            if (_stack.Count > 0) { var p = _stack.Pop(); _stack.Push((p.name, p.start, p.child + total)); }
        }

        public static void Frame(double ms, float now)
        {
            if (!On) return;
            _frameMs += ms; _frames++;
            if (ms > _frameMax) _frameMax = ms;
            if (now < _nextLog) return;
            _nextLog = now + 30f;
            if (_frames == 0) return;
            var top = _ms.OrderByDescending(kv => kv.Value).Take(8)
                         .Select(kv => $"{kv.Key} {kv.Value / _frames:0.00}");
            Plugin.Log.LogInfo($"[Perf] mod time per frame: avg {_frameMs / _frames:0.00} ms, worst {_frameMax:0.0} ms over {_frames} frames | " + string.Join(", ", top));
            _ms.Clear(); _frameMs = 0; _frameMax = 0; _frames = 0;
        }
    }
}
