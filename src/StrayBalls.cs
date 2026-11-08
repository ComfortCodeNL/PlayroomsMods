using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace PlayroomsCoop
{
    internal static partial class World
    {
        public static Feature Strays;

        private static MethodInfo _sample, _linecast, _checkSphere;
        private static PropertyInfo _hitPos;
        private static object _qtiIgnore;
        private static readonly List<object> _navFilters = new List<object>();
        private static int _sightMask;
        private static float _ballR = 0.1f;
        private static bool _strayReady, _strayBroken;

        private static readonly List<(string room, int idx, object spot)> _strayScan = new List<(string, int, object)>();
        private static readonly Queue<object> _strayRecheck = new Queue<object>();
        private static IntPtr _strayWorld;
        private static readonly List<(string room, int idx, object spot, Vector3 to)> _strayFound = new List<(string, int, object, Vector3)>();
        private static bool _strayFilling;
        private static int _strayCursor, _strayChecked, _strayNoMesh, _strayLastDay = -1;
        private static float _nextStrayPass = 10f;
        private static readonly System.Random _strayRng = new System.Random();

        private const int StrayPerFrame = 40;
        private const float StrayMaxShare = 0.03f;
        private static readonly float[] SightHeights = { 0.3f, 1.2f, 2.2f };

        private static void StrayInit() => Strays = new Feature("StrayBalls", Plugin.FixStrayBalls.Value);

        private static bool StrayBind()
        {
            if (_strayReady) return true;
            if (_strayBroken) return false;
            var nav = R.T("UnityEngine.AI.NavMesh"); var phys = R.T("UnityEngine.Physics");
            var qti = R.T("UnityEngine.QueryTriggerInteraction");
            if (nav == null || phys == null || qti == null) return false;
            foreach (var m in nav.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                var ps = m.GetParameters();
                if (m.Name == "SamplePosition" && ps.Length == 4 && ps[3].ParameterType.Name == "NavMeshQueryFilter") _sample = m;
            }
            foreach (var m in phys.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                var ps = m.GetParameters();
                if (m.Name == "Linecast" && ps.Length == 4 && ps[2].ParameterType == typeof(int) && ps[3].ParameterType == qti) _linecast = m;
                if (m.Name == "CheckSphere" && ps.Length == 4 && ps[2].ParameterType == typeof(int) && ps[3].ParameterType == qti) _checkSphere = m;
            }
            if (_sample == null || _linecast == null || _checkSphere == null)
            {
                _strayBroken = true;
                Plugin.Log.LogWarning("[Strays] NavMesh or physics queries not found; out-of-bounds fix off");
                return false;
            }
            _hitPos = _sample.GetParameters()[1].ParameterType.GetElementType().GetProperty("position");
            var ftype = _sample.GetParameters()[3].ParameterType;
            var ids = new List<int>();
            try
            {
                int n = Convert.ToInt32(R.CallStatic(nav, "GetSettingsCount"));
                for (int i = 0; i < n; i++) ids.Add(R.Get<int>(R.CallStatic(nav, "GetSettingsByIndex", i), "agentTypeID"));
            }
            catch { }
            if (ids.Count == 0) ids.Add(0);
            foreach (int id in ids)
            {
                var f = Activator.CreateInstance(ftype);
                ftype.GetProperty("areaMask").SetValue(f, -1);
                ftype.GetProperty("agentTypeID").SetValue(f, id);
                _navFilters.Add(f);
            }
            _qtiIgnore = Enum.ToObject(qti, 1);
            _sightMask = WallMask();
            try { _ballR = Math.Max(0.03f, Convert.ToSingle(R.Call(_ballService, "get_BallRadius"))); } catch { }
            _strayReady = true;
            return true;
        }

        private static int WallMask()
        {
            foreach (var tn in new[] { "TwoPixels.Ball.BallVent", "TwoPixels.Player.ShovelBehaviour" })
            {
                try
                {
                    var v = R.GetStatic(R.T(tn), "SightMask");
                    if (v != null) { int m = v is int i ? i : R.Get<int>(v, "value"); if (m != 0) return m & NonWallLayersOff(); }
                }
                catch { }
            }
            return ~(1 << 2) & NonWallLayersOff();
        }

        private static int NonWallLayersOff()
        {
            int mask = ~0;
            try
            {
                foreach (var b in R.Items(R.Get(_ballService, "_activePhysicsBalls"))) { mask &= ~(1 << R.Get<int>(((Component)b).gameObject, "layer")); break; }
                var body = PlayerBody(); if (body != null) mask &= ~(1 << R.Get<int>(body.gameObject, "layer"));
            }
            catch { }
            return mask;
        }

        private static Vector3? NavPoint(Vector3 p)
        {
            Vector3? best = null; float bestD = float.MaxValue;
            foreach (var f in _navFilters)
            {
                var args = new object[] { p, null, 3f, f };
                if (!(bool)_sample.Invoke(null, args) || args[1] == null) continue;
                var q = (Vector3)_hitPos.GetValue(args[1]);
                float d = Dist(p, q);
                if (d < bestD) { bestD = d; best = q; }
            }
            return best;
        }

        private static bool InBounds(Vector3 p, out Vector3? nav)
        {
            nav = null;
            if ((bool)_checkSphere.Invoke(null, new object[] { p, _ballR * 0.6f, _sightMask, _qtiIgnore })) { nav = NavPoint(p); return false; }
            nav = NavPoint(p);
            if (nav == null) return false;
            foreach (float h in SightHeights)
            {
                var q = nav.Value + new Vector3(0f, h, 0f);
                if (!(bool)_linecast.Invoke(null, new object[] { p, q, _sightMask, _qtiIgnore })) return true;
            }
            return false;
        }

        private static void StrayTick(float now)
        {
            if (!InGameWorld() || !StrayBind()) return;
            try { if (Convert.ToBoolean(R.GetStatic(R.T("TwoPixels.Hotel.HotelNavMesh"), "IsRebuilding"))) return; } catch { }
            IntPtr world = R.Ptr(_ballService);
            if (world != _strayWorld)
            {
                _strayWorld = world; _strayScan.Clear(); _strayFound.Clear(); _strayRecheck.Clear(); _strayOk.Clear();
                _gather.Clear(); _strayFilling = false; _nextStrayPass = now + 8f; _strayLastDay = -1; _strayRetries = 0;
                return;
            }

            int budget = StrayPerFrame;
            while (budget > 0 && _strayRecheck.Count > 0)
            {
                var spot = _strayRecheck.Dequeue();
                var room = R.Get(spot, "_room");
                if (room == null || !Alive(room)) continue;
                try { if (Convert.ToBoolean(R.Get(room, "IsHidden"))) continue; } catch { }
                int idx = IndexOf(room, spot);
                if (idx >= 0) { CheckStray(R.Get<string>(room, "RoomId"), idx, spot, true); budget--; }
            }

            if (_strayFilling) { GatherSome(1500); return; }
            if (_strayScan.Count == 0)
            {
                int day = -1;
                try { day = Convert.ToInt32(R.Get(FindDayTime(now), "CurrentDay")); } catch { }
                bool due = _nextStrayPass != float.MaxValue ? now >= _nextStrayPass : day != _strayLastDay;
                if (!due) return;
                if (day != _strayLastDay && _nextStrayPass == float.MaxValue) _strayRetries = 0;
                _strayLastDay = day; _nextStrayPass = float.MaxValue;
                BeginStrayPass();
                return;
            }
            for (; budget > 0 && _strayCursor < _strayScan.Count; budget--, _strayCursor++)
            {
                var (room, idx, spot) = _strayScan[_strayCursor];
                CheckStray(room, idx, spot, false);
            }
            if (_strayCursor >= _strayScan.Count) EndStrayPass(now);
        }

        private static readonly Dictionary<IntPtr, Vector3> _strayOk = new Dictionary<IntPtr, Vector3>();
        private static readonly List<(string id, object room)> _gather = new List<(string, object)>();
        private static int _gatherRoom, _gatherSpot, _strayRetries;

        private static void BeginStrayPass()
        {
            _strayScan.Clear(); _strayFound.Clear(); _gather.Clear();
            _strayCursor = 0; _strayChecked = 0; _strayNoMesh = 0; _gatherRoom = 0; _gatherSpot = 0;
            var ids = new List<string>(_rooms.Keys); ids.Sort(StringComparer.Ordinal);
            var players = _connected ? Net.WorkSplit() : new List<ulong> { Net.Me };
            for (int i = 0; i < ids.Count; i++)
            {
                var room = _rooms[ids[i]];
                if (!Alive(room)) continue;
                ulong who = players[i % players.Count];
                if (players.Count > 1)
                {
                    var at = ((Component)room).transform.position; float best = float.MaxValue;
                    foreach (var id in players) { var h = Net.HeadOf(id); if (h.HasValue && Dist(h.Value, at) < best) { best = Dist(h.Value, at); who = id; } }
                }
                if (who == Net.Me) _gather.Add((ids[i], room));
            }
            _strayFilling = true;
        }

        private static void GatherSome(int n)
        {
            while (n > 0 && _gatherRoom < _gather.Count)
            {
                var (id, room) = _gather[_gatherRoom];
                bool hidden = false;
                try { hidden = Alive(room) && Convert.ToBoolean(R.Get(room, "IsHidden")); } catch { }
                var spots = Alive(room) && !hidden ? R.Get(room, "_ballSpots") : null;
                int count = spots != null ? R.Count(spots) : 0;
                for (; n > 0 && _gatherSpot < count; _gatherSpot++, n--)
                {
                    var spot = R.At(spots, _gatherSpot);
                    if (spot == null || R.Get<bool>(spot, "IsCollected")) continue;
                    if (_strayOk.TryGetValue(R.Ptr(spot), out var okAt) && Dist(okAt, (Vector3)R.Get(spot, "Position")) < 0.05f) continue;
                    _strayScan.Add((id, _gatherSpot, spot));
                }
                if (_gatherSpot >= count) { _gatherRoom++; _gatherSpot = 0; }
            }
            if (_gatherRoom >= _gather.Count) _strayFilling = false;
        }

        private static void CheckStray(string room, int idx, object spot, bool immediate)
        {
            if (R.Get<bool>(spot, "IsCollected")) return;
            var inst = R.Get(spot, "Instance");
            if (inst != null && SafeBool(inst, "IsActive") && Speed2(inst) > 0.01f) return;
            Vector3 p = inst != null && SafeBool(inst, "IsActive") ? ((Component)inst).transform.position : (Vector3)R.Get(spot, "Position");
            _strayChecked++;
            if (InBounds(p, out var nav)) { _strayOk[R.Ptr(spot)] = (Vector3)R.Get(spot, "Position"); return; }
            if (nav == null) { _strayNoMesh++; return; }
            var to = nav.Value + new Vector3((float)(_strayRng.NextDouble() - 0.5) * 0.6f, _ballR + 0.05f, (float)(_strayRng.NextDouble() - 0.5) * 0.6f);
            if (immediate) MoveStray(room, idx, to);
            else _strayFound.Add((room, idx, spot, to));
        }

        private static void EndStrayPass(float now)
        {
            int n = _strayScan.Count;
            _strayScan.Clear();
            if (n == 0) return;
            if (_strayNoMesh > n / 2)
            {
                if (++_strayRetries <= 3) { _nextStrayPass = now + 15f; _strayLastDay = -1; Plugin.Log.LogInfo("[Strays] walkable area not ready; retrying soon"); }
                else Plugin.Log.LogInfo("[Strays] walkable area still not ready; next check on the next day");
                return;
            }
            _strayRetries = 0;
            if (_strayFound.Count > n * StrayMaxShare)
            {
                Plugin.Log.LogWarning($"[Strays] {_strayFound.Count} of {n} balls look out of bounds; that's too many to trust, nothing moved");
                _strayFound.Clear(); return;
            }
            foreach (var f in _strayFound) MoveStray(f.room, f.idx, f.to);
            if (_strayFound.Count > 0) Plugin.Log.LogInfo($"[Strays] moved {_strayFound.Count} out-of-bounds balls back into play (checked {n})");
            _strayFound.Clear();
        }

        private static void MoveStray(string room, int idx, Vector3 to)
        {
            ApplyMove(room, idx, to);
            if (_connected) _outMoved.Add((room, idx, to));
            var spot = SpotAt(room, idx); if (spot != null) _strayOk[R.Ptr(spot)] = to;
        }

        private static void QueueStrayCheck(object spot)
        {
            if (spot != null && Strays.Enabled && _strayRecheck.Count < 2000) _strayRecheck.Enqueue(spot);
        }
    }
}
