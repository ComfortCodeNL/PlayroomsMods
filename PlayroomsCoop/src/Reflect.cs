using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes;

namespace PlayroomsCoop
{
    internal static class R
    {
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
        private const BindingFlags Stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;

        private static readonly Dictionary<string, Type> _types = new Dictionary<string, Type>();
        private static readonly Dictionary<(Type, string), MemberInfo> _members = new Dictionary<(Type, string), MemberInfo>();
        private static readonly Dictionary<(Type, string, int), MethodInfo[]> _methods = new Dictionary<(Type, string, int), MethodInfo[]>();

        public static Type T(string fullName)
        {
            if (_types.TryGetValue(fullName, out var cached)) return cached;
            Type found = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                found = asm.GetType(fullName, false) ?? asm.GetType("Il2Cpp" + fullName, false);
                if (found != null) break;
            }
            if (found != null) _types[fullName] = found;
            return found;
        }

        private static MemberInfo Member(Type t, string name)
        {
            if (_members.TryGetValue((t, name), out var m)) return m;
            m = (MemberInfo)t.GetProperty(name, Inst | BindingFlags.Static) ?? t.GetField(name, Inst | BindingFlags.Static);
            _members[(t, name)] = m;
            return m;
        }

        public static object Get(object o, string name)
        {
            if (o == null) return null;
            var m = Member(o.GetType(), name) ?? throw new MissingMemberException(o.GetType().Name, name);
            return m is PropertyInfo p ? p.GetValue(o) : ((FieldInfo)m).GetValue(o);
        }

        public static T Get<T>(object o, string name)
        {
            var v = Get(o, name);
            if (v == null) return default;
            if (v is T t) return t;
            return (T)Convert.ChangeType(v, typeof(T));
        }

        public static object GetStatic(Type t, string name)
        {
            var m = Member(t, name) ?? throw new MissingMemberException(t.Name, name);
            return m is PropertyInfo p ? p.GetValue(null) : ((FieldInfo)m).GetValue(null);
        }

        public static void SetStatic(Type t, string name, object value)
        {
            var m = Member(t, name) ?? throw new MissingMemberException(t.Name, name);
            if (m is PropertyInfo p) p.SetValue(null, Coerce(value, p.PropertyType));
            else ((FieldInfo)m).SetValue(null, Coerce(value, ((FieldInfo)m).FieldType));
        }

        public static void Set(object o, string name, object value)
        {
            var m = Member(o.GetType(), name) ?? throw new MissingMemberException(o.GetType().Name, name);
            if (m is PropertyInfo p)
            {
                if (p.SetMethod == null) throw new MissingMemberException(o.GetType().Name, name + " (read-only)");
                p.SetValue(o, Coerce(value, p.PropertyType));
            }
            else ((FieldInfo)m).SetValue(o, Coerce(value, ((FieldInfo)m).FieldType));
        }

        public static bool Has(object o, string name) => o != null && Member(o.GetType(), name) != null;

        private static object Coerce(object v, Type target)
        {
            if (v == null || target.IsInstanceOfType(v)) return v;
            if (target.IsEnum) return Enum.ToObject(target, v);
            if (v is IConvertible && typeof(IConvertible).IsAssignableFrom(target)) return Convert.ChangeType(v, target);
            return v;
        }

        public static object Call(object o, string name, params object[] args)
        {
            if (o == null) throw new ArgumentNullException(nameof(o), "Call " + name);
            var mi = Pick(o.GetType(), name, args, Inst) ?? throw new MissingMethodException(o.GetType().Name, name);
            return mi.Invoke(o, Coerce(mi, args));
        }

        public static object CallStatic(Type t, string name, params object[] args)
        {
            var mi = Pick(t, name, args, Stat) ?? throw new MissingMethodException(t.Name, name);
            return mi.Invoke(null, Coerce(mi, args));
        }

        public static bool HasMethod(object o, string name, int paramCount) =>
            o != null && Methods(o.GetType(), name, paramCount, Inst).Length > 0;

        private static MethodInfo[] Methods(Type t, string name, int count, BindingFlags flags)
        {
            if (_methods.TryGetValue((t, name, count), out var arr)) return arr;
            arr = t.GetMethods(flags).Where(m => m.Name == name && !m.IsGenericMethodDefinition && m.GetParameters().Length == count).ToArray();
            _methods[(t, name, count)] = arr;
            return arr;
        }

        private static MethodInfo Pick(Type t, string name, object[] args, BindingFlags flags)
        {
            args ??= Array.Empty<object>();
            var cands = Methods(t, name, args.Length, flags);
            if (cands.Length <= 1) return cands.FirstOrDefault();
            foreach (var m in cands)
            {
                var ps = m.GetParameters();
                bool ok = true;
                for (int i = 0; i < ps.Length && ok; i++)
                {
                    var pt = ps[i].ParameterType;
                    if (pt.IsByRef) pt = pt.GetElementType();
                    ok = args[i] == null ? !pt.IsValueType : pt.IsInstanceOfType(args[i]) || (pt.IsEnum && args[i] is int);
                }
                if (ok) return m;
            }
            return cands[0];
        }

        private static object[] Coerce(MethodInfo mi, object[] args)
        {
            var ps = mi.GetParameters();
            if (args == null || args.Length == 0) return args;
            var outArgs = (object[])args.Clone();
            for (int i = 0; i < ps.Length; i++)
            {
                var pt = ps[i].ParameterType;
                if (pt.IsByRef) pt = pt.GetElementType();
                outArgs[i] = Coerce(outArgs[i], pt);
            }
            return outArgs;
        }

        public static int Count(object coll)
        {
            if (coll == null) return 0;
            if (Has(coll, "Count")) return Get<int>(coll, "Count");
            if (Has(coll, "Length")) return Get<int>(coll, "Length");
            if (coll is ICollection c) return c.Count;
            throw new MissingMemberException(coll.GetType().Name, "Count");
        }

        public static object At(object coll, int index)
        {
            if (HasMethod(coll, "get_Item", 1)) return Call(coll, "get_Item", index);
            if (coll is IList l) return l[index];
            throw new MissingMethodException(coll.GetType().Name, "get_Item");
        }

        public static List<(object key, object value)> Pairs(object dict)
        {
            var list = new List<(object, object)>();
            if (dict == null) return list;
            if (dict is IDictionary d) { foreach (DictionaryEntry e in d) list.Add((e.Key, e.Value)); return list; }
            var en = Call(dict, "GetEnumerator");
            while (Convert.ToBoolean(Call(en, "MoveNext")))
            {
                var cur = Get(en, "Current");
                list.Add((Get(cur, "Key"), Get(cur, "Value")));
            }
            return list;
        }

        private static readonly Dictionary<Type, (MethodInfo count, MethodInfo item)> _indexers = new Dictionary<Type, (MethodInfo, MethodInfo)>();

        public static List<object> Items(object coll) => Items(coll, 0, -1, out _);

        public static List<object> Items(object coll, int sliceStart, int sliceLen, out int total)
        {
            total = -1;
            var list = new List<object>();
            if (coll == null) return list;
            if (coll is IEnumerable e) { foreach (var x in e) list.Add(x); return list; }
            var t = coll.GetType();
            if (!_indexers.TryGetValue(t, out var ix))
            {
                var cnt = t.GetProperty("Count", Inst)?.GetGetMethod();
                var item = t.GetMethod("get_Item", Inst, null, new[] { typeof(int) }, null);
                ix = cnt != null && item != null ? (cnt, item) : (null, null);
                _indexers[t] = ix;
            }
            if (ix.count != null)
            {
                int n = Convert.ToInt32(ix.count.Invoke(coll, null));
                if (sliceLen >= 0)
                {
                    total = n;
                    if (n == 0) return list;
                    var a1 = new object[1];
                    for (int i = 0; i < Math.Min(sliceLen, n); i++) { a1[0] = (sliceStart + i) % n; list.Add(ix.item.Invoke(coll, a1)); }
                    return list;
                }
                var arg = new object[1];
                for (int i = 0; i < n; i++) { arg[0] = i; list.Add(ix.item.Invoke(coll, arg)); }
                return list;
            }
            var en = Call(coll, "GetEnumerator");
            while (Convert.ToBoolean(Call(en, "MoveNext"))) list.Add(Get(en, "Current"));
            return list;
        }

        private static MethodInfo _findByType, _findOfType;
        private static bool _findResolved;

        public static List<object> FindAll(Type t)
        {
            var list = new List<object>();
            if (t == null) return list;
            if (!_findResolved)
            {
                _findResolved = true;
                foreach (var m in typeof(UnityEngine.Object).GetMethods(BindingFlags.Static | BindingFlags.Public))
                {
                    if (!m.IsGenericMethodDefinition) continue;
                    var ps = m.GetParameters();
                    if (m.Name == "FindObjectsByType" && ps.Length == 1 && ps[0].ParameterType.IsEnum) _findByType = m;
                    if (m.Name == "FindObjectsOfType" && ps.Length == 0) _findOfType = m;
                }
            }

            object arr;
            if (_findByType != null)
            {
                var g = _findByType.MakeGenericMethod(t);
                arr = g.Invoke(null, new[] { Enum.ToObject(g.GetParameters()[0].ParameterType, 0) });
            }
            else if (_findOfType != null) arr = _findOfType.MakeGenericMethod(t).Invoke(null, null);
            else throw new MissingMethodException("UnityEngine.Object", "FindObjectsByType");

            if (arr is IEnumerable e) { foreach (var x in e) if (x != null) list.Add(x); }
            else { int n = Count(arr); for (int i = 0; i < n; i++) { var x = At(arr, i); if (x != null) list.Add(x); } }
            return list;
        }

        public static IntPtr Ptr(object o) => o is Il2CppObjectBase b ? b.Pointer : IntPtr.Zero;

        public static bool Same(object a, object b) => a != null && b != null && Ptr(a) == Ptr(b) && Ptr(a) != IntPtr.Zero;

        private static object _harmony;
        private static MethodInfo _patch;
        private static ConstructorInfo _harmonyMethodCtor;

        public static bool Patch(Type target, string method, int paramCount, Type hooks, string prefix, string postfix)
        {
            try
            {
                if (target == null) { Plugin.Log.LogWarning($"Patch skipped: type missing for {method}"); return false; }
                if (_harmony == null)
                {
                    var hType = T("HarmonyLib.Harmony") ?? throw new Exception("HarmonyLib not loaded");
                    var hmType = T("HarmonyLib.HarmonyMethod");
                    _harmony = Activator.CreateInstance(hType, Plugin.Guid);
                    _patch = hType.GetMethods().First(m => m.Name == "Patch" && m.GetParameters().Length >= 3 &&
                                                           m.GetParameters()[0].ParameterType == typeof(MethodBase));
                    _harmonyMethodCtor = hmType.GetConstructor(new[] { typeof(MethodInfo) });
                }

                var original = target.GetMethods(Inst | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .FirstOrDefault(m => m.Name == method && m.GetParameters().Length == paramCount);
                if (original == null) { Plugin.Log.LogWarning($"Patch skipped: {target.Name}.{method} not found"); return false; }

                var args = new object[_patch.GetParameters().Length];
                args[0] = original;
                if (prefix != null) args[1] = _harmonyMethodCtor.Invoke(new object[] { hooks.GetMethod(prefix, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public) });
                if (postfix != null) args[2] = _harmonyMethodCtor.Invoke(new object[] { hooks.GetMethod(postfix, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public) });
                _patch.Invoke(_harmony, args);
                Plugin.Log.LogInfo($"Hooked {target.Name}.{method}");
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not hook {target?.Name}.{method}: {(e.InnerException ?? e).Message}");
                return false;
            }
        }
    }

    internal sealed class Feature
    {
        public readonly string Name;
        public bool Enabled;
        private int _errors;

        public Feature(string name, bool enabled) { Name = name; Enabled = enabled; }

        public void Rearm(bool enabled) { Enabled = enabled; _errors = 0; }

        public bool Try(Action a, string what)
        {
            if (!Enabled) return false;
            bool timed = Perf.BeginTop(Name);
            try { a(); return true; }
            catch (Exception e)
            {
                e = (e as TargetInvocationException)?.InnerException ?? e;
                _errors++;
                if (_errors <= 5) Plugin.Log.LogWarning($"[{Name}] {what}: {e.GetType().Name}: {e.Message}");
                if (_errors >= 40)
                {
                    Enabled = false;
                    Plugin.Log.LogError($"[{Name}] turned off after repeated errors. Other co-op features keep working.");
                }
                return false;
            }
            finally { if (timed) Perf.End(); }
        }
    }
}
