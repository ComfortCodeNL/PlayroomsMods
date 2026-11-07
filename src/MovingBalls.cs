using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlayroomsCoop
{
    internal static partial class World
    {
        private static void PollMovingBalls(float now)
        {
            if (!Alive(_ballService)) return;

            if (now >= _nextActiveScan)
            {
                _nextActiveScan = now + 0.15f;
                object active = null;
                try { active = R.Get(_ballService, "_activePhysicsBalls"); }
                catch (Exception e) { if (_stepWarned.Add("_activePhysicsBalls")) Plugin.Log.LogWarning("[Balls] can't see moving balls: " + e.Message); }
                var me = LocalHead; var them = RemoteHead;
                if (active != null && me != null)
                {
                    var tracked = new HashSet<IntPtr>();
                    foreach (var mv in _moving.Values) tracked.Add(R.Ptr(mv.Ball));
                    foreach (var pp0 in _puppets.Values) tracked.Add(R.Ptr(pp0.Ball));
                    // big piles wake hundreds of balls: read a slice per scan, cheapest checks first
                    var all = R.Items(active, _scanCursor, ScanBudget, out int n);
                    if (n < 0) n = all.Count;
                    int budget = all.Count;
                    for (int bi = 0; bi < budget; bi++)
                    {
                        var ball = all[bi];
                        if (ball == null || tracked.Contains(R.Ptr(ball))) continue;
                        var bpos = ((Component)ball).transform.position;
                        if (RemoteSweepNear(bpos, now)) continue;
                        bool quiet = LocalSweepNear(bpos, now);
                        float dMe = HDist(bpos, me.Value);
                        if (!quiet && dMe > 6f) continue;
                        if (!quiet && them != null && HDist(bpos, them.Value) < dMe) continue;
                        var spot = R.Get(ball, "Owner");
                        if (spot == null) continue;
                        IntPtr p = R.Ptr(spot);
                        if (_moving.ContainsKey(p) || _puppets.ContainsKey(p)) continue;
                        if (_recentPuppet.TryGetValue(p, out float pt) && now - pt < 3f) continue;
                        if (_settleCool.TryGetValue(p, out float sc) && now - sc < 1.5f && Speed2(ball) < 0.01f) continue;
                        if (quiet ? _quietCount >= MaxQuiet : _moving.Count - _quietCount >= MaxMoving) { CapHit(now); continue; }
                        var room = R.Get(spot, "_room");
                        if (room == null) continue;
                        int idx = IndexOf(room, spot);
                        if (idx < 0) continue;
                        var pos = ((Component)ball).transform.position;
                        _moving[p] = new Mover { Spot = spot, Ball = ball, Room = R.Get<string>(room, "RoomId"), Idx = idx, Start = pos, LastSent = pos, Since = now, Anchor = pos, AnchorAt = now, Quiet = quiet };
                        if (quiet) _quietCount++;
                    }
                    _scanCursor = n == 0 ? 0 : (_scanCursor + budget) % n;
                }
            }

            var me2 = LocalHead;
            var keys = new List<IntPtr>(_moving.Keys);
            int quietBudget = 250, quietSeen = 0;
            for (int ki = 0; ki < keys.Count; ki++)
            {
                var p = keys[(ki + _moveCursor) % keys.Count];
                if (!_moving.TryGetValue(p, out var m)) continue;
                if (m.Quiet && ++quietSeen > quietBudget) continue;
                bool alive = Alive(m.Ball) && R.Same(R.Get(m.Ball, "Owner"), m.Spot);
                if (!alive || R.Get<bool>(m.Spot, "IsCollected"))
                {
                    _moving.Remove(p);
                    if (alive || R.Get<bool>(m.Spot, "IsCollected")) continue;
                    var endSpot = (Vector3)R.Get(m.Spot, "Position");
                    if (Dist(m.Start, endSpot) > 0.15f) _outMoved.Add((m.Room, m.Idx, endSpot));
                    continue;
                }
                var pos = ((Component)m.Ball).transform.position;
                if (!m.Quiet && Dist(pos, m.LastSent) > 0.01f) { _outLive.Add((m.Room, m.Idx, pos)); m.LastSent = pos; }

                float speed2 = 0f;
                try { speed2 = Convert.ToSingle(R.Get(m.Ball, "SqrVelocityMagnitude")); } catch { }
                bool activeNow = true;
                try { activeNow = R.Get<bool>(m.Ball, "IsActive"); } catch { }
                bool drifting = false;
                if (now - m.AnchorAt >= 1f)
                {
                    drifting = Dist(pos, m.Anchor) < 0.05f && now - m.Since > 2f;
                    m.Anchor = pos; m.AnchorAt = now;
                }
                bool far = me2 != null && HDist(pos, me2.Value) > 10f;
                if (drifting || far) { _moving.Remove(p); _settleCool[p] = now; _outMoved.Add((m.Room, m.Idx, pos)); QueueStrayCheck(m.Spot); continue; }
                if (!activeNow || speed2 < 0.0025f)
                {
                    if (m.StillSince == 0f) m.StillSince = now;
                    else if (now - m.StillSince > 0.4f)
                    {
                        _moving.Remove(p);
                        if (Dist(m.Start, pos) > 0.15f) { _outMoved.Add((m.Room, m.Idx, pos)); QueueStrayCheck(m.Spot); }
                    }
                }
                else m.StillSince = 0f;
            }

            if (keys.Count > 0) _moveCursor = (_moveCursor + quietBudget) % Math.Max(1, keys.Count);
            _quietCount = 0; foreach (var mv in _moving.Values) if (mv.Quiet) _quietCount++;
            if (_moving.Count > 0 && now - _lastMoveLog > 10f)
            {
                _lastMoveLog = now;
                Plugin.Log.LogInfo($"[Balls] tracking {_moving.Count} moving balls ({_quietCount} swept, rest only; sent {_liveSentCount} live updates, received {_liveRecvCount}; {_restSkipped} rests already matched, {_restApplied} corrected; vents: {_ventFedHere} eaten here, {_ventFed} fed for partner)");
            }

            if (_settleCool.Count > 500) _settleCool.Clear();

            foreach (var p in new List<IntPtr>(_recentTaken.Keys))
            {
                var (spot, room, idx, time) = _recentTaken[p];
                if (now - time > 90f) { _recentTaken.Remove(p); continue; }
                if (R.Get<bool>(spot, "IsCollected")) continue;
                if (R.Get(spot, "Instance") != null) continue;
                _recentTaken.Remove(p);
                _outMoved.Add((room, idx | ReviveFlag, (Vector3)R.Get(spot, "Position")));
            }
        }

        private const int ScanBudget = 400;
        private static int _scanCursor;
        private const int MaxMoving = 200, MaxQuiet = 5000, MaxGlides = 150;
        private const float GlideTime = 0.4f;

        private static bool TryGlide(object spot, Vector3 from, Vector3 to, float now)
        {
            if (_puppets.Count >= MaxGlides || Dist(from, to) > 2.5f) return false;
            IntPtr p = R.Ptr(spot);
            if (_puppets.ContainsKey(p) || _moving.ContainsKey(p)) return false;
            var pp = MakePuppet(spot, from);
            if (pp == null) return false;
            float rt = now - _clockOffset - InterpDelay;
            pp.Samples.Add((rt, from));
            pp.Samples.Add((rt + GlideTime * 0.5f, Vector3.Lerp(from, to, 0.8f)));
            pp.Samples.Add((rt + GlideTime, to));
            pp.Target = to; pp.LastUpdate = now;
            return true;
        }
        private const int ReviveFlag = 0x40000000;
        private static int _quietCount, _moveCursor, _restSkipped, _restApplied;

        private static readonly Queue<(string room, int idx, Vector3 pos, float at, int tries)> _restQueue = new Queue<(string, int, Vector3, float, int)>();

        private static void ApplyRests(float now, int budget)
        {
            int n = Math.Min(budget, _restQueue.Count);
            for (int i = 0; i < n; i++)
            {
                var e = _restQueue.Dequeue();
                if (now < e.at) { _restQueue.Enqueue(e); continue; }
                bool revive = (e.idx & ReviveFlag) != 0;
                int idx = e.idx & ~ReviveFlag;
                var spot = SpotAt(e.room, idx);
                if (spot == null) continue;
                bool collected = R.Get<bool>(spot, "IsCollected");
                if (collected && !revive) continue;
                if (!collected)
                {
                    var inst = R.Get(spot, "Instance");
                    bool active = inst != null && SafeBool(inst, "IsActive");
                    Vector3 cur = active ? ((Component)inst).transform.position : (Vector3)R.Get(spot, "Position");
                    if (active && Speed2(inst) > 0.01f && e.tries < 6 && !_puppets.ContainsKey(R.Ptr(spot)))
                    {
                        _restQueue.Enqueue((e.room, e.idx, e.pos, now + 0.5f, e.tries + 1));
                        continue;
                    }
                    if (Dist(cur, e.pos) < Math.Max(0.03f, Plugin.BallCorrection.Value)) { _restSkipped++; continue; }
                    _restApplied++;
                    if (TryGlide(spot, cur, e.pos, now)) continue;
                }
                else _restApplied++;
                ApplyMove(e.room, idx, e.pos);
            }
        }
        private static readonly Dictionary<IntPtr, float> _settleCool = new Dictionary<IntPtr, float>();

        private static float Speed2(object ball)
        {
            try { return Convert.ToSingle(R.Get(ball, "SqrVelocityMagnitude")); } catch { return 0f; }
        }
        private static float _lastCapLog = -99f;
        private static void CapHit(float now)
        {
            if (now - _lastCapLog < 10f) return;
            _lastCapLog = now;
            Plugin.Log.LogInfo($"[Balls] moving ball limit reached ({MaxMoving})");
        }

        private static void ApplyLive(string roomId, int idx, Vector3 pos, float sentAt)
        {
            var spot = SpotAt(roomId, idx);
            if (spot == null || R.Get<bool>(spot, "IsCollected")) return;
            IntPtr p = R.Ptr(spot);
            if (_moving.ContainsKey(p)) return;
            float now = Time.realtimeSinceStartup;
            if (!_puppets.ContainsKey(p) && LocalSweepNear(pos, now)) return;

            if (!_puppets.TryGetValue(p, out var pp) || !Alive(pp.Ball) || !R.Same(R.Get(pp.Ball, "Owner"), spot))
            {
                pp = MakePuppet(spot, pos);
                if (pp == null) return;
            }
            var list = pp.Samples;
            int at = list.Count;
            while (at > 0 && list[at - 1].t > sentAt) at--;
            if (at > 0 && Math.Abs(list[at - 1].t - sentAt) < 0.0001f) return;
            list.Insert(at, (sentAt, pos));
            if (list.Count > 16) list.RemoveAt(0);
            pp.Target = list[list.Count - 1].p;
            pp.LastUpdate = now;
        }

        private static Puppet MakePuppet(object spot, Vector3 pos)
        {
            IntPtr p = R.Ptr(spot);
            {
                var ball = R.Get(spot, "Instance");
                bool activeBall = ball != null && SafeBool(ball, "IsActive");
                if (!activeBall && _ballService != null)
                {
                    _applying = true;
                    try { Step(() => R.Call(_ballService, "PromoteBall", spot), "PromoteBall"); }
                    finally { _applying = false; }
                    ball = R.Get(spot, "Instance");
                    if (ball != null && !SafeBool(ball, "IsActive")) Step(() => R.Call(ball, "Activate"), "Activate");
                }
                if (ball == null) return null;
                var pp = new Puppet { Spot = spot, Ball = ball, Target = pos, LastShown = pos };
                try { pp.Radius = Math.Max(0.02f, ((Component)ball).transform.localScale.x * 0.5f); } catch { }
                try { pp.Body = R.Get(ball, "Body"); } catch { }
                if (pp.Body != null)
                {
                    pp.OrigKinematic = Convert.ToBoolean(R.Get(pp.Body, "isKinematic"));
                    R.Set(pp.Body, "isKinematic", true);
                }
                ((Component)ball).transform.position = pos;
                _puppets[p] = pp;
                return pp;
            }
        }

        private static readonly List<IntPtr> _puppetKeys = new List<IntPtr>();
        private static System.Reflection.MethodInfo _invalidateDraw;

        private static void TickPuppets(float now, float dt)
        {
            if (_puppets.Count == 0) return;
            float renderT = now - _clockOffset - InterpDelay;
            _puppetKeys.Clear();
            _puppetKeys.AddRange(_puppets.Keys);
            foreach (var p in _puppetKeys)
            {
                var pp = _puppets[p];
                if (!Alive(pp.Ball)) { _puppets.Remove(p); continue; }
                var t = ((Component)pp.Ball).transform;
                if (_isHost && NearVent(t.position))
                {
                    ReleasePuppet(pp);
                    _puppets.Remove(p);
                    continue;
                }
                var pos = Sample(pp.Samples, renderT);
                Vector3 d = pos - pp.LastShown;
                // a ball that has stopped needs no transform write or redraw
                if (d.sqrMagnitude > 0.0000001f || !pp.Shown)
                {
                    t.position = pos;
                    float dist = Mathf.Sqrt(d.x * d.x + d.z * d.z);
                    if (dist > 0.0005f)
                    {
                        var axis = new Vector3(d.z / dist, 0f, -d.x / dist);
                        t.rotation = Quaternion.AngleAxis(dist / pp.Radius * Mathf.Rad2Deg, axis) * t.rotation;
                    }
                    pp.LastShown = pos; pp.Shown = true;
                    InvalidateDraw(pp.Ball);
                }
                bool pastEnd = pp.Samples.Count == 0 || renderT > pp.Samples[pp.Samples.Count - 1].t + 0.15f;
                if (now - pp.LastUpdate > 0.6f && pastEnd)
                {
                    t.position = pp.Target;
                    try { R.Set(pp.Spot, "Position", pp.Target); } catch { }
                    ReleasePuppet(pp);
                    _puppets.Remove(p);
                }
            }
        }

        private static void InvalidateDraw(object ball)
        {
            try
            {
                if (_invalidateDraw == null || _invalidateDraw.DeclaringType == null || !_invalidateDraw.DeclaringType.IsInstanceOfType(ball))
                    _invalidateDraw = ball.GetType().GetMethod("InvalidateDrawMatrix", Type.EmptyTypes);
                _invalidateDraw?.Invoke(ball, null);
            }
            catch { }
        }

        private static Vector3 Sample(List<(float t, Vector3 p)> s, float t)
        {
            if (s.Count == 0) return Vector3.zero;
            if (t <= s[0].t) return s[0].p;
            for (int i = 1; i < s.Count; i++)
            {
                if (t <= s[i].t)
                {
                    float span = s[i].t - s[i - 1].t;
                    float f = span > 0.0001f ? (t - s[i - 1].t) / span : 1f;
                    return Vector3.Lerp(s[i - 1].p, s[i].p, f);
                }
            }
            var last = s[s.Count - 1];
            if (s.Count < 2) return last.p;
            var prev = s[s.Count - 2];
            float dt = last.t - prev.t;
            if (dt < 0.0001f) return last.p;
            float over = Math.Min(t - last.t, 0.1f);
            return last.p + (last.p - prev.p) * (over / dt);
        }

        private static void ReleasePuppet(Puppet pp)
        {
            try
            {
                if (pp.Body != null && Alive(pp.Ball))
                    R.Set(pp.Body, "isKinematic", SafeBool(pp.Ball, "IsActive") ? false : pp.OrigKinematic);
            }
            catch { }
            try { _recentPuppet[R.Ptr(pp.Spot)] = Time.realtimeSinceStartup; } catch { }
        }
    }
}
