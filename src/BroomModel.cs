using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace PlayroomsCoop
{
    internal static partial class World
    {
        private static object _broomVisual;
        private static Vector3 _broomHoldPos, _broomHoldScale = Vector3.one;
        private static Quaternion _broomHoldRot;
        private static bool _broomHoldKnown;
        private static float _nextBroomCapture;
        private static bool _broomCopyFailed;

        private static void CaptureBroomHold(float now)
        {
            if (now < _nextBroomCapture) return;
            _nextBroomCapture = now + 1f;
            var shovel = FindShovel();
            if (shovel == null) return;
            var data = R.Get(shovel, "ShovelBehaviourData");
            if (data == null) return;
            object shown = null;
            foreach (var name in new[] { "UpgradedVisual", "BasicVisual" })
            {
                var v = R.Get(data, name) as GameObject;
                if (v != null && Convert.ToBoolean(R.Get(v, "activeInHierarchy"))) { shown = v; break; }
            }
            if (shown == null) return;
            var cam = R.GetStatic(R.T("UnityEngine.Camera"), "main") as Component;
            if (cam == null) return;

            var vis = ((GameObject)shown).transform;
            var ct = cam.transform; var fwd = ct.forward;
            var pivot = new GameObject("PlayroomsCoop_BroomPivot").transform;
            pivot.position = ct.position;
            pivot.rotation = Quaternion.Euler(0f, Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg, 0f);
            var probe = new GameObject("PlayroomsCoop_BroomProbe").transform;
            try
            {
                probe.position = vis.position; probe.rotation = vis.rotation;
                probe.SetParent(pivot, true);
                _broomHoldPos = R.Get<Vector3>(probe, "localPosition");
                _broomHoldRot = R.Get<Quaternion>(probe, "localRotation");
                _broomHoldScale = R.Get<Vector3>(vis, "lossyScale");
                bool first = !_broomHoldKnown || !R.Same(_broomVisual, shown);
                _broomVisual = shown; _broomHoldKnown = true;
                if (first) Plugin.Log.LogInfo($"[Broom] model found: held at {_broomHoldPos} from the camera, scale {_broomHoldScale}");
            }
            finally { UnityEngine.Object.Destroy(probe.gameObject); UnityEngine.Object.Destroy(pivot.gameObject); }
        }

        internal static bool BuildGameBroom(Transform parent, out Transform root)
        {
            root = null;
            if (_broomCopyFailed || !_broomHoldKnown || !Alive(_broomVisual)) return false;
            try
            {
                var vis = ((GameObject)_broomVisual).transform;
                var holder = new GameObject("PlayroomsCoop_GameBroom").transform;
                holder.position = vis.position; holder.rotation = vis.rotation;
                var mfT = R.T("UnityEngine.MeshFilter"); var mrT = R.T("UnityEngine.MeshRenderer");
                var smrT = R.T("UnityEngine.SkinnedMeshRenderer"); var rT = R.T("UnityEngine.Renderer");
                int parts = 0;
                foreach (var r in Generic(vis.gameObject, "GetComponentsInChildren", rT, true) as System.Collections.IEnumerable ?? new object[0])
                {
                    object mesh = null;
                    var rgo = ((Component)r).gameObject;
                    var skinned = TryCast(r, smrT);
                    if (skinned != null) mesh = R.Get(skinned, "sharedMesh");
                    else if (TryCast(r, mrT) != null) { var mf = Generic(rgo, "GetComponent", mfT); if (mf != null) mesh = R.Get(mf, "sharedMesh"); }
                    if (mesh == null) continue;

                    var node = new GameObject("Part");
                    var nt = node.transform;
                    nt.position = ((Component)r).transform.position;
                    nt.rotation = ((Component)r).transform.rotation;
                    nt.localScale = R.Get<Vector3>(((Component)r).transform, "lossyScale");
                    nt.SetParent(holder, true);
                    R.Set(Generic(node, "AddComponent", mfT), "sharedMesh", mesh);
                    var nr = Generic(node, "AddComponent", mrT);
                    R.Set(nr, "sharedMaterials", R.Get(r, "sharedMaterials"));
                    parts++;
                }
                if (parts == 0) { UnityEngine.Object.Destroy(holder.gameObject); return false; }
                holder.SetParent(parent, false);
                R.Set(holder, "localPosition", _broomHoldPos);
                R.Set(holder, "localRotation", _broomHoldRot);
                holder.localScale = Vector3.one;
                root = holder;
                Plugin.Log.LogInfo($"[Broom] showing the game's broom model on the other player ({parts} parts)");
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[Broom] game model copy failed, using the simple broom: " + e.Message);
                _broomCopyFailed = true;
                return false;
            }
        }

        private static object TryCast(object o, Type t)
        {
            var m = o.GetType().GetMethod("TryCast");
            return m?.MakeGenericMethod(t).Invoke(o, null);
        }

        private static object Generic(object go, string name, Type t, params object[] args)
        {
            foreach (var m in go.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (m.Name != name || !m.IsGenericMethodDefinition || m.GetParameters().Length != args.Length) continue;
                if (args.Length == 1 && m.GetParameters()[0].ParameterType != typeof(bool)) continue;
                return m.MakeGenericMethod(t).Invoke(go, args);
            }
            throw new MissingMethodException(go.GetType().Name, name + "<T>");
        }
    }
}
