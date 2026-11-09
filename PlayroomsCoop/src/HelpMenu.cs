using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlayroomsCoop
{
    internal static partial class World
    {
        private static bool _replayingDevicesHome;

        private static void HelpMenuHooks()
        {
            if (R.T("TwoPixels.Popups.HelpPopup.HelpPopup") == null) return;
            R.Patch(R.T(BallServiceT), "ResetRoomBalls", 1, typeof(Hooks), nameof(Hooks.ResetBallsPrefix), nameof(Hooks.ResetBallsPostfix));
            R.Patch(R.T("TwoPixels.Ball.DeviceHomecoming"), "ReturnAllHome", 0, typeof(Hooks), null, nameof(Hooks.DevicesHomePostfix));
            R.Patch(R.T("TwoPixels.Popups.HelpPopup.HelpPopup"), "OnClickRestartDay", 0, typeof(Hooks), nameof(Hooks.RestartDayPrefix), null);
            R.Patch(R.T("TwoPixels.BackroomCleaner.GameManager"), "RestartDay", 0, typeof(Hooks), nameof(Hooks.RestartDayPrefix), null);
        }

        internal static List<(int idx, Vector3 pos)> BallSpotsBefore(object room)
        {
            if (!_connected || _applying || room == null) return null;
            var list = new List<(int, Vector3)>();
            try
            {
                var spots = R.Get(room, "_ballSpots");
                int n = R.Count(spots);
                for (int i = 0; i < n; i++)
                {
                    var spot = R.At(spots, i);
                    if (spot == null || R.Get<bool>(spot, "IsCollected")) continue;
                    list.Add((i, (Vector3)R.Get(spot, "Position")));
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("[Help] reset balls: " + e.Message); return null; }
            return list;
        }

        internal static void SendResetBalls(object room, List<(int idx, Vector3 pos)> before)
        {
            if (before == null || !_connected) return;
            try
            {
                var id = R.Get<string>(room, "RoomId");
                var spots = R.Get(room, "_ballSpots");
                int moved = 0;
                foreach (var (idx, pos) in before)
                {
                    var spot = R.At(spots, idx);
                    if (spot == null || R.Get<bool>(spot, "IsCollected")) continue;
                    var now = (Vector3)R.Get(spot, "Position");
                    if (Dist(now, pos) < 0.05f) continue;
                    _outMoved.Add((id, idx, now));
                    moved++;
                }
                if (moved > 0) Plugin.Log.LogInfo($"[Help] reset {moved} balls in {id} for everyone");
            }
            catch (Exception e) { Plugin.Log.LogWarning("[Help] reset balls: " + e.Message); }
        }

        internal static void SendDevicesHome(int count)
        {
            if (!_connected || _replayingDevicesHome) return;
            Protocol.W.Begin(Msg.DevicesHome).SendAll(SteamNet.SendReliable);
            Plugin.Log.LogInfo($"[Help] devices sent home ({count}); every player follows");
        }

        private static void OnDevicesHome()
        {
            if (!InGameWorld()) return;
            _replayingDevicesHome = true;
            try
            {
                int n = Convert.ToInt32(R.CallStatic(R.T("TwoPixels.Ball.DeviceHomecoming"), "ReturnAllHome"));
                Plugin.Log.LogInfo($"[Help] {Net.NameOf(Net.Sender)} sent the devices home ({n} here)");
                CoopBehaviour.Notify($"{Net.NameOf(Net.Sender)} sent all devices home");
            }
            catch (Exception e) { Plugin.Log.LogWarning("[Help] devices home: " + e.Message); }
            finally { _replayingDevicesHome = false; }
        }

        internal static bool AllowRestartDay()
        {
            if (!_connected) return true;
            const string text = "Restart day is off in co-op. Everyone would end up in a different day.";
            try { R.CallStatic(R.T("TwoPixels.Popups.HelpPopup.HelpPopup"), "ShowInfo", text); }
            catch { CoopBehaviour.Notify(text); }
            return false;
        }
    }
}
