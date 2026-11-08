using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlayroomsCoop
{
    internal static partial class World
    {
        private static object _director;
        private static IntPtr _tunedDirector;
        private static readonly Dictionary<string, object> _directorBase = new Dictionary<string, object>();
        private static float _nextDirectorCheck;

        private static readonly string[] ScareCounts = { "minScaresPerDay", "maxScaresPerDay" };
        private static readonly string[] ScareWaits = { "restPhase", "retryPause", "dayMinutesPerScare", "cooldownAfterScare" };

        private static float _tunedMult = 1f;

        private static int _scarePlayers = 1;

        private static float ScareMultiplier()
        {
            _scarePlayers = 1;
            bool cozy = false;
            try { cozy = Convert.ToBoolean(R.GetStatic(R.T("TwoPixels.BackroomCleaner.GameMode"), "IsCozy")); } catch { }
            if (cozy) return 1f;
            int players = 1;
            foreach (var p in Net.Peers.Values) if (p.InWorld) players++;
            _scarePlayers = players;
            float step = Mathf.Clamp(Plugin.AmbientScareMultiplier.Value, 1f, 2f) - 1f;
            return Mathf.Clamp(1f + step * (players - 1), 1f, 4f);
        }

        private static readonly Dictionary<string, double> _directorWritten = new Dictionary<string, double>();

        // other mods may write these too: a value we didn't write becomes the new base
        private static void TuneDirector(float now)
        {
            if (now < _nextDirectorCheck) return;
            _nextDirectorCheck = now + 5f;
            float mult = ScareMultiplier();
            if (!Alive(_director))
            {
                _director = null;
                foreach (var d in R.FindAll(R.T("TwoPixels.EasterEgg.HorrorDirector"))) { _director = d; break; }
            }
            if (_director == null) return;
            bool fresh = R.Ptr(_director) != _tunedDirector;
            if (fresh) { _directorBase.Clear(); _directorWritten.Clear(); _tunedMult = 1f; }
            bool changed = fresh || Math.Abs(mult - _tunedMult) > 0.01f;
            foreach (var n in ScareCounts)
            {
                if (!R.Has(_director, n)) continue;
                double cur = R.Get<int>(_director, n);
                if (!_directorWritten.TryGetValue(n, out double w) || Math.Abs(cur - w) > 0.001) { _directorBase[n] = cur / _tunedMult; changed = true; }
            }
            foreach (var n in ScareWaits)
            {
                if (!R.Has(_director, n)) continue;
                double cur = R.Get<float>(_director, n);
                if (!_directorWritten.TryGetValue(n, out double w) || Math.Abs(cur - w) > 0.001) { _directorBase[n] = cur * _tunedMult; changed = true; }
            }
            if (!changed) return;
            _tunedDirector = R.Ptr(_director);
            _tunedMult = mult;
            var parts = new List<string>();
            foreach (var n in ScareCounts)
            {
                if (!_directorBase.TryGetValue(n, out var b0)) continue;
                double bv = Convert.ToDouble(b0);
                int nv = (int)Math.Round(bv * mult); R.Set(_director, n, nv); _directorWritten[n] = nv;
                parts.Add($"{n} {bv:0.#} -> {nv}");
            }
            foreach (var n in ScareWaits)
            {
                if (!_directorBase.TryGetValue(n, out var b0)) continue;
                double bv = Convert.ToDouble(b0);
                float nv = (float)(bv / mult); R.Set(_director, n, nv); _directorWritten[n] = nv;
                parts.Add($"{n} {bv:0.#} -> {nv:0.#}");
            }
            Plugin.Log.LogInfo($"[Scares] daytime scares x{mult:F1} ({_scarePlayers} in the world): " + string.Join(", ", parts));
        }

        private static void RestoreDirector()
        {
            if (_tunedDirector == IntPtr.Zero) return;
            if (Alive(_director) && R.Ptr(_director) == _tunedDirector)
                foreach (var kv in _directorBase)
                {
                    double bv = Convert.ToDouble(kv.Value);
                    if (Array.IndexOf(ScareCounts, kv.Key) >= 0) R.Set(_director, kv.Key, (int)Math.Round(bv));
                    else R.Set(_director, kv.Key, (float)bv);
                }
            _tunedDirector = IntPtr.Zero; _tunedMult = 1f;
            _directorBase.Clear(); _directorWritten.Clear();
        }

        private static void ClientCaught()
        {
            var enemy = FindEnemy(Time.realtimeSinceStartup);
            if (enemy == null) return;
            var go = ((Component)enemy).gameObject;
            if (!go.activeSelf) go.SetActive(true);
            Plugin.Log.LogInfo("Caught by the entity (host's)");
            var es = EnemyService();
            if (es != null && Respawn.Enabled) { try { R.Call(es, "OnPlayerCaught", enemy); return; } catch (Exception e) { Plugin.Log.LogWarning("[Respawn] client catch: " + e.Message); } }
            R.Call(enemy, "PlayJumpscare", new object[] { null });
        }

        private const string DoorT = "TwoPixels.Interactables.DoorInteractable";
        private static readonly Dictionary<string, object> _doors = new Dictionary<string, object>();
        private static readonly Dictionary<string, bool> _doorLast = new Dictionary<string, bool>();
        private static readonly Dictionary<string, float> _doorIgnoreUntil = new Dictionary<string, float>();
        private static float _nextDoorPoll, _lastDoorScan = -99f;

        private static string DoorKey(object door)
        {
            var c = (Component)door;
            var p = c.transform.position;
            string lead = "";
            try { lead = R.Get<string>(door, "leadsToRoomId") ?? ""; } catch { }
            return $"{lead}|{c.gameObject.name}|{(int)Math.Round(p.x * 2)}|{(int)Math.Round(p.y * 2)}|{(int)Math.Round(p.z * 2)}";
        }

        private static void ScanDoors(float now)
        {
            _lastDoorScan = now;
            _doors.Clear();
            foreach (var d in R.FindAll(R.T(DoorT))) _doors[DoorKey(d)] = d;
            ScanPortals();
            ScanLockers();
            ScanHotel();
        }

        private static void PollDoors(float now)
        {
            if (now - _lastDoorScan > 5f) ScanDoors(now);
            DoorWaitTick(now);
            KeptPairsTick();
            PollLockers(now);
            PollHotel(now);
            foreach (var kv in _doors)
            {
                if (!Alive(kv.Value)) continue;
                bool open = Convert.ToBoolean(R.Get(kv.Value, "IsOpen"));
                bool known = _doorLast.TryGetValue(kv.Key, out bool last);
                _doorLast[kv.Key] = open;
                if (!known || open == last) continue;
                if (_doorIgnoreUntil.TryGetValue(kv.Key, out float until) && now < until) continue;
                var w = Protocol.W.Begin(Msg.DoorState); w.Str(kv.Key); w.U8(open ? (byte)1 : (byte)0);
                w.SendAll(SteamNet.SendReliable);
            }
        }

        private static void ApplyDoor(string key, bool open)
        {
            float now = Time.realtimeSinceStartup;
            if (key.StartsWith(LockerPrefix, StringComparison.Ordinal)) { ApplyLocker(key, open, now); return; }
            if (key.StartsWith(HotelPrefix, StringComparison.Ordinal)) { ApplyHotel(key, open, now); return; }
            if (!_doors.TryGetValue(key, out var door) || !Alive(door))
            {
                ScanDoors(now);
                if (!_doors.TryGetValue(key, out door)) return;
            }
            _doorCloseWait.Remove(key);
            bool cur = Convert.ToBoolean(R.Get(door, "IsOpen"));
            if (cur == open) return;
            if (!open && PlayerInDoorway(door)) { _doorCloseWait[key] = now; return; }
            _doorIgnoreUntil[key] = now + 1.5f;
            if (_coupledDoor.TryGetValue(key, out var coupled)) _doorIgnoreUntil[coupled] = now + 1.5f;
            if (open) R.Call(door, "OpenForNavigation");
            else R.Call(door, "RequestClose");
        }

        private static bool _hubCascade;
        internal static void OnHubDoorChange(bool start) => _hubCascade = start;

        internal static bool OnDoorRequestClose()
        {
            if (!_hubCascade || !_connected || !Plugin.KeepDoorsOpen.Value) return true;
            return false;
        }

        private static readonly Dictionary<string, float> _doorCloseWait = new Dictionary<string, float>();

        private static bool PlayerInDoorway(object door)
        {
            var body = PlayerBody();
            if (body == null) return false;
            var d = ((Component)door).transform.position; var p = body.position;
            return HDist(d, p) < 1.8f && Math.Abs(d.y - p.y) < 2.5f;
        }

        private static void DoorWaitTick(float now)
        {
            if (_doorCloseWait.Count == 0) return;
            foreach (var key in new List<string>(_doorCloseWait.Keys))
            {
                if (now - _doorCloseWait[key] > 20f || !_doors.TryGetValue(key, out var door) || !Alive(door)) { _doorCloseWait.Remove(key); continue; }
                if (PlayerInDoorway(door)) continue;
                _doorCloseWait.Remove(key);
                _doorIgnoreUntil[key] = now + 1.5f;
                if (Convert.ToBoolean(R.Get(door, "IsOpen"))) R.Call(door, "RequestClose");
            }
        }

        private static int _dayLastPhase = -1, _dayLastDay = -1;
        private static float _nextDayPoll, _dayIgnoreUntil;

        private static void PollDay(float now)
        {
            var dts = FindDayTime(now);
            if (dts == null) return;
            int phase = Convert.ToInt32(R.Get(dts, "_phase"));
            int day = Convert.ToInt32(R.Get(dts, "CurrentDay"));
            if (_dayLastPhase < 0) { _dayLastPhase = phase; _dayLastDay = day; return; }
            if (phase == _dayLastPhase && day == _dayLastDay) return;
            if (day != _dayLastDay) RespawnNewDay();
            _dayLastPhase = phase; _dayLastDay = day;
            if (now < _dayIgnoreUntil) return;
            var w = Protocol.W.Begin(Msg.DayState); w.I32(phase); w.I32(day);
            w.SendAll(SteamNet.SendReliable);
            Plugin.Log.LogInfo($"Day cycle moved on here: day {day}, phase {phase}");
        }

        private static int _pendPhase = -1, _pendDay = -1;
        private static float _pendSince, _pendCloseAt = -1f;
        private static bool _pendClosedEndsNight;
        private const string BedT = "TwoPixels.Interactables.BedInteractable";

        private static object Terminal(out object bed)
        {
            bed = null;
            object best = null, bestBed = null; float bestD = float.MaxValue;
            var me = PlayerBody();
            foreach (var b in R.FindAll(R.T(BedT)))
            {
                object t = null;
                try { t = R.Get(b, "windows95Terminal"); } catch { }
                if (t != null && TerminalBusy(t)) { bed = b; return t; }
                float d = me != null ? Dist(((Component)b).transform.position, me.position) : 0f;
                if (d < bestD) { bestD = d; best = t; bestBed = b; }
            }
            bed = bestBed;
            if (best != null) return best;
            foreach (var t in R.FindAll(R.T("TwoPixels.Windows95.Windows95Terminal"))) return t;
            return null;
        }

        private static float _surfaceCloseAt = -1f;

        private static bool CloseSurfaces(float now)
        {
            bool any = false;
            foreach (var f in R.FindAll(R.T("TwoPixels.Interactables.FocusedSurfaceInteractable")))
            {
                bool open = false, closing = false;
                try { open = Convert.ToBoolean(R.Get(f, "IsOpen")); closing = Convert.ToBoolean(R.Get(f, "IsClosing")); } catch { }
                if (!open && !closing) continue;
                any = true;
                if (_surfaceCloseAt < 0f) { try { R.Call(f, "Close"); } catch { } }
                else if (now - _surfaceCloseAt > 2.5f) { try { R.Call(f, "AbandonQuietly", true); } catch { } }
            }
            if (!any) { _surfaceCloseAt = -1f; return false; }
            if (_surfaceCloseAt < 0f) { _surfaceCloseAt = now; CoopBehaviour.Notify("The day moved on: closing the board"); }
            return true;
        }

        private static bool TerminalBusy(object term)
        {
            if (term == null || !Alive(term)) return false;
            bool open = false, closing = false;
            try { open = Convert.ToBoolean(R.Get(term, "IsOpen")); } catch { }
            try { closing = Convert.ToBoolean(R.Get(term, "_isClosing")); } catch { }
            return open || closing;
        }

        private static ulong _pendFrom;

        private static void ApplyDay(int phase, int day)
        {
            _pendFrom = Net.Sender;
            _pendPhase = phase; _pendDay = day; _pendSince = Time.realtimeSinceStartup;
            _pendCloseAt = -1f; _pendClosedEndsNight = false;
            _dayIgnoreUntil = _pendSince + 8f;
            DayPendingTick(_pendSince);
        }

        private static void DayPendingTick(float now)
        {
            if (_pendDay < 0) return;
            var dts = FindDayTime(now);
            if (dts == null) { if (now - _pendSince > 10f) _pendDay = -1; return; }
            int lp = Convert.ToInt32(R.Get(dts, "_phase"));
            int ld = Convert.ToInt32(R.Get(dts, "CurrentDay"));
            bool endsNight = _pendDay > ld || (_pendDay == ld && lp == 2 && (_pendPhase == 3 || _pendPhase == 0));
            bool lightsOut = _pendDay == ld && lp == 0 && (_pendPhase == 1 || _pendPhase == 2);
            if (!endsNight && !lightsOut) { _pendDay = -1; return; }
            _dayIgnoreUntil = now + 8f;

            if (CloseSurfaces(now)) return;

            var term = Terminal(out var bed);
            if (TerminalBusy(term))
            {
                if (_pendCloseAt < 0f)
                {
                    _pendCloseAt = now;
                    bool night = false;
                    try { night = Convert.ToBoolean(R.Call(term, "IsNight")); } catch { }
                    if (endsNight && night)
                    {
                        _pendClosedEndsNight = true;
                        try { R.Call(term, "CloseAndEndNight"); } catch (Exception e) { Plugin.Log.LogWarning("[Day] close-and-end-night failed: " + e.Message); _pendClosedEndsNight = false; }
                    }
                    else
                    {
                        try { R.Call(term, "Close"); } catch (Exception e) { Plugin.Log.LogWarning("[Day] closing the computer failed: " + e.Message); }
                    }
                    if (endsNight) CoopBehaviour.Notify($"{Net.NameOf(_pendFrom)} clicked Lights out: closing your computer");
                    return;
                }
                if (now - _pendCloseAt < 4f) return;
                Plugin.Log.LogWarning("[Day] the computer didn't close in time; forcing it");
                try { R.Call(term, "FinishClose"); } catch { }
                try { R.Call(term, "ReleaseCamera"); } catch { }
                try { R.Call(term, "RestoreInput"); } catch { }
            }
            if (_pendCloseAt >= 0f && now - _pendCloseAt < 0.6f) return;

            if (endsNight)
            {
                if (!_pendClosedEndsNight)
                {
                    bool done = false;
                    if (bed != null) { try { R.Call(bed, "OnTerminalClosed", true); done = true; } catch { } }
                    if (!done) R.Call(dts, "WakeUp");
                }
                Plugin.Log.LogInfo($"{Net.NameOf(_pendFrom)} clicked Lights out: starting day {_pendDay} here too");
                _bedCheckAt = now + 6f;
            }
            else
            {
                float rem0 = R.Get<float>(dts, "RemainingTime");
                float dur = R.Get<float>(dts, "CurrentPhaseDuration");
                R.Set(dts, "_timer", dur + 0.5f);
                if (R.Get<float>(dts, "RemainingTime") > rem0 + 0.01f) R.Set(dts, "_timer", 0f);
                Plugin.Log.LogInfo($"{Net.NameOf(_pendFrom)}'s shift ended: ending the shift here too");
            }
            _pendDay = -1;
        }

        private static float _bedCheckAt = -1f;

        private static void BedCheckTick(float now)
        {
            if (_bedCheckAt < 0f || now < _bedCheckAt) return;
            var term = Terminal(out var bed);
            object popup = null; bool interactable = true, open = false, transitioning = false;
            try { popup = R.Get(bed, "_openDayPopup"); } catch { }
            try { interactable = Convert.ToBoolean(R.Get(bed, "IsInteractable")); } catch { }
            try { open = Convert.ToBoolean(R.Get(term, "IsOpen")); } catch { }
            try { transitioning = Convert.ToBoolean(R.Get(FindDayTime(now), "IsTransitioning")); } catch { }
            if (popup != null || transitioning) { _bedCheckAt = now + 2f; return; }
            _bedCheckAt = -1f;
            Plugin.Log.LogInfo($"[Day] after wake-up: computer open={open}, bed usable={interactable}");
            if (open)
            {
                try { R.Call(term, "FinishClose"); } catch { }
                try { R.Call(term, "ReleaseCamera"); } catch { }
                try { R.Call(term, "RestoreInput"); } catch { }
            }
            try { if (bed != null) R.Call(bed, "RefreshInteractable"); } catch { }
        }

        private static object _dayTime;
        private static float _lastDaySearch = -99f;

        private static object FindDayTime(float now)
        {
            if (Alive(_dayTime)) return _dayTime;
            if (now - _lastDaySearch < 3f) return null;
            _lastDaySearch = now;
            foreach (var d in R.FindAll(R.T(DayTimeServiceT))) { _dayTime = d; break; }
            return _dayTime;
        }

        private static void HostClock(float now)
        {
            if (!Clock.Enabled || now < _nextClockSend) return;
            _nextClockSend = now + 2f;
            Clock.Try(() =>
            {
                var dts = FindDayTime(now);
                if (dts == null || !DayRunning(dts)) return;
                var w = Protocol.W.Begin(Msg.DayTime);
                w.I32(Convert.ToInt32(R.Get(dts, "_phase"))); w.F32(R.Get<float>(dts, "_timer"));
                w.SendAll(SteamNet.SendUnreliable);
            }, "host clock");
        }

        internal static bool DayRunning(object dts)
        {
            try { return !R.Has(dts, "HasStarted") || Convert.ToBoolean(R.Get(dts, "HasStarted")); }
            catch { return true; }
        }

        private static void ApplyClock(int phase, float timer)
        {
            var dts = FindDayTime(Time.realtimeSinceStartup);
            if (dts == null || !DayRunning(dts)) return;
            if (Convert.ToInt32(R.Get(dts, "_phase")) != phase) return;
            if (Math.Abs(R.Get<float>(dts, "_timer") - timer) > 2f) R.Set(dts, "_timer", timer);
        }
    }
}
