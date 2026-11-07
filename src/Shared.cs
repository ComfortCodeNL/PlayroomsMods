using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlayroomsCoop
{
    internal static class Shared
    {
        private enum Kind : byte { Robot = 1, Vacuum = 2, Station = 3 }

        private sealed class Obj
        {
            public Kind Kind; public int Index; public object Ref;
            public bool Frozen; public bool OrigKinematic;
            public bool Mine;
            public float LastCarried;
            public bool HasPose;
            public readonly List<(float t, Vector3 p, Quaternion r)> Samples = new List<(float, Vector3, Quaternion)>();
            public bool CollidersOff;
            public Vector3 SentPos; public Vector3 SentEuler; public int SentStored = int.MinValue; public bool SentFlag, SentCarried; public float SentAt = -99f;
            public int MirroredStored = int.MinValue;
            public bool MirroredFlag; public bool HasFlag;
            public float LastState;
            public float ClaimSentAt = -99f;
        }

        private const string RobotT = "TwoPixels.Ball.RobotVacuum";
        private const string VacuumT = "TwoPixels.Ball.VacuumDevice";
        private const string StationT = "TwoPixels.Ball.RobotStation";

        public static Feature Feature;
        private static bool _connected, _isHost;
        private static ulong _peer;
        private static readonly Dictionary<(Kind, int), Obj> _objs = new Dictionary<(Kind, int), Obj>();
        private static readonly HashSet<(Kind, int)> _dupWarned = new HashSet<(Kind, int)>();
        private static readonly HashSet<(Kind, int)> _missingWarned = new HashSet<(Kind, int)>();
        private static string _lastStationList;
        private static float _lastScan = -99f, _nextSend, _nextCarrySend, _clockOffset;
        private static bool _hasOffset;
        private const float Delay = 0.1f;

        public static void Init() => Feature = new Feature("Robots/Vacuums", Plugin.SyncObjects.Value);
        public static void NewWorld() => Feature.Rearm(Plugin.SyncObjects.Value);

        public static void OnConnected(ulong peer, bool isHost)
        {
            _connected = true; _peer = peer; _isHost = isHost; _hasOffset = false;
            _objs.Clear(); _lastScan = -99f;
        }

        public static void OnDisconnected()
        {
            _connected = false; _peer = 0;
            foreach (var o in _objs.Values) Feature.Try(() => Unfreeze(o), "unfreeze");
            _objs.Clear();
        }

        private static void Scan(float now)
        {
            _lastScan = now;
            foreach (var kv in new List<KeyValuePair<(Kind, int), Obj>>(_objs))
                if (!World.Alive(kv.Value.Ref)) _objs.Remove(kv.Key);

            var seen = new HashSet<(Kind, int)>();
            void Add(Kind kind, object r, int index)
            {
                var key = (kind, index);
                if (!seen.Add(key))
                {
                    if (_dupWarned.Add(key)) Plugin.Log.LogWarning($"[Objects] two {kind}s share index {index}; only the first is synced");
                    return;
                }
                if (_objs.TryGetValue(key, out var o)) { o.Ref = r; return; }
                _objs[key] = new Obj { Kind = kind, Index = index, Ref = r, Mine = _isHost };
            }
            foreach (var r in R.FindAll(R.T(RobotT))) Add(Kind.Robot, r, Convert.ToInt32(R.Get(r, "robotIndex")));
            foreach (var v in R.FindAll(R.T(VacuumT))) Add(Kind.Vacuum, v, Convert.ToInt32(R.Get(v, "DeviceIndex")));
            var st = R.T(StationT);
            if (st != null) foreach (var c in R.FindAll(st)) Add(Kind.Station, c, Convert.ToInt32(R.Get(c, "stationIndex")));
            var ids = new List<string>();
            foreach (var kv in _objs)
                if (kv.Key.Item1 == Kind.Station)
                {
                    var p = ((Component)kv.Value.Ref).transform.position;
                    ids.Add($"{kv.Key.Item2}@({p.x:0.0},{p.y:0.0},{p.z:0.0})");
                }
            string list = string.Join(" ", ids);
            if (list != _lastStationList) { _lastStationList = list; Plugin.Log.LogInfo("[Objects] charging stations here: " + (list.Length > 0 ? list : "none")); }
        }

        private static bool Carried(Obj o) => Convert.ToBoolean(R.Get(o.Ref, "IsCarried"));
        private static int Stored(Obj o) => o.Kind == Kind.Station ? 0 : Convert.ToInt32(R.Get(o.Ref, "StoredBalls"));
        private static void SetStored(Obj o, int v) { if (o.Kind != Kind.Station) R.Set(o.Ref, "StoredBalls", Math.Max(0, Math.Min(v, 100000))); }
        private static bool Flag(Obj o) => o.Kind == Kind.Robot && Convert.ToBoolean(R.Get(o.Ref, "IsPaused"));

        private static void SetFlag(Obj o, bool v, bool owner)
        {
            if (o.Kind == Kind.Robot) R.Set(o.Ref, "IsPaused", v);
        }

        private static object Body(Obj o)
        {
            if (!World.Alive(o.Ref)) return null;
            try
            {
                var rb = R.Get(o.Ref, o.Kind == Kind.Robot ? "body" : o.Kind == Kind.Vacuum ? "deviceRigidbody" : "stationBody");
                return World.Alive(rb) ? rb : null;
            }
            catch { return null; }
        }

        private static object Col(Obj o)
        {
            try { return R.Get(o.Ref, o.Kind == Kind.Robot ? "bodyCollider" : o.Kind == Kind.Vacuum ? "deviceCollider" : "stationCollider"); } catch { return null; }
        }

        private static void SetCollidersOff(Obj o, bool off)
        {
            if (o.CollidersOff == off) return;
            o.CollidersOff = off;
            var c = Col(o);
            if (c != null) R.Set(c, "enabled", !off);
        }

        private static void Freeze(Obj o)
        {
            if (o.Frozen) return;
            o.Frozen = true;
            ((Behaviour)o.Ref).enabled = false;
            var rb = Body(o);
            if (rb != null) { o.OrigKinematic = Convert.ToBoolean(R.Get(rb, "isKinematic")); R.Set(rb, "isKinematic", true); }
        }

        private static void Unfreeze(Obj o)
        {
            if (!o.Frozen || !World.Alive(o.Ref)) { o.Frozen = false; return; }
            o.Frozen = false;
            SetCollidersOff(o, false);
            var rb = Body(o);
            if (rb != null) R.Set(rb, "isKinematic", o.OrigKinematic);
            ((Behaviour)o.Ref).enabled = true;
        }

        public static void Tick(float now, float dt)
        {
            if (!_connected || !Feature.Enabled) return;
            Feature.Try(() =>
            {
                if (now - _lastScan > 3f) Scan(now);
                bool send = now >= _nextSend, carrySend = now >= _nextCarrySend;
                if (send) _nextSend = now + 0.1f;
                if (carrySend) _nextCarrySend = now + 0.033f;

                foreach (var o in _objs.Values)
                {
                    if (!World.Alive(o.Ref)) continue;
                    if (o.Mine) TickOwned(o, now, send, carrySend);
                    else TickMirror(o, now, dt);
                }
            }, "tick");
        }

        private static void TickOwned(Obj o, float now, bool send, bool carrySend)
        {
            Unfreeze(o);
            bool carried = Carried(o);
            if (carried) o.LastCarried = now;

            if (!_isHost && !carried && now - o.LastCarried > 2.5f)
            {
                var t0 = ((Component)o.Ref).transform;
                var w0 = Begin(Msg.ObjRelease, o);
                WritePose(w0, t0); w0.I32(Stored(o)); w0.U8(Flag(o) ? (byte)1 : (byte)0);
                w0.SendTo(_peer, SteamNet.SendReliable);
                Plugin.Log.LogInfo($"[Objects] put down {o.Kind} {o.Index}: handed back to the host");
                o.Mine = false; o.HasPose = false; o.MirroredStored = int.MinValue; o.HasFlag = false; o.Samples.Clear();
                Freeze(o);
                return;
            }

            if (!(carried ? carrySend : send)) return;
            var t = ((Component)o.Ref).transform;
            var pos = t.position; var e = t.eulerAngles;
            int stored = Stored(o); bool flag = Flag(o);
            bool changed = carried != o.SentCarried || stored != o.SentStored || flag != o.SentFlag ||
                           Dist2(pos, o.SentPos) > 0.0001f || Dist2(e, o.SentEuler) > 0.25f;
            if (!changed && now - o.SentAt < 1f) return;
            o.SentPos = pos; o.SentEuler = e; o.SentStored = stored; o.SentFlag = flag; o.SentCarried = carried; o.SentAt = now;
            var w = Begin(Msg.ObjState, o);
            w.F32(now); WritePose(w, t); w.I32(stored); w.U8(flag ? (byte)1 : (byte)0); w.U8(carried ? (byte)1 : (byte)0);
            w.SendTo(_peer, SteamNet.SendUnreliable);
        }

        private static void TickMirror(Obj o, float now, float dt)
        {
            if (Carried(o))
            {
                if (now - o.ClaimSentAt > 1f)
                {
                    o.ClaimSentAt = now;
                    Begin(Msg.ObjClaim, o).SendTo(_peer, SteamNet.SendReliable);
                    if (!o.Mine) Plugin.Log.LogInfo($"[Objects] picked up {o.Kind} {o.Index}: you move it now");
                }
                o.Mine = true; o.LastCarried = now; o.SentStored = int.MinValue;
                Unfreeze(o);
                return;
            }

            Freeze(o);

            if (o.HasFlag)
            {
                bool f = Flag(o);
                if (f != o.MirroredFlag)
                {
                    o.MirroredFlag = f;
                    var w = Begin(Msg.ObjFlag, o); w.U8(f ? (byte)1 : (byte)0); w.SendTo(_peer, SteamNet.SendReliable);
                }
            }
            if (o.MirroredStored != int.MinValue)
            {
                int s = Stored(o);
                if (s != o.MirroredStored)
                {
                    var w = Begin(Msg.ObjDelta, o); w.I32(s - o.MirroredStored); w.SendTo(_peer, SteamNet.SendReliable);
                    o.MirroredStored = s;
                }
            }

            if (!o.HasPose || o.Samples.Count == 0) return;
            var smp = o.Samples;
            float renderT = now - _clockOffset - Delay;
            Vector3 pos = smp[smp.Count - 1].p; Quaternion rot = smp[smp.Count - 1].r;
            if (renderT <= smp[0].t) { pos = smp[0].p; rot = smp[0].r; }
            else
                for (int i = 1; i < smp.Count; i++)
                    if (renderT <= smp[i].t)
                    {
                        float span = smp[i].t - smp[i - 1].t;
                        float f = span > 0.0001f ? (renderT - smp[i - 1].t) / span : 1f;
                        pos = Vector3.Lerp(smp[i - 1].p, smp[i].p, f);
                        rot = Quaternion.Lerp(smp[i - 1].r, smp[i].r, f);
                        break;
                    }
            var t = ((Component)o.Ref).transform;
            t.position = pos; t.rotation = rot;
        }

        private static float Dist2(Vector3 a, Vector3 b) { float x = a.x - b.x, y = a.y - b.y, z = a.z - b.z; return x * x + y * y + z * z; }

        private static bool _replayingVoice;
        private static readonly Dictionary<string, object> _clips = new Dictionary<string, object>();
        private static float _nextClipScan = -99f;

        public static void Hooks()
        {
            R.Patch(R.T(RobotT), "SayClip", 3, typeof(PlayroomsCoop.Hooks), nameof(PlayroomsCoop.Hooks.RobotSayPrefix), nameof(PlayroomsCoop.Hooks.RobotSayPostfix));
        }

        private static Obj RobotOf(object robot)
        {
            foreach (var o in _objs.Values) if (o.Kind == Kind.Robot && R.Same(o.Ref, robot)) return o;
            return null;
        }

        internal static bool OnSayPrefix(object robot, ref bool result)
        {
            if (_replayingVoice || !_connected || !Feature.Enabled) return true;
            var o = RobotOf(robot);
            if (o == null || o.Mine) return true;
            result = false;
            return false;
        }

        internal static void OnSaid(object robot, object clip, bool interrupt, bool said)
        {
            if (!said || _replayingVoice || !_connected || !Feature.Enabled || clip == null) return;
            Feature.Try(() =>
            {
                var o = RobotOf(robot);
                if (o == null || !o.Mine) return;
                string name = ((UnityEngine.Object)clip).name;
                if (string.IsNullOrEmpty(name)) return;
                var w = Begin(Msg.RobotSay, o); w.Str(name); w.U8(interrupt ? (byte)1 : (byte)0);
                w.F32(R.Get<float>(clip, "length"));
                w.SendTo(_peer, SteamNet.SendReliable);
            }, "send voice line");
        }

        private static object FindClip(string name, float length)
        {
            string key = name + "|" + Math.Round(length, 2);
            if (_clips.TryGetValue(key, out var c) && World.Alive(c)) return c;
            float now = Time.realtimeSinceStartup;
            if (now - _nextClipScan < 0f) return null;
            _nextClipScan = now + 5f;
            _clips.Clear();
            foreach (var m in R.T("UnityEngine.Resources").GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                if (m.Name != "FindObjectsOfTypeAll" || !m.IsGenericMethodDefinition || m.GetParameters().Length != 0) continue;
                var all = m.MakeGenericMethod(R.T("UnityEngine.AudioClip")).Invoke(null, null) as System.Collections.IEnumerable;
                if (all != null)
                    foreach (var a in all)
                    {
                        var n = ((UnityEngine.Object)a).name;
                        if (!string.IsNullOrEmpty(n)) _clips[n + "|" + Math.Round(R.Get<float>(a, "length"), 2)] = a;
                    }
                break;
            }
            return _clips.TryGetValue(key, out c) ? c : null;
        }

        private static void PlayLine(Obj o, string name, bool interrupt, float length)
        {
            var clip = FindClip(name, length);
            if (clip == null) { Plugin.Log.LogWarning($"[Objects] robot voice line '{name}' not found here"); return; }
            var mi = o.Ref.GetType().GetMethod("SayClip");
            var ps = mi.GetParameters();
            object sub = ps[2].ParameterType.IsValueType ? Activator.CreateInstance(ps[2].ParameterType) : null;
            _replayingVoice = true;
            try { mi.Invoke(o.Ref, new[] { clip, (object)interrupt, sub }); }
            finally { _replayingVoice = false; }
        }

        private static Writer Begin(Msg type, Obj o)
        {
            var w = Protocol.W.Begin(type); w.U8((byte)o.Kind); w.I32(o.Index); return w;
        }

        private static void WritePose(Writer w, Transform t)
        {
            var p = t.position; var e = t.eulerAngles;
            w.F32(p.x); w.F32(p.y); w.F32(p.z); w.F32(e.x); w.F32(e.y); w.F32(e.z);
        }

        public static void Handle(Msg type, byte[] buf, int len)
        {
            if (!Feature.Enabled) return;
            var r = new Reader(buf, len, Protocol.HeaderSize);
            var key = ((Kind)r.U8(), r.I32());
            if (!_objs.TryGetValue(key, out var o) || !World.Alive(o.Ref))
            {
                Scan(Time.realtimeSinceStartup);
                if (!_objs.TryGetValue(key, out o))
                {
                    if (_missingWarned.Add(key)) Plugin.Log.LogWarning($"[Objects] partner's {key.Item1} {key.Item2} isn't in this world");
                    return;
                }
            }
            float now = Time.realtimeSinceStartup;

            switch (type)
            {
                case Msg.ObjState:
                case Msg.ObjRelease:
                {
                    float sentAt = 0f;
                    if (type == Msg.ObjState)
                    {
                        sentAt = r.F32();
                        float off = now - sentAt;
                        if (!_hasOffset || off < _clockOffset) { _clockOffset = off; _hasOffset = true; }
                        else _clockOffset += (off - _clockOffset) * 0.01f;
                    }
                    var pos = new Vector3(r.F32(), r.F32(), r.F32());
                    var euler = new Vector3(r.F32(), r.F32(), r.F32());
                    int stored = r.I32(); bool flag = r.U8() == 1;
                    bool carried = type == Msg.ObjState && r.Remaining > 0 && r.U8() == 1;
                    if (type == Msg.ObjRelease)
                    {
                        Feature.Try(() =>
                        {
                            o.Mine = true; o.Samples.Clear();
                            Unfreeze(o);
                            if (o.Kind == Kind.Vacuum) R.Call(o.Ref, "TeleportTo", pos, Quaternion.Euler(euler.x, euler.y, euler.z));
                            else if (o.Kind == Kind.Station)
                            {
                                R.Call(o.Ref, "WritePose", pos, Quaternion.Euler(euler.x, euler.y, euler.z));
                                R.Call(o.Ref, "FinishPlacement");
                            }
                            else
                            {
                                var t = ((Component)o.Ref).transform;
                                t.position = pos; t.rotation = Quaternion.Euler(euler.x, euler.y, euler.z);
                            }
                            SetStored(o, stored);
                            if (Flag(o) != flag) SetFlag(o, flag, true);
                        }, "take back");
                        return;
                    }
                    if (o.Mine) return;
                    var rot = Quaternion.Euler(euler.x, euler.y, euler.z);
                    if (o.Samples.Count > 0 && Dist2(pos, o.Samples[o.Samples.Count - 1].p) > 25f) o.Samples.Clear();
                    int at = o.Samples.Count;
                    while (at > 0 && o.Samples[at - 1].t > sentAt) at--;
                    o.Samples.Insert(at, (sentAt, pos, rot));
                    if (o.Samples.Count > 20) o.Samples.RemoveAt(0);
                    o.HasPose = true; o.LastState = now;
                    Feature.Try(() =>
                    {
                        if (o.Frozen)
                        {
                            SetCollidersOff(o, carried);
                            var rb = Body(o);
                            if (rb != null && !Convert.ToBoolean(R.Get(rb, "isKinematic"))) R.Set(rb, "isKinematic", true);
                        }
                        if (Stored(o) != stored) SetStored(o, stored);
                        o.MirroredStored = stored;
                        if (!o.HasFlag || Flag(o) != flag) SetFlag(o, flag, false);
                        o.MirroredFlag = flag; o.HasFlag = true;
                    }, "mirror");
                    break;
                }
                case Msg.RobotSay:
                {
                    string name = r.Str(); bool interrupt = r.U8() == 1; float length = r.F32();
                    if (o.Kind == Kind.Robot && !o.Mine) Feature.Try(() => PlayLine(o, name, interrupt, length), "robot voice line");
                    break;
                }
                case Msg.ObjClaim:
                    Plugin.Log.LogInfo($"[Objects] partner picked up {o.Kind} {o.Index}");
                    o.Mine = false; o.HasPose = false; o.Samples.Clear(); o.SentStored = int.MinValue;
                    Feature.Try(() => Freeze(o), "freeze");
                    break;
                case Msg.ObjFlag:
                {
                    bool flag = r.U8() == 1;
                    if (o.Mine) Feature.Try(() => SetFlag(o, flag, true), "remote toggle");
                    break;
                }
                case Msg.ObjDelta:
                {
                    int delta = r.I32();
                    if (o.Mine) Feature.Try(() => SetStored(o, Stored(o) + delta), "remote balls");
                    break;
                }
            }
        }
    }
}
