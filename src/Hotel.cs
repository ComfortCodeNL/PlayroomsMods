using System;
using System.Collections.Generic;

namespace PlayroomsCoop
{
    // Hotel cleaning schedule. Checkmarks are kept per room interior (by its room key), so they're
    // mirrored by key over the door messages with an "H|" prefix.
    internal static partial class World
    {
        private const string HotelT = "TwoPixels.Hotel.HotelRooms";
        private const string InteriorT = "TwoPixels.Hotel.HotelRoomInterior";
        private const string HotelPrefix = "H|";
        private static object _hotel;
        private static readonly Dictionary<string, object> _interiors = new Dictionary<string, object>();

        private static void ScanHotel()
        {
            _interiors.Clear();
            _hotel = null;
            var ht = R.T(HotelT);
            if (ht == null) return;
            foreach (var h in R.FindAll(ht)) { _hotel = h; break; }
            if (_hotel == null) return;
            foreach (var i in R.FindAll(R.T(InteriorT)))
            {
                string key = null;
                try { key = R.Get<string>(i, "RoomKey"); } catch { }
                if (!string.IsNullOrEmpty(key)) _interiors[HotelPrefix + key] = i;
            }
        }

        private static void PollHotel(float now)
        {
            if (!Alive(_hotel)) return;
            foreach (var kv in _interiors)
            {
                if (!Alive(kv.Value)) continue;
                bool done = Convert.ToBoolean(R.Call(_hotel, "IsDone", kv.Value));
                bool known = _doorLast.TryGetValue(kv.Key, out bool last);
                _doorLast[kv.Key] = done;
                if (!known || done == last) continue;
                if (_doorIgnoreUntil.TryGetValue(kv.Key, out float until) && now < until) continue;
                var w = Protocol.W.Begin(Msg.DoorState); w.Str(kv.Key); w.U8(done ? (byte)1 : (byte)0);
                w.SendTo(_peer, SteamNet.SendReliable);
            }
        }

        private static void ApplyHotel(string key, bool done, float now)
        {
            if (!_interiors.TryGetValue(key, out var interior) || !Alive(interior) || !Alive(_hotel))
            {
                ScanHotel();
                if (!_interiors.TryGetValue(key, out interior) || _hotel == null) return;
            }
            if (Convert.ToBoolean(R.Call(_hotel, "IsDone", interior)) == done) return;
            _doorIgnoreUntil[key] = now + 1.5f;
            _doorLast[key] = done;
            R.Call(_hotel, "SetDone", interior, done);
        }
    }
}
