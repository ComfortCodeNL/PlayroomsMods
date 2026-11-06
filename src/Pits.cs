using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlayroomsCoop
{
    internal static partial class World
    {
        private static object Pit(string id)
        {
            if (_pits.TryGetValue(id, out var pit) && Alive(pit)) return pit;
            RefreshWorld(Time.realtimeSinceStartup);
            return _pits.TryGetValue(id, out pit) ? pit : null;
        }

        private static void PollPits()
        {
            foreach (var kv in _pits)
            {
                if (!Alive(kv.Value)) continue;
                int amount = R.Get<int>(kv.Value, "BallAmount");
                if (!_pitLast.TryGetValue(kv.Key, out int last)) { _pitLast[kv.Key] = amount; continue; }
                if (amount > last)
                {
                    _pitLast[kv.Key] = amount;
                    _outPits[kv.Key] = amount;
                }
            }
        }

        private const int MaxPitStep = 5000;

        private static void QueuePit(string id, int amount)
        {
            var pit = Pit(id);
            if (pit == null) return;
            int local = R.Get<int>(pit, "BallAmount");
            _pitTarget.TryGetValue(id, out int target);
            int from = Math.Max(target, local);
            amount = Math.Min(amount, from + MaxPitStep);
            if (_pitLast.TryGetValue(id, out int last)) _pitLast[id] = Math.Max(last, amount); else _pitLast[id] = Math.Max(local, amount);
            if (amount <= from) return;
            _pitTarget[id] = amount;
            _pitPending.TryGetValue(id, out int pending);
            _pitPending[id] = pending + (amount - from);
        }

        private static void DeliverPending(int budget)
        {
            if (_pitPending.Count == 0) return;
            Upgrades.NetZero(() =>
            {
                foreach (var id in new List<string>(_pitPending.Keys))
                {
                    if (budget <= 0) break;
                    var pit = Pit(id);
                    if (pit == null) { _pitPending.Remove(id); continue; }
                    int n = _pitPending[id];
                    _applying = true;
                    try
                    {
                        while (n > 0 && budget > 0)
                        {
                            int color = 0;
                            try { if (_ballService != null) color = Convert.ToInt32(R.Call(_ballService, "GetRandomColorIndex")); } catch { }
                            bool ok;
                            try { ok = DropBall(pit, color); }
                            catch (Exception e)
                            {
                                if (_stepWarned.Add("OnCountDelivered")) Plugin.Log.LogWarning("[Pits] delivery failed, setting amounts directly: " + ((e as System.Reflection.TargetInvocationException)?.InnerException ?? e).Message);
                                ok = false;
                            }
                            if (!ok) { SetPitDirect(pit, _pitTarget.TryGetValue(id, out int t) ? t : 0); n = 0; break; }
                            n--; budget--;
                        }
                    }
                    finally { _applying = false; }
                    if (n <= 0) _pitPending.Remove(id); else _pitPending[id] = n;
                }
            });
        }

        private static bool _dropLogged;

        private static bool DropBall(object pit, int color)
        {
            string style = (Plugin.PitDropStyle.Value ?? "vacuum").Trim().ToLowerInvariant();
            int before = R.Get<int>(pit, "BallAmount");
            bool ok;
            if (style == "flight")
            {
                if (R.Get<int>(pit, "FreeCapacity") <= 0) return false;
                R.Call(pit, "LaunchRolledInFlight", DropStart(pit), color);
                ok = true;
                if (R.Get<int>(pit, "BallAmount") == before)
                {
                    R.Set(pit, "BallAmount", before + 1);
                    R.Call(pit, "NotifyBallAmountChanged", true);
                }
            }
            else
            {
                int channel = style == "robot" ? 3 : style == "handvacuum" ? 5 : 4;
                ok = Convert.ToBoolean(R.Call(pit, "OnCountDelivered", channel, color));
            }
            if (!_dropLogged)
            {
                _dropLogged = true;
                Plugin.Log.LogInfo($"[Pits] drop style '{style}': count {before} -> {R.Get<int>(pit, "BallAmount")}");
            }
            return ok;
        }

        private static Vector3 DropStart(object pit)
        {
            Vector3 rim = (Vector3)R.Call(pit, "GetPitRimCenter");
            if (RemoteHead.HasValue)
            {
                var d = RemoteHead.Value - rim;
                if (d.x * d.x + d.y * d.y + d.z * d.z < 36f) return RemoteHead.Value + new Vector3(0f, -0.3f, 0f);
            }
            return rim + new Vector3(0f, 1.5f, 0f);
        }

        private static void SetPitDirect(object pit, int amount)
        {
            if (R.Get<int>(pit, "BallAmount") >= amount) return;
            R.Set(pit, "BallAmount", amount);
            Step(() => R.Call(pit, "NotifyBallAmountChanged", false), "NotifyBallAmountChanged");
            Step(() => R.Call(pit, "RefreshBallSurface"), "RefreshBallSurface");
        }
    }
}
