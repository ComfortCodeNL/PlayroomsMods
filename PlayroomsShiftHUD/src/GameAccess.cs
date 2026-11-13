using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ComfortCode.Playrooms.ShiftHUD
{
    // Read-only accessor. All game data is resolved from the user's locally generated
    // IL2CPP proxy assemblies. No classes are linked from the upstream co-op mod.
    internal static class GameAccess
    {
        private const BindingFlags InstanceFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly Dictionary<string, Type> TypeCache =
            new Dictionary<string, Type>();
        private static readonly Dictionary<(Type, string), MemberInfo> MemberCache =
            new Dictionary<(Type, string), MemberInfo>();
        private static MethodInfo _findByType;
        private static MethodInfo _findOfType;
        private static bool _searchedFind;

        internal static Type GameType(string name)
        {
            if (TypeCache.TryGetValue(name, out var t)) return t;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType(name, false) ?? asm.GetType("Il2Cpp" + name, false);
                if (t == null) continue;
                TypeCache[name] = t;
                return t;
            }
            return null; // Search again after game scene assemblies have loaded.
        }

        internal static object Read(object instance, string name)
        {
            if (instance == null) return null;
            var key = (instance.GetType(), name);
            if (!MemberCache.TryGetValue(key, out var member))
            {
                member = (MemberInfo)key.Item1.GetProperty(name, InstanceFlags) ??
                    key.Item1.GetField(name, InstanceFlags);
                if (member == null) return null;
                MemberCache[key] = member;
            }
            if (member is PropertyInfo prop) return prop.GetValue(instance);
            return ((FieldInfo)member).GetValue(instance);
        }

        internal static string Text(object item, string name)
        {
            try { return Read(item, name) as string; }
            catch { return null; }
        }

        internal static bool TryFloat(object item, string name, out float value)
        {
            value = 0f;
            try
            {
                var raw = Read(item, name);
                if (raw == null) return false;
                value = Convert.ToSingle(raw);
                return !float.IsNaN(value) && !float.IsInfinity(value);
            }
            catch { return false; }
        }

        internal static bool TryInt(object item, string name, out int value)
        {
            value = 0;
            try
            {
                var raw = Read(item, name);
                if (raw == null) return false;
                value = Convert.ToInt32(raw);
                return true;
            }
            catch { return false; }
        }

        internal static bool TryBool(object item, string name, out bool value)
        {
            value = false;
            try
            {
                var raw = Read(item, name);
                if (raw == null) return false;
                value = Convert.ToBoolean(raw);
                return true;
            }
            catch { return false; }
        }

        internal static int Count(object list)
        {
            if (list == null) return -1;
            if (list is ICollection c) return c.Count;
            try
            {
                var n = Read(list, "Count") ?? Read(list, "Length");
                return n == null ? -1 : Convert.ToInt32(n);
            }
            catch { return -1; }
        }

        internal static object At(object list, int index)
        {
            if (list == null || index < 0) return null;
            if (list is IList l) return l[index];
            Type t = list.GetType();
            var method = t.GetMethod("get_Item", InstanceFlags, null,
                new[] { typeof(int) }, null);
            return method?.Invoke(list, new object[] { index });
        }

        internal static bool Alive(object instance)
        {
            try { return instance is UnityEngine.Object obj && obj != null; }
            catch { return false; }
        }

        internal static List<object> Find(string gameTypeName)
        {
            var results = new List<object>();
            var t = GameType(gameTypeName);
            if (t == null) return results;
            if (!_searchedFind)
            {
                _searchedFind = true;
                foreach (var m in typeof(UnityEngine.Object).GetMethods(
                    BindingFlags.Public | BindingFlags.Static))
                {
                    if (!m.IsGenericMethodDefinition) continue;
                    var parameters = m.GetParameters();
                    if (m.Name == "FindObjectsByType" && parameters.Length == 1 &&
                        parameters[0].ParameterType.IsEnum) _findByType = m;
                    else if (m.Name == "FindObjectsOfType" && parameters.Length == 0)
                        _findOfType = m;
                }
            }

            object raw = null;
            if (_findByType != null)
            {
                var m = _findByType.MakeGenericMethod(t);
                raw = m.Invoke(null, new object[] {
                    Enum.ToObject(m.GetParameters()[0].ParameterType, 0)
                });
            }
            else if (_findOfType != null)
                raw = _findOfType.MakeGenericMethod(t).Invoke(null, null);

            if (raw is IEnumerable items)
            {
                foreach (var item in items)
                    if (Alive(item)) results.Add(item);
            }
            else
            {
                int count = Count(raw);
                for (int i = 0; i < count; i++)
                {
                    var item = At(raw, i);
                    if (Alive(item)) results.Add(item);
                }
            }
            return results;
        }

        internal static object First(string name)
        {
            var all = Find(name);
            return all.Count > 0 ? all[0] : null;
        }

        internal static Transform Player()
        {
            foreach (var service in Find("TwoPixels.Ball.BallService"))
            {
                try
                {
                    var body = Read(service, "playerTransform") as Transform;
                    if (body != null) return body;
                }
                catch { }
            }
            try { return Camera.main != null ? Camera.main.transform : null; }
            catch { return null; }
        }

        internal static bool MenuShowing()
        {
            try
            {
                var menu = First("TwoPixels.UserInterfaces.MainMenuCanvas");
                if (menu == null) return false;
                var canvas = Read(menu, "userInterfaceCanvas");
                return TryBool(canvas, "IsShowing", out var visible) && visible;
            }
            catch { return false; }
        }

        internal static string CurrentRoomId()
        {
            try
            {
                var service = First("TwoPixels.Room.RoomService");
                if (service == null) return null;
                foreach (string key in new [] {
                    "CurrentRoom", "CurrentRoomObject", "PlayerRoom", "ActiveRoom",
                    "_currentRoom", "_playerRoom"
                })
                {
                    var value = Read(service, key);
                    if (value == null) continue;
                    var id = value as string ?? Text(value, "RoomId");
                    if (!string.IsNullOrEmpty(id)) return id;
                }
            }
            catch { }
            return null;
        }

        internal static bool Position(object spot, out Vector3 pos)
        {
            pos = default;
            try
            {
                var value = Read(spot, "Position");
                if (value is Vector3 p) { pos = p; return true; }
            }
            catch { }
            return false;
        }

        internal static bool Collected(object spot, out bool collected)
            => TryBool(spot, "IsCollected", out collected);
    }
}
