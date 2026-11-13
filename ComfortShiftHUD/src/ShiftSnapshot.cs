using System;
using System.Collections.Generic;
using System.Linq;

namespace ComfortCode.Playrooms.ShiftHud
{
    // One immutable view of the current game state; reading only, never editing
    // balls, room progress, co-op authority, clock or save data.
    internal sealed class ShiftSnapshot
    {
        internal string CurrentRoom = "Nog geen kamer herkend";
        internal int CurrentTotal = -1;
        internal int CurrentRemaining = -1;
        internal int CompletedCount;
        internal int RoomCount;
        internal readonly List<string> CompletedNames = new List<string>();
        internal bool ShiftKnown;
        internal bool ShiftEnded;
        // Only the current playable shift, never night/dawn/new-day screens.
        internal bool FinalTenSeconds;
        internal int CurrentDay = -1;
        internal int RemainingSeconds = -1;

        internal static ShiftSnapshot Capture()
        {
            var result = new ShiftSnapshot();
            var byId = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            // Prefer RoomService.Rooms, which includes tracked but potentially
            // inactive rooms, then supplement with active scene RoomObjects.
            try
            {
                foreach (var room in GameBridge.ServiceRooms())
                {
                    var id = GameBridge.RoomId(room);
                    if (string.IsNullOrWhiteSpace(id) || byId.ContainsKey(id)) continue;
                    byId[id] = room;
                }
            }
            catch (Exception e)
            {
                Plugin.Logger?.LogWarning("[ShiftHUD] RoomService.Rooms not readable; using active rooms: " + e.Message);
            }
            try
            {
                foreach (var room in GameBridge.FindAll("TwoPixels.Room.RoomObject"))
                {
                    var id = GameBridge.RoomId(room);
                    if (string.IsNullOrWhiteSpace(id) || byId.ContainsKey(id)) continue;
                    byId[id] = room;
                }
            }
            catch (Exception e)
            {
                Plugin.Logger?.LogWarning("[ShiftHUD] Active room scan unavailable: " + e.Message);
            }

            object current = null;
            try { current = GameBridge.PlayerRoom(); } catch { }
            string currentId = GameBridge.Alive(current) ? GameBridge.RoomId(current) : null;
            if (!string.IsNullOrWhiteSpace(currentId))
            {
                result.CurrentRoom = currentId;
                if (!byId.ContainsKey(currentId)) byId[currentId] = current;
            }

            foreach (var pair in byId.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
            {
                var room = pair.Value;
                int total = GameBridge.Int(room, "TotalBallCount");
                int collected = GameBridge.Int(room, "CollectedBallCount");
                if (total < 0 || total > 1000000 || collected < 0) continue;
                int remaining = Math.Max(0, total - Math.Min(total, collected));

                if (pair.Key.Equals(currentId, StringComparison.OrdinalIgnoreCase))
                {
                    result.CurrentTotal = total;
                    result.CurrentRemaining = remaining;
                }

                // Do not mark rooms with no ball spots as already cleaned.
                if (total <= 0) continue;
                result.RoomCount++;
                if (remaining != 0) continue;
                result.CompletedCount++;
                result.CompletedNames.Add(pair.Key);
            }

            var day = GameBridge.First("TwoPixels.DayTime.DayTimeService");
            if (day == null) return result;
            int phase = GameBridge.Int(day, "_phase");
            bool hasStarted = GameBridge.Bool(day, "HasStarted", false);
            result.CurrentDay = GameBridge.Int(day, "CurrentDay");
            result.ShiftKnown = hasStarted && phase >= 0;
            // The original co-op mod treats phase 0 as the running day and
            // phases 1/2 as shift ended / lights out / night. Phase 3 is dawn.
            result.ShiftEnded = result.ShiftKnown && (phase == 1 || phase == 2);
            if (result.ShiftKnown)
            {
                float remaining = GameBridge.Float(day, "RemainingTime");
                if (phase == 0 && !float.IsNaN(remaining) && !float.IsInfinity(remaining))
                {
                    // This is only a warning. Do not alter _phase, _timer,
                    // RemainingTime, light system, ball spots or player input.
                    result.FinalTenSeconds = remaining > 0f && remaining <= 10f;
                    result.RemainingSeconds = Math.Max(0, Math.Min(86400, (int)Math.Ceiling(remaining)));
                }
                else if (result.ShiftEnded)
                    result.RemainingSeconds = 0;
            }
            return result;
        }

        internal string ShiftLabel
        {
            get
            {
                if (!ShiftKnown) return "--:-- (wacht op shift)";
                if (ShiftEnded) return "00:00";
                if (RemainingSeconds < 0) return "--:--";
                return string.Format("{0:00}:{1:00}", RemainingSeconds / 60, RemainingSeconds % 60);
            }
        }
    }
}
