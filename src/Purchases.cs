using System;
using System.Collections.Generic;

namespace PlayroomsCoop
{
    internal static partial class Upgrades
    {
        private const string SkillServiceT = "TwoPixels.Services.Skill.SkillService";
        private const string PurchaseT = "TwoPixels.Interactables.PurchaseInteractable";
        private const string NewMarksT = "TwoPixels.Windows95.Win95NewMarks";
        private const string SkillsWindowT = "TwoPixels.Windows95.Windows95SkillsWindow";
        private const string ShopWindowT = "TwoPixels.Windows95.Windows95ShopWindow";
        private const int SourceOther = 0, SourceRefund = 2;
        private const int SinkOther = 0;

        public static Feature Wallet, Feature;
        private static bool _connected, _isHost, _walletReady, _baselined;
        private static ulong _peer;
        public static bool Applying { get; private set; }
        private static bool _replayingPurchase;

        internal static bool OnEquipPurchasedTool() => !_replayingPurchase;

        private static object _skillService;
        private static Dictionary<string, int> _lastBalances = new Dictionary<string, int>();
        private static Dictionary<string, int> _lastLevels = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> _lastPurchases = new Dictionary<string, int>();
        private static readonly Dictionary<string, object> _shopItems = new Dictionary<string, object>();
        private static float _nextPoll, _nextShopPoll, _lastShopRefresh = -99f, _nextSnapshotTry;
        private static IntPtr _snapshotSentFor;
        private static byte[] _pendingSnapshot;

        public static void Init()
        {
            Feature = new Feature("Upgrades", Plugin.SyncUpgrades.Value);
            Wallet = new Feature("Wallet", Plugin.SharedWallet.Value);
        }

        public static void OnConnected(ulong peer, bool isHost)
        {
            _connected = true; _peer = peer; _isHost = isHost;
            WalletReset();
            _walletReady = isHost || !Wallet.Enabled;
            _baselined = false;
            _snapshotSentFor = IntPtr.Zero;
            _lastPurchases.Clear();
            _pendingSnapshot = null;
        }

        public static void OnDisconnected() { _connected = false; _peer = 0; }

        public static void Tick(float now)
        {
            if (!_connected) return;
            var ss = SkillService();
            if (ss == null) return;
            if (_pendingSnapshot != null) { var p = _pendingSnapshot; ApplySnapshot(p, p.Length); }

            if (now >= _nextSnapshotTry && R.Ptr(ss) != _snapshotSentFor)
            {
                _nextSnapshotTry = now + 2f;
                _snapshotSentFor = R.Ptr(ss);
                Feature.Try(() => SendSnapshot(ss), "send snapshot");
            }

            if (now < _nextPoll) return;
            _nextPoll = now + 0.25f;

            if (!_baselined)
            {
                _baselined = true;
                Wallet.Try(() => _lastBalances = Balances(ss), "baseline wallet");
                Feature.Try(() => _lastLevels = LiveLevels(ss), "baseline skills");
                return;
            }

            if (_walletReady && Wallet.Enabled) Wallet.Try(() => WalletTick(ss, now), "wallet poll");

            Feature.Try(() =>
            {
                var levels = LiveLevels(ss);
                foreach (var kv in levels)
                {
                    if (!_lastLevels.TryGetValue(kv.Key, out int last)) continue;
                    if (kv.Value <= last) continue;
                    var w = Protocol.W.Begin(Msg.SkillUp); w.Str(kv.Key); w.I32(kv.Value);
                    w.SendTo(_peer, SteamNet.SendReliable);
                    Plugin.Log.LogInfo($"Upgrade bought here: {kv.Key} -> level {kv.Value}");
                }
                _lastLevels = levels;
            }, "skill poll");

            if (now >= _nextShopPoll)
            {
                _nextShopPoll = now + 1f;
                Feature.Try(() =>
                {
                    RefreshShop(now, false);
                    foreach (var kv in _shopItems)
                    {
                        if (!World.Alive(kv.Value)) continue;
                        int count = Convert.ToInt32(R.Get(kv.Value, "PurchasedCount"));
                        bool known = _lastPurchases.TryGetValue(kv.Key, out int last);
                        _lastPurchases[kv.Key] = count;
                        if (!known || count <= last) continue;
                        var w = Protocol.W.Begin(Msg.ShopBuy); w.Str(kv.Key); w.I32(count);
                        w.SendTo(_peer, SteamNet.SendReliable);
                        Plugin.Log.LogInfo($"Shop item bought here: {kv.Key} (owned {count})");
                    }
                }, "shop poll");
            }
        }

        private static string ShopKey(object item)
        {
            try
            {
                var t = R.T(NewMarksT);
                if (t != null && R.CallStatic(t, "ShopId", item) is string id && id.Length > 0) return id;
            }
            catch { }
            string stat = null;
            try { stat = R.Get<string>(item, "rewardStatisticName"); } catch { }
            return (stat ?? "") + "|" + ((UnityEngine.Object)item).name;
        }

        private static void RefreshShop(float now, bool force)
        {
            if (!force && now - _lastShopRefresh < 5f) return;
            _lastShopRefresh = now;
            _shopItems.Clear();
            foreach (var item in R.FindAll(R.T(PurchaseT))) _shopItems[ShopKey(item)] = item;
        }

        private static List<(string id, int amount)> SkillCost(object ss, string skillId, int currentLevel)
        {
            var list = new List<(string, int)>();
            var skills = R.Get(ss, "Skills");
            object def = null;
            try
            {
                foreach (var (k, v) in R.Pairs(skills))
                {
                    var d = v ?? k;
                    if (d != null && R.Get<string>(d, "SkillId") == skillId) { def = d; break; }
                }
            }
            catch
            {
                int n = R.Count(skills);
                for (int i = 0; i < n && def == null; i++)
                {
                    var d = R.At(skills, i);
                    if (d != null && R.Get<string>(d, "SkillId") == skillId) def = d;
                }
            }
            if (def == null) return null;
            var levels = R.Get(def, "Levels");
            if (currentLevel < 0 || currentLevel >= R.Count(levels)) return null;
            var costs = R.Get(R.At(levels, currentLevel), "Costs");
            int c = R.Count(costs);
            for (int i = 0; i < c; i++)
            {
                var cost = R.At(costs, i);
                list.Add((R.Get<string>(R.Get(cost, "Currency"), "CurrencyId"), R.Get<int>(cost, "Amount")));
            }
            return list;
        }

        private static string Pretty(string id)
        {
            if (string.IsNullOrEmpty(id)) return "an upgrade";
            id = id.Replace('_', ' ').Replace('-', ' ');
            return char.ToUpperInvariant(id[0]) + id.Substring(1);
        }

        private static void ApplySkill(string skillId, int target, bool notify)
        {
            var ss = SkillService();
            if (ss == null) return;
            int guard = 0;
            while (guard++ < 20)
            {
                int cur = Convert.ToInt32(R.Call(ss, "GetSkillLevel", skillId));
                if (cur >= target) break;
                var cost = SkillCost(ss, skillId, cur);
                string failure = null;
                NetZero(() =>
                {
                    if (cost != null) foreach (var (id, amt) in cost) Grant(ss, id, amt, SourceRefund);
                    else foreach (var id in Balances(ss).Keys) Grant(ss, id, 1000000, SourceRefund);
                    try { R.Call(ss, "UnlockSkill", skillId); }
                    catch (Exception e) { failure = ((e as System.Reflection.TargetInvocationException)?.InnerException ?? e).Message; }
                });
                if (failure != null)
                {
                    Plugin.Log.LogInfo($"Partner's upgrade {skillId} level {cur + 1} not available here yet (room or requirement not reached)");
                    break;
                }
                int now = Convert.ToInt32(R.Call(ss, "GetSkillLevel", skillId));
                if (now <= cur)
                {
                    Plugin.Log.LogWarning($"Couldn't apply partner's upgrade {skillId} (requirements not met in this world yet)");
                    break;
                }
                Plugin.Log.LogInfo($"Partner's upgrade applied: {skillId} -> level {now}");
            }
            _lastLevels = LiveLevels(ss);
            if (notify) CoopBehaviour.Notify($"Partner bought: {SteamNet.Plain(Pretty(skillId))}");
            RefreshWindows();
        }

        private static void ApplyPurchase(string key, int target, bool notify)
        {
            RefreshShop(UnityEngine.Time.realtimeSinceStartup, !_shopItems.ContainsKey(key));
            if (!_shopItems.TryGetValue(key, out var item) || !World.Alive(item))
            {
                Plugin.Log.LogWarning($"Partner's purchase '{key}' isn't in this world");
                return;
            }
            var ss = SkillService();
            int guard = 0;
            while (guard++ < 20 && ss != null)
            {
                int cur = Convert.ToInt32(R.Get(item, "PurchasedCount"));
                if (cur >= target) break;
                int price = Convert.ToInt32(R.Get(item, "CurrentPrice"));
                var currency = R.Get(item, "Currency");
                string curId = currency != null ? R.Get<string>(currency, "CurrencyId") : null;
                NetZero(() =>
                {
                    if (curId != null) Grant(ss, curId, price, SourceRefund);
                    _replayingPurchase = true;
                    try { R.Call(item, "CompletePurchase"); }
                    finally { _replayingPurchase = false; }
                });
                int now = Convert.ToInt32(R.Get(item, "PurchasedCount"));
                if (now <= cur) { Plugin.Log.LogWarning($"Couldn't apply partner's purchase {key}"); break; }
                Plugin.Log.LogInfo($"Partner's purchase applied: {key} (owned {now})");
            }
            _lastPurchases[key] = Convert.ToInt32(R.Get(item, "PurchasedCount"));
            string name = key;
            try { name = R.Get<string>(item, "ItemName") ?? key; } catch { }
            if (notify) CoopBehaviour.Notify($"Partner bought: {SteamNet.Plain(name)}");
            RefreshWindows();
        }

        private static void RefreshWindows()
        {
            foreach (var tn in new[] { SkillsWindowT, ShopWindowT })
            {
                try
                {
                    foreach (var w in R.FindAll(R.T(tn)))
                    {
                        try { R.Call(w, "Refresh"); } catch { }
                        if (tn == SkillsWindowT) { try { R.Call(w, "RefreshDetails"); } catch { } }
                    }
                }
                catch { }
            }
        }

        private static void SendSnapshot(object ss)
        {
            var w = Protocol.W.Begin(Msg.UpgradeSnapshot);
            bool sendWallet = _isHost && Wallet.Enabled;
            var bal = sendWallet ? Balances(ss) : new Dictionary<string, int>();
            w.U16((ushort)bal.Count);
            foreach (var kv in bal) { w.Str(kv.Key); w.I32(kv.Value); }

            var levels = Feature.Enabled ? LiveLevels(ss) : new Dictionary<string, int>();
            w.U16((ushort)levels.Count);
            foreach (var kv in levels) { w.Str(kv.Key); w.I32(kv.Value); }

            RefreshShop(UnityEngine.Time.realtimeSinceStartup, true);
            var counts = new List<(string, int)>();
            if (Feature.Enabled)
                foreach (var kv in _shopItems)
                    if (World.Alive(kv.Value)) counts.Add((kv.Key, Convert.ToInt32(R.Get(kv.Value, "PurchasedCount"))));
            w.U16((ushort)counts.Count);
            foreach (var (k, c) in counts) { w.Str(k); w.I32(c); }
            w.SendTo(_peer, SteamNet.SendReliable);
            Plugin.Log.LogInfo($"Sent upgrade snapshot: wallet {(sendWallet ? "yes" : "no")}, {levels.Count} skills, {counts.Count} shop items");
        }

        private static void ApplySnapshot(byte[] buf, int len)
        {
            var r = new Reader(buf, len, Protocol.HeaderSize);
            var bal = new List<(string, int)>();
            int n = r.U16(); for (int i = 0; i < n; i++) bal.Add((r.Str(), r.I32()));
            var levels = new List<(string, int)>();
            n = r.U16(); for (int i = 0; i < n; i++) levels.Add((r.Str(), r.I32()));
            var counts = new List<(string, int)>();
            n = r.U16(); for (int i = 0; i < n; i++) counts.Add((r.Str(), r.I32()));

            var ss = SkillService();
            if (ss == null)
            {
                _pendingSnapshot = new byte[len];
                Buffer.BlockCopy(buf, 0, _pendingSnapshot, 0, len);
                return;
            }
            _pendingSnapshot = null;

            if (Feature.Enabled)
            {
                var mine = LiveLevels(ss);
                foreach (var (id, lvl) in levels)
                {
                    mine.TryGetValue(id, out int cur);
                    if (lvl > cur) Feature.Try(() => ApplySkill(id, lvl, false), "snapshot skill");
                }
                foreach (var (key, c) in counts) Feature.Try(() => ApplyPurchase(key, c, false), "snapshot shop");
            }

            if (!_isHost && Wallet.Enabled && bal.Count > 0)
            {
                Wallet.Try(() =>
                {
                    Applying = true;
                    try
                    {
                        foreach (var (id, target) in bal)
                        {
                            int cur = Balance(ss, id);
                            if (target > cur) Grant(ss, id, target - cur, SourceOther);
                            else if (target < cur) Spend(ss, id, cur - target);
                        }
                    }
                    finally { Applying = false; }
                    _lastBalances = Balances(ss);
                    _walletReady = true;
                    Plugin.Log.LogInfo("Wallet now shared with the host");
                }, "set wallet");
            }
            _lastLevels = LiveLevels(ss);
            RefreshWindows();
        }

        public static void Handle(Msg type, byte[] buf, int len)
        {
            var r = new Reader(buf, len, Protocol.HeaderSize);
            switch (type)
            {
                case Msg.WalletDelta:
                {
                    string id = r.Str(); int d = r.I32(); uint seq = r.U32();
                    if (!Wallet.Enabled) return;
                    Wallet.Try(() => { var ss = SkillService(); if (ss != null) OnWalletDelta(ss, id, d, seq); }, "apply wallet");
                    break;
                }
                case Msg.WalletState:
                {
                    uint ack = r.U32(); int n = r.U16();
                    var host = new Dictionary<string, int>();
                    for (int i = 0; i < n; i++) { string id = r.Str(); host[id] = r.I32(); }
                    if (!Wallet.Enabled) return;
                    Wallet.Try(() => { var ss = SkillService(); if (ss != null) OnWalletState(ss, ack, host); }, "host wallet");
                    break;
                }
                case Msg.SkillUp:
                {
                    string id = r.Str(); int lvl = r.I32();
                    if (Feature.Enabled) Feature.Try(() => ApplySkill(id, lvl, true), "apply upgrade");
                    break;
                }
                case Msg.ShopBuy:
                {
                    string key = r.Str(); int c = r.I32();
                    if (Feature.Enabled) Feature.Try(() => ApplyPurchase(key, c, true), "apply purchase");
                    break;
                }
                case Msg.UpgradeSnapshot:
                    ApplySnapshot(buf, len);
                    break;
            }
        }
    }
}
