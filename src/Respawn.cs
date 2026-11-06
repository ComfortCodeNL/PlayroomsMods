using System;
using UnityEngine;

namespace PlayroomsCoop
{
    internal static partial class World
    {
        public static Feature Respawn;

        private static bool _partnerDead, _partnerSafe, _localSafe, _sentSafe;
        private static float _respawnAt = -1f, _respawnCooldownUntil, _nextSafeCheck;
        private static object _lockdown;
        private const float JumpscareTime = 2.5f;

        private static void RespawnInit() => Respawn = new Feature("Respawn", Plugin.CoopRespawn.Value);

        private static void RespawnReset()
        {
            _partnerDead = false; _partnerSafe = false; _localSafe = false; _sentSafe = false;
            _respawnAt = -1f;
        }

        private static object Lockdown()
        {
            if (Alive(_lockdown)) return _lockdown;
            _lockdown = null;
            foreach (var l in R.FindAll(R.T("TwoPixels.Interactables.CleanRoomLockdown"))) { _lockdown = l; break; }
            return _lockdown;
        }

        private static bool IsDayPhase()
        {
            try { var dts = FindDayTime(Time.realtimeSinceStartup); return dts == null || Convert.ToInt32(R.Get(dts, "_phase")) == 0; }
            catch { return true; }
        }

        internal static bool OnDeathFlow(object playJumpscare, ref bool result)
        {
            if (!_connected || !Respawn.Enabled) return true;
            float now = Time.realtimeSinceStartup;
            if (_respawnAt >= 0f || now < _respawnCooldownUntil) { result = false; return false; }
            bool day = IsDayPhase();
            if (_partnerDead || !(day || _partnerSafe))
            {
                Plugin.Log.LogInfo($"[Respawn] caught with nobody safe (partner dead: {_partnerDead}, safe: {_partnerSafe}): normal death");
                return true;
            }
            try { if (playJumpscare != null) R.Call(playJumpscare, "Invoke", new object[] { null }); }
            catch (Exception e) { Plugin.Log.LogWarning("[Respawn] jumpscare: " + e.Message); }
            _respawnAt = now + JumpscareTime;
            Protocol.SendSimple(_peer, Msg.PlayerRespawned, SteamNet.SendReliable);
            Plugin.Log.LogInfo("[Respawn] caught, partner is safe: back to the clean room");
            result = false;
            return false;
        }

        internal static void OnDeathFlowDone(bool started)
        {
            if (!started || !_connected) return;
            Protocol.SendSimple(_peer, Msg.PlayerDied, SteamNet.SendReliable);
            Plugin.Log.LogInfo("[Respawn] you died");
        }

        private static void RespawnTick(float now)
        {
            if (_respawnAt >= 0f && now >= _respawnAt)
            {
                _respawnAt = -1f;
                _respawnCooldownUntil = now + 8f;
                BringHome();
                CoopBehaviour.Notify("You were caught. Your partner is safe, so you're back in the clean room");
            }
            if (now < _nextSafeCheck) return;
            _nextSafeCheck = now + 0.5f;
            var lk = Lockdown();
            bool safe = false;
            try { safe = lk != null && Convert.ToBoolean(R.Call(lk, "IsPlayerInSafeVolume")); } catch { }
            if (safe == _localSafe && _sentSafe) return;
            _localSafe = safe; _sentSafe = true;
            var w = Protocol.W.Begin(Msg.SafeState); w.U8(safe ? (byte)1 : (byte)0);
            w.SendTo(_peer, SteamNet.SendReliable);
        }

        private static void BringHome()
        {
            foreach (var f in R.FindAll(R.T("TwoPixels.Interactables.FocusedSurfaceInteractable")))
            {
                try { if (Convert.ToBoolean(R.Get(f, "IsOpen"))) R.Call(f, "AbandonQuietly", true); } catch { }
            }
            var lk = Lockdown();
            if (lk != null)
            {
                try { R.Call(lk, "StartCoroutine", R.Call(lk, "BringHome")); return; }
                catch (Exception e) { Plugin.Log.LogWarning("[Respawn] BringHome: " + e.Message); }
            }
            try
            {
                foreach (var c in R.FindAll(R.T("TwoPixels.Player.PlayerBehaviourController"))) { R.Call(c, "ResetToStartPosition"); break; }
                var es = EnemyService(); if (es != null) R.Call(es, "StandDownForNight");
            }
            catch (Exception e) { Plugin.Log.LogWarning("[Respawn] reset position: " + e.Message); }
        }

        private static void OnRespawnMessage(Msg type, byte[] buf, int len)
        {
            switch (type)
            {
                case Msg.PlayerDied:
                    _partnerDead = true;
                    Plugin.Log.LogInfo("[Respawn] partner died");
                    break;
                case Msg.PlayerRespawned:
                    CoopBehaviour.Notify("Your partner was caught and is back in the clean room");
                    break;
                case Msg.SafeState:
                    _partnerSafe = new Reader(buf, len, Protocol.HeaderSize).U8() == 1;
                    break;
            }
        }

        private static void RespawnNewDay() => _partnerDead = false;
    }
}
