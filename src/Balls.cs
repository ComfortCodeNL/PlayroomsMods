using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlayroomsCoop
{
    internal static partial class World
    {
        private static void ProcessPendingTakes(int budget)
        {
            while (budget-- > 0 && _pendingTakes.Count > 0)
            {
                if (_rooms.Count == 0) return;
                var (room, idx) = _pendingTakes.Peek();
                if (_warnedRooms.Contains(room)) { _pendingTakes.Dequeue(); continue; }
                if (!_rooms.TryGetValue(room, out var roomObj) || !Alive(roomObj))
                {
                    RefreshWorld(Time.realtimeSinceStartup);
                    if (!_rooms.TryGetValue(room, out roomObj))
                    {
                        _pendingTakes.Dequeue();
                        if (_warnedRooms.Add(room)) Plugin.Log.LogWarning($"Room '{room}' from the other player isn't loaded here.");
                        continue;
                    }
                }
                _pendingTakes.Dequeue();
                Balls.Try(() => ApplyTake(roomObj, idx), "apply pickup");
            }
        }

        private static void ApplyTake(object room, int idx)
        {
            var spots = R.Get(room, "_ballSpots");
            if (idx < 0 || idx >= R.Count(spots)) return;
            var spot = R.At(spots, idx);
            if (spot == null || R.Get<bool>(spot, "IsCollected")) return;

            var bs = _ballService;
            object pos = R.Get(spot, "Position");
            _applying = true;
            try
            {
                var inst = R.Get(spot, "Instance");
                if (inst != null && bs != null)
                {
                    Step(() => R.Call(bs, "RemoveActive", inst), "RemoveActive");
                    Step(() => R.Call(bs, "ReleaseBallInstance", inst), "ReleaseBallInstance");
                }
                if (bs != null) Step(() => R.Call(bs, "RemoveDormant", spot), "RemoveDormant");
                Step(() => R.Call(spot, "OnBallCollected"), "OnBallCollected");
                if (!R.Get<bool>(spot, "IsCollected")) Step(() => R.Set(spot, "IsCollected", true), "IsCollected");
                if (bs != null && pos != null) Step(() => R.Call(bs, "PromoteBallsRestingOn", pos), "PromoteBallsRestingOn");
            }
            finally { _applying = false; }
        }

        private static readonly HashSet<string> _stepWarned = new HashSet<string>();
        private static void Step(Action a, string name)
        {
            try { a(); }
            catch (Exception e)
            {
                e = (e as System.Reflection.TargetInvocationException)?.InnerException ?? e;
                if (_stepWarned.Add(name)) Plugin.Log.LogWarning($"[Balls] step {name} failed: {e.Message}");
            }
        }

        private static void FlushOutgoing()
        {
            if (_outTaken.Count > 0)
            {
                for (int start = 0; start < _outTaken.Count; start += 2000)
                {
                    int n = Math.Min(2000, _outTaken.Count - start);
                    var w = Protocol.W.Begin(Msg.BallsTaken);
                    w.U16((ushort)n);
                    for (int i = 0; i < n; i++) { w.Str(_outTaken[start + i].room); w.I32(_outTaken[start + i].idx); }
                    w.SendTo(_peer, SteamNet.SendReliable);
                }
                _outTaken.Clear();
            }
            if (_outLive.Count > 0)
            {
                float sentAt = Time.realtimeSinceStartup;
                for (int start = 0; start < _outLive.Count; start += 60)
                {
                    int n = Math.Min(60, _outLive.Count - start);
                    var rooms = new List<string>();
                    for (int i = 0; i < n; i++) if (!rooms.Contains(_outLive[start + i].room)) rooms.Add(_outLive[start + i].room);
                    var w = Protocol.W.Begin(Msg.BallsLive);
                    w.F32(sentAt);
                    w.U8((byte)rooms.Count);
                    foreach (var rm in rooms) w.Str(rm);
                    w.U16((ushort)n);
                    for (int i = 0; i < n; i++)
                    {
                        var m = _outLive[start + i];
                        w.U8((byte)rooms.IndexOf(m.room)); w.I32(m.idx); w.F32(m.pos.x); w.F32(m.pos.y); w.F32(m.pos.z);
                    }
                    w.SendTo(_peer, SteamNet.SendUnreliable);
                }
                _liveSentCount += _outLive.Count;
                _outLive.Clear();
            }
            if (_outMoved.Count > 0)
            {
                for (int start = 0; start < _outMoved.Count; start += 1500)
                {
                    int n = Math.Min(1500, _outMoved.Count - start);
                    var w = Protocol.W.Begin(Msg.BallsMoved);
                    w.U16((ushort)n);
                    for (int i = 0; i < n; i++)
                    {
                        var m = _outMoved[start + i];
                        w.Str(m.room); w.I32(m.idx); w.F32(m.pos.x); w.F32(m.pos.y); w.F32(m.pos.z);
                    }
                    w.SendTo(_peer, SteamNet.SendReliable);
                }
                _outMoved.Clear();
            }
            if (_outPits.Count > 0)
            {
                foreach (var kv in _outPits)
                {
                    var w = Protocol.W.Begin(Msg.PitAmount);
                    w.Str(kv.Key); w.I32(kv.Value);
                    w.SendTo(_peer, SteamNet.SendReliable);
                }
                _outPits.Clear();
            }
        }

        private static void SendSnapshot()
        {
            RefreshWorld(Time.realtimeSinceStartup);
            var w = Protocol.W.Begin(Msg.Snapshot);
            w.U16((ushort)_rooms.Count);
            int collected = 0;
            byte[] bits = Array.Empty<byte>();
            foreach (var kv in _rooms)
            {
                var spots = R.Get(kv.Value, "_ballSpots");
                int n = R.Count(spots);
                int bytes = (n + 7) / 8;
                if (bits.Length < bytes) bits = new byte[bytes];
                Array.Clear(bits, 0, bytes);
                for (int i = 0; i < n; i++)
                {
                    var s = R.At(spots, i);
                    if (s != null && R.Get<bool>(s, "IsCollected")) { bits[i >> 3] |= (byte)(1 << (i & 7)); collected++; }
                }
                w.Str(kv.Key); w.I32(n); w.Bytes(bits, bytes);
            }
            w.U16((ushort)_pits.Count);
            foreach (var kv in _pits) { w.Str(kv.Key); w.I32(R.Get<int>(kv.Value, "BallAmount")); }
            w.SendTo(_peer, SteamNet.SendReliable);
            Plugin.Log.LogInfo($"Sent world snapshot: {_rooms.Count} rooms, {collected} balls collected, {_pits.Count} pits ({w.Len / 1024} KB)");
        }

        private static object SpotAt(string roomId, int idx)
        {
            if (!_rooms.TryGetValue(roomId, out var room) || !Alive(room))
            {
                RefreshWorld(Time.realtimeSinceStartup);
                if (!_rooms.TryGetValue(roomId, out room)) return null;
            }
            var spots = R.Get(room, "_ballSpots");
            if (idx < 0 || idx >= R.Count(spots)) return null;
            return R.At(spots, idx);
        }

        private static bool SafeBool(object o, string member)
        {
            try { return R.Get<bool>(o, member); } catch { return false; }
        }

        private static void ApplyMove(string roomId, int idx, Vector3 pos)
        {
            if (!_rooms.TryGetValue(roomId, out var room) || !Alive(room))
            {
                RefreshWorld(Time.realtimeSinceStartup);
                if (!_rooms.TryGetValue(roomId, out room)) return;
            }
            var spots = R.Get(room, "_ballSpots");
            if (idx < 0 || idx >= R.Count(spots)) return;
            var spot = R.At(spots, idx);
            if (spot == null) return;
            if (_puppets.TryGetValue(R.Ptr(spot), out var pup)) { ReleasePuppet(pup); _puppets.Remove(R.Ptr(spot)); }
            var bs = _ballService;
            _applying = true;
            try
            {
                bool wasCollected = R.Get<bool>(spot, "IsCollected");
                var inst = R.Get(spot, "Instance");
                if (inst != null && SafeBool(inst, "IsActive"))
                {
                    Step(() => ((Component)inst).transform.position = pos, "move instance");
                    Step(() => R.Call(inst, "InvalidateDrawMatrix"), "InvalidateDrawMatrix");
                    return;
                }
                if (inst != null && bs != null)
                    Step(() => R.Call(bs, "ReturnBallInstance", spot), "ReturnBallInstance");
                if (!wasCollected && bs != null) Step(() => R.Call(bs, "RemoveDormant", spot), "RemoveDormant");
                Step(() => R.Set(spot, "Position", pos), "Position");
                if (wasCollected)
                {
                    Step(() => R.Set(spot, "IsCollected", false), "un-collect");
                    Step(() =>
                    {
                        int c = R.Get<int>(room, "_collectedBallCount");
                        if (c > 0) R.Set(room, "_collectedBallCount", c - 1);
                    }, "room count");
                }
                if (bs != null) Step(() => R.Call(bs, "RegisterDormant", spot), "RegisterDormant");
            }
            finally { _applying = false; }
        }
    }
}
