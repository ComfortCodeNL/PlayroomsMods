using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Runtime;
using Il2CppInterop.Runtime.Runtime.VersionSpecific.Class;
using Il2CppInterop.Runtime.Runtime.VersionSpecific.Type;
using L = PlayroomsPatcher.ClassLayout;

namespace PlayroomsPatcher
{
    internal unsafe class ClassHandler : INativeClassStructHandler
    {
        public int Size() => L.Size;

        public INativeClassStruct CreateNewStruct(int vTableSlots)
        {
            int bytes = L.Size + vTableSlots * sizeof(VirtualInvokeData);
            var ptr = Marshal.AllocHGlobal(bytes);
            new Span<byte>((void*)ptr, bytes).Clear();
            Slots.Get(ptr, () => IntPtr.Zero);
            return new ClassWrapper(ptr);
        }

        public INativeClassStruct Wrap(Il2CppClass* classPointer) => classPointer == null ? null : new ClassWrapper((IntPtr)classPointer);

        public static void Install()
        {
            var f = typeof(UnityVersionHandler).GetField("classStructHandler", BindingFlags.Static | BindingFlags.NonPublic)
                    ?? throw new MissingFieldException("UnityVersionHandler.classStructHandler");
            f.SetValue(null, new ClassHandler());
        }
    }

    internal unsafe class ClassWrapper : INativeClassStruct
    {
        public ClassWrapper(IntPtr p) { Pointer = p; }
        public IntPtr Pointer { get; }
        private byte* B => (byte*)Pointer;
        private ref T At<T>(int off) where T : unmanaged => ref *(T*)(B + off);

        public Il2CppClass* ClassPointer => (Il2CppClass*)Pointer;
        public IntPtr VTable => Pointer + L.Size;

        public ref uint InstanceSize => ref At<uint>(L.InstanceSize);
        public ref ushort VtableCount => ref At<ushort>(L.VtableCount);
        public ref ushort InterfaceCount => ref At<ushort>(L.InterfacesCount);
        public ref ushort InterfaceOffsetsCount => ref At<ushort>(L.InterfaceOffsetsCount);
        public ref byte TypeHierarchyDepth => ref At<byte>(L.TypeHierarchyDepth);
        public ref int NativeSize => ref At<int>(L.NativeSize);
        public ref uint ActualSize => ref At<uint>(L.ActualSize);
        public ref ushort MethodCount => ref At<ushort>(L.MethodCount);
        public ref ushort FieldCount => ref At<ushort>(L.FieldCount);
        public ref Il2CppClassAttributes Flags => ref At<Il2CppClassAttributes>(L.Flags);

        public ref IntPtr Name => ref At<IntPtr>(L.Name);
        public ref IntPtr Namespace => ref At<IntPtr>(L.Namespace);
        public INativeTypeStruct ByValArg => UnityVersionHandler.Wrap((Il2CppTypeStruct*)(B + L.ByValArg));
        public INativeTypeStruct ThisArg => UnityVersionHandler.Wrap((Il2CppTypeStruct*)(B + L.ThisArg));

        public ref Il2CppImage* Image => ref At<Il2CppImageRef>(L.Image).P;
        public ref Il2CppClass* Parent => ref At<Il2CppClassRef>(L.Parent).P;
        public ref Il2CppClass* ElementClass => ref At<Il2CppClassRef>(L.ElementClass).P;
        public ref Il2CppClass* CastClass => ref At<Il2CppClassRef>(L.CastClass).P;
        public ref Il2CppClass* DeclaringType => ref At<Il2CppClassRef>(L.DeclaringType).P;
        public ref Il2CppClass* Class => ref At<Il2CppClassRef>(L.Klass).P;
        public ref Il2CppFieldInfo* Fields => ref At<Il2CppFieldRef>(L.Fields).P;
        public ref Il2CppMethodInfo** Methods => ref At<Il2CppMethodsRef>(L.Methods).P;
        public ref Il2CppRuntimeInterfaceOffsetPair* InterfaceOffsets => ref At<Il2CppOffsetsRef>(L.InterfaceOffsets).P;
        public ref Il2CppClass** TypeHierarchy => ref At<Il2CppClassesRef>(L.TypeHierarchy).P;

        // implemented interfaces: the leading entries of the offsets table (6000.6), or our own array for injected classes
        public ref Il2CppClass** ImplementedInterfaces
        {
            get => ref *(Il2CppClass***)Slots.Get(Pointer, BuildInterfaces);
        }

        private IntPtr BuildInterfaces()
        {
            int n = InterfaceCount;
            if (n == 0) return IntPtr.Zero;
            var offsets = (byte*)At<IntPtr>(L.InterfaceOffsets);
            if (offsets == null) return IntPtr.Zero;
            var arr = (IntPtr*)Marshal.AllocHGlobal(n * IntPtr.Size);
            for (int i = 0; i < n; i++) arr[i] = *(IntPtr*)(offsets + i * 16);
            return (IntPtr)arr;
        }

        public bool ValueType { get => ByValArg.ValueType && ThisArg.ValueType; set { } }
        public bool EnumType { get => Bit(L.Bitfield0, L.BitEnumType); set => SetBit(L.Bitfield0, L.BitEnumType, value); }
        public bool IsGeneric { get => Bit(L.Bitfield0, L.BitIsGeneric); set => SetBit(L.Bitfield0, L.BitIsGeneric, value); }
        public bool Initialized { get => Bit(L.Bitfield0, L.BitInitialized); set => SetBit(L.Bitfield0, L.BitInitialized, value); }
        public bool InitializedAndNoError { get => Bit(L.Bitfield0, L.BitInitializedAndNoError); set => SetBit(L.Bitfield0, L.BitInitializedAndNoError, value); }
        public bool SizeInited { get => Bit(L.Bitfield1, L.BitSizeInited); set => SetBit(L.Bitfield1, L.BitSizeInited, value); }
        public bool HasFinalize { get => Bit(L.Bitfield1, L.BitHasFinalize); set => SetBit(L.Bitfield1, L.BitHasFinalize, value); }
        public bool IsVtableInitialized { get => Bit(L.Bitfield1, L.BitVtableInitialized); set => SetBit(L.Bitfield1, L.BitVtableInitialized, value); }

        private bool Bit(int off, int bit) => (B[off] & (1 << bit)) != 0;
        private void SetBit(int off, int bit, bool v) { if (v) B[off] |= (byte)(1 << bit); else B[off] &= (byte)~(1 << bit); }

        private struct Il2CppImageRef { public Il2CppImage* P; }
        private struct Il2CppClassRef { public Il2CppClass* P; }
        private struct Il2CppClassesRef { public Il2CppClass** P; }
        private struct Il2CppFieldRef { public Il2CppFieldInfo* P; }
        private struct Il2CppMethodsRef { public Il2CppMethodInfo** P; }
        private struct Il2CppOffsetsRef { public Il2CppRuntimeInterfaceOffsetPair* P; }
    }

    // 6000.6 has no separate implemented-interfaces array: one pointer cell per class holds it
    internal static unsafe class Slots
    {
        private static readonly ConcurrentDictionary<IntPtr, IntPtr> _cells = new ConcurrentDictionary<IntPtr, IntPtr>();

        public static IntPtr Get(IntPtr klass, Func<IntPtr> first) => _cells.GetOrAdd(klass, _ =>
        {
            var cell = Marshal.AllocHGlobal(IntPtr.Size);
            *(IntPtr*)cell = first();
            return cell;
        });
    }
}
