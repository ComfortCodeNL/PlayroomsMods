using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PlayroomsCoop
{
    internal static class LagGuard
    {
        private const string SettingsServiceT = "TwoPixels.Services.Settings.SettingsService";
        private const int MaxLevel = 3, DirectionalLight = 1, NoShadows = 0;

        private sealed class PresetBackup
        {
            public object Preset; public IntPtr Ptr;
            public float Activation, Deactivation, Residency, DormantShadow, Lod, DistantLod, PortalScale;
            public ShadowCastingMode DormantMode; public bool WaterBob;
        }

        private static object _settings;
        private static PresetBackup _backup;
        private static readonly Dictionary<IntPtr, (object light, int shadows)> _lights = new Dictionary<IntPtr, (object, int)>();
        private static float _fps = 60f, _lowSince = -1f, _highSince = -1f, _nextCheck, _nextFind;
        private static int _level, _appliedLevel = -1;
        private static float _appliedScale = -1f;
        private static bool _active, _warned;

        public static int Level => _level;

        public static void Tick(float now, float dt, bool inCoop)
        {
            if (!inCoop)
            {
                if (_active) Restore("left co-op");
                return;
            }
            _active = true;
            if (dt > 0f && dt < 1f) _fps = Mathf.Lerp(_fps, 1f / dt, Math.Min(1f, dt * 2f));
            if (now < _nextCheck) return;
            _nextCheck = now + 0.5f;
            try
            {
                StepLevel(now);
                Apply(now);
            }
            catch (Exception e)
            {
                if (!_warned) { _warned = true; Plugin.Log.LogWarning("[Perf] couldn't change graphics: " + e.Message); }
            }
        }

        // Adaptive level
        private static void StepLevel(float now)
        {
            if (!Plugin.AutoLowerGraphics.Value) { _level = 0; return; }
            float low = Plugin.LowFps.Value;
            if (_fps < low)
            {
                _highSince = -1f;
                if (_lowSince < 0f) _lowSince = now;
                else if (now - _lowSince > 5f && _level < MaxLevel)
                {
                    _level++; _lowSince = now;
                    Plugin.Log.LogInfo($"[Perf] fps {_fps:0} is under {low:0}, graphics lowered to step {_level}");
                }
            }
            else if (_fps > low * 1.5f)
            {
                _lowSince = -1f;
                if (_highSince < 0f) _highSince = now;
                else if (now - _highSince > 30f && _level > 0)
                {
                    _level--; _highSince = now;
                    Plugin.Log.LogInfo($"[Perf] fps {_fps:0} is steady, graphics raised to step {_level}");
                }
            }
            else { _lowSince = -1f; _highSince = -1f; }
        }

        // Apply
        private static void Apply(float now)
        {
            var preset = CurrentPreset(now);
            if (preset == null) return;
            float scale = Mathf.Clamp(Plugin.PileRadiusScale.Value, 0.3f, 1f);
            IntPtr ptr = R.Ptr(preset);
            if (_backup != null && _backup.Ptr != ptr) { RestorePreset(); _appliedLevel = -1; }
            if (_backup != null && _appliedLevel == _level && Math.Abs(_appliedScale - scale) < 0.001f) return;

            if (_backup == null) _backup = Save(preset);
            var b = _backup;
            float pile = scale * (_level >= 2 ? 0.8f : 1f);
            float minRes = 0f;
            try { minRes = Convert.ToSingle(R.Get(preset, "MinColliderResidencyRadius")); } catch { }
            R.Set(preset, "ballActivationRadius", b.Activation * pile);
            R.Set(preset, "ballDeactivationRadius", Math.Max(b.Activation * pile + 0.5f, b.Deactivation * pile));
            R.Set(preset, "ballColliderResidencyRadius", Math.Max(minRes, b.Residency * pile));
            R.Set(preset, "ballDormantShadowCastingMode", _level >= 1 ? ShadowCastingMode.Off : b.DormantMode);
            R.Set(preset, "ballDormantShadowDistance", _level >= 1 ? 0f : b.DormantShadow);
            R.Set(preset, "portalRenderScale", _level >= 1 ? Math.Min(b.PortalScale, 0.6f) : b.PortalScale);
            R.Set(preset, "ballLodDistance", _level >= 2 ? b.Lod * 0.6f : b.Lod);
            R.Set(preset, "ballDistantLodDistance", _level >= 2 ? b.DistantLod * 0.6f : b.DistantLod);
            R.Set(preset, "waterBobbing", _level >= 2 ? false : b.WaterBob);
            Notify();

            if (_level >= 3) LightShadowsOff(); else RestoreLights();
            _appliedLevel = _level; _appliedScale = scale;
            Plugin.Log.LogInfo($"[Perf] co-op graphics: pile radius x{pile:0.00}, step {_level}");
        }

        private static object CurrentPreset(float now)
        {
            if (_settings == null || !World.Alive(_settings))
            {
                _settings = null;
                if (now < _nextFind) return null;
                _nextFind = now + 5f;
                foreach (var s in R.FindAll(R.T(SettingsServiceT))) { _settings = s; break; }
                if (_settings == null) return null;
            }
            return R.Get(_settings, "QualityPreset");
        }

        private static PresetBackup Save(object p) => new PresetBackup
        {
            Preset = p, Ptr = R.Ptr(p),
            Activation = R.Get<float>(p, "ballActivationRadius"),
            Deactivation = R.Get<float>(p, "ballDeactivationRadius"),
            Residency = R.Get<float>(p, "ballColliderResidencyRadius"),
            DormantShadow = R.Get<float>(p, "ballDormantShadowDistance"),
            DormantMode = (ShadowCastingMode)Convert.ToInt32(R.Get(p, "ballDormantShadowCastingMode")),
            Lod = R.Get<float>(p, "ballLodDistance"),
            DistantLod = R.Get<float>(p, "ballDistantLodDistance"),
            PortalScale = R.Get<float>(p, "portalRenderScale"),
            WaterBob = R.Get<bool>(p, "waterBobbing"),
        };

        private static void Notify()
        {
            try
            {
                var evt = R.Get(_settings, "OnQualityPresetChanged");
                if (evt != null) R.Call(evt, "Invoke");
            }
            catch { }
        }

        // Lights
        private static void LightShadowsOff()
        {
            foreach (var l in R.FindAll(R.T("UnityEngine.Light")))
            {
                if (l == null) continue;
                int type = Convert.ToInt32(R.Get(l, "type")), shadows = Convert.ToInt32(R.Get(l, "shadows"));
                if (type == DirectionalLight || shadows == NoShadows) continue;
                IntPtr p = R.Ptr(l);
                if (!_lights.ContainsKey(p)) _lights[p] = (l, shadows);
                R.Set(l, "shadows", NoShadows);
            }
        }

        private static void RestoreLights()
        {
            foreach (var kv in _lights)
                try { if (World.Alive(kv.Value.light)) R.Set(kv.Value.light, "shadows", kv.Value.shadows); } catch { }
            _lights.Clear();
        }

        // Restore
        private static void RestorePreset()
        {
            var b = _backup;
            _backup = null;
            if (b == null || !World.Alive(b.Preset)) return;
            try
            {
                R.Set(b.Preset, "ballActivationRadius", b.Activation);
                R.Set(b.Preset, "ballDeactivationRadius", b.Deactivation);
                R.Set(b.Preset, "ballColliderResidencyRadius", b.Residency);
                R.Set(b.Preset, "ballDormantShadowCastingMode", b.DormantMode);
                R.Set(b.Preset, "ballDormantShadowDistance", b.DormantShadow);
                R.Set(b.Preset, "portalRenderScale", b.PortalScale);
                R.Set(b.Preset, "ballLodDistance", b.Lod);
                R.Set(b.Preset, "ballDistantLodDistance", b.DistantLod);
                R.Set(b.Preset, "waterBobbing", b.WaterBob);
                Notify();
            }
            catch { }
        }

        public static void Restore(string why)
        {
            _active = false;
            bool changed = _backup != null || _lights.Count > 0;
            RestorePreset();
            RestoreLights();
            _level = 0; _appliedLevel = -1; _appliedScale = -1f; _lowSince = -1f; _highSince = -1f;
            if (changed) Plugin.Log.LogInfo("[Perf] graphics back to your settings (" + why + ")");
        }
    }
}
