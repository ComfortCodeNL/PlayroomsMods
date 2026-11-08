using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlayroomsCoop
{
    internal static class Props
    {
        private sealed class Prop
        {
            public string Key; public object Body; public Transform T;
            public Vector3 LastPos; public Quaternion LastRot;
            public bool Moving; public float StillSince; public Vector3 LastSentPos; public Quaternion LastSentRot;
            public bool Puppet; public bool OrigKinematic; public float LastUpdate, PuppetEnded;
            public readonly List<(float t, Vector3 p, Quaternion r)> Samples = new List<(float, Vector3, Quaternion)>();
        }

        public static Feature Feature;
        private static bool _connected, _isHost, _snapshotSent;
        private static readonly Dictionary<string, Prop> _props = new Dictionary<string, Prop>();
        private static float _lastScan = -99f, _nextTick;
        private const float Delay = 0.1f;

        public static void Init() => Feature = new Feature("Props", Plugin.SyncProps.Value);

        public static void OnConnected(bool isHost)
        {
            _connected = true; _isHost = isHost; _snapshotSent = false;
            _props.Clear(); _lastScan = -99f;
        }

        public static void OnDisconnected()
        {
            foreach (var p in _props.Values) if (p.Puppet) EndPuppet(p);
            _connected = false; _props.Clear();
        }

        private static string PathOf(Transform t)
        {
            var parts = new List<string>();
            object cur = t;
            for (int depth = 0; cur != null && depth < 12; depth++)
            {
                var tr = (Transform)cur;
                int idx = 0;
                try { idx = Convert.ToInt32(R.Call(tr, "GetSiblingIndex")); } catch { }
                parts.Add(tr.gameObject.name + "#" + idx);
                cur = R.Get(tr, "parent");
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static readonly HashSet<IntPtr> _excludedRoots = new HashSet<IntPtr>();

        private static void CollectExclusions()
        {
            _excludedRoots.Clear();
            foreach (var tn in new[] { "TwoPixels.Ball.RobotVacuum", "TwoPixels.Ball.VacuumDevice", "TwoPixels.Ball.RobotStation",
                                       "TwoPixels.Enemy.EnemyController", "TwoPixels.Collectables.SparrowBrain",
                                       "TwoPixels.Collectables.CollectableInteractable", "TwoPixels.Ball.Ball",
                                       "TwoPixels.Player.PlayerBehaviourController", "UnityEngine.CharacterController" })
            {
                var t = R.T(tn);
                if (t == null) continue;
                foreach (var o in R.FindAll(t)) _excludedRoots.Add(R.Ptr(((Component)o).gameObject));
            }
            _playerChain.Clear();
            try
            {
                object cur = World.PlayerBody();
                for (int depth = 0; cur != null && depth < 4; depth++)
                {
                    var tr = (Transform)cur;
                    if (depth == 0) _excludedRoots.Add(R.Ptr(tr.gameObject));
                    _playerChain.Add(R.Ptr(tr.gameObject));
                    cur = R.Get(tr, "parent");
                }
            }
            catch { }
            try
            {
                var cam = R.GetStatic(R.T("UnityEngine.Camera"), "main") as Component;
                if (cam != null) _excludedRoots.Add(R.Ptr(cam.gameObject));
            }
            catch { }
        }

        private static readonly HashSet<IntPtr> _playerChain = new HashSet<IntPtr>();

        private static bool Excluded(Transform t)
        {
            object cur = t;
            for (int depth = 0; cur != null && depth < 8; depth++)
            {
                var tr = (Transform)cur;
                if (_excludedRoots.Contains(R.Ptr(tr.gameObject))) return true;
                cur = R.Get(tr, "parent");
            }
            return false;
        }

        private static void Scan(float now)
        {
            _lastScan = now;
            var rbType = R.T("UnityEngine.Rigidbody");
            if (rbType == null) return;
            CollectExclusions();
            var seen = new HashSet<string>();
            var byBody = new Dictionary<IntPtr, string>();
            foreach (var p in _props.Values) { try { byBody[R.Ptr(p.Body)] = p.Key; } catch { } }
            foreach (var rb in R.FindAll(rbType))
            {
                var t = ((Component)rb).transform;
                if (t == null) continue;
                if (Excluded(t) || _playerChain.Contains(R.Ptr(t.gameObject)))
                {
                    if (byBody.TryGetValue(R.Ptr(rb), out var gone) && _props.TryGetValue(gone, out var gp))
                    {
                        if (gp.Puppet) EndPuppet(gp);
                        _props.Remove(gone);
                    }
                    continue;
                }
                bool kin = false;
                try { kin = Convert.ToBoolean(R.Get(rb, "isKinematic")); } catch { }
                byBody.TryGetValue(R.Ptr(rb), out string key);
                if (key != null && !_props.ContainsKey(key)) key = null;
                if (key == null)
                {
                    if (kin) continue;
                    key = PathOf(t);
                    if (_props.ContainsKey(key)) continue;
                    _props[key] = new Prop { Key = key, Body = rb, T = t, LastPos = t.position, LastRot = t.rotation, LastSentPos = t.position, LastSentRot = t.rotation };
                }
                seen.Add(key);
            }
            foreach (var k in new List<string>(_props.Keys))
                if (!seen.Contains(k) && !_props[k].Puppet) _props.Remove(k);
        }

        public static void Tick(float now, float dt)
        {
            if (!_connected || !Feature.Enabled || !World.InGameWorld()) return;
            Feature.Try(() =>
            {
                if (now - _lastScan > 5f) Scan(now);
                if (!_snapshotSent && _props.Count > 0)
                {
                    _snapshotSent = true;
                    if (_isHost) SendRest(new List<Prop>(_props.Values), "layout");
                }

                TickPuppets(now);
                if (now < _nextTick) return;
                _nextTick = now + 0.05f;

                var me = World.LocalHead;
                var live = new List<Prop>(); var rested = new List<Prop>();
                foreach (var p in _props.Values)
                {
                    if (p.Puppet || !World.Alive(p.T)) continue;
                    var pos = p.T.position; var rot = p.T.rotation;
                    bool moved = Dist(pos, p.LastPos) > 0.002f || Angle(rot, p.LastRot) > 0.3f;
                    p.LastPos = pos; p.LastRot = rot;

                    if (moved && !p.Moving)
                    {
                        if (now - p.PuppetEnded < 2f || me == null) continue;
                        float dMe = Dist(pos, me.Value);
                        if (dMe > 8f || Net.OtherCloser(pos, dMe)) continue;
                        p.Moving = true; p.StillSince = 0f;
                    }
                    if (!p.Moving) continue;

                    if (Dist(pos, p.LastSentPos) > 0.005f || Angle(rot, p.LastSentRot) > 0.5f)
                    {
                        live.Add(p); p.LastSentPos = pos; p.LastSentRot = rot;
                    }
                    if (!moved)
                    {
                        if (p.StillSince == 0f) p.StillSince = now;
                        else if (now - p.StillSince > 0.6f) { p.Moving = false; rested.Add(p); }
                    }
                    else p.StillSince = 0f;
                }
                if (live.Count > 0) SendLive(live, now);
                if (rested.Count > 0) SendRest(rested, null);
            }, "props");
        }

        private static float Dist(Vector3 a, Vector3 b) { float x = a.x - b.x, y = a.y - b.y, z = a.z - b.z; return Mathf.Sqrt(x * x + y * y + z * z); }

        private static float Angle(Quaternion a, Quaternion b)
        {
            float dot = Math.Abs(a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w);
            return dot >= 1f ? 0f : 2f * (float)Math.Acos(Math.Min(1f, dot)) * Mathf.Rad2Deg;
        }

        private static void WritePose(Writer w, Prop p)
        {
            var pos = p.T.position; var r = p.T.rotation;
            w.Str(p.Key); w.F32(pos.x); w.F32(pos.y); w.F32(pos.z); w.F32(r.x); w.F32(r.y); w.F32(r.z); w.F32(r.w);
        }

        private static void SendLive(List<Prop> list, float now)
        {
            for (int s = 0; s < list.Count; s += 100)
            {
                int n = Math.Min(100, list.Count - s);
                var w = Protocol.W.Begin(Msg.PropsLive);
                w.F32(now); w.U16((ushort)n);
                for (int i = 0; i < n; i++) WritePose(w, list[s + i]);
                w.SendAll(SteamNet.SendUnreliable);
            }
        }

        private static void SendRest(List<Prop> list, string why)
        {
            for (int s = 0; s < list.Count; s += 300)
            {
                int n = Math.Min(300, list.Count - s);
                var w = Protocol.W.Begin(Msg.PropsRest);
                w.U16((ushort)n);
                for (int i = 0; i < n; i++) WritePose(w, list[s + i]);
                w.SendAll(SteamNet.SendReliable);
            }
            if (why != null) Plugin.Log.LogInfo($"[Props] sent {why} of {list.Count} loose objects");
        }

        public static void Handle(Msg type, byte[] buf, int len)
        {
            if (!Feature.Enabled) return;
            var r = new Reader(buf, len, Protocol.HeaderSize);
            float now = Time.realtimeSinceStartup;
            float sentAt = 0f;
            if (type == Msg.PropsLive)
            {
                sentAt = r.F32();
                sentAt += Net.Offset(Net.Sender, sentAt);
            }
            int n = r.U16();
            var items = new List<(string key, Vector3 pos, Quaternion rot)>();
            for (int i = 0; i < n; i++)
            {
                string key = r.Str();
                var pos = new Vector3(r.F32(), r.F32(), r.F32());
                var rot = new Quaternion { x = r.F32(), y = r.F32(), z = r.F32(), w = r.F32() };
                items.Add((key, pos, rot));
            }
            Feature.Try(() =>
            {
                if (_props.Count == 0) Scan(now);
                foreach (var (key, pos, rot) in items)
                {
                    if (!_props.TryGetValue(key, out var p) || !World.Alive(p.T)) continue;
                    if (_playerChain.Contains(R.Ptr(p.T.gameObject)) || Excluded(p.T)) { _props.Remove(key); continue; }
                    if (type == Msg.PropsRest)
                    {
                        if (p.Moving) continue;
                        if (p.Puppet) EndPuppet(p);
                        p.T.position = pos; p.T.rotation = rot;
                        p.LastPos = pos; p.LastRot = rot; p.LastSentPos = pos; p.LastSentRot = rot;
                        p.PuppetEnded = now;
                        continue;
                    }
                    if (p.Moving) continue;
                    if (!p.Puppet)
                    {
                        p.Puppet = true;
                        try { p.OrigKinematic = Convert.ToBoolean(R.Get(p.Body, "isKinematic")); R.Set(p.Body, "isKinematic", true); } catch { }
                        p.Samples.Clear();
                    }
                    int at = p.Samples.Count;
                    while (at > 0 && p.Samples[at - 1].t > sentAt) at--;
                    p.Samples.Insert(at, (sentAt, pos, rot));
                    if (p.Samples.Count > 16) p.Samples.RemoveAt(0);
                    p.LastUpdate = now;
                }
            }, "apply props");
        }

        private static void TickPuppets(float now)
        {
            float renderT = now - Delay;
            foreach (var p in _props.Values)
            {
                if (!p.Puppet || p.Samples.Count == 0) continue;
                if (!World.Alive(p.T)) { p.Puppet = false; continue; }
                var s = p.Samples;
                Vector3 pos = s[s.Count - 1].p; Quaternion rot = s[s.Count - 1].r;
                if (renderT <= s[0].t) { pos = s[0].p; rot = s[0].r; }
                else
                    for (int i = 1; i < s.Count; i++)
                        if (renderT <= s[i].t)
                        {
                            float span = s[i].t - s[i - 1].t;
                            float f = span > 0.0001f ? (renderT - s[i - 1].t) / span : 1f;
                            pos = Vector3.Lerp(s[i - 1].p, s[i].p, f);
                            rot = Quaternion.Lerp(s[i - 1].r, s[i].r, f);
                            break;
                        }
                p.T.position = pos; p.T.rotation = rot;
                p.LastPos = pos; p.LastRot = rot;
                if (now - p.LastUpdate > 1.5f) EndPuppet(p);
            }
        }

        private static void EndPuppet(Prop p)
        {
            p.Puppet = false; p.Samples.Clear();
            p.PuppetEnded = Time.realtimeSinceStartup;
            try { if (World.Alive(p.T)) R.Set(p.Body, "isKinematic", p.OrigKinematic); } catch { }
        }
    }
}
