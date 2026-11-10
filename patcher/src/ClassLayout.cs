using System;
using System.Runtime.InteropServices;

namespace PlayroomsPatcher
{
    internal static class ClassLayout
    {
        public const int Image = 0x00;
        public const int Name = 0x10;
        public const int Namespace = 0x18;
        public const int ByValArg = 0x20;
        public const int ThisArg = 0x30;
        public const int ElementClass = 0x40;
        public const int CastClass = 0x48;
        public const int DeclaringType = 0x50;
        public const int Parent = 0x58;
        public const int GenericClass = 0x60;
        public const int Klass = 0x78;
        public const int Fields = 0x80;
        public const int Methods = 0x88;
        public const int InterfaceOffsets = 0x90;
        public const int StaticFields = 0x98;
        public const int TypeHierarchy = 0xA8;
        public const int Events = 0xB0;
        public const int Properties = 0xB8;
        public const int NestedTypes = 0xC0;
        public const int UnityUserData = 0xC8;
        public const int InstanceSize = 0xF0;
        public const int ActualSize = 0xF8;
        public const int NativeSize = 0x100;
        public const int Flags = 0x110;
        public const int Token = 0x114;
        public const int MethodCount = 0x118;
        public const int PropertyCount = 0x11A;
        public const int FieldCount = 0x11C;
        public const int EventCount = 0x11E;
        public const int NestedCount = 0x120;
        public const int VtableCount = 0x122;
        public const int InterfacesCount = 0x124;
        public const int InterfaceOffsetsCount = 0x126;
        public const int TypeHierarchyDepth = 0x128;
        public const int Rank = 0x12A;
        public const int Bitfield0 = 0x12D;
        public const int Bitfield1 = 0x12E;
        public const int Size = 0x130;

        // Bitfield0
        public const int BitInitializedAndNoError = 0, BitInitialized = 1, BitEnumType = 2, BitIsGeneric = 4;
        // Bitfield1
        public const int BitSizeInited = 0, BitHasFinalize = 1, BitVtableInitialized = 5;

        [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string name);
        [DllImport("kernel32", CharSet = CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module, string name);

        public static string Verify()
        {
            var ga = GetModuleHandleW("GameAssembly.dll");
            if (ga == IntPtr.Zero) return "GameAssembly.dll is not loaded";
            (string fn, int off)[] checks =
            {
                ("il2cpp_class_get_name", Name), ("il2cpp_class_get_namespace", Namespace),
                ("il2cpp_class_get_parent", Parent), ("il2cpp_class_get_declaring_type", DeclaringType),
                ("il2cpp_class_get_element_class", ElementClass), ("il2cpp_class_get_flags", Flags),
                ("il2cpp_class_get_type_token", Token), ("il2cpp_class_instance_size", InstanceSize),
                ("il2cpp_class_get_type", ByValArg), ("il2cpp_class_get_userdata_offset", UnityUserData),
                ("il2cpp_class_get_static_field_data", StaticFields), ("il2cpp_class_num_fields", FieldCount),
                ("il2cpp_class_get_rank", Rank), ("il2cpp_class_get_image", Image),
            };
            foreach (var (fn, off) in checks)
            {
                var p = GetProcAddress(ga, fn);
                if (p == IntPtr.Zero) return fn + " is missing";
                int got = Accessor.ReadOffset(p);
                if (got < 0) return fn + ": unexpected code";
                if (got != off) return $"{fn} reads 0x{got:X}, expected 0x{off:X}";
            }
            return null;
        }
    }

    internal static unsafe class Accessor
    {
        public static int ReadOffset(IntPtr fn)
        {
            byte* p = (byte*)fn;
            for (int hop = 0; hop < 3; hop++)
            {
                if (p[0] == 0xE9) { p = p + 5 + *(int*)(p + 1); continue; }
                if (p[0] == 0xFF && p[1] == 0x25) { p = *(byte**)(p + 6 + *(int*)(p + 2)); continue; }
                break;
            }
            int i = 0;
            if (p[0] == 0xB8 && p[5] == 0xC3) return *(int*)(p + 1);
            if (p[i] == 0x48) i++;
            byte op = p[i];
            if (op == 0x0F && (p[i + 1] == 0xB6 || p[i + 1] == 0xB7)) { i += 2; return Disp(p, i); }
            if (op == 0x8B || op == 0x8D) { i++; return Disp(p, i); }
            return -1;
        }

        private static int Disp(byte* p, int i)
        {
            byte modrm = p[i];
            int mod = modrm >> 6, rm = modrm & 7;
            if (rm != 1) return -1;
            if (mod == 0) return 0;
            if (mod == 1) return (sbyte)p[i + 1];
            if (mod == 2) return *(int*)(p + i + 1);
            return -1;
        }
    }
}
