"""Static checks for the independent Comfort Shift HUD source.
Does not replace a .NET build or actual Unity 6000.6 / host-guest client tests.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
HUD = ROOT / "ComfortShiftHUD"
read = lambda path: (ROOT / path).read_text("utf-8")

class ShiftHudSourceTests(unittest.TestCase):
    def test_independent_plugin_without_upstream_source_copy(self):
        src = "\n".join(x.read_text("utf-8") for x in (HUD / "src").glob("*.cs"))
        self.assertIn("nl.comfortcode.playrooms.shifthud", src)
        self.assertIn("BepInDependency.DependencyFlags.SoftDependency", src)
        self.assertIn("AddComponent<ShiftHudBehaviour>()", src)
        self.assertNotIn("namespace PlayroomsCoop", src)
        self.assertNotIn("SaveGame(", src)
        self.assertNotIn("SendHost(", src)
        self.assertNotIn("SendAll(", src)

    def test_actual_game_interop_fields(self):
        text = read("ComfortShiftHUD/src/ShiftSnapshot.cs")
        for name in ("PlayerRoom", "RoomObject", "RoomId", "TotalBallCount",
                     "CollectedBallCount", "DayTimeService", "RemainingTime", "HasStarted"):
            self.assertIn(name, text) if name not in ("PlayerRoom",) else self.assertIn(name, read("ComfortShiftHUD/src/GameBridge.cs"))
        self.assertIn("phase == 1 || phase == 2", text)

    def test_completes_only_nonempty_rooms(self):
        text = read("ComfortShiftHUD/src/ShiftSnapshot.cs")
        self.assertIn("if (total <= 0) continue;", text)
        self.assertIn("result.CompletedNames.Add(pair.Key)", text)
        self.assertIn("result.CompletedCount++", text)
        self.assertIn("result.CurrentRemaining = remaining;", text)

    def test_light_hook_is_fail_open_and_does_not_edit_clock(self):
        text = read("ComfortShiftHUD/src/NightLightKeeper.cs")
        self.assertIn('OnDayTimeChanged', text)
        self.assertIn('KeepLightsOn.Value', text)
        self.assertIn('if (phase != 1) return true;', text)
        self.assertIn('return false;', text)
        self.assertIn('catch (Exception e)', text)
        self.assertNotIn('"_timer"', text)
        self.assertNotIn('WakeUp(', text)
        self.assertNotIn('EnterPhase(', text)
        self.assertNotIn('RenderSettings', text)
        self.assertNotIn('GameAssembly.dll', text)

    def test_hud_visible_without_room_and_end_day_text(self):
        text = read("ComfortShiftHUD/src/ShiftHudBehaviour.cs")
        self.assertIn("END DAY", text)
        self.assertIn("Ballen over:", text)
        self.assertIn("Shift eindigt over:", text)
        self.assertIn("Kamers opgeruimd:", text)
        self.assertIn("Voltooide kamers:", text)
        self.assertIn("Unity OnGUI callback active", text)
        self.assertNotIn("if (!_snapshot.CurrentRoom)", text)

    def test_install_only_own_dll_with_dry_run_and_backups(self):
        script = read("scripts/install-comfort-shift-hud.ps1")
        self.assertIn("if (-not $Apply)", script)
        self.assertIn("ComfortShiftHUD.dll", script)
        self.assertIn("SHA256", script)
        self.assertIn("ComfortShiftHUD.original.dll", script)
        self.assertIn("comfort-backups", script)
        self.assertIn("PlayroomsPatcher.dll", script)
        self.assertNotIn("generate-compat108.ps1", script)
        self.assertNotIn("Remove-Item -LiteralPath $interop", script)
        self.assertNotIn("Copy-Item -LiteralPath $patcher", script)

    def test_rollback_is_scoped(self):
        script = read("scripts/rollback-comfort-shift-hud.ps1")
        self.assertIn("ComfortShiftHUD", script)
        self.assertIn("PreviousDllExisted", script)
        self.assertNotIn("Remove-Item -LiteralPath $interop", script)
        self.assertNotIn("Remove-Item -LiteralPath $patcher", script)
        self.assertNotIn("GameAssembly.dll", script)

    def test_project_refs_only_installed_assemblies(self):
        project = read("ComfortShiftHUD/ComfortShiftHUD.csproj")
        self.assertIn("<TargetFramework>net6.0</TargetFramework>", project)
        self.assertIn("0Harmony.dll", project)
        self.assertIn("BepInEx.Unity.IL2CPP.dll", project)
        self.assertIn("UnityEngine.IMGUIModule.dll", project)
        # Do not require TextRenderingModule: extra IL2CPP metadata can cause
        # Roslyn CS7038 during emit. Draw banner with IMGUI's CalcSize instead.
        self.assertNotIn('UnityEngine.TextRenderingModule.dll', project)
        self.assertNotIn('UnityEngine.TextRenderingModule.dll', read("scripts/install-comfort-shift-hud.ps1"))
        ui = read("ComfortShiftHUD/src/ShiftHudBehaviour.cs")
        self.assertNotIn("TextAnchor.MiddleCenter", ui)
        self.assertNotIn("_large.CalcSize(", ui)
        self.assertNotIn("GUI.DrawTexture(", ui)
        self.assertNotIn("Texture2D.whiteTexture", ui)
        self.assertIn("GUI.Box(new Rect(x, y, width, height)", ui)
        self.assertIn("GUI.Box(new Rect(bx, by, w, 78f)", ui)
        self.assertIn("bx + (w - textWidth) * 0.5f", ui)
        self.assertIn("HUD frame rendered without exception.", ui)
        self.assertIn("HUD drawing failed at stage", ui)
        self.assertIn("[HideFromIl2Cpp]", ui)
        self.assertIn("<ProduceReferenceAssembly>false</ProduceReferenceAssembly>", project)
        self.assertIn("<DebugType>none</DebugType>", project)
        self.assertIn("build-diagnostic.log", read("scripts/install-comfort-shift-hud.ps1"))
        self.assertNotIn("PlayroomsCoop.dll", project)

if __name__ == "__main__":
    unittest.main()
