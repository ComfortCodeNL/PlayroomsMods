using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ComfortCode.Playrooms.ShiftHud
{
    // Independently implemented reflection adapter for Playrooms IL2CPP wrapper types.
    // It does not invoke any co-op networking, game save or ball mutation methods.
    internal static class GameBridge
    {
        private const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        internal const BindingFlags AnyMethod = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private static readonly Dictionary<string, Type> TypeCache = new Dictionary<string, Type>();
        private static readonly Dictionary<(Type, string), MemberInfo> MemberCache = new Dictionary<(Type, string), MemberInfo>();
        private static MethodInfo _findByType, _findOfType;
        private static bool _findResolved;

        internal static Type Type(string fullName)
        {
            if (TypeCache.TryGetValue(fullName, out var cached)) return cached;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var result = assembly.GetType(fullName, false) ?? assembly.GetType("Il2Cpp" + fullName, false);
                if (result == null) continue;
                TypeCache[fullName] = result;
                return result;
            }
            return null; // Assemblies may not have loaded yet.
        }

        internal static object Read(object instance, string key)
        {
            if (instance == null) return null;
            var id = (instance.GetType(), key);
            if (!MemberCache.TryGetValue(id, out var member))
            {
                member = (MemberInfo)id.Item1.GetProperty(key, Instance) ?? id.Item1.GetField(key, Instance);
                if (member != null) MemberCache[id] = member;
            }
            if (member is PropertyInfo p) return p.GetValue(instance);
            if (member is FieldInfo f) return f.GetValue(instance);
            return null;
        }

        internal static int Int(object item, string key, int fallback = -1)
        {
            try { var v = Read(item, key); return v == null ? fallback : Convert.ToInt32(v); }
            catch { return fallback; }
        }

        internal static float Float(object item, string key, float fallback = float.NaN)
        {
            try { var v = Read(item, key); return v == null ? fallback : Convert.ToSingle(v); }
            catch { return fallback; }
        }

        internal static bool Bool(object item, string key, bool fallback = false)
        {
            try { var v = Read(item, key); return v == null ? fallback : Convert.ToBoolean(v); }
            catch { return fallback; }
        }

        internal static string String(object item, string key)
        {
            try { return Read(item, key) as string; }
            catch { return null; }
        }

        internal static bool Alive(object item) => item is UnityEngine.Object unityObject && unityObject != null;

        internal static IEnumerable<object> FindAll(string fullName)
        {
            var type = Type(fullName);
            if (type == null) yield break;
            if (!_findResolved)
            {
                _findResolved = true;
                foreach (var method in typeof(UnityEngine.Object).GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (!method.IsGenericMethodDefinition) continue;
                    var parameters = method.GetParameters();
                    if (method.Name == "FindObjectsByType" && parameters.Length == 1 &&
                        parameters[0].ParameterType.IsEnum) _findByType = method;
                    else if (method.Name == "FindObjectsOfType" && parameters.Length == 0)
                        _findOfType = method;
                }
            }
            object array = null;
            if (_findByType != null)
            {
                var method = _findByType.MakeGenericMethod(type);
                array = method.Invoke(null, new[] { Enum.ToObject(method.GetParameters()[0].ParameterType, 0) });
            }
            else if (_findOfType != null)
                array = _findOfType.MakeGenericMethod(type).Invoke(null, null);
            if (array == null) yield break;
            if (array is IEnumerable enumerable)
            {
                foreach (object o in enumerable) if (Alive(o)) yield return o;
                yield break;
            }
            var count = Int(array, "Length", Int(array, "Count", 0));
            if (count < 0 || count > 10000) yield break;
            var get = array.GetType().GetMethod("get_Item", Instance, null, new[] { typeof(int) }, null);
            if (get == null) yield break;
            for (int i = 0; i < count; i++)
            {
                var o = get.Invoke(array, new object[] { i });
                if (Alive(o)) yield return o;
            }
        }

        internal static object First(string name)
        {
            foreach (var o in FindAll(name)) return o;
            return null;
        }

        internal static string RoomId(object room)
            => String(room, "RoomId") ?? String(room, "roomId");

        internal static IEnumerable<object> ServiceRooms()
        {
            // RoomService.Rooms is an Il2CppReferenceArray<RoomObject> in the
            // verified Playrooms interop. It may include rooms not currently
            // active in the scene, unlike a FindObjectsByType-only scan.
            var service = First("TwoPixels.Room.RoomService");
            if (service == null) yield break;
            object rooms = null;
            try { rooms = Read(service, "Rooms"); }
            catch { /* Missing service property: use the active-object fallback. */ }
            if (rooms == null) yield break;
            if (rooms is IEnumerable enumerable)
            {
                foreach (object room in enumerable) if (Alive(room)) yield return room;
                yield break;
            }
            int count = Int(rooms, "Length", Int(rooms, "Count", 0));
            if (count < 0 || count > 10000) yield break;
            var getter = rooms.GetType().GetMethod("get_Item", Instance, null, new[] { typeof(int) }, null);
            if (getter == null) yield break;
            for (int i = 0; i < count; i++)
            {
                object room;
                try { room = getter.Invoke(rooms, new object[] { i }); }
                catch { continue; }
                if (Alive(room)) yield return room;
            }
        }

        internal static object PlayerRoom()
        {
            var service = First("TwoPixels.Room.RoomService");
            return service == null ? null : Read(service, "PlayerRoom");
        }
    }
}
