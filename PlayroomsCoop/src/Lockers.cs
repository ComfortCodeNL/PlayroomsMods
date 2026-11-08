using System;
using System.Collections.Generic;

namespace PlayroomsCoop
{
    internal static partial class World
    {
        private const string LockerT = "TwoPixels.School.LockerDoor";
        private const string LockerPrefix = "L|";
        private static readonly Dictionary<string, object> _lockers = new Dictionary<string, object>();

        private static void ScanLockers()
        {
            _lockers.Clear();
            var t = R.T(LockerT);
            if (t == null) return;
            foreach (var l in R.FindAll(t)) _lockers[LockerPrefix + DoorKey(l)] = l;
        }

        private static void PollLockers(float now)
        {
            foreach (var kv in _lockers)
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

        // SetOpen skips the Opened event, so only the opener's locker launches its balls
        private static void ApplyLocker(string key, bool open, float now)
        {
            if (!_lockers.TryGetValue(key, out var locker) || !Alive(locker))
            {
                ScanLockers();
                if (!_lockers.TryGetValue(key, out locker)) return;
            }
            if (Convert.ToBoolean(R.Get(locker, "IsOpen")) == open) return;
            _doorIgnoreUntil[key] = now + 1.5f;
            _doorLast[key] = open;
            R.Call(locker, "SetOpen", open);
        }
    }
}
