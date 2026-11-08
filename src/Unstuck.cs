using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlayroomsCoop
{
    internal static partial class World
    {
        public static Feature Unstuck;

        private const float UnstuckCooldown = 10f;
        private static float _unstuckReadyAt;
        private static readonly float[] RingRadii = { 1.0f, 1.6f, 2.3f };

        private const int ResFreed = 0, ResCooldown = 2, ResHunted = 3, ResUnavailable = 4;

        private static void UnstuckInit() => Unstuck = new Feature("Unstuck", Plugin.BetterUnstuck.Value);

        private static void UnstuckHooks()
        {
            R.Patch(R.T("TwoPixels.Popups.PauseMenuPopup.PauseMenuPopup"), "OnClickUnstuckButton", 0, typeof(Hooks), nameof(Hooks.UnstuckPrefix), null);
            R.Patch(R.T("TwoPixels.Player.PlayerUnstuck"), "get_CooldownRemaining", 0, typeof(Hooks), null, nameof(Hooks.UnstuckCooldownPostfix));
        }

        private static float UnstuckRemaining() => Math.Max(0f, _unstuckReadyAt - Time.realtimeSinceStartup);

        internal static void OnUnstuckCooldown(ref float result)
        {
            if (Unstuck.Enabled) result = Math.Max(result, UnstuckRemaining());
        }

        internal static bool OnUnstuckButton(object popup)
        {
            if (!Unstuck.Enabled) return true;
            object unstuck = null;
            try { unstuck = R.Get(R.GetStatic(R.T("TwoPixels.Player.PersistentPlayer"), "Instance"), "Unstuck"); } catch { }
            if (unstuck == null) return true;
            int res = ResUnavailable;
            if (!Unstuck.Try(() => res = DoUnstuck(unstuck), "unstuck")) return true;
            try { R.Call(popup, "Close"); } catch { }
            try
            {
                var show = unstuck.GetType().GetMethod("ShowResult");
                show.Invoke(unstuck, new[] { Enum.ToObject(show.GetParameters()[0].ParameterType, res) });
            }
            catch (Exception e) { Plugin.Log.LogWarning("[Unstuck] result popup: " + e.Message); }
            return false;
        }

        private static int DoUnstuck(object unstuck)
        {
            if (!Convert.ToBoolean(R.Call(unstuck, "Resolve"))) return ResUnavailable;
            if (Convert.ToBoolean(R.Call(unstuck, "IsUnavailable"))) return ResUnavailable;
            if (UnstuckRemaining() > 0f) return ResCooldown;
            if (Convert.ToBoolean(R.Call(unstuck, "IsHunted"))) return ResHunted;

            var body = R.Get(unstuck, "_body");
            if (body == null) return ResUnavailable;
            Vector3 feet = R.Get<Vector3>(body, "position");

            if (TryPlayerSpot(unstuck, feet, out Vector3 spot, out string who))
            {
                if (!Convert.ToBoolean(R.CallStatic(R.T("TwoPixels.Helper.PlayerTeleport"), "Offset", spot - feet))) return ResUnavailable;
                try { R.Set(body, "linearVelocity", Vector3.zero); } catch { }
                _unstuckReadyAt = Time.realtimeSinceStartup + UnstuckCooldown;
                Plugin.Log.LogInfo($"[Unstuck] moved next to {who} ({Dist(feet, spot):0.0} m)");
                CoopBehaviour.Notify($"Unstuck: moved next to {who}");
                return ResFreed;
            }

            foreach (var c in R.FindAll(R.T("TwoPixels.Player.PlayerBehaviourController")))
            {
                R.Call(c, "ResetToStartPosition");
                _unstuckReadyAt = Time.realtimeSinceStartup + UnstuckCooldown;
                Plugin.Log.LogInfo("[Unstuck] no other player to go to: back to the clean room");
                CoopBehaviour.Notify("Unstuck: back to the clean room");
                return ResFreed;
            }
            return ResUnavailable;
        }

        private static bool TryPlayerSpot(object unstuck, Vector3 myFeet, out Vector3 spot, out string who)
        {
            spot = default; who = null;
            if (!_connected) return false;
            var others = new List<Net.Peer>();
            foreach (var p in Net.Peers.Values) if (p.InWorld && !p.Dead && p.Head.HasValue) others.Add(p);
            others.Sort((a, b) => Dist(a.Head.Value, myFeet).CompareTo(Dist(b.Head.Value, myFeet)));
            foreach (var p in others)
            {
                if (!TrySpotBeside(unstuck, p.Head.Value, out spot)) continue;
                who = p.Name;
                return true;
            }
            return false;
        }

        private static bool TrySpotBeside(object unstuck, Vector3 head, out Vector3 spot)
        {
            spot = default;

            float eye = 1.6f;
            var me = PlayerBody();
            if (me != null && LocalHead.HasValue) eye = Mathf.Clamp(LocalHead.Value.y - me.position.y, 1.0f, 2.2f);
            if (!Ground(head + Vector3.up * 0.1f, 3.5f, out Vector3 partnerGround)) return false;
            float footLift = me != null && Ground(me.position + Vector3.up * 0.5f, 2f, out Vector3 myGround) ? Mathf.Clamp(me.position.y - myGround.y, 0f, 1f) : 0f;
            Vector3 partnerFeet = partnerGround + Vector3.up * footLift;
            if (Math.Abs(head.y - eye - partnerFeet.y) > 1.0f) return false;

            if (StrayBind())
            {
                var nav = NavPoint(partnerGround);
                if (!nav.HasValue || HDist(nav.Value, partnerGround) > 1.0f || Math.Abs(nav.Value.y - partnerGround.y) > 1.2f) return false;
            }

            int dirs = 8;
            foreach (float r in RingRadii)
            {
                for (int i = 0; i < dirs; i++)
                {
                    double a = i * Math.PI * 2.0 / dirs;
                    Vector3 off = new Vector3((float)Math.Cos(a) * r, 0f, (float)Math.Sin(a) * r);
                    Vector3 probe = new Vector3(partnerFeet.x + off.x, head.y, partnerFeet.z + off.z);
                    if (Blocked(head, probe)) continue;
                    if (!Ground(probe, eye + 1.5f, out Vector3 g)) continue;
                    if (Math.Abs(g.y - partnerGround.y) > 0.6f) continue;
                    Vector3 cand = g + Vector3.up * footLift;
                    if (!Convert.ToBoolean(R.Call(unstuck, "IsCapsuleFree", cand))) continue;
                    spot = cand;
                    return true;
                }
            }
            return false;
        }

        private static bool Ground(Vector3 from, float depth, out Vector3 point)
        {
            point = default;
            if (!Physics.Raycast(from, Vector3.down, out RaycastHit hit, depth, UnstuckMask(), QueryTriggerInteraction.Ignore)) return false;
            point = new Vector3(from.x, from.y - hit.distance, from.z);
            return true;
        }

        private static bool Blocked(Vector3 a, Vector3 b)
        {
            float len = Dist(a, b);
            if (len < 0.01f) return false;
            var dir = new Vector3((b.x - a.x) / len, (b.y - a.y) / len, (b.z - a.z) / len);
            return Physics.Raycast(a, dir, out RaycastHit _, len, UnstuckMask(), QueryTriggerInteraction.Ignore);
        }

        private static int UnstuckMask() => _strayReady ? _sightMask : ~(1 << 2) & NonWallLayersOff();
    }
}
