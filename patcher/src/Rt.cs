using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;

namespace PlayroomsPatcher
{
    internal static unsafe class Rt
    {
        public const int TVoid = 0x01, TBool = 0x02, TChar = 0x03, TI1 = 0x04, TU1 = 0x05, TI2 = 0x06, TU2 = 0x07,
            TI4 = 0x08, TU4 = 0x09, TI8 = 0x0A, TU8 = 0x0B, TR4 = 0x0C, TR8 = 0x0D, TString = 0x0E, TPtr = 0x0F,
            TValueType = 0x11, TClass = 0x12, TVar = 0x13, TArray = 0x14, TGenericInst = 0x15, TTypedByRef = 0x16,
            TI = 0x18, TU = 0x19, TFnPtr = 0x1B, TObject = 0x1C, TSzArray = 0x1D, TMVar = 0x1E;

        private static IntPtr _runtimeType, _methodBase;
        private static readonly Dictionary<(IntPtr, string), IntPtr> _methods = new Dictionary<(IntPtr, string), IntPtr>();

        public static string Str(IntPtr utf8) => utf8 == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(utf8);

        public static void Init()
        {
            var corlib = IL2CPP.il2cpp_get_corlib();
            _runtimeType = IL2CPP.il2cpp_class_from_name(corlib, "System", "RuntimeType");
            _methodBase = IL2CPP.il2cpp_class_from_name(corlib, "System.Reflection", "MethodBase");
            if (_runtimeType == IntPtr.Zero) throw new InvalidOperationException("System.RuntimeType not found");
        }

        private static IntPtr Find(IntPtr klass, string name)
        {
            if (_methods.TryGetValue((klass, name), out var m)) return m;
            for (var k = klass; k != IntPtr.Zero && m == IntPtr.Zero; k = IL2CPP.il2cpp_class_get_parent(k))
                m = IL2CPP.il2cpp_class_get_method_from_name(k, name, 0);
            _methods[(klass, name)] = m;
            return m;
        }

        public static IntPtr Call(IntPtr obj, string name)
        {
            if (obj == IntPtr.Zero) return IntPtr.Zero;
            var m = Find(IL2CPP.il2cpp_object_get_class(obj), name);
            if (m == IntPtr.Zero) return IntPtr.Zero;
            IntPtr exc = IntPtr.Zero;
            var r = IL2CPP.il2cpp_runtime_invoke(m, obj, null, ref exc);
            return exc == IntPtr.Zero ? r : IntPtr.Zero;
        }

        public static int CallInt(IntPtr obj, string name, int fallback = -1)
        {
            var boxed = Call(obj, name);
            return boxed == IntPtr.Zero ? fallback : *(int*)IL2CPP.il2cpp_object_unbox(boxed);
        }

        public static IntPtr TypeObject(IntPtr type) => type == IntPtr.Zero ? IntPtr.Zero : IL2CPP.il2cpp_type_get_object(type);
        public static IntPtr TypeOf(IntPtr typeObject) => typeObject == IntPtr.Zero ? IntPtr.Zero : *(IntPtr*)(typeObject + 2 * IntPtr.Size);

        public static List<IntPtr> Items(IntPtr array)
        {
            var list = new List<IntPtr>();
            if (array == IntPtr.Zero) return list;
            uint n = IL2CPP.il2cpp_array_length(array);
            var data = (IntPtr*)(array + 4 * IntPtr.Size);
            for (int i = 0; i < n && i < 4096; i++) list.Add(data[i]);
            return list;
        }

        public static List<IntPtr> TypeArray(IntPtr obj, string method)
        {
            var list = new List<IntPtr>();
            foreach (var t in Items(Call(obj, method))) list.Add(TypeOf(t));
            return list;
        }

        public static string ManagedString(IntPtr il2cppString) => il2cppString == IntPtr.Zero ? null : IL2CPP.Il2CppStringToManaged(il2cppString);

        public delegate IntPtr Next(IntPtr klass, ref IntPtr iter);
        public static IEnumerable<IntPtr> Iterate(IntPtr klass, Next next)
        {
            IntPtr iter = IntPtr.Zero, item;
            var list = new List<IntPtr>();
            while ((item = next(klass, ref iter)) != IntPtr.Zero) list.Add(item);
            return list;
        }
    }
}
