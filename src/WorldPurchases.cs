using System;
using System.Collections.Generic;

namespace PlayroomsCoop
{
    internal static class Unlocks
    {
        private const string StatServiceT = "TwoPixels.Services.Statistic.StatisticService";

        public static Feature Feature;
        private static bool _connected, _baselined, _snapshotSent, _isHost;
        private static bool _applying;
        private static object _stats;
        private static readonly Dictionary<string, int> _last = new Dictionary<string, int>();
        private static readonly HashSet<string> _known = new HashSet<string>();
        private static readonly List<string> _prefixes = new List<string>();
        private static readonly HashSet<string> _levels = new HashSet<string>();
        private static readonly string[] IntroKeys = { "first_door_opened" };
        private static float _nextPoll, _lastKeyScan = -99f;

        public static void Init() => Feature = new Feature("Unlocks", Plugin.SyncUnlocks.Value);

        public static void OnConnected(bool isHost)
        {
            _connected = true; _isHost = isHost; _baselined = false; _snapshotSent = false;
            _last.Clear();
        }
        public static void OnDisconnected() { _connected = false; }

        private static object Stats()
        {
            if (World.Alive(_stats)) return _stats;
            _stats = null;
            foreach (var s in R.FindAll(R.T(StatServiceT))) { _stats = s; break; }
            return _stats;
        }

        private static void TryAddKey(object o, string member)
        {
            try { var k = R.Get<string>(o, member); if (!string.IsNullOrEmpty(k)) _known.Add(k); } catch { }
        }

        private static void ScanKeys(float now)
        {
            _lastKeyScan = now;
            foreach (var k in IntroKeys) _known.Add(k);
            foreach (var it in R.FindAll(R.T("TwoPixels.Interactables.PurchaseInteractable"))) TryAddKey(it, "rewardStatisticName");
            foreach (var v in R.FindAll(R.T("TwoPixels.Ball.BallVent"))) TryAddKey(v, "UnlockStatistic");
            foreach (var d in R.FindAll(R.T("TwoPixels.Interactables.DoorInteractable"))) TryAddKey(d, "UnlockStatisticName");
            foreach (var c in R.FindAll(R.T("TwoPixels.Collectables.CollectableInteractable")))
            {
                try { var def = R.Get(c, "definition"); if (def != null) TryAddKey(def, "Statistic"); } catch { }
            }
            try
            {
                var dt = R.T("TwoPixels.Collectables.CollectableDefinition");
                if (dt != null && R.GetStatic(dt, "StatisticPrefix") is string pre && pre.Length > 0 && !_prefixes.Contains(pre)) _prefixes.Add(pre);
            }
            catch { }
            foreach (var c in R.FindAll(R.T("TwoPixels.Display.DayTimeClock"))) TryAddKey(c, "UnlockStatistic");
            foreach (var tv in R.FindAll(R.T("TwoPixels.Collectables.CleanroomTv")))
            {
                try { var k = R.Get<string>(tv, "ChannelStatistic"); if (!string.IsNullOrEmpty(k)) _levels.Add(k); } catch { }
            }
            foreach (var tn in new[] { "TwoPixels.Ball.BallVent", "TwoPixels.Ball.RobotVacuum", "TwoPixels.Ball.RobotStation" })
            {
                var t = R.T(tn);
                if (t == null) continue;
                try { var k = R.GetStatic(t, "OwnedStatistic") as string; if (!string.IsNullOrEmpty(k)) _known.Add(k); } catch { }
            }
        }

        private static bool IsUnlockKey(string key)
        {
            if (_known.Contains(key) || _levels.Contains(key)) return true;
            foreach (var pre in _prefixes) if (key.StartsWith(pre, StringComparison.Ordinal)) return true;
            string k = key.ToLowerInvariant();
            return k.Contains("unlock") || k.Contains("owned") || k.Contains("purchas") || k.Contains("bought") || k.Contains("granted");
        }

        private static Dictionary<string, int> Read(object stats)
        {
            var result = new Dictionary<string, int>();
            var dict = R.Get(stats, "Statistics");
            foreach (var (k, v) in R.Pairs(dict))
            {
                string key = k as string ?? k?.ToString();
                if (!string.IsNullOrEmpty(key) && IsUnlockKey(key)) result[key] = Convert.ToInt32(v);
            }
            return result;
        }

        public static void Tick(float now)
        {
            if (!_connected || !Feature.Enabled || now < _nextPoll) return;
            _nextPoll = now + 0.5f;
            Feature.Try(() =>
            {
                var stats = Stats();
                if (stats == null) return;
                if (now - _lastKeyScan > 10f) ScanKeys(now);
                var cur = Read(stats);

                var changed = new List<(string, int)>();
                foreach (var kv in cur)
                {
                    bool had = _last.TryGetValue(kv.Key, out int last);
                    if (_levels.Contains(kv.Key))
                    {
                        if ((!_snapshotSent && _isHost) || (had && kv.Value != last) || (!had && _baselined))
                            changed.Add((kv.Key, kv.Value));
                    }
                    else if (!_snapshotSent || (had && kv.Value > last) || (!had && _baselined && kv.Value > 0))
                        changed.Add((kv.Key, kv.Value));
                    _last[kv.Key] = kv.Value;
                }
                _baselined = true;
                _snapshotSent = true;
                if (changed.Count == 0) return;

                for (int start = 0; start < changed.Count; start += 500)
                {
                    int n = Math.Min(500, changed.Count - start);
                    var w = Protocol.W.Begin(Msg.StatSet);
                    w.U16((ushort)n);
                    for (int i = 0; i < n; i++) { w.Str(changed[start + i].Item1); w.I32(changed[start + i].Item2); }
                    w.SendAll(SteamNet.SendReliable);
                }
            }, "poll unlocks");
        }

        public static void Handle(byte[] buf, int len)
        {
            if (!Feature.Enabled) return;
            var r = new Reader(buf, len, Protocol.HeaderSize);
            int n = r.U16();
            var items = new List<(string, int)>();
            for (int i = 0; i < n; i++) items.Add((r.Str(), r.I32()));
            Feature.Try(() =>
            {
                var stats = Stats();
                if (stats == null) return;
                if (_lastKeyScan < 0f) ScanKeys(UnityEngine.Time.realtimeSinceStartup);
                var cur = Read(stats);
                int applied = 0;
                foreach (var (key, value) in items)
                {
                    if (value < 0 || value > 100000) continue;
                    if (!IsUnlockKey(key)) { float t = UnityEngine.Time.realtimeSinceStartup; if (t - _lastKeyScan > 10f) ScanKeys(t); if (!IsUnlockKey(key)) continue; }
                    cur.TryGetValue(key, out int local);
                    if (_levels.Contains(key) ? value == local : value <= local) continue;
                    _applying = true;
                    try { R.Call(stats, "SetStatistic", key, value); applied++; }
                    finally { _applying = false; }
                    _last[key] = value;
                }
                if (applied > 0)
                {
                    Plugin.Log.LogInfo($"Another player's world purchases applied: {applied}");
                }
            }, "apply unlocks");
        }
    }
}
