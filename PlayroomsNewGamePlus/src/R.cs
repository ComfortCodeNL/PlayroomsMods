using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes;

namespace PlayroomsNewGamePlus;

internal static class R
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
    private const BindingFlags Stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
    private const BindingFlags Any = Inst | Stat;

    private static readonly Dictionary<string, Type> _types = new();
    private static readonly Dictionary<(Type, string), MemberInfo> _members = new();
    private static readonly Dictionary<(Type, string, int, bool), MethodInfo[]> _methods = new();

    private static MethodInfo _findByType, _findOfType;
    private static bool _findResolved;
    private static object _harmony;
    private static MethodInfo _patch;
    private static ConstructorInfo _harmonyMethodCtor;

    public static Type T(string fullName)
    {
        if (_types.TryGetValue(fullName, out var cached)) return cached;
        Type type = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = asm.GetType(fullName, false) ?? asm.GetType("Il2Cpp" + fullName, false);
            if (type != null) break;
        }
        if (type != null) _types[fullName] = type;
        return type;
    }

    private static MemberInfo Member(Type t, string name)
    {
        if (_members.TryGetValue((t, name), out var m)) return m;
        m = (MemberInfo)t.GetProperty(name, Any) ?? t.GetField(name, Any);
        _members[(t, name)] = m;
        return m;
    }

    public static bool Has(object o, string name) => o != null && Member(o.GetType(), name) != null;

    public static object Get(object o, string name)
    {
        if (o == null) return null;
        var m = Member(o.GetType(), name) ?? throw new MissingMemberException(o.GetType().Name, name);
        return m is PropertyInfo p ? p.GetValue(o) : ((FieldInfo)m).GetValue(o);
    }

    public static T Get<T>(object o, string name)
    {
        object v = Get(o, name);
        if (v == null) return default;
        if (v is T t) return t;
        return (T)Convert.ChangeType(v, typeof(T));
    }

    public static void Set(object o, string name, object value)
    {
        if (o == null) throw new ArgumentNullException(nameof(o), "Set " + name);
        var m = Member(o.GetType(), name) ?? throw new MissingMemberException(o.GetType().Name, name);
        if (m is PropertyInfo p) p.SetValue(o, Coerce(value, p.PropertyType));
        else ((FieldInfo)m).SetValue(o, Coerce(value, ((FieldInfo)m).FieldType));
    }

    public static void SetStatic(Type t, string name, object value)
    {
        var m = Member(t, name) ?? throw new MissingMemberException(t.Name, name);
        if (m is PropertyInfo p) p.SetValue(null, Coerce(value, p.PropertyType));
        else ((FieldInfo)m).SetValue(null, Coerce(value, ((FieldInfo)m).FieldType));
    }

    private static MethodInfo _tryCast;

    public static object Cast(object o, string typeName)
    {
        if (o is not Il2CppObjectBase) return null;
        var t = T(typeName);
        if (t == null) return null;
        if (t.IsInstanceOfType(o)) return o;
        _tryCast ??= typeof(Il2CppObjectBase).GetMethod("TryCast");
        return _tryCast?.MakeGenericMethod(t).Invoke(o, null);
    }

    public static object GetStatic(Type t, string name)
    {
        var m = Member(t, name) ?? throw new MissingMemberException(t.Name, name);
        return m is PropertyInfo p ? p.GetValue(null) : ((FieldInfo)m).GetValue(null);
    }

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
        o != null && Methods(o.GetType(), name, paramCount, false).Length != 0;

    private static MethodInfo[] Methods(Type t, string name, int count, bool isStatic)
    {
        if (_methods.TryGetValue((t, name, count, isStatic), out var v)) return v;
        v = t.GetMethods(isStatic ? Stat : Inst)
             .Where(m => m.Name == name && !m.IsGenericMethodDefinition && m.GetParameters().Length == count)
             .ToArray();
        _methods[(t, name, count, isStatic)] = v;
        return v;
    }

    private static MethodInfo Pick(Type t, string name, object[] args, BindingFlags flags)
    {
        args ??= Array.Empty<object>();
        var list = Methods(t, name, args.Length, (flags & BindingFlags.Static) != 0);
        if (list.Length <= 1) return list.FirstOrDefault();
        foreach (var mi in list)
        {
            var ps = mi.GetParameters();
            bool ok = true;
            for (int i = 0; i < ps.Length && ok; i++)
            {
                var pt = ps[i].ParameterType;
                if (pt.IsByRef) pt = pt.GetElementType();
                ok = args[i] == null ? !pt.IsValueType : pt.IsInstanceOfType(args[i]) || (pt.IsEnum && args[i] is int);
            }
            if (ok) return mi;
        }
        return list[0];
    }

    private static object[] Coerce(MethodInfo mi, object[] args)
    {
        if (args == null || args.Length == 0) return args;
        var ps = mi.GetParameters();
        var res = (object[])args.Clone();
        for (int i = 0; i < ps.Length; i++)
        {
            var pt = ps[i].ParameterType;
            if (pt.IsByRef) pt = pt.GetElementType();
            res[i] = Coerce(res[i], pt);
        }
        return res;
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

    public static List<object> Items(object coll)
    {
        var list = new List<object>();
        if (coll == null) return list;
        if (coll is IEnumerable e)
        {
            foreach (var x in e) list.Add(x);
            return list;
        }
        int n = Count(coll);
        for (int i = 0; i < n; i++) list.Add(At(coll, i));
        return list;
    }

    public static List<(object key, object value)> Pairs(object dict)
    {
        var list = new List<(object, object)>();
        if (dict == null) return list;
        if (dict is IDictionary d)
        {
            foreach (DictionaryEntry de in d) list.Add((de.Key, de.Value));
            return list;
        }
        object en = Call(dict, "GetEnumerator");
        while (Convert.ToBoolean(Call(en, "MoveNext")))
        {
            object cur = Get(en, "Current");
            list.Add((Get(cur, "Key"), Get(cur, "Value")));
        }
        return list;
    }

    public static int DictInt(object dict, string key, int fallback = 0)
    {
        if (dict == null) return fallback;
        if (!Convert.ToBoolean(Call(dict, "ContainsKey", key))) return fallback;
        return Convert.ToInt32(Call(dict, "get_Item", key));
    }

    public static List<object> FindAll(Type t)
    {
        var list = new List<object>();
        if (t == null) return list;
        if (!_findResolved)
        {
            _findResolved = true;
            foreach (var mi in typeof(UnityEngine.Object).GetMethods(BindingFlags.Static | BindingFlags.Public))
            {
                if (!mi.IsGenericMethodDefinition) continue;
                var ps = mi.GetParameters();
                if (mi.Name == "FindObjectsByType" && ps.Length == 1 && ps[0].ParameterType.IsEnum) _findByType = mi;
                if (mi.Name == "FindObjectsOfType" && ps.Length == 0) _findOfType = mi;
            }
        }
        object arr;
        if (_findByType != null)
        {
            var g = _findByType.MakeGenericMethod(t);
            arr = g.Invoke(null, new[] { Enum.ToObject(g.GetParameters()[0].ParameterType, 0) });
        }
        else if (_findOfType != null)
        {
            arr = _findOfType.MakeGenericMethod(t).Invoke(null, null);
        }
        else throw new MissingMethodException("UnityEngine.Object", "FindObjectsByType");

        foreach (var x in Items(arr)) if (x != null) list.Add(x);
        return list;
    }

    public static object FindFirst(string typeName)
    {
        var l = FindAll(T(typeName));
        return l.Count > 0 ? l[0] : null;
    }

    public static IntPtr Ptr(object o) => o is Il2CppObjectBase b ? b.Pointer : IntPtr.Zero;

    public static bool Patch(Type target, string method, int paramCount, Type hooks, string prefix, string postfix)
    {
        try
        {
            if (target == null)
            {
                Plugin.Log.LogWarning((object)"Patch skipped: type missing for " + method);
                return false;
            }
            if (_harmony == null)
            {
                var harmonyT = T("HarmonyLib.Harmony") ?? throw new Exception("HarmonyLib not loaded");
                var hmT = T("HarmonyLib.HarmonyMethod");
                _harmony = Activator.CreateInstance(harmonyT, Plugin.Guid);
                _patch = harmonyT.GetMethods().First(m => m.Name == "Patch" && m.GetParameters().Length >= 3 && m.GetParameters()[0].ParameterType == typeof(MethodBase));
                _harmonyMethodCtor = hmT.GetConstructor(new[] { typeof(MethodInfo) });
            }
            var mi = target.GetMethods(BindingFlags.DeclaredOnly | Any)
                           .FirstOrDefault(m => m.Name == method && m.GetParameters().Length == paramCount);
            if (mi == null)
            {
                Plugin.Log.LogWarning((object)"Patch skipped: " + target.Name + "." + method + " not found");
                return false;
            }
            var args = new object[_patch.GetParameters().Length];
            args[0] = mi;
            if (prefix != null) args[1] = _harmonyMethodCtor.Invoke(new object[] { hooks.GetMethod(prefix, Stat) });
            if (postfix != null) args[2] = _harmonyMethodCtor.Invoke(new object[] { hooks.GetMethod(postfix, Stat) });
            _patch.Invoke(_harmony, args);
            Plugin.Log.LogInfo((object)"Hooked " + target.Name + "." + method);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning((object)"Could not hook " + target?.Name + "." + method + ": " + (ex.InnerException ?? ex).Message);
            return false;
        }
    }
}
