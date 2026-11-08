using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;

namespace PlayroomsCoop
{
    public partial class CoopBehaviour
    {
        private static float GlowValue => Mathf.Clamp(Plugin.Glow.Value, 0f, 3f);
        private static Color MyColor => Net.ColorFor(Net.MySlot, Net.MyColor);
        [HideFromIl2Cpp]
        private static Color PeerColor(Net.Peer p) => Net.ColorFor(p.Slot, p.Color);

        private void EnsureAvatars()
        {
            if (_local == null || !_local.Alive) _local = new Avatar(Net.Label(Net.MySlot), MyColor, true, GlowValue);
        }

        [HideFromIl2Cpp]
        private Avatar AvatarFor(Net.Peer p)
        {
            if (_avatars.TryGetValue(p.Id, out var av) && av.Alive) return av;
            av = new Avatar(Net.Label(p.Slot), PeerColor(p), false, GlowValue);
            av.SetVisible(false);
            _avatars[p.Id] = av;
            return av;
        }

        private void RefreshAvatarColors()
        {
            if (_local != null && _local.Alive) { _local.SetColor(MyColor, GlowValue); _local.Label = Net.Label(Net.MySlot); }
            foreach (var kv in _avatars)
            {
                var p = Net.Get(kv.Key);
                if (p == null || !kv.Value.Alive) continue;
                kv.Value.SetColor(PeerColor(p), GlowValue); kv.Value.Label = Net.Label(p.Slot);
            }
        }

        private void DestroyAvatars()
        {
            _local?.Destroy(); _local = null;
            foreach (var av in _avatars.Values) av.Destroy();
            _avatars.Clear();
        }

        [HideFromIl2Cpp]
        private void UpdatePoses(float now, float dt)
        {
            EnsureAvatars();
            bool inWorld = World.InGameWorld();
            foreach (var kv in _avatars)
            {
                var p = Net.Get(kv.Key);
                var av = kv.Value;
                if (p == null || !av.Alive) continue;
                av.Tick(dt);
                p.Head = av.HasPose ? av.HeadPosition : (Vector3?)null;
                if (!inWorld || !p.InWorld) av.SetVisible(false);
            }

            if (!inWorld)
            {
                _local.SetVisible(false);
                if (now >= _nextSend)
                {
                    _nextSend = now + 0.5f;
                    var hidden = _lastPose; hidden.InWorld = false; if (hidden.Seq == 0) hidden.Seq = ++_seq;
                    Protocol.SendState(hidden);
                }
                return;
            }
            _local.SetVisible(true);

            var cam = FindPlayerCamera(now);
            if (cam == null)
            {
                if (now >= _nextSend) { _nextSend = now + 0.5f; Protocol.SendState(_lastPose); }
                return;
            }

            Transform t = cam.transform;
            Vector3 pos = t.position;
            Vector3 fwd = t.forward;
            float yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg;

            bool detached = false;
            var body = World.PlayerBody();
            if (body != null)
            {
                Vector3 bp = body.position;
                float dx = pos.x - bp.x, dz = pos.z - bp.z;
                detached = dx * dx + dz * dz > 0.35f * 0.35f ||
                           (_hasBodyOffset && Math.Abs(pos.y - bp.y - _bodyEyeOffset) > 0.9f);
                if (!detached)
                {
                    _bodyEyeOffset = pos.y - bp.y; _hasBodyOffset = true;
                    _attachedYaw = yaw; _attachedPitch = pitch;
                }
                else
                {
                    pos = new Vector3(bp.x, bp.y + (_hasBodyOffset ? _bodyEyeOffset : 1.6f), bp.z);
                    yaw = _attachedYaw; pitch = _attachedPitch;
                }
            }

            if (!detached)
            {
                float measured = 1.6f;
                if (Physics.Raycast(pos, Vector3.down, out RaycastHit hit, 3f, ~0, QueryTriggerInteraction.Ignore))
                    measured = hit.distance;
                _eyeH = Mathf.Lerp(_eyeH, Mathf.Clamp(measured, 0.45f, 2.4f), 1f - Mathf.Exp(-dt * 10f));
            }

            _local.SetImmediate(pos, yaw, pitch, _eyeH);
            World.LocalHead = pos;

            int rate = Mathf.Clamp(Plugin.SendRate.Value, 10, 60);
            if (now >= _nextSend)
            {
                _nextSend = now + 1f / rate;
                _lastPose = new PoseState { Seq = ++_seq, X = pos.x, Y = pos.y, Z = pos.z, Yaw = yaw, Pitch = pitch, EyeHeight = _eyeH, InWorld = true, Sweeping = World.LocalSweeping(now) };
                Protocol.SendState(_lastPose);
            }
        }

        [HideFromIl2Cpp]
        private Camera FindPlayerCamera(float now)
        {
            if (_cam != null && _cam.isActiveAndEnabled && now - _lastCamSearch < 1f) return _cam;
            _lastCamSearch = now;

            var main = Camera.main;
            if (main != null && main.isActiveAndEnabled) return _cam = main;

            try
            {
                foreach (var bs in R.FindAll(R.T("TwoPixels.Ball.BallService")))
                {
                    if (R.Call(bs, "GetCamera") is Camera c && c != null) return _cam = c;
                }
            }
            catch { /* game not loaded */ }
            return _cam = null;
        }
    }
}
