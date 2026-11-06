using System;
using System.Collections.Generic;

namespace PlayroomsCoop
{
    internal static partial class Upgrades
    {
        internal static object SkillService()
        {
            if (World.Alive(_skillService)) return _skillService;
            _skillService = null;
            foreach (var s in R.FindAll(R.T(SkillServiceT))) { _skillService = s; break; }
            return _skillService;
        }

        private static Dictionary<string, int> ReadIntDict(object ss, string field)
        {
            var result = new Dictionary<string, int>();
            var save = R.Get(ss, "SaveData");
            var dict = save != null ? R.Get(save, field) : null;
            if (dict == null) return result;
            foreach (var (k, v) in R.Pairs(dict))
            {
                string id = k as string ?? k?.ToString();
                if (!string.IsNullOrEmpty(id)) result[id] = Convert.ToInt32(v);
            }
            return result;
        }

        private static readonly HashSet<string> _currencyIds = new HashSet<string>();
        private static float _lastIdScan = -99f;

        private static void AddCurrencyId(object def)
        {
            try { var id = def != null ? R.Get<string>(def, "CurrencyId") : null; if (!string.IsNullOrEmpty(id)) _currencyIds.Add(id); } catch { }
        }

        private static HashSet<string> CurrencyIds(object ss)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now - _lastIdScan < 5f && _currencyIds.Count > 0) return _currencyIds;
            _lastIdScan = now;
            try { foreach (var k in ReadIntDict(ss, "Currencies").Keys) _currencyIds.Add(k); } catch { }
            try { var bs = World.BallServiceObj; if (bs != null) AddCurrencyId(R.Get(bs, "collectCurrency")); } catch { }
            try
            {
                RefreshShop(now, false);
                foreach (var item in _shopItems.Values) if (World.Alive(item)) AddCurrencyId(R.Get(item, "Currency"));
            }
            catch { }
            return _currencyIds;
        }

        private static Dictionary<string, int> Balances(object ss)
        {
            var result = new Dictionary<string, int>();
            if (ss == null) return result;
            foreach (var id in CurrencyIds(ss))
            {
                try { result[id] = Convert.ToInt32(R.Call(ss, "GetCurrencyBalance", id)); } catch { }
            }
            return result;
        }

        private static readonly HashSet<string> _skillIds = new HashSet<string>();
        private static float _lastSkillScan = -99f;

        private static Dictionary<string, int> LiveLevels(object ss)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now - _lastSkillScan > 10f || _skillIds.Count == 0)
            {
                _lastSkillScan = now;
                try { foreach (var k in ReadIntDict(ss, "SkillLevels").Keys) _skillIds.Add(k); } catch { }
                try
                {
                    var skills = R.Get(ss, "Skills");
                    List<object> defs;
                    try { defs = new List<object>(); foreach (var (k, v) in R.Pairs(skills)) defs.Add(v ?? k); }
                    catch { defs = R.Items(skills); }
                    foreach (var d in defs)
                    {
                        try { var id = R.Get<string>(d, "SkillId"); if (!string.IsNullOrEmpty(id)) _skillIds.Add(id); } catch { }
                    }
                }
                catch { }
            }
            var result = new Dictionary<string, int>();
            foreach (var id in _skillIds)
            {
                try { result[id] = Convert.ToInt32(R.Call(ss, "GetSkillLevel", id)); } catch { }
            }
            return result;
        }

        private static int Balance(object ss, string id) => Convert.ToInt32(R.Call(ss, "GetCurrencyBalance", id));
        private static void Grant(object ss, string id, int amt, int source) { if (amt > 0) R.Call(ss, "GrantCurrency", id, amt, source); }
        private static bool Spend(object ss, string id, int amt) => amt <= 0 || Convert.ToBoolean(R.Call(ss, "SpendCurrency", id, amt, SinkOther));

        internal static void NetZero(Action op, bool keepEarnings = false)
        {
            var ss = SkillService();
            if (ss == null) { op(); return; }
            var before = Balances(ss);
            Applying = true;
            try { op(); }
            finally
            {
                try
                {
                    var after = Balances(ss);
                    foreach (var kv in after)
                    {
                        before.TryGetValue(kv.Key, out int b);
                        int diff = kv.Value - b;
                        if (diff > 0) Spend(ss, kv.Key, diff);
                        else if (diff < 0) Grant(ss, kv.Key, -diff, SourceRefund);
                    }
                }
                finally { Applying = false; }
            }
        }

        private static uint _walletSeq, _walletAck;
        private static readonly List<(uint seq, string id, int amount)> _unacked = new List<(uint, string, int)>();
        private static bool _walletDirty;
        private static float _nextWalletState;

        private static void WalletReset() { _walletSeq = 0; _walletAck = 0; _unacked.Clear(); _walletDirty = true; _nextWalletState = 0f; }

        private static void WalletTick(object ss, float now)
        {
            var bal = Balances(ss);
            if (_isHost)
            {
                foreach (var kv in bal)
                    if (!_lastBalances.TryGetValue(kv.Key, out int last) || last != kv.Value) _walletDirty = true;
                _lastBalances = bal;
                if (now < _nextWalletState || (!_walletDirty && now < _nextWalletState + 0.75f)) return;
                _nextWalletState = now + 0.25f; _walletDirty = false;
                var w = Protocol.W.Begin(Msg.WalletState); w.U32(_walletAck); w.U16((ushort)bal.Count);
                foreach (var kv in bal) { w.Str(kv.Key); w.I32(kv.Value); }
                w.SendTo(_peer, SteamNet.SendReliable);
                return;
            }
            SendOwnChanges(bal);
        }

        private static void SendOwnChanges(Dictionary<string, int> bal)
        {
            foreach (var kv in bal)
            {
                if (!_lastBalances.TryGetValue(kv.Key, out int last)) continue;
                int d = kv.Value - last;
                if (d == 0) continue;
                _unacked.Add((++_walletSeq, kv.Key, d));
                var w = Protocol.W.Begin(Msg.WalletDelta); w.Str(kv.Key); w.I32(d); w.U32(_walletSeq);
                w.SendTo(_peer, SteamNet.SendReliable);
            }
            _lastBalances = bal;
        }

        private static void OnWalletDelta(object ss, string id, int d, uint seq)
        {
            if (!_isHost || (int)(seq - _walletAck) <= 0) return;
            if (d > 100000000 || d < -100000000) { Plugin.Log.LogWarning($"[Wallet] ignored an impossible change ({d})"); _walletAck = seq; return; }
            _walletAck = seq;
            Applying = true;
            try { if (d > 0) Grant(ss, id, d, SourceOther); else Spend(ss, id, -d); }
            finally { Applying = false; }
            _walletDirty = true;
        }

        private static void OnWalletState(object ss, uint ack, Dictionary<string, int> host)
        {
            if (_isHost) return;
            if (_walletReady) SendOwnChanges(Balances(ss));
            _unacked.RemoveAll(u => (int)(u.seq - ack) <= 0);
            Applying = true;
            try
            {
                foreach (var kv in host)
                {
                    int target = kv.Value;
                    foreach (var u in _unacked) if (u.id == kv.Key) target += u.amount;
                    int cur = Balance(ss, kv.Key);
                    if (target > cur) Grant(ss, kv.Key, target - cur, SourceOther);
                    else if (target < cur) Spend(ss, kv.Key, cur - target);
                    _lastBalances[kv.Key] = Balance(ss, kv.Key);
                }
            }
            finally { Applying = false; }
            _walletReady = true;
        }
    }
}
