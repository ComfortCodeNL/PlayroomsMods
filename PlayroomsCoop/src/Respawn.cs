using System;
using UnityEngine;

namespace PlayroomsCoop
{
    internal static partial class World
    {
        public static Feature Respawn;

        private static bool _localSafe, _sentSafe, _localDead;
        private static float _respawnAt = -1f, _respawnCooldownUntil, _nextSafeCheck, _followDeathAt = -1f;
        private static object _lockdown;
        private const float JumpscareTime = 2.5f;

        private static void RespawnInit() => Respawn = new Feature("Respawn", Plugin.CoopRespawn.Value);

        private static void RespawnReset()
        {
            foreach (var p in Net.Peers.Values) { p.Dead = false; p.Safe = false; }
            _localSafe = false; _sentSafe = false;
            _respawnAt = -1f; _followDeathAt = -1f; _localDead = false;
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
            bool alive = false, safe = false;
            foreach (var p in Net.Peers.Values) { if (!p.Dead) alive = true; if (!p.Dead && p.Safe) safe = true; }
            if (!alive || !(day || safe))
            {
                Plugin.Log.LogInfo($"[Respawn] caught with nobody safe (anyone alive: {alive}, anyone in the clean room: {safe}): normal death");
                return true;
            }
            try { if (playJumpscare != null) R.Call(playJumpscare, "Invoke", new object[] { null }); }
            catch (Exception e) { Plugin.Log.LogWarning("[Respawn] jumpscare: " + e.Message); }
            _respawnAt = now + JumpscareTime;
            Protocol.W.Begin(Msg.PlayerRespawned).SendAll(SteamNet.SendReliable);
            Plugin.Log.LogInfo("[Respawn] caught, someone else is safe: back to the clean room");
            result = false;
            return false;
        }

        internal static void OnDeathFlowDone(bool started)
        {
            if (!started || !_connected) return;
            _localDead = true; _followDeathAt = -1f;
            Protocol.W.Begin(Msg.PlayerDied).SendAll(SteamNet.SendReliable);
            Plugin.Log.LogInfo("[Respawn] you died");
        }

        private static void RespawnTick(float now)
        {
            if (_followDeathAt >= 0f && now >= _followDeathAt) FollowPartnerDeath();
            if (_respawnAt >= 0f && now >= _respawnAt)
            {
                _respawnAt = -1f;
                _respawnCooldownUntil = now + 8f;
                BringHome();
                CoopBehaviour.Notify("You were caught. Someone else is safe, so you're back in the clean room");
            }
            if (now < _nextSafeCheck) return;
            _nextSafeCheck = now + 0.5f;
            var lk = Lockdown();
            bool safe = false;
            try { safe = lk != null && Convert.ToBoolean(R.Call(lk, "IsPlayerInSafeVolume")); } catch { }
            if (safe == _localSafe && _sentSafe) return;
            _localSafe = safe; _sentSafe = true;
            var w = Protocol.W.Begin(Msg.SafeState); w.U8(safe ? (byte)1 : (byte)0);
            w.SendAll(SteamNet.SendReliable);
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
            var p = Net.Get(Net.Sender);
            if (p == null) return;
            switch (type)
            {
                case Msg.PlayerDied:
                    p.Dead = true;
                    Plugin.Log.LogInfo($"[Respawn] {p.Name} died");
                    if (!_localDead && _respawnAt < 0f) { _followDeathAt = Time.realtimeSinceStartup + 1.5f; _followName = p.Name; }
                    break;
                case Msg.PlayerRespawned:
                    CoopBehaviour.Notify($"{p.Name} was caught and is back in the clean room");
                    break;
                case Msg.SafeState:
                    p.Safe = new Reader(buf, len, Protocol.HeaderSize).U8() == 1;
                    break;
            }
        }

        private static void RespawnNewDay() { foreach (var p in Net.Peers.Values) p.Dead = false; }

        internal static void OnPeerJoined() { _sentSafe = false; _capsSent = false; }

        private static string _followName;

        private static void FollowPartnerDeath()
        {
            _followDeathAt = -1f;
            if (_localDead) return;
            var es = EnemyService();
            if (es == null) return;
            try { if (Convert.ToBoolean(R.Get(es, "_deathFlowActive"))) return; } catch { }
            _localDead = true;
            Plugin.Log.LogInfo("[Respawn] someone was caught with nobody safe: ending the run here too");
            CoopBehaviour.Notify($"{SteamNet.Plain(_followName ?? "A player")} was caught and nobody was safe. The run is over");
            try { R.Set(es, "_deathFlowActive", true); } catch { }
            try { R.Call(es, "ShowGameOver"); }
            catch (Exception e) { Plugin.Log.LogWarning("[Respawn] game over: " + e.Message); }
        }
    }
}
