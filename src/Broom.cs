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
        private static uint _sweepSeq, _lastRemoteSeq;
        private static bool _haveRemoteSeq;

        private static object _localShovel;
        private static Transform _localShovelBody;
        private static readonly List<SweepSample> _outSweep = new List<SweepSample>();
        private static float _lastLocalSweep = -99f, _sweepRadius = 3f, _nextRadiusRead;
        private static Vector3 _lastLocalSweepPos;
        private static bool _peerBroom;

        private static readonly List<SweepSample> _inSweep = new List<SweepSample>();
        private static float _lastRemoteSweep = -99f, _lastRemoteSweepT = -99f;
        private static Vector3 _lastRemoteSweepPos;
        private static bool _replaying, _capsSent, _canReplay;
        private static float _nextCapsCheck;
        private static GameObject _broomProxy;
        private static object _remoteState;
        private static readonly string[] SwapFields = { "_sweptBalls", "_inSightBalls", "_blockedUntil" };
        private static readonly Dictionary<string, object> _remoteScratch = new Dictionary<string, object>();
        private static int _replays;

        private static void BroomInit()
        {
            Broom = new Feature("Broom", Plugin.ReplayBroom.Value);
        }

        private static void BroomReset()
        {
            _outSweep.Clear(); _inSweep.Clear();
            _peerBroom = false; _capsSent = false; _canReplay = false;
            _lastLocalSweep = _lastRemoteSweep = _lastRemoteSweepT = -99f;
            _haveRemoteSeq = false;
            _remoteState = null; _remoteScratch.Clear();
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
                if (!_peerBroom || !Broom.Enabled) return;
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
                w.SendTo(_peer, SteamNet.SendUnreliable);
            }
            _outSweep.Clear();
        }

        internal static bool LocalSweeping(float now) => _connected && now - _lastLocalSweep < 0.4f;

        private static bool LocalSweepNear(Vector3 p, float now)
        {
            if (!_peerBroom || now - _lastLocalSweep > 1.5f) return false;
            return HDist(p, _lastLocalSweepPos) < _sweepRadius + 1.5f;
        }

        private static bool RemoteSweepNear(Vector3 p, float now)
        {
            if (!_canReplay || now - _lastRemoteSweep > 1.5f) return false;
            return HDist(p, _lastRemoteSweepPos) < _sweepRadius + 1.5f;
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
                w.SendTo(_peer, SteamNet.SendReliable);
                Plugin.Log.LogInfo(can ? "[Broom] partner's broom will be replayed here" : "[Broom] can't replay the partner's broom here; using live ball positions");
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
            _localShovelBody = null; _remoteState = null; _remoteScratch.Clear();
            _shovelWorld = _localShovel != null ? world : IntPtr.Zero;
            return _localShovel;
        }

        private static void OnSweepMessage(byte[] buf, int len)
        {
            if (!_canReplay) return;
            var r = new Reader(buf, len, Protocol.HeaderSize);
            int n = r.U8();
            float now = Time.realtimeSinceStartup;
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
                float off = now - s.T;
                if (!_hasClockOffset || off < _clockOffset) { _clockOffset = off; _hasClockOffset = true; }
                if (_haveRemoteSeq && (int)(s.Seq - _lastRemoteSeq) <= 0) continue;
                int at = _inSweep.Count;
                while (at > 0 && (int)(_inSweep[at - 1].Seq - s.Seq) > 0) at--;
                if (at > 0 && _inSweep[at - 1].Seq == s.Seq) continue;
                _inSweep.Insert(at, s);
            }
            if (_inSweep.Count > 200) _inSweep.RemoveRange(0, _inSweep.Count - 200);
        }

        public static void FixedTick()
        {
            if (!_connected || !_canReplay || _inSweep.Count == 0) return;
            Broom.Try(ReplayDue, "replay partner's broom");
        }

        private static void ReplayDue()
        {
            float renderT = Time.realtimeSinceStartup - _clockOffset - InterpDelay;
            if (_inSweep[0].T > renderT) return;
            int budget = _inSweep.Count > 6 ? 3 : _inSweep.Count > 2 ? 2 : 1;
            for (int i = 0; i < budget && _inSweep.Count > 0; i++)
            {
                var s = _inSweep[0];
                _inSweep.RemoveAt(0);
                _lastRemoteSweepT = s.T; _lastRemoteSeq = s.Seq; _haveRemoteSeq = true;
                if (renderT - s.T > 1.5f) continue;
                ReplayOne(s);
            }
        }

        private static void ReplayOne(SweepSample s)
        {
            var shovel = FindShovel();
            if (shovel == null) return;
            if (_broomProxy == null)
            {
                _broomProxy = new GameObject("PlayroomsCoop_PartnerBroom");
                UnityEngine.Object.DontDestroyOnLoad(_broomProxy);
            }
            _broomProxy.transform.position = s.Pos;
            _lastRemoteSweep = Time.realtimeSinceStartup;
            _lastRemoteSweepPos = s.Pos;

            var body = R.Get(shovel, "_playerTransform");
            var state = R.Get(shovel, "ShovelBehaviourState");
            if (_remoteState == null && state != null) _remoteState = NewLike(state);
            var local = new Dictionary<string, object>();
            foreach (var f in SwapFields)
            {
                if (!R.Has(shovel, f)) continue;
                var v = R.Get(shovel, f);
                if (v == null) continue;
                local[f] = v;
                if (!_remoteScratch.ContainsKey(f)) _remoteScratch[f] = NewLike(v);
            }

            _replaying = true; _applying = true;
            try
            {
                R.Set(shovel, "_playerTransform", _broomProxy.transform);
                foreach (var f in local.Keys) R.Set(shovel, f, _remoteScratch[f]);
                if (_remoteState != null) R.Set(shovel, "ShovelBehaviourState", _remoteState);
                R.Call(shovel, "Sweep", s.Dir, s.Speed);
                if (_replays++ == 0) Plugin.Log.LogInfo("[Broom] replaying the partner's broom");
            }
            finally
            {
                try { R.Set(shovel, "_playerTransform", body); } catch { }
                foreach (var kv in local) { try { R.Set(shovel, kv.Key, kv.Value); } catch { } }
                try { if (_remoteState != null) R.Set(shovel, "ShovelBehaviourState", state); } catch { }
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
