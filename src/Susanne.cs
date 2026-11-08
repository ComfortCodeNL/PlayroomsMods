using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlayroomsCoop
{
    internal static partial class World
    {
        public static Feature Susanne;

        private sealed class Sus
        {
            public byte Kind; public string TypeName; public object Ref;
            public ulong Owner;
            public bool Frozen, OrigRootMotion;
            public float IdleSince = -1f, LastState, NextSend;
            public bool HasPose; public Vector3 Pos; public Quaternion Rot;
            public bool Visible, ShownVisible;
            public int Phase, AnimHash, Steps = -1, Warn = -1, Rush = -1, Scream = -1;
            public float Norm, Speed = 1f, RushSpeed; public bool Done;
        }

        private static readonly Sus[] _sus =
        {
            new Sus { Kind = 0, TypeName = "TwoPixels.Enemy.SusanneHallMonitor" },
            new Sus { Kind = 1, TypeName = "TwoPixels.Enemy.SusanneOffice" },
        };
        private static float _nextSusFind;

        private static void SusanneInit() => Susanne = new Feature("Susanne", Plugin.SyncSusanne.Value);

        private static bool SusIdle(Sus s, int phase) => s.Kind == 0 ? phase == 0 : (phase == 0 || phase == 1 || phase == 4);

        private static int SusPhase(Sus s) => Convert.ToInt32(R.Get(s.Ref, "_phase"));

        private static void SusanneReset()
        {
            foreach (var s in _sus)
            {
                if (s.Frozen && Alive(s.Ref)) Susanne.Try(() => Unfreeze(s, s.Phase, s.Visible, s.Done), "release Susanne");
                s.Owner = 0; s.Frozen = false; s.HasPose = false; s.IdleSince = -1f;
                s.Steps = s.Warn = s.Rush = s.Scream = -1;
            }
        }

        private static void SusanneTick(float now, float dt)
        {
            if (now >= _nextSusFind)
            {
                _nextSusFind = now + 3f;
                foreach (var s in _sus)
                {
                    if (Alive(s.Ref)) continue;
                    s.Ref = null; s.Owner = 0; s.Frozen = false;
                    var t = R.T(s.TypeName);
                    if (t != null) foreach (var o in R.FindAll(t)) { s.Ref = o; break; }
                }
            }
            foreach (var s in _sus)
            {
                if (!Alive(s.Ref)) continue;
                if (s.Owner == 0)
                {
                    if (!SusIdle(s, SusPhase(s)))
                    {
                        s.Owner = Net.Me; s.IdleSince = -1f;
                        var w = Protocol.W.Begin(Msg.SusanneClaim); w.U8(s.Kind);
                        w.SendAll(SteamNet.SendReliable);
                        Plugin.Log.LogInfo($"[Susanne] {Name(s)} is after you");
                    }
                }
                else if (s.Owner == Net.Me) OwnerTick(s, now);
                else
                {
                    if (now - s.LastState > 10f || Net.Get(s.Owner) == null) { Unfreeze(s, s.Phase, false, s.Done); s.Owner = 0; continue; }
                    MirrorTick(s, dt);
                }
            }
        }

        private static string Name(Sus s) => s.Kind == 0 ? "the hall monitor" : "the office Susanne";

        private static void OwnerTick(Sus s, float now)
        {
            int phase = SusPhase(s);
            if (SusIdle(s, phase))
            {
                if (s.IdleSince < 0f) s.IdleSince = now;
                else if (now - s.IdleSince > 1f)
                {
                    s.Owner = 0;
                    var w = Protocol.W.Begin(Msg.SusanneRelease);
                    w.U8(s.Kind); w.U8((byte)phase); w.U8(SusVisible(s) ? (byte)1 : (byte)0); w.U8(SusDone(s) ? (byte)1 : (byte)0);
                    w.SendAll(SteamNet.SendReliable);
                    Plugin.Log.LogInfo($"[Susanne] {Name(s)} is back in hiding");
                    return;
                }
            }
            else s.IdleSince = -1f;
            if (now < s.NextSend) return;
            s.NextSend = now + 0.05f;

            var tr = ((Component)s.Ref).transform;
            var pos = tr.position; var rot = tr.rotation;
            int hash = 0; float norm = 0f, speed = 1f, rush = 0f;
            var anim = R.Get(s.Ref, "animator");
            if (anim != null)
            {
                try
                {
                    var info = R.Call(anim, "GetCurrentAnimatorStateInfo", 0);
                    hash = R.Get<int>(info, "fullPathHash"); norm = R.Get<float>(info, "normalizedTime");
                    speed = R.Get<float>(anim, "speed");
                    if (s.Kind == 0) rush = Convert.ToSingle(R.Call(anim, "GetFloat", RushHash(s)));
                }
                catch { }
            }
            var st = Protocol.W.Begin(Msg.SusanneState);
            st.U8(s.Kind); st.U8((byte)phase); st.U8(SusVisible(s) ? (byte)1 : (byte)0); st.U8(SusDone(s) ? (byte)1 : (byte)0);
            st.F32(pos.x); st.F32(pos.y); st.F32(pos.z); st.F32(rot.x); st.F32(rot.y); st.F32(rot.z); st.F32(rot.w);
            st.I32(hash); st.F32(norm); st.F32(speed); st.F32(rush);
            if (s.Kind == 0)
            {
                st.I32(R.Get<int>(s.Ref, "_steps")); st.I32(R.Get<int>(s.Ref, "_lastWarn"));
                st.I32(R.Get<int>(s.Ref, "_lastRush")); st.I32(R.Get<int>(s.Ref, "_lastScream"));
            }
            st.SendAll(SteamNet.SendUnreliable);
        }

        private static int RushHash(Sus s)
        {
            try { return Convert.ToInt32(R.GetStatic(s.Ref.GetType(), "RushSpeedHash")); } catch { return 0; }
        }

        private static bool SusVisible(Sus s)
        {
            try
            {
                foreach (var r in R.Items(R.Get(s.Ref, "_renderers")))
                    if (r != null) return R.Get<bool>(r, "enabled");
            }
            catch { }
            return s.Kind == 0 && R.Get<bool>(s.Ref, "IsOut");
        }

        private static bool SusDone(Sus s) => s.Kind == 1 && R.Has(s.Ref, "_doneToday") && R.Get<bool>(s.Ref, "_doneToday");

        private static void OnSusanneMessage(Msg type, byte[] buf, int len)
        {
            var r = new Reader(buf, len, Protocol.HeaderSize);
            int kind = r.U8();
            if (kind >= _sus.Length) return;
            var s = _sus[kind];
            if (!Alive(s.Ref)) return;
            float now = Time.realtimeSinceStartup;
            ulong from = Net.Sender;
            switch (type)
            {
                case Msg.SusanneClaim:
                    if (s.Owner != 0 && s.Owner != from && Net.SlotOf(s.Owner) < Net.SlotOf(from))
                    {
                        if (s.Owner == Net.Me) { var w = Protocol.W.Begin(Msg.SusanneClaim); w.U8(s.Kind); w.SendAll(SteamNet.SendReliable); }
                        return;
                    }
                    if (s.Owner != from)
                    {
                        Freeze(s);
                        s.Owner = from; s.LastState = now; s.HasPose = false;
                        s.Steps = s.Warn = s.Rush = s.Scream = -1;
                        Plugin.Log.LogInfo($"[Susanne] {Name(s)} is after {Net.NameOf(from)}");
                    }
                    break;
                case Msg.SusanneRelease:
                {
                    int phase = r.U8(); bool vis = r.U8() == 1, done = r.U8() == 1;
                    if (s.Owner != from) return;
                    Unfreeze(s, phase, vis, done);
                    s.Owner = 0;
                    break;
                }
                case Msg.SusanneState:
                {
                    if (s.Owner != from) return;
                    s.Phase = r.U8(); s.Visible = r.U8() == 1; s.Done = r.U8() == 1;
                    s.Pos = new Vector3(r.F32(), r.F32(), r.F32());
                    var q = default(Quaternion); q.x = r.F32(); q.y = r.F32(); q.z = r.F32(); q.w = r.F32(); s.Rot = q;
                    s.AnimHash = r.I32(); s.Norm = r.F32(); s.Speed = r.F32(); s.RushSpeed = r.F32();
                    if (s.Kind == 0 && r.Remaining >= 16)
                    {
                        int steps = r.I32(), warn = r.I32(), rush = r.I32(), scream = r.I32();
                        MirrorSounds(s, steps, warn, rush, scream);
                    }
                    if (!s.HasPose) { var tr = ((Component)s.Ref).transform; tr.position = s.Pos; tr.rotation = s.Rot; }
                    s.HasPose = true; s.LastState = now;
                    break;
                }
            }
        }

        private static void Freeze(Sus s)
        {
            if (s.Frozen) return;
            s.Frozen = true;
            ((Behaviour)s.Ref).enabled = false;
            var anim = R.Get(s.Ref, "animator");
            if (anim != null)
            {
                try { s.OrigRootMotion = R.Get<bool>(anim, "applyRootMotion"); R.Set(anim, "applyRootMotion", false); } catch { }
            }
            s.ShownVisible = s.Visible = SusVisible(s);
        }

        private static void Unfreeze(Sus s, int phase, bool visible, bool done)
        {
            if (!s.Frozen) return;
            s.Frozen = false;
            try { R.Set(s.Ref, "_phase", phase); } catch { }
            if (s.Kind == 1 && done) { try { R.Set(s.Ref, "_doneToday", true); } catch { } }
            try { R.Call(s.Ref, "SetVisible", visible); } catch { }
            var anim = R.Get(s.Ref, "animator");
            if (anim != null) { try { R.Set(anim, "applyRootMotion", s.OrigRootMotion); } catch { } }
            ((Behaviour)s.Ref).enabled = true;
        }

        private static void MirrorTick(Sus s, float dt)
        {
            if (!s.HasPose) return;
            var tr = ((Component)s.Ref).transform;
            float k = 1f - Mathf.Exp(-dt * 15f);
            tr.position = Vector3.Lerp(tr.position, s.Pos, k);
            tr.rotation = Quaternion.Lerp(tr.rotation, s.Rot, k);
            if (s.Visible != s.ShownVisible)
            {
                s.ShownVisible = s.Visible;
                try { R.Call(s.Ref, "SetVisible", s.Visible); } catch { }
            }
            var anim = R.Get(s.Ref, "animator");
            if (anim == null || s.AnimHash == 0) return;
            try
            {
                var info = R.Call(anim, "GetCurrentAnimatorStateInfo", 0);
                int cur = R.Get<int>(info, "fullPathHash");
                float norm = R.Get<float>(info, "normalizedTime");
                if (cur != s.AnimHash || Math.Abs(norm - s.Norm) > 0.25f)
                    R.Call(anim, "Play", s.AnimHash, 0, s.Norm);
                R.Set(anim, "speed", s.Speed);
                if (s.Kind == 0) { int h = RushHash(s); if (h != 0) R.Call(anim, "SetFloat", h, s.RushSpeed); }
            }
            catch { }
        }

        private static void MirrorSounds(Sus s, int steps, int warn, int rush, int scream)
        {
            if (s.Steps >= 0 && steps > s.Steps) { try { R.Call(s.Ref, "PlayStep"); } catch { } }
            if (s.Warn >= 0 && warn != s.Warn) PlayClip(s, "warnClips", warn);
            if (s.Rush >= 0 && rush != s.Rush) PlayClip(s, "rushClips", rush);
            if (s.Scream >= 0 && scream != s.Scream) PlayClip(s, "screamClips", scream);
            s.Steps = steps; s.Warn = warn; s.Rush = rush; s.Scream = scream;
        }

        private static void PlayClip(Sus s, string list, int idx)
        {
            try
            {
                var clips = R.Get(s.Ref, list);
                if (clips == null || idx < 0 || idx >= R.Count(clips)) return;
                var src = R.Get(s.Ref, "voiceSource");
                if (src != null) R.Call(src, "PlayOneShot", R.At(clips, idx));
            }
            catch { }
        }
    }
}
