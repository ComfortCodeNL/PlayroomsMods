using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;

namespace PlayroomsCoop
{
    public partial class CoopBehaviour
    {
        private Color MyColor => _role == Role.Host ? P1Color : P2Color;
        private Color PeerColor => _role == Role.Host ? P2Color : P1Color;
        private static Color P1Color => Plugin.ParseColor(Plugin.Player1Color.Value, new Color(0.84f, 0.23f, 0.23f));
        private static Color P2Color => Plugin.ParseColor(Plugin.Player2Color.Value, new Color(0.23f, 0.42f, 0.91f));
        private string MyLabel => _role == Role.Host ? "P1" : "P2";
        private string PeerLabel => _role == Role.Host ? "P2" : "P1";

        private void EnsureAvatars()
        {
            float glow = Mathf.Clamp(Plugin.Glow.Value, 0f, 3f);
            if (_local == null || !_local.Alive) _local = new Avatar(MyLabel, MyColor, true, glow);
            if (_remote == null || !_remote.Alive)
            {
                _remote = new Avatar(PeerLabel, PeerColor, false, glow);
                _remote.SetVisible(false);
            }
        }

        private void DestroyAvatars()
        {
            _local?.Destroy(); _local = null;
            _remote?.Destroy(); _remote = null;
        }

        [HideFromIl2Cpp]
        private void UpdatePoses(float now, float dt)
        {
            EnsureAvatars();
            _remote.Tick(dt);
            World.RemoteHead = _remote.HasPose ? _remote.HeadPosition : (Vector3?)null;

            bool inWorld = World.InGameWorld();
            if (!inWorld || !_remoteInWorld) _remote.SetVisible(false);
            if (!inWorld)
            {
                _local.SetVisible(false);
                if (now >= _nextSend)
                {
                    _nextSend = now + 0.5f;
                    var hidden = _lastPose; hidden.InWorld = false; if (hidden.Seq == 0) hidden.Seq = ++_seq;
                    Protocol.SendState(_peer, hidden);
                }
                return;
            }
            _local.SetVisible(true);

            var cam = FindPlayerCamera(now);
            if (cam == null)
            {
                if (now >= _nextSend) { _nextSend = now + 0.5f; Protocol.SendState(_peer, _lastPose); }
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
                Protocol.SendState(_peer, _lastPose);
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
