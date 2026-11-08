using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlayroomsCoop
{
    // used by other mods through reflection. add members, never change or remove them
    public static class CoopApi
    {
        public const int ApiVersion = 2;

        public static bool Connected => CoopBehaviour.ApiConnected;
        public static bool IsHost => CoopBehaviour.ApiIsHost;
        public static bool IsGuest => CoopBehaviour.ApiIsGuest;

        public static int PlayerCount => Connected ? Net.Count : 1;
        public static int MaxPlayers => Net.MaxPlayers;

        public static string[] Players
        {
            get
            {
                var ids = new List<ulong> { Net.Me };
                if (Connected) foreach (var p in Net.Peers.Values) ids.Add(p.Id);
                ids.Sort((a, b) => Net.SlotOf(a).CompareTo(Net.SlotOf(b)));
                var names = new string[ids.Count];
                for (int i = 0; i < ids.Count; i++) names[i] = Net.NameOf(ids[i]);
                return names;
            }
        }

        public static bool EveryoneInMenu
        {
            get
            {
                bool meInWorld;
                try { meInWorld = World.InGameWorld(); } catch { meInWorld = false; }
                if (meInWorld) return false;
                if (!Connected) return true;
                foreach (var p in Net.Peers.Values) if (p.InWorld) return false;
                return true;
            }
        }

        public static bool ReplayingPurchase => Upgrades.IsReplayingPurchase;

        public static void Notify(string text) => CoopBehaviour.Notify(text);

        private static uint _nextRequest = 1;
        private static readonly Dictionary<uint, (Action<bool> done, float at)> _waiting = new Dictionary<uint, (Action<bool>, float)>();
        private const float CounterTimeout = 6f;

        public static void TakeCounter(string key, int limit, Action<bool> done)
        {
            if (!ValidCounter(key, limit)) { done?.Invoke(false); return; }
            if (!IsGuest)
            {
                bool ok = false;
                try { ok = TakeLocal(key, limit); }
                catch (Exception e) { Plugin.Log.LogWarning("Counter " + key + ": " + e.Message); }
                done?.Invoke(ok);
                return;
            }
            uint id = _nextRequest++;
            _waiting[id] = (done, Time.realtimeSinceStartup);
            var w = Protocol.W.Begin(Msg.ModCounterTake); w.U32(id); w.Str(key); w.I32(limit);
            if (!Net.SendHost(w, SteamNet.SendReliable)) { _waiting.Remove(id); done?.Invoke(false); }
        }

        internal static bool ValidCounter(string key, int limit) =>
            !string.IsNullOrEmpty(key) && key.Length <= 64 && key.IndexOf("unlock", StringComparison.OrdinalIgnoreCase) >= 0
            && limit > 0 && limit <= 1000;

        private static bool TakeLocal(string key, int limit)
        {
            if (!ValidCounter(key, limit)) return false;
            object stats = null;
            foreach (var s in R.FindAll(R.T("TwoPixels.Services.Statistic.StatisticService"))) { stats = s; break; }
            if (stats == null) return false;
            var dict = R.Get(stats, "Statistics");
            int value = Convert.ToBoolean(R.Call(dict, "ContainsKey", key)) ? Convert.ToInt32(R.Call(dict, "get_Item", key)) : 0;
            if (value >= limit) return false;
            R.Call(stats, "SetStatistic", key, value + 1);
            return true;
        }

        internal static void Handle(Msg type, ulong from, byte[] buf, int len)
        {
            var r = new Reader(buf, len, Protocol.HeaderSize);
            if (type == Msg.ModCounterTake)
            {
                if (!IsHost) return;
                uint id = r.U32(); string key = r.Str(); int limit = r.I32();
                bool ok = false;
                try { ok = TakeLocal(key, limit); }
                catch (Exception e) { Plugin.Log.LogWarning("Counter " + key + ": " + e.Message); }
                var w = Protocol.W.Begin(Msg.ModCounterResult); w.U32(id); w.U8(ok ? (byte)1 : (byte)0);
                Net.SendTo(from, w, SteamNet.SendReliable);
            }
            else if (type == Msg.ModCounterResult)
            {
                if (from != Net.HostId) return;
                uint id = r.U32(); bool ok = r.U8() == 1;
                if (!_waiting.TryGetValue(id, out var wt)) return;
                _waiting.Remove(id);
                try { wt.done?.Invoke(ok); } catch (Exception e) { Plugin.Log.LogWarning("Counter answer: " + e.Message); }
            }
        }

        internal static void Tick(float now)
        {
            if (_waiting.Count == 0) return;
            List<uint> late = null;
            foreach (var kv in _waiting)
                if (now - kv.Value.at > CounterTimeout || !Connected) (late ??= new List<uint>()).Add(kv.Key);
            if (late == null) return;
            foreach (var id in late)
            {
                var done = _waiting[id].done;
                _waiting.Remove(id);
                try { done?.Invoke(false); } catch { }
            }
        }
    }
}
