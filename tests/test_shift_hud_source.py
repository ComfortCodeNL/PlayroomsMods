"""Independent source safety checks for PlayroomsShiftHUD.

These checks do NOT replace a .NET build or a real Unity/Playrooms test.
"""
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]
HUD = ROOT / "PlayroomsShiftHUD"


def read(path):
    return (ROOT / path).read_text(encoding="utf-8")


class ShiftHudSourceTests(unittest.TestCase):
    def test_standalone_plugin_does_not_replace_coop(self):
        p = read("PlayroomsShiftHUD/src/Plugin.cs")
        self.assertIn('nl.comfortcode.playrooms.shifthud', p)
        self.assertIn('BepInDependency.DependencyFlags.SoftDependency', p)
        self.assertIn('com.playroomscoop.mod', p)
        self.assertIn('AddComponent<ShiftHudBehaviour>()', p)
        self.assertFalse((HUD / "PlayroomsCoop.dll").exists())

    def test_room_count_is_chunked_and_read_only(self):
        s = read("PlayroomsShiftHUD/src/ShiftState.cs")
        a = read("PlayroomsShiftHUD/src/GameAccess.cs")
        self.assertIn('TwoPixels.Room.RoomObject', s)
        self.assertIn('"_ballSpots"', s)
        self.assertIn('"IsCollected"', a)
        self.assertIn('ScanChunk(750)', s)
        self.assertIn('room.Total > 0 && room.Remaining == 0', s)
        self.assertIn('room.Valid = !room.Failed', s)
        self.assertNotIn('SetValue(', s)
        self.assertNotIn('OnBallCollected()', s)
        self.assertNotIn('SendHost(', s)

    def test_shift_uses_original_daytime_service(self):
        s = read("PlayroomsShiftHUD/src/ShiftState.cs")
        self.assertIn('TwoPixels.DayTime.DayTimeService', s)
        self.assertIn('"RemainingTime"', s)
        self.assertIn('"_phase"', s)
        self.assertIn('"CurrentDay"', s)
        self.assertIn('_phase > 0', s)
        self.assertNotIn('R.Set(', s)
        self.assertNotIn('SetValue(', s)
        self.assertNotIn('Time.timeScale =', s)

    def test_missing_time_does_not_hide_ball_hud(self):
        s = read("PlayroomsShiftHUD/src/ShiftState.cs")
        self.assertIn('if (dts == null)', s)
        self.assertIn('GameAccess.First("TwoPixels.Ball.BallService") != null', s)
        self.assertIn('Kamer niet herkend', s)

    def test_end_day_and_local_worklight_are_visual_only(self):
        b = read("PlayroomsShiftHUD/src/ShiftHudBehaviour.cs")
        self.assertIn('END DAY', b)
        self.assertIn('ShiftEnded', b)
        self.assertIn('LightType.Point', b)
        self.assertIn('LightShadows.None', b)
        self.assertIn('RemoveLight();', b)
        self.assertNotIn('gameObject.SetActive(false)', b)
        self.assertNotIn('"_timer"', b)
        self.assertNotIn('WakeUp(', b)
        self.assertNotIn('StopDay(', b)

    def test_hud_is_not_guarded_by_room_presence(self):
        b = read("PlayroomsShiftHUD/src/ShiftHudBehaviour.cs")
        self.assertIn('if (!Plugin.ShowHud.Value || !_state.InWorld) return;', b)
        self.assertIn('Unity OnGUI callback active', b)
        self.assertIn('Unity Update callback active', b)
        self.assertIn('Kamers zoeken', b)
        self.assertIn('Ballen:', b)
        self.assertIn('Opgeruimd:', b)

    def test_game_references_are_local_not_bundled(self):
        proj = read("PlayroomsShiftHUD/PlayroomsShiftHUD.csproj")
        self.assertIn('netstandard2.1', proj)
        self.assertIn('$(GameDir)/BepInEx/core', proj)
        self.assertIn('$(GameDir)/BepInEx/interop', proj)
        self.assertIn('UnityEngine.IMGUIModule.dll', proj)
        self.assertNotIn('PlayroomsCoop/src', proj)

    def test_installer_updates_only_own_plugin(self):
        script = read("scripts/install-shift-hud.ps1")
        self.assertIn('if (-not $Apply)', script)
        self.assertIn('PlayroomsShiftHUD.dll', script)
        self.assertIn('Get-FileHash -LiteralPath $targetFile', script)
        self.assertIn('comfort-backups', script)
        self.assertIn('Playrooms.exe', script)
        self.assertNotIn('Remove-Item -LiteralPath $interop', script)
        self.assertNotIn('Copy-Item -LiteralPath $patcher', script)
        self.assertNotIn('generate-compat108.ps1', script)

    def test_rollback_restores_only_own_dll(self):
        s = read("scripts/rollback-shift-hud.ps1")
        self.assertIn('PlayroomsShiftHUD.dll.original', s)
        self.assertIn('OriginalExisted', s)
        self.assertIn('ComfortShiftHUD', s)
        self.assertNotIn('Remove-Item -LiteralPath $interop', s)
        self.assertNotIn('Remove-Item -LiteralPath $loader', s)

    def test_docs_do_not_claim_live_game_success(self):
        doc = read("PlayroomsShiftHUD/README.md").lower()
        self.assertIn('nog geen bewezen werkende release', doc)
        self.assertIn('lokaal', doc)
        self.assertIn('end day', doc)
        self.assertIn('install-shift-hud.ps1', doc)


if __name__ == "__main__":
    unittest.main()
