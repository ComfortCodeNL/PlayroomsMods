using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace PlayroomsCoop
{
    internal class Avatar
    {
        private const float Radius = 0.28f;
        private const float HeadClearance = 0.15f;

        public readonly bool IsLocal;
        public string Label;
        public Color Color;

        private readonly GameObject _root;
        private readonly Transform _top, _mid, _bottom, _eyes;
        private readonly List<Renderer> _renderers = new List<Renderer>();

        private Vector3 _pos, _targetPos;
        private float _yaw, _targetYaw, _pitch, _targetPitch, _eyeH = 1.6f, _targetEyeH = 1.6f;
        private bool _hasPose;
        private float _standH = -1f, _highSince = -1f;

        private Transform _broom, _broomHandle, _broomHead, _broomBristles, _gameBroom;
        private bool _gameBroomTried;
        private bool _broomOn;
        private float _broomBlend;

        public Vector3 HeadPosition => _root != null ? _root.transform.position : Vector3.zero;
        public bool Alive => _root != null;
        public bool HasPose => _hasPose && _root != null && _root.activeSelf;

        public Avatar(string label, Color color, bool isLocal, float glow)
        {
            IsLocal = isLocal;
            Label = label;
            Color = color;

            _root = new GameObject("PlayroomsCoop_Avatar_" + label);
            Object.DontDestroyOnLoad(_root);

            var bodyMat = Materials.Get(color, glow);
            var eyeMat = Materials.Get(new Color(0.9f, 0.9f, 0.9f), 0f);
            var pupilMat = Materials.Get(new Color(0.03f, 0.03f, 0.03f), 0f);

            _top = Part(PrimitiveType.Sphere, _root.transform, bodyMat).transform;
            _mid = Part(PrimitiveType.Cylinder, _root.transform, bodyMat).transform;
            _bottom = Part(PrimitiveType.Sphere, _root.transform, bodyMat).transform;
            _top.localScale = _bottom.localScale = Vector3.one * (Radius * 2f);

            _eyes = new GameObject("Eyes").transform;
            _eyes.SetParent(_root.transform, false);
            float eyeX = 0.095f, eyeUp = 0.12f;
            float eyeFwd = Mathf.Sqrt(Radius * Radius - eyeX * eyeX - eyeUp * eyeUp) - 0.01f;
            foreach (float side in new[] { -1f, 1f })
            {
                var white = Part(PrimitiveType.Sphere, _eyes, eyeMat).transform;
                white.localPosition = new Vector3(side * eyeX, eyeUp, eyeFwd);
                white.localScale = Vector3.one * 0.085f;

                var pupil = Part(PrimitiveType.Sphere, _eyes, pupilMat).transform;
                pupil.localPosition = new Vector3(side * eyeX, eyeUp, eyeFwd + 0.034f);
                pupil.localScale = new Vector3(0.05f, 0.05f, 0.03f);
            }

            if (isLocal)
            {
                foreach (var r in _renderers) r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            }

            ApplyHeight(_eyeH);
        }

        private GameObject Part(PrimitiveType type, Transform parent, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                r.sharedMaterial = mat;
                _renderers.Add(r);
            }
            return go;
        }

        private float BodyHeight(float h)
        {
            float now = Time.realtimeSinceStartup;
            if (_standH < 0f) _standH = h;
            if (h > _standH * 1.05f)
            {
                if (_highSince < 0f) _highSince = now;
                else if (now - _highSince > 1.5f) { _standH = h; _highSince = -1f; }
                return _standH;
            }
            _highSince = -1f;
            if (h >= _standH * 0.92f) _standH += (h - _standH) * 0.1f;
            return h;
        }

        private void ApplyHeight(float eyeHeight)
        {
            eyeHeight = Mathf.Clamp(BodyHeight(eyeHeight), 0.45f, 2.4f);
            float topC = HeadClearance - Radius;
            float botC = -eyeHeight + Radius;
            if (botC > topC) botC = topC;

            _top.localPosition = new Vector3(0, topC, 0);
            _bottom.localPosition = new Vector3(0, botC, 0);
            float len = topC - botC;
            _mid.gameObject.SetActive(len > 0.01f);
            _mid.localPosition = new Vector3(0, (topC + botC) * 0.5f, 0);
            _mid.localScale = new Vector3(Radius * 2f, Mathf.Max(len * 0.5f, 0.001f), Radius * 2f);
            _eyes.localPosition = new Vector3(0, topC, 0);
        }

        private void ApplyPose(Vector3 pos, float yaw, float pitch, float eyeH)
        {
            _root.transform.position = pos;
            _root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            _eyes.localRotation = Quaternion.Euler(Mathf.Clamp(pitch, -60f, 60f) * 0.45f, 0f, 0f);
            ApplyHeight(eyeH);
        }

        public void SetImmediate(Vector3 pos, float yaw, float pitch, float eyeH)
        {
            if (_root == null) return;
            _pos = _targetPos = pos; _yaw = _targetYaw = yaw; _pitch = _targetPitch = pitch; _eyeH = _targetEyeH = eyeH;
            _hasPose = true;
            ApplyPose(pos, yaw, pitch, eyeH);
        }

        public void SetTarget(in PoseState s)
        {
            _targetPos = new Vector3(s.X, s.Y, s.Z);
            _targetYaw = s.Yaw;
            _targetPitch = s.Pitch;
            _targetEyeH = s.EyeHeight;
            if (!_hasPose || (_targetPos - _pos).sqrMagnitude > 36f)
            {
                _pos = _targetPos; _yaw = _targetYaw; _pitch = _targetPitch; _eyeH = _targetEyeH;
                _hasPose = true;
            }
            SetVisible(true);
        }

        public void Tick(float dt)
        {
            if (_root == null || !_hasPose || IsLocal) return;
            float k = 1f - Mathf.Exp(-dt * 16f);
            _pos = Vector3.Lerp(_pos, _targetPos, k);
            _yaw = Mathf.LerpAngle(_yaw, _targetYaw, k);
            _pitch = Mathf.Lerp(_pitch, _targetPitch, k);
            _eyeH = Mathf.Lerp(_eyeH, _targetEyeH, k);
            ApplyPose(_pos, _yaw, _pitch, _eyeH);
            TickBroom(dt);
        }

        public void SetBroom(bool on) => _broomOn = on;

        private void BuildBroom()
        {
            _broom = new GameObject("Broom").transform;
            _broom.SetParent(_root.transform, false);
            _broomHandle = Part(PrimitiveType.Cylinder, _broom, Materials.Get(new Color(0.55f, 0.36f, 0.2f), 0f)).transform;
            _broomHead = Part(PrimitiveType.Cube, _broom, Materials.Get(new Color(0.22f, 0.22f, 0.25f), 0f)).transform;
            _broomBristles = Part(PrimitiveType.Cube, _broom, Materials.Get(new Color(0.85f, 0.72f, 0.4f), 0f)).transform;
        }

        private void TickBroom(float dt)
        {
            _broomBlend = Mathf.Clamp01(_broomBlend + (_broomOn ? dt * 6f : -dt * 4f));
            if (_gameBroom == null && !_gameBroomTried && _broomOn && Plugin.ShowBroom.Value)
            {
                if (World.BuildGameBroom(_root.transform, out _gameBroom)) _gameBroomTried = true;
            }
            if (_gameBroom != null)
            {
                if (_broom != null && _broom.gameObject.activeSelf) _broom.gameObject.SetActive(false);
                bool show = _broomBlend > 0.5f;
                if (_gameBroom.gameObject.activeSelf != show) _gameBroom.gameObject.SetActive(show);
                return;
            }
            if (_broomBlend <= 0f) { if (_broom != null && _broom.gameObject.activeSelf) _broom.gameObject.SetActive(false); return; }
            if (_broom == null) BuildBroom();
            if (!_broom.gameObject.activeSelf) _broom.gameObject.SetActive(true);

            float eye = Mathf.Clamp(_eyeH, 0.45f, 2.4f);
            float reach = Mathf.Lerp(0.5f, 1.05f, _broomBlend);
            float handY = -eye * 0.5f, handZ = 0.32f;
            float footY = -eye + 0.07f, footZ = Mathf.Max(handZ + 0.2f, reach);
            float dy = handY - footY, dz = handZ - footZ;
            float len = Mathf.Sqrt(dy * dy + dz * dz);
            float tilt = Mathf.Atan2(dz, dy) * Mathf.Rad2Deg;

            _broomHandle.localPosition = new Vector3(0.12f, (handY + footY) * 0.5f + dy / len * 0.15f, (handZ + footZ) * 0.5f + dz / len * 0.15f);
            _broomHandle.localRotation = Quaternion.Euler(tilt, 0f, 0f);
            _broomHandle.localScale = new Vector3(0.035f, len * 0.5f + 0.15f, 0.035f);
            _broomHead.localPosition = new Vector3(0.12f, footY, footZ);
            _broomHead.localRotation = Quaternion.Euler(0f, 0f, 0f);
            _broomHead.localScale = new Vector3(0.5f, 0.07f, 0.14f);
            _broomBristles.localPosition = new Vector3(0.12f, footY - 0.055f, footZ);
            _broomBristles.localRotation = Quaternion.Euler(0f, 0f, 0f);
            _broomBristles.localScale = new Vector3(0.48f, 0.05f, 0.11f);
        }

        public void SetColor(Color c, float glow)
        {
            if (_root == null || (Math.Abs(c.r - Color.r) < 0.001f && Math.Abs(c.g - Color.g) < 0.001f && Math.Abs(c.b - Color.b) < 0.001f)) return;
            Color = c;
            var mat = Materials.Get(c, glow);
            foreach (var t in new[] { _top, _mid, _bottom })
            {
                var r = t != null ? t.gameObject.GetComponent<Renderer>() : null;
                if (r != null) r.sharedMaterial = mat;
            }
        }

        public void SetVisible(bool v)
        {
            if (_root != null && _root.activeSelf != v) _root.SetActive(v);
        }

        public void Destroy()
        {
            if (_root != null) Object.Destroy(_root);
        }
    }

    internal static class Materials
    {
        private static readonly Dictionary<string, Material> _cache = new Dictionary<string, Material>();
        private static Material _template;

        private static readonly string[] FallbackShaders =
        {
            "Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit", "HDRP/Lit", "Standard",
            "Universal Render Pipeline/Unlit", "Unlit/Color", "Sprites/Default", "UI/Default",
        };

        public static Material Get(Color c, float glow)
        {
            string key = $"{c.r:F3},{c.g:F3},{c.b:F3},{glow:F2}";
            if (_cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var m = new Material(Template());
            m.name = "PlayroomsCoop_" + key;
            SetColorIfPresent(m, "_BaseColor", c);
            SetColorIfPresent(m, "_Color", c);
            SetColorIfPresent(m, "_UnlitColor", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.35f);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.35f);

            if (glow > 0f)
            {
                Color e = c * glow;
                m.EnableKeyword("_EMISSION");
                SetColorIfPresent(m, "_EmissionColor", e);
                SetColorIfPresent(m, "_EmissiveColor", e * (6f * Mathf.Max(0.1f, Plugin.HdrpGlowScale.Value)));
                SetColorIfPresent(m, "_EmissiveColorLDR", e);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            _cache[key] = m;
            return m;
        }

        private static void SetColorIfPresent(Material m, string prop, Color c)
        {
            if (m.HasProperty(prop)) m.SetColor(prop, c);
        }

        private static Material Template()
        {
            if (_template != null) return _template;

            var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var r = probe.GetComponent<Renderer>();
                var mat = r != null ? r.sharedMaterial : null;
                if (mat != null && mat.shader != null && mat.shader.isSupported && !mat.shader.name.Contains("Error"))
                {
                    _template = new Material(mat);
                    Plugin.Log.LogInfo("Avatar material: using pipeline default shader " + mat.shader.name);
                    return _template;
                }
            }
            finally { Object.Destroy(probe); }

            foreach (var name in FallbackShaders)
            {
                var sh = Shader.Find(name);
                if (sh != null && sh.isSupported)
                {
                    _template = new Material(sh);
                    Plugin.Log.LogInfo("Avatar material: using fallback shader " + name);
                    return _template;
                }
            }
            throw new Exception("No usable shader found for avatars");
        }
    }
}
