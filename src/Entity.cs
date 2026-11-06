using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlayroomsCoop
{
    internal static partial class World
    {
        private static object _enemyService;
        private static bool _standDownSet, _seeHost, _seeP2, _lastNetActive;
        private static float _nextLosCheck, _nextEnemyBuild;

        private static object EnemyService()
        {
            if (Alive(_enemyService)) return _enemyService;
            _enemyService = null;
            foreach (var s in R.FindAll(R.T("TwoPixels.Enemy.EnemyService"))) { _enemyService = s; break; }
            return _enemyService;
        }

        private static object FindEnemy(float now)
        {
            if (Alive(_enemy) && now - _lastEnemySearch < 1f) return _enemy;
            _lastEnemySearch = now;
            object e = null;
            var es = EnemyService();
            if (es != null) { try { e = R.Get(es, "_activeEnemy"); } catch { } }
            if (!Alive(e) && Alive(_enemy)) e = _enemy;
            if (!Alive(e) && now >= _nextEnemyBuild)
            {
                _nextEnemyBuild = now + 10f;
                if (!_isHost && es != null)
                {
                    try { R.Call(es, "PrebuildEnemy"); e = R.Get(es, "_activeEnemy"); } catch { }
                }
                if (!Alive(e)) foreach (var x in R.FindAll(R.T(EnemyControllerT))) { e = x; break; }
            }
            _enemy = Alive(e) ? e : null;
            return _enemy;
        }

        private static bool EnemyOut(object enemy)
        {
            if (enemy == null || !((Component)enemy).gameObject.activeInHierarchy) return false;
            var es = EnemyService();
            if (es == null) return true;
            try { return Convert.ToBoolean(R.Get(es, "IsEnemyOut")); } catch { return true; }
        }

        private static bool Sees(object perception, object target)
        {
            var prev = R.Get(perception, "_player");
            try
            {
                R.Set(perception, "_player", target);
                return Convert.ToBoolean(R.Call(perception, "CanSee"));
            }
            catch { return false; }
            finally { R.Set(perception, "_player", prev); }
        }

        private static void HostEntity(float now)
        {
            if (!Entity.Enabled) return;
            Entity.Try(() =>
            {
                var enemy = FindEnemy(now);
                bool isOut = EnemyOut(enemy);

                if (now >= _nextEnemySend)
                {
                    _nextEnemySend = now + 0.1f;
                    var w = Protocol.W.Begin(Msg.Enemy);
                    if (!isOut) { w.U8(0); w.F32(0); w.F32(0); w.F32(0); w.F32(0); w.F32(0); w.U8(0); }
                    else
                    {
                        var t = ((Component)enemy).transform;
                        var p = t.position;
                        float speed = 0f;
                        var agent = R.Get(enemy, "_agent");
                        if (agent != null) { var v = (Vector3)R.Get(agent, "velocity"); speed = Mathf.Sqrt(v.x * v.x + v.z * v.z); }
                        w.U8(1); w.F32(p.x); w.F32(p.y); w.F32(p.z); w.F32(t.eulerAngles.y); w.F32(speed);
                        w.U8((byte)Convert.ToInt32(R.Get(enemy, "_state")));
                    }
                    w.SendTo(_peer, SteamNet.SendUnreliable);
                }

                if (!isOut) return;
                UpdateTarget(enemy, now);
            }, "host entity");
        }

        private static void UpdateTarget(object enemy, float now)
        {
            var current = R.Get(enemy, "_player");
            if (current != null && (_proxy == null || !R.Same(current, _proxy.transform)))
                _hostPlayerTransform = current;
            if (_hostPlayerTransform == null || RemoteHead == null || LocalHead == null) return;

            if (_proxy == null)
            {
                _proxy = new GameObject("PlayroomsCoop_Player2Target");
                UnityEngine.Object.DontDestroyOnLoad(_proxy);
            }
            var hostT = (Transform)_hostPlayerTransform;
            float offset = LocalHead.Value.y - hostT.position.y;
            var head = RemoteHead.Value;
            _proxy.transform.position = new Vector3(head.x, head.y - offset, head.z);

            var e = ((Component)enemy).transform.position;
            float dHost = Dist(e, hostT.position), dP2 = Dist(e, _proxy.transform.position);
            bool cooldown = now < _caughtCooldownUntil;

            var perception = R.Get(enemy, "perception");
            if (perception != null && now >= _nextLosCheck)
            {
                _nextLosCheck = now + 0.25f;
                _seeHost = Sees(perception, _hostPlayerTransform);
                _seeP2 = Sees(perception, _proxy.transform);
            }

            float sense = Math.Max(0f, Plugin.EntitySenseRadius.Value);
            bool nearHost = dHost <= sense, nearP2 = dP2 <= sense;

            bool wantP2;
            if (cooldown) wantP2 = false;
            else if (nearHost || nearP2)
                wantP2 = nearP2 && (!nearHost || dP2 < dHost);
            else if (_seeP2 && !_seeHost) wantP2 = true;
            else if (_seeHost && !_seeP2) wantP2 = false;
            else if (_seeHost && _seeP2)
                wantP2 = _targetingProxy ? dP2 < dHost + 0.5f : dP2 + 0.5f < dHost;
            else wantP2 = _targetingProxy ? dP2 < dHost + 2f : dP2 + 2f < dHost;

            if (wantP2 && !_targetingProxy) { SetEnemyTarget(enemy, _proxy.transform, true); Protocol.SendSimple(_peer, Msg.EnemyNotice, SteamNet.SendReliable); Plugin.Log.LogInfo($"[Entity] now hunting Player 2 (sees P2: {_seeP2}, sees host: {_seeHost}, P2 {dP2:F1} m, host {dHost:F1} m)"); }
            else if (!wantP2 && _targetingProxy) { SetEnemyTarget(enemy, _hostPlayerTransform, false); Plugin.Log.LogInfo($"[Entity] now hunting the host (sees P2: {_seeP2}, sees host: {_seeHost}, P2 {dP2:F1} m, host {dHost:F1} m)"); }
        }

        private static float HDist(Vector3 a, Vector3 b) { float dx = a.x - b.x, dz = a.z - b.z; return Mathf.Sqrt(dx * dx + dz * dz); }

        private static float Dist(Vector3 a, Vector3 b) { float dx = a.x - b.x, dy = a.y - b.y, dz = a.z - b.z; return Mathf.Sqrt(dx * dx + dy * dy + dz * dz); }

        private static void SetEnemyTarget(object enemy, object target, bool proxy)
        {
            if (target == null) return;
            R.Set(enemy, "_player", target);
            var perception = R.Get(enemy, "perception");
            if (perception != null) R.Set(perception, "_player", target);
            _targetingProxy = proxy;
        }

        private static void RestoreEnemy()
        {
            if (_isHost)
            {
                if (_targetingProxy && Alive(_enemy) && _hostPlayerTransform != null) SetEnemyTarget(_enemy, _hostPlayerTransform, false);
                _targetingProxy = false;
            }
            else UnfreezeLocalEnemy();
        }

        private static void ClientEntity(float now, float dt)
        {
            if (!Entity.Enabled) return;
            Entity.Try(() =>
            {
                bool fresh = now - _enemyNet.time < 3f;
                var enemy = FindEnemy(now);
                if (enemy == null) { _enemyFrozen = false; return; }
                if (!fresh) { UnfreezeLocalEnemy(); return; }

                var es = EnemyService();
                if (es != null && !_standDownSet)
                {
                    try { R.Call(es, "StopPlacementCoroutines"); } catch { }
                    try { R.Set(es, "_standDown", true); } catch { }
                    _standDownSet = true;
                }

                var go = ((Component)enemy).gameObject;
                if (_enemyNet.active != _lastNetActive)
                {
                    _lastNetActive = _enemyNet.active;
                    if (_enemyNet.active) PlayOn("TwoPixels.Enemy.EnemySpawnCue", "Play");
                    Plugin.Log.LogInfo(_enemyNet.active ? "[Entity] host's entity is out: showing it here" : "[Entity] host's entity parked: hiding it here");
                }
                if (!_enemyNet.active)
                {
                    if (go.activeSelf) go.SetActive(false);
                    return;
                }
                if (!go.activeSelf) go.SetActive(true);

                if (!_enemyFrozen || R.Ptr(enemy) != _frozenEnemyPtr)
                {
                    ((Behaviour)enemy).enabled = false;
                    var agent = R.Get(enemy, "_agent");
                    if (agent != null) R.Set(agent, "enabled", false);
                    _enemyFrozen = true; _frozenEnemyPtr = R.Ptr(enemy);
                    _enemyShownPos = _enemyNet.pos; _enemyShownYaw = _enemyNet.yaw;
                }

                var t = ((Component)enemy).transform;
                Vector3 target = _enemyNet.pos;
                if (Dist(_enemyShownPos, target) > 8f) _enemyShownPos = target;
                float k = 1f - Mathf.Exp(-dt * 12f);
                _enemyShownPos = Vector3.Lerp(_enemyShownPos, target, k);
                _enemyShownYaw = Mathf.LerpAngle(_enemyShownYaw, _enemyNet.yaw, k);
                t.position = _enemyShownPos;
                t.rotation = Quaternion.Euler(0f, _enemyShownYaw, 0f);

                var animator = R.Get(enemy, "animator");
                if (animator != null)
                {
                    int hash = Convert.ToInt32(R.GetStatic(enemy.GetType(), "SpeedHash"));
                    R.Call(animator, "SetFloat", hash, _enemyNet.speed);
                }
            }, "client entity");
        }

        private static void UnfreezeLocalEnemy()
        {
            if (_standDownSet)
            {
                try { var es = EnemyService(); if (es != null) R.Set(es, "_standDown", false); } catch { }
                _standDownSet = false;
            }
            if (!_enemyFrozen) return;
            _enemyFrozen = false;
            if (!Alive(_enemy) || R.Ptr(_enemy) != _frozenEnemyPtr) return;
            var agent = R.Get(_enemy, "_agent");
            if (agent != null) R.Set(agent, "enabled", true);
            ((Behaviour)_enemy).enabled = true;
        }

        private static void PlayOn(string typeName, string method)
        {
            try
            {
                foreach (var o in R.FindAll(R.T(typeName))) { R.Call(o, method); break; }
            }
            catch (Exception e)
            {
                if (_stepWarned.Add(typeName + method)) Plugin.Log.LogWarning($"[Entity] couldn't play {typeName}.{method}: {((e as System.Reflection.TargetInvocationException)?.InnerException ?? e).Message}");
            }
        }
    }
}
