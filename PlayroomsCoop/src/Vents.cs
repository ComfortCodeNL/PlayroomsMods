using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlayroomsCoop
{
    internal static partial class World
    {
        private static readonly List<(object vent, Vector3 intake)> _vents = new List<(object, Vector3)>();

        private static void ScanVents()
        {
            _ventIntakes.Clear(); _vents.Clear();
            foreach (var v in R.FindAll(R.T("TwoPixels.Ball.BallVent")))
            {
                try
                {
                    Vector3 at;
                    if (R.Get(v, "intakePoint") is Transform ip) at = ip.position;
                    else at = ((Component)v).transform.position;
                    _ventIntakes.Add(at); _vents.Add((v, at));
                }
                catch { }
                if (_isHost || Plugin.ClientVents.Value) continue;
                try
                {
                    var b = (Behaviour)v;
                    if (b.enabled && SafeBool(v, "IsUnlocked")) { b.enabled = false; _disabledVents.Add(v); }
                }
                catch { }
            }
        }

        private static readonly List<(string room, int idx, Vector3 at)> _outVentFeed = new List<(string, int, Vector3)>();
        private static bool _feeding;
        private static int _ventFed, _ventFedHere;

        internal static bool OnVentConsume(object vent, object ball, ref bool result)
        {
            if (!_connected || _isHost || _feeding || !Plugin.ClientVents.Value || !Balls.Enabled) return true;
            try
            {
                var spot = R.Get(ball, "Owner");
                var room = spot != null ? R.Get(spot, "_room") : null;
                if (room == null) return true;
                int idx = IndexOf(room, spot);
                if (idx < 0) return true;
                string id = R.Get<string>(room, "RoomId");
                Vector3 at = ((Component)vent).transform.position;
                try { at = (Vector3)R.Call(vent, "GetIntakePoint"); } catch { }
                _moving.Remove(R.Ptr(spot));
                ApplyTake(room, idx);
                _outVentFeed.Add((id, idx, at));
                _ventFedHere++;
                try { R.Call(vent, "QueueIntakeFeedback"); } catch { }
                result = true;
                return false;
            }
            catch (Exception e)
            {
                if (_stepWarned.Add("vent consume")) Plugin.Log.LogWarning("[Balls] client vent: " + e.Message);
                return true;
            }
        }

        private static void FlushVentFeed()
        {
            if (_outVentFeed.Count == 0) return;
            for (int start = 0; start < _outVentFeed.Count; start += 500)
            {
                int n = Math.Min(500, _outVentFeed.Count - start);
                var w = Protocol.W.Begin(Msg.VentFeed);
                w.U16((ushort)n);
                for (int i = 0; i < n; i++)
                {
                    var f = _outVentFeed[start + i];
                    w.Str(f.room); w.I32(f.idx); w.F32(f.at.x); w.F32(f.at.y); w.F32(f.at.z);
                }
                w.SendHost(SteamNet.SendReliable);
            }
            _outVentFeed.Clear();
        }

        private static void OnVentFeed(byte[] buf, int len)
        {
            if (!_isHost) return;
            var r = new Reader(buf, len, Protocol.HeaderSize);
            int n = r.U16();
            for (int i = 0; i < n; i++)
            {
                string room = r.Str(); int idx = r.I32();
                var at = new Vector3(r.F32(), r.F32(), r.F32());
                Balls.Try(() => FeedVent(room, idx, at), "feed vent");
            }
        }

        private static void FeedVent(string roomId, int idx, Vector3 at)
        {
            var spot = SpotAt(roomId, idx);
            if (spot == null || R.Get<bool>(spot, "IsCollected")) return;
            if (_vents.Count == 0) ScanVents();
            object vent = null; float best = float.MaxValue;
            foreach (var (v, p) in _vents) { if (!Alive(v)) continue; float d = Dist(p, at); if (d < best) { best = d; vent = v; } }
            IntPtr sp = R.Ptr(spot);
            DropPuppet(spot);
            _moving.Remove(sp);
            var ball = R.Get(spot, "Instance");
            if ((ball == null || !SafeBool(ball, "IsActive")) && _ballService != null)
            {
                _applying = true;
                try { Step(() => R.Call(_ballService, "PromoteBall", spot), "PromoteBall"); }
                finally { _applying = false; }
                ball = R.Get(spot, "Instance");
                if (ball != null && !SafeBool(ball, "IsActive")) Step(() => R.Call(ball, "Activate"), "Activate");
            }
            bool ok = false;
            if (vent != null && ball != null)
            {
                _feeding = true;
                try { ok = Convert.ToBoolean(R.Call(vent, "ConsumeIntoPit", ball)); }
                finally { _feeding = false; }
            }
            if (ok) { _ventFed++; return; }
            var pos = ball != null ? ((Component)ball).transform.position : (Vector3)R.Get(spot, "Position");
            _outMoved.Add((roomId, idx | ReviveFlag, pos));
        }

        private static bool NearVent(Vector3 p)
        {
            foreach (var v in _ventIntakes) if (Dist(p, v) < 1.5f) return true;
            return false;
        }
    }
}
