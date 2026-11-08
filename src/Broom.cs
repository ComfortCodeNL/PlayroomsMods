using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlayroomsCoop
{
    internal static partial class World
    {
        private const string ShovelT = "TwoPixels.Player.ShovelBehaviour";
        public static Feature Broom;

        private struct SweepSample { public uint Seq; public float T; public Vector3 Pos, Dir; public float Speed; }
        private static uint _sweepSeq;

        private static object _localShovel;
        private static Transform _localShovelBody;
        private static readonly List<SweepSample> _outSweep = new List<SweepSample>();
        private static float _lastLocalSweep = -99f, _sweepRadius = 3f, _nextRadiusRead;
        private static Vector3 _lastLocalSweepPos;

        private sealed class RemoteBroom
        {
            public readonly List<SweepSample> In = new List<SweepSample>();
            public uint LastSeq; public bool HaveSeq;
            public float LastSweep = -99f; public Vector3 LastPos;
            public GameObject Proxy; public object State;
            public readonly Dictionary<string, object> Scratch = new Dictionary<string, object>();
        }
        private static readonly Dictionary<ulong, RemoteBroom> _remoteBrooms = new Dictionary<ulong, RemoteBroom>();
        private static bool _replaying, _capsSent, _canReplay;
        private static float _nextCapsCheck;
        private static readonly string[] SwapFields = { "_sweptBalls", "_inSightBalls", "_blockedUntil" };
        private static int _replays;

        private static void BroomInit()
        {
            Broom = new Feature("Broom", Plugin.ReplayBroom.Value);
        }

        private static void BroomReset()
        {
            _outSweep.Clear();
            foreach (var rb in _remoteBrooms.Values) if (rb.Proxy != null) UnityEngine.Object.Destroy(rb.Proxy);
            _remoteBrooms.Clear();
            _capsSent = false; _canReplay = false;
            _lastLocalSweep = -99f;
        }

        internal static void OnLocalSweep(object shovel, Vector3 dir, float speed)
        {
            if (_replaying || !_connected) return;
            try
            {
                if (_localShovel == null || R.Ptr(_localShovel) != R.Ptr(shovel))
                {
                    _localShovel = shovel; _localShovelBody = null;
                    _shovelWorld = Alive(_ballService) ? R.Ptr(_ballService) : IntPtr.Zero;
                }
                if (_localShovelBody == null) _localShovelBody = R.Get(shovel, "_playerTransform") as Transform;
                if (_localShovelBody == null) return;
                float now = Time.realtimeSinceStartup;
                _lastLocalSweep = now;
                _lastLocalSweepPos = _localShovelBody.position;
                if (!PeerBroom() || !Broom.Enabled) return;
                if (_outSweep.Count < 64)
                    _outSweep.Add(new SweepSample { Seq = ++_sweepSeq, T = now, Pos = _lastLocalSweepPos, Dir = dir, Speed = speed });
            }
            catch { }
        }

        private static void FlushSweeps()
        {
            if (_outSweep.Count == 0) return;
            for (int start = 0; start < _outSweep.Count; start += 30)
            {
                int n = Math.Min(30, _outSweep.Count - start);
                var w = Protocol.W.Begin(Msg.BroomSweep);
                w.U8((byte)n);
                for (int i = 0; i < n; i++)
                {
                    var s = _outSweep[start + i];
                    w.U32(s.Seq); w.F32(s.T); w.F32(s.Pos.x); w.F32(s.Pos.y); w.F32(s.Pos.z);
                    w.F32(s.Dir.x); w.F32(s.Dir.y); w.F32(s.Dir.z); w.F32(s.Speed);
                }
                w.SendAll(SteamNet.SendUnreliable);
            }
            _outSweep.Clear();
        }

        internal static bool LocalSweeping(float now) => _connected && now - _lastLocalSweep < 0.4f;

        private static bool LocalSweepNear(Vector3 p, float now)
        {
            if (!PeerBroom() || now - _lastLocalSweep > 1.5f) return false;
            return HDist(p, _lastLocalSweepPos) < _sweepRadius + 1.5f;
        }

        private static bool RemoteSweepNear(Vector3 p, float now)
        {
            if (!_canReplay) return false;
            foreach (var rb in _remoteBrooms.Values)
                if (now - rb.LastSweep <= 1.5f && HDist(p, rb.LastPos) < _sweepRadius + 1.5f) return true;
            return false;
        }

        private static bool PeerBroom()
        {
            foreach (var pr in Net.Peers.Values) if (pr.BroomReplay) return true;
            return false;
        }

        private static void BroomCapsTick(float now)
        {
            if (now < _nextCapsCheck) return;
            _nextCapsCheck = now + 3f;
            if (now >= _nextRadiusRead && _localShovel != null)
            {
                _nextRadiusRead = now + 5f;
                try { _sweepRadius = Math.Max(1f, R.Get<float>(_localShovel, "SweepRadius")); } catch { }
            }
            bool can = Broom.Enabled && InGameWorld() && FindShovel() != null;
            if (can != _canReplay || !_capsSent)
            {
                _canReplay = can; _capsSent = true;
                var w = Protocol.W.Begin(Msg.BroomCaps); w.U8(can ? (byte)1 : (byte)0);
                w.SendAll(SteamNet.SendReliable);
                Plugin.Log.LogInfo(can ? "[Broom] other players' brooms will be replayed here" : "[Broom] can't replay other players' brooms here; using live ball positions");
            }
        }

        private static IntPtr _shovelWorld;
        private static float _nextShovelFind;

        private static object FindShovel()
        {
            IntPtr world = Alive(_ballService) ? R.Ptr(_ballService) : IntPtr.Zero;
            if (_localShovel != null && world == _shovelWorld) return _localShovel;
            float now = Time.realtimeSinceStartup;
            if (now < _nextShovelFind) return null;
            _nextShovelFind = now + 2f;
            _localShovel = MenuInput.FindPlayerBehaviour("ShovelBehaviour");
            _localShovelBody = null;
            foreach (var rb in _remoteBrooms.Values) { rb.State = null; rb.Scratch.Clear(); }
            _shovelWorld = _localShovel != null ? world : IntPtr.Zero;
            return _localShovel;
        }

        private static void OnSweepMessage(byte[] buf, int len)
        {
            if (!_canReplay) return;
            ulong from = Net.Sender;
            if (!_remoteBrooms.TryGetValue(from, out var rb)) _remoteBrooms[from] = rb = new RemoteBroom();
            var r = new Reader(buf, len, Protocol.HeaderSize);
            int n = r.U8();
            for (int i = 0; i < n; i++)
            {
                var s = new SweepSample
                {
                    Seq = r.U32(),
                    T = r.F32(),
                    Pos = new Vector3(r.F32(), r.F32(), r.F32()),
                    Dir = new Vector3(r.F32(), r.F32(), r.F32()),
                    Speed = r.F32(),
                };
                s.T += Net.Offset(from, s.T);
                if (rb.HaveSeq && (int)(s.Seq - rb.LastSeq) <= 0) continue;
                int at = rb.In.Count;
                while (at > 0 && (int)(rb.In[at - 1].Seq - s.Seq) > 0) at--;
                if (at > 0 && rb.In[at - 1].Seq == s.Seq) continue;
                rb.In.Insert(at, s);
            }
            if (rb.In.Count > 200) rb.In.RemoveRange(0, rb.In.Count - 200);
        }

        public static void FixedTick()
        {
            if (!_connected || !_canReplay || _remoteBrooms.Count == 0) return;
            foreach (var kv in _remoteBrooms)
            {
                if (kv.Value.In.Count == 0) continue;
                var rb = kv.Value;
                Broom.Try(() => ReplayDue(rb), "replay other player's broom");
            }
        }

        private static void ReplayDue(RemoteBroom rb)
        {
            float renderT = Time.realtimeSinceStartup - InterpDelay;
            if (rb.In[0].T > renderT) return;
            int budget = rb.In.Count > 6 ? 3 : rb.In.Count > 2 ? 2 : 1;
            for (int i = 0; i < budget && rb.In.Count > 0; i++)
            {
                var s = rb.In[0];
                rb.In.RemoveAt(0);
                rb.LastSeq = s.Seq; rb.HaveSeq = true;
                if (renderT - s.T > 1.5f) continue;
                ReplayOne(rb, s);
            }
        }

        private static void ReplayOne(RemoteBroom rb, SweepSample s)
        {
            var shovel = FindShovel();
            if (shovel == null) return;
            if (rb.Proxy == null)
            {
                rb.Proxy = new GameObject("PlayroomsCoop_OtherBroom");
                UnityEngine.Object.DontDestroyOnLoad(rb.Proxy);
            }
            rb.Proxy.transform.position = s.Pos;
            rb.LastSweep = Time.realtimeSinceStartup;
            rb.LastPos = s.Pos;

            var body = R.Get(shovel, "_playerTransform");
            var state = R.Get(shovel, "ShovelBehaviourState");
            if (rb.State == null && state != null) rb.State = NewLike(state);
            var local = new Dictionary<string, object>();
            foreach (var f in SwapFields)
            {
                if (!R.Has(shovel, f)) continue;
                var v = R.Get(shovel, f);
                if (v == null) continue;
                local[f] = v;
                if (!rb.Scratch.ContainsKey(f)) rb.Scratch[f] = NewLike(v);
            }

            _replaying = true; _applying = true;
            try
            {
                R.Set(shovel, "_playerTransform", rb.Proxy.transform);
                foreach (var f in local.Keys) R.Set(shovel, f, rb.Scratch[f]);
                if (rb.State != null) R.Set(shovel, "ShovelBehaviourState", rb.State);
                R.Call(shovel, "Sweep", s.Dir, s.Speed);
                if (_replays++ == 0) Plugin.Log.LogInfo("[Broom] replaying another player's broom");
            }
            finally
            {
                try { R.Set(shovel, "_playerTransform", body); } catch { }
                foreach (var kv in local) { try { R.Set(shovel, kv.Key, kv.Value); } catch { } }
                try { if (rb.State != null) R.Set(shovel, "ShovelBehaviourState", state); } catch { }
                _replaying = false; _applying = false;
            }
        }

        private static object NewLike(object o)
        {
            var t = o.GetType();
            try { return Activator.CreateInstance(t); }
            catch
            {
                int n = R.Count(o);
                return Activator.CreateInstance(t, (long)n);
            }
        }
    }
}
