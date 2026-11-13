using System;
using System.Collections.Generic;
using UnityEngine;

namespace ComfortCode.Playrooms.ShiftHUD
{
    // Scene-local, non-mutating tracker. Never updates ball spots, game time or saves.
    // Counts are published only after an entire chunked scan completes.
    internal sealed class ShiftState
    {
        private sealed class Room
        {
            internal string Id;
            internal object Instance;
            internal object Spots;
            internal int Total;
            internal int Remaining;
            internal int Cursor;
            internal int Uncollected;
            internal bool Failed;
            internal bool Valid;
        }

        private readonly List<Room> _rooms = new List<Room>();
        private string _currentRoomId;
        private float _nextClock, _nextRooms, _nextSelect;
        private int _scanRoomIndex;
        private bool _scanning, _hasClock, _isInWorld;
        private int _day = -1, _phase = -1;
        private float _remainingSeconds;
        private int _lastLoggedRooms = -1;

        internal bool InWorld => _isInWorld;
        internal bool ShiftEnded => _isInWorld && _hasClock && _phase > 0;
        internal bool ClockAvailable => _hasClock;
        internal int Day => _day;
        internal int Phase => _phase;
        internal int RoomCount => _rooms.Count;
        internal string CurrentRoom => _currentRoomId ?? "Kamer niet herkend";
        internal bool IsCounting => _scanning;
        internal string ShiftTime
        {
            get
            {
                if (!_hasClock) return "Niet beschikbaar";
                if (ShiftEnded) return "00:00";
                var time = TimeSpan.FromSeconds(Math.Min(86400f, Math.Max(0f, _remainingSeconds)));
                return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"mm\:ss");
            }
        }

        private Room Selected => _rooms.Find(r => r.Id == _currentRoomId);
        internal string Balls
        {
            get
            {
                Room room = Selected;
                if (room == null) return "Kamer niet gevonden";
                if (!room.Valid) return _scanning ? "Ballen tellen..." : "Telling onbekend";
                return room.Remaining.ToString("N0") + " / " + room.Total.ToString("N0") + " over";
            }
        }

        internal int CountedRooms
        {
            get
            {
                int n = 0;
                foreach (Room room in _rooms) if (room.Valid && room.Total > 0) n++;
                return n;
            }
        }
        internal int CleanedRooms
        {
            get
            {
                int n = 0;
                foreach (Room room in _rooms)
                    if (room.Valid && room.Total > 0 && room.Remaining == 0) n++;
                return n;
            }
        }

        internal List<string> CleanedRoomNames(int maximum)
        {
            var names = new List<string>();
            foreach (Room room in _rooms)
                if (room.Valid && room.Total > 0 && room.Remaining == 0)
                    names.Add(room.Id);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            if (names.Count > maximum) names.RemoveRange(maximum, names.Count - maximum);
            return names;
        }

        internal void Tick(float now)
        {
            if (now >= _nextClock)
            {
                _nextClock = now + 0.5f;
                UpdateClock();
            }
            if (now >= _nextRooms && !_scanning)
            {
                _nextRooms = now + Mathf.Clamp(Plugin.RefreshSeconds.Value, 1f, 15f);
                RefreshRooms();
            }
            if (_scanning) ScanChunk(750);

            if (now >= _nextSelect)
            {
                _nextSelect = now + 1.25f;
                SelectCurrentRoom();
            }
        }

        private void UpdateClock()
        {
            try
            {
                if (GameAccess.MenuShowing())
                {
                    ResetForMenu();
                    return;
                }
                var dts = GameAccess.First("TwoPixels.DayTime.DayTimeService");
                if (dts == null)
                {
                    _hasClock = false;
                    return;
                }
                if (GameAccess.TryBool(dts, "HasStarted", out var started) && !started)
                {
                    _hasClock = false;
                    return;
                }
                if (!GameAccess.TryInt(dts, "_phase", out int phase) ||
                    !GameAccess.TryFloat(dts, "RemainingTime", out float seconds))
                {
                    _hasClock = false;
                    return;
                }
                GameAccess.TryInt(dts, "CurrentDay", out int day);
                if (_day >= 0 && day != _day)
                {
                    // The next shift starts in a new day. No time is ever changed here.
                    Plugin.Logger?.LogInfo("New day detected: " + day);
                }
                if (_phase == 0 && phase != 0)
                    Plugin.Logger?.LogInfo("Shift ended; displaying END DAY and local worklight.");
                _day = day;
                _phase = phase;
                _remainingSeconds = Math.Max(0f, seconds);
                _hasClock = true;
                _isInWorld = true;
            }
            catch (Exception ex)
            {
                _hasClock = false;
                Plugin.Logger?.LogWarning("Shift clock unavailable: " + ex.Message);
            }
        }

        private void ResetForMenu()
        {
            _hasClock = false;
            _isInWorld = false;
            _phase = -1;
            _day = -1;
            _rooms.Clear();
            _currentRoomId = null;
            _scanning = false;
            _scanRoomIndex = 0;
            _nextRooms = 0;
            _lastLoggedRooms = -1;
        }

        private void RefreshRooms()
        {
            try
            {
                // The game owns these objects. Holding references is safe only while
                // the current scene lives; each refresh rebuilds the list.
                var discovered = GameAccess.Find("TwoPixels.Room.RoomObject");
                _rooms.Clear();
                foreach (var obj in discovered)
                {
                    try
                    {
                        var id = GameAccess.Text(obj, "RoomId");
                        if (string.IsNullOrWhiteSpace(id)) continue;
                        var spots = GameAccess.Read(obj, "_ballSpots");
                        var count = GameAccess.Count(spots);
                        if (count < 0 || count > 1000000) continue;
                        _rooms.Add(new Room {
                            Id = id, Instance = obj, Spots = spots,
                            Total = count, Remaining = count, Cursor = 0
                        });
                    }
                    catch { }
                }
                _rooms.Sort((a, b) =>
                    StringComparer.OrdinalIgnoreCase.Compare(a.Id, b.Id));
                if (_rooms.Count != _lastLoggedRooms)
                {
                    _lastLoggedRooms = _rooms.Count;
                    Plugin.Logger?.LogInfo("Room discovery: " + _rooms.Count +
                        " rooms. The HUD stays visible even if none are found.");
                }
                _scanRoomIndex = 0;
                _scanning = _rooms.Count > 0;
                if (_rooms.Count == 0) _currentRoomId = null;
            }
            catch (Exception ex)
            {
                _rooms.Clear();
                _scanning = false;
                Plugin.Logger?.LogWarning("Room discovery failed: " + ex.Message);
            }
        }

        private void ScanChunk(int budget)
        {
            while (budget > 0 && _scanRoomIndex < _rooms.Count)
            {
                var room = _rooms[_scanRoomIndex];
                if (!GameAccess.Alive(room.Instance))
                {
                    room.Failed = true;
                    room.Cursor = room.Total;
                }
                int end = Math.Min(room.Total, room.Cursor + budget);
                for (; room.Cursor < end; room.Cursor++)
                {
                    budget--;
                    try
                    {
                        object spot = GameAccess.At(room.Spots, room.Cursor);
                        if (!GameAccess.Collected(spot, out bool collected))
                            room.Failed = true;
                        else if (!collected) room.Uncollected++;
                    }
                    catch { room.Failed = true; }
                }
                if (room.Cursor >= room.Total)
                {
                    room.Valid = !room.Failed;
                    if (room.Valid) room.Remaining = room.Uncollected;
                    _scanRoomIndex++;
                }
            }
            if (_scanRoomIndex >= _rooms.Count)
                _scanning = false;
        }

        private void SelectCurrentRoom()
        {
            try
            {
                if (_rooms.Count == 0) { _currentRoomId = null; return; }
                string id = GameAccess.CurrentRoomId();
                if (!string.IsNullOrEmpty(id) && _rooms.Exists(r => r.Id == id))
                {
                    _currentRoomId = id;
                    return;
                }

                Transform player = GameAccess.Player();
                if (player == null)
                {
                    _currentRoomId = null;
                    return;
                }
                Vector3 here = player.position;
                float best = float.MaxValue;
                Room nearest = null;
                foreach (var room in _rooms)
                {
                    float distance = float.MaxValue;
                    try
                    {
                        // A handful of stable samples from each room, to avoid
                        // an expensive search through all balls every frame.
                        for (int k = 0; k < Math.Min(7, room.Total); k++)
                        {
                            int index = room.Total <= 1 ? 0 :
                                (int)((long)(room.Total - 1) * k /
                                      (Math.Min(7, room.Total) - 1));
                            if (!GameAccess.Position(GameAccess.At(room.Spots, index), out Vector3 p))
                                continue;
                            Vector3 delta = p - here;
                            float d = delta.x * delta.x + delta.z * delta.z +
                                0.2f * delta.y * delta.y;
                            distance = Math.Min(distance, d);
                        }
                        if (distance == float.MaxValue && room.Instance is Component c)
                            distance = (c.transform.position - here).sqrMagnitude;
                        if (distance < best)
                        {
                            best = distance;
                            nearest = room;
                        }
                    }
                    catch { }
                }
                _currentRoomId = nearest?.Id;
            }
            catch
            {
                _currentRoomId = null;
            }
        }
    }
}
