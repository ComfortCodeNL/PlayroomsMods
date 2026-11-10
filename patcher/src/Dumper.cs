using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Metadata.Tables;
using BepInEx.Logging;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Runtime;

namespace PlayroomsPatcher
{
    internal sealed unsafe class Dumper
    {
        private readonly ManualLogSource _log;
        private readonly Dictionary<IntPtr, TypeDefinition> _types = new Dictionary<IntPtr, TypeDefinition>();
        private readonly Dictionary<IntPtr, MethodDefinition> _methods = new Dictionary<IntPtr, MethodDefinition>();
        private readonly Dictionary<ModuleDefinition, Injected> _injected = new Dictionary<ModuleDefinition, Injected>();
        private readonly Dictionary<(IntPtr, ModuleDefinition), TypeSignature> _sigs = new Dictionary<(IntPtr, ModuleDefinition), TypeSignature>();
        private readonly List<(IntPtr klass, TypeDefinition td)> _order = new List<(IntPtr, TypeDefinition)>();
        private Pe _pe;

        public int Skipped { get; private set; }
        public int TypeCount => _types.Count;
        public int MethodCount => _methods.Count;
        public readonly List<string> Problems = new List<string>();

        private sealed class Injected
        {
            public IMethodDefOrRef Token, Address, FieldOffset;
        }

        public Dumper(ManualLogSource log) { _log = log; }

        public List<AssemblyDefinition> Dump()
        {
            Rt.Init();
            _pe = new Pe(GetModuleHandleW("GameAssembly.dll"));

            var corlibImage = IL2CPP.il2cpp_get_corlib();
            var images = new List<IntPtr>();
            uint count = 0;
            var asms = (IntPtr*)IL2CPP.il2cpp_domain_get_assemblies(IL2CPP.il2cpp_domain_get(), ref count);
            for (int i = 0; i < count; i++) images.Add(IL2CPP.il2cpp_assembly_get_image(asms[i]));
            images.Remove(corlibImage);
            images.Insert(0, corlibImage);

            // Assemblies
            var modules = new Dictionary<IntPtr, ModuleDefinition>();
            AssemblyDefinition corlib = null;
            var resolver = new MemoryResolver();
            var result = new List<AssemblyDefinition>();
            foreach (var image in images)
            {
                string file = Rt.Str(IL2CPP.il2cpp_image_get_name(image)) ?? "Unknown.dll";
                string name = file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? file.Substring(0, file.Length - 4) : file;
                var asm = new AssemblyDefinition(name, new Version(0, 0, 0, 0));
                var module = new ModuleDefinition(name + ".dll", corlib == null ? null : new AssemblyReference(corlib));
                asm.Modules.Add(module);
                if (corlib == null) corlib = asm;
                module.MetadataResolver = new DefaultMetadataResolver(resolver);
                resolver.Add(asm);
                modules[image] = module;
                result.Add(asm);
            }

            // Types
            foreach (var image in images)
            {
                var module = modules[image];
                uint n = (uint)IL2CPP.il2cpp_image_get_class_count(image);
                for (uint i = 0; i < n; i++)
                {
                    var klass = IL2CPP.il2cpp_image_get_class(image, i);
                    if (klass == IntPtr.Zero) continue;
                    string tn = Rt.Str(IL2CPP.il2cpp_class_get_name(klass));
                    if (tn == "<Module>") continue;
                    var td = new TypeDefinition(Rt.Str(IL2CPP.il2cpp_class_get_namespace(klass)) ?? "", tn, (TypeAttributes)IL2CPP.il2cpp_class_get_flags(klass));
                    _types[klass] = td;
                    _order.Add((klass, td));
                }
            }
            foreach (var (klass, td) in _order)
            {
                var decl = IL2CPP.il2cpp_class_get_declaring_type(klass);
                if (decl != IntPtr.Zero && _types.TryGetValue(decl, out var outer)) { td.Namespace = null; outer.NestedTypes.Add(td); }
                else modules[IL2CPP.il2cpp_class_get_image(klass)].TopLevelTypes.Add(td);
            }
            foreach (var m in modules.Values) _injected[m] = Inject(m, corlib);

            // Generic parameters
            foreach (var (klass, td) in _order) Guard(td, () => TypeGenerics(klass, td));
            foreach (var (klass, td) in _order) Guard(td, () => Hierarchy(klass, td));
            foreach (var (klass, td) in _order) Guard(td, () => Members(klass, td));
            return result;
        }

        private void Guard(TypeDefinition td, Action a)
        {
            try { a(); }
            catch (Exception e) { Problem($"{td.FullName}: {e.Message}"); }
        }

        private void Problem(string text)
        {
            Skipped++;
            if (Problems.Count < 400) Problems.Add(text);
        }

        // Hierarchy

        private void TypeGenerics(IntPtr klass, TypeDefinition td)
        {
            if (!IL2CPP.il2cpp_class_is_generic(klass)) return;
            var obj = Rt.TypeObject(IL2CPP.il2cpp_class_get_type(klass));
            foreach (var p in Rt.Items(Rt.Call(obj, "GetGenericArguments")))
                td.GenericParameters.Add(new GenericParameter(Rt.ManagedString(Rt.Call(p, "get_Name")) ?? "T",
                    (GenericParameterAttributes)Rt.CallInt(p, "get_GenericParameterAttributes", 0)));
        }

        private void Hierarchy(IntPtr klass, TypeDefinition td)
        {
            var module = td.DeclaringModule;
            var parent = IL2CPP.il2cpp_class_get_parent(klass);
            if (parent != IntPtr.Zero) td.BaseType = DefOrRef(Sig(IL2CPP.il2cpp_class_get_type(parent), module));
            foreach (var i in Rt.Iterate(klass, IL2CPP.il2cpp_class_get_interfaces))
                td.Interfaces.Add(new InterfaceImplementation(DefOrRef(Sig(IL2CPP.il2cpp_class_get_type(i), module))));
            if (td.GenericParameters.Count > 0) Constraints(Rt.TypeObject(IL2CPP.il2cpp_class_get_type(klass)), td.GenericParameters, module);
        }

        private void Constraints(IntPtr owner, IList<GenericParameter> parameters, ModuleDefinition module)
        {
            var args = Rt.Items(Rt.Call(owner, "GetGenericArguments"));
            for (int i = 0; i < args.Count && i < parameters.Count; i++)
                foreach (var c in Rt.TypeArray(args[i], "GetGenericParameterConstraints"))
                    if (c != IntPtr.Zero) parameters[i].Constraints.Add(new GenericParameterConstraint(DefOrRef(Sig(c, module))));
        }

        // Members

        private void Members(IntPtr klass, TypeDefinition td)
        {
            var module = td.DeclaringModule;
            var inj = _injected[module];
            bool valueType = IL2CPP.il2cpp_class_is_valuetype(klass);

            foreach (var f in Rt.Iterate(klass, IL2CPP.il2cpp_class_get_fields))
            {
                try
                {
                    var attrs = (FieldAttributes)IL2CPP.il2cpp_field_get_flags(f);
                    var ft = IL2CPP.il2cpp_field_get_type(f);
                    var fd = new FieldDefinition(Rt.Str(IL2CPP.il2cpp_field_get_name(f)), attrs, new FieldSignature(Sig(ft, module)));
                    if ((attrs & FieldAttributes.Literal) != 0) fd.Constant = Literal(f, ft);
                    if ((attrs & FieldAttributes.Static) == 0)
                    {
                        int off = (int)IL2CPP.il2cpp_field_get_offset(f);
                        if (valueType && off > 0) off -= 2 * IntPtr.Size;
                        fd.CustomAttributes.Add(Attr(module, inj.FieldOffset, ("Offset", $"0x{off:X}")));
                    }
                    td.Fields.Add(fd);
                }
                catch (Exception e) { Problem($"{td.FullName} field: {e.Message}"); }
            }

            foreach (var m in Rt.Iterate(klass, IL2CPP.il2cpp_class_get_methods))
            {
                try { td.Methods.Add(Method(klass, m, module, inj)); }
                catch (Exception e) { Problem($"{td.FullName}.{Rt.Str(IL2CPP.il2cpp_method_get_name(m))}: {e.Message}"); }
            }

            foreach (var p in Rt.Iterate(klass, IL2CPP.il2cpp_class_get_properties))
            {
                try
                {
                    var get = IL2CPP.il2cpp_property_get_get_method(p);
                    var set = IL2CPP.il2cpp_property_get_set_method(p);
                    _methods.TryGetValue(get, out var gm);
                    _methods.TryGetValue(set, out var sm);
                    var acc = gm ?? sm;
                    if (acc == null) continue;
                    var ps = acc.Signature.ParameterTypes.ToList();
                    TypeSignature type;
                    if (gm != null) type = gm.Signature.ReturnType;
                    else { type = ps[ps.Count - 1]; ps.RemoveAt(ps.Count - 1); }
                    var sig = acc.IsStatic ? PropertySignature.CreateStatic(type, ps) : PropertySignature.CreateInstance(type, ps);
                    var pd = new PropertyDefinition(Rt.Str(IL2CPP.il2cpp_property_get_name(p)), (PropertyAttributes)IL2CPP.il2cpp_property_get_flags(p), sig);
                    if (gm != null) pd.Semantics.Add(new MethodSemantics(gm, MethodSemanticsAttributes.Getter));
                    if (sm != null) pd.Semantics.Add(new MethodSemantics(sm, MethodSemanticsAttributes.Setter));
                    td.Properties.Add(pd);
                }
                catch (Exception e) { Problem($"{td.FullName} property: {e.Message}"); }
            }
        }

        private MethodDefinition Method(IntPtr klass, IntPtr m, ModuleDefinition module, Injected inj)
        {
            uint implFlags = 0;
            var attrs = (MethodAttributes)IL2CPP.il2cpp_method_get_flags(m, ref implFlags);
            bool isStatic = (attrs & MethodAttributes.Static) != 0;

            var md = new MethodDefinition(Rt.Str(IL2CPP.il2cpp_method_get_name(m)), attrs, null) { ImplAttributes = (MethodImplAttributes)implFlags };
            int generic = 0;
            IntPtr methodObj = IntPtr.Zero;
            if (IL2CPP.il2cpp_method_is_generic(m))
            {
                methodObj = IL2CPP.il2cpp_method_get_object(m, klass);
                foreach (var p in Rt.Items(Rt.Call(methodObj, "GetGenericArguments")))
                {
                    md.GenericParameters.Add(new GenericParameter(Rt.ManagedString(Rt.Call(p, "get_Name")) ?? "T",
                        (GenericParameterAttributes)Rt.CallInt(p, "get_GenericParameterAttributes", 0)));
                    generic++;
                }
            }

            var ret = Sig(IL2CPP.il2cpp_method_get_return_type(m), module);
            uint pc = IL2CPP.il2cpp_method_get_param_count(m);
            var ps = new List<TypeSignature>();
            for (uint i = 0; i < pc; i++) ps.Add(Sig(IL2CPP.il2cpp_method_get_param(m, i), module));
            var sig = isStatic ? MethodSignature.CreateStatic(ret, ps) : MethodSignature.CreateInstance(ret, ps);
            if (generic > 0) { sig.GenericParameterCount = generic; sig.IsGeneric = true; }
            md.Signature = sig;
            for (uint i = 0; i < pc; i++)
                md.ParameterDefinitions.Add(new ParameterDefinition((ushort)(i + 1), Rt.Str(IL2CPP.il2cpp_method_get_param_name(m, i)) ?? $"arg{i}", 0));
            if (generic > 0) Constraints(methodObj, md.GenericParameters, module);

            md.CustomAttributes.Add(Attr(module, inj.Token, ("Token", $"0x{IL2CPP.il2cpp_method_get_token(m):X}")));
            var ptr = UnityVersionHandler.Wrap((Il2CppMethodInfo*)m).MethodPointer;
            if (ptr != IntPtr.Zero && _pe.Contains(ptr))
            {
                long rva = (long)ptr - (long)_pe.Base;
                var fields = new List<(string, string)> { ("RVA", $"0x{rva:X}") };
                if (_pe.FileOffset(rva, out long fo)) fields.Add(("Offset", $"0x{fo:X}"));
                md.CustomAttributes.Add(Attr(module, inj.Address, fields.ToArray()));
            }
            _methods[m] = md;
            return md;
        }

        private Constant Literal(IntPtr field, IntPtr type)
        {
            int kind = IL2CPP.il2cpp_type_get_type(type);
            if (kind == Rt.TValueType)
            {
                var k = IL2CPP.il2cpp_class_from_type(type);
                if (!IL2CPP.il2cpp_class_is_enum(k)) return null;
                kind = IL2CPP.il2cpp_type_get_type(IL2CPP.il2cpp_class_get_type(IL2CPP.il2cpp_class_enum_basetype(k)));
            }
            ulong raw = 0;
            IL2CPP.il2cpp_field_static_get_value(field, &raw);
            byte* b = (byte*)&raw;
            switch (kind)
            {
                case Rt.TBool: return Constant.FromValue(*b != 0);
                case Rt.TChar: return Constant.FromValue(*(char*)b);
                case Rt.TI1: return Constant.FromValue(*(sbyte*)b);
                case Rt.TU1: return Constant.FromValue(*b);
                case Rt.TI2: return Constant.FromValue(*(short*)b);
                case Rt.TU2: return Constant.FromValue(*(ushort*)b);
                case Rt.TI4: return Constant.FromValue(*(int*)b);
                case Rt.TU4: return Constant.FromValue(*(uint*)b);
                case Rt.TI8: return Constant.FromValue(*(long*)b);
                case Rt.TU8: return Constant.FromValue(*(ulong*)b);
                case Rt.TR4: return Constant.FromValue(*(float*)b);
                case Rt.TR8: return Constant.FromValue(*(double*)b);
                case Rt.TString:
                    var s = Rt.ManagedString((IntPtr)raw);
                    return s == null ? Constant.FromNull() : Constant.FromValue(s);
                default: return null;
            }
        }

        // Signatures

        private TypeSignature Sig(IntPtr type, ModuleDefinition module)
        {
            if (type == IntPtr.Zero) return module.CorLibTypeFactory.Object;
            if (_sigs.TryGetValue((type, module), out var cached)) return cached;
            var s = BuildSig(type, module);
            _sigs[(type, module)] = s;
            return s;
        }

        private TypeSignature BuildSig(IntPtr type, ModuleDefinition module)
        {
            var f = module.CorLibTypeFactory;
            if (IL2CPP.il2cpp_type_is_byref(type))
            {
                var inner = Rt.TypeOf(Rt.Call(Rt.TypeObject(type), "GetElementType"));
                return new ByReferenceTypeSignature(inner != IntPtr.Zero ? Sig(inner, module) : ByValue(type, module));
            }
            return ByValue(type, module);
        }

        private TypeSignature ByValue(IntPtr type, ModuleDefinition module)
        {
            var f = module.CorLibTypeFactory;
            int kind = IL2CPP.il2cpp_type_get_type(type);
            switch (kind)
            {
                case Rt.TVoid: return f.Void;
                case Rt.TBool: return f.Boolean;
                case Rt.TChar: return f.Char;
                case Rt.TI1: return f.SByte;
                case Rt.TU1: return f.Byte;
                case Rt.TI2: return f.Int16;
                case Rt.TU2: return f.UInt16;
                case Rt.TI4: return f.Int32;
                case Rt.TU4: return f.UInt32;
                case Rt.TI8: return f.Int64;
                case Rt.TU8: return f.UInt64;
                case Rt.TR4: return f.Single;
                case Rt.TR8: return f.Double;
                case Rt.TString: return f.String;
                case Rt.TObject: return f.Object;
                case Rt.TI: return f.IntPtr;
                case Rt.TU: return f.UIntPtr;
                case Rt.TTypedByRef: return f.TypedReference;
                case Rt.TFnPtr: return f.IntPtr;
                case Rt.TClass:
                case Rt.TValueType:
                {
                    var k = IL2CPP.il2cpp_class_from_type(type);
                    if (!_types.TryGetValue(k, out var td)) return f.Object;
                    return new TypeDefOrRefSignature(Ref(td, module), kind == Rt.TValueType);
                }
                case Rt.TGenericInst:
                {
                    var obj = Rt.TypeObject(type);
                    var def = Rt.TypeOf(Rt.Call(obj, "GetGenericTypeDefinition"));
                    var k = def != IntPtr.Zero ? IL2CPP.il2cpp_class_from_type(def) : IntPtr.Zero;
                    if (k == IntPtr.Zero || !_types.TryGetValue(k, out var td)) return f.Object;
                    var args = Rt.TypeArray(obj, "GetGenericArguments").Select(a => Sig(a, module)).ToArray();
                    return new GenericInstanceTypeSignature(Ref(td, module), IL2CPP.il2cpp_class_is_valuetype(k), args);
                }
                case Rt.TSzArray:
                    return new SzArrayTypeSignature(Sig(Rt.TypeOf(Rt.Call(Rt.TypeObject(type), "GetElementType")), module));
                case Rt.TArray:
                {
                    var obj = Rt.TypeObject(type);
                    return new ArrayTypeSignature(Sig(Rt.TypeOf(Rt.Call(obj, "GetElementType")), module), Math.Max(1, Rt.CallInt(obj, "GetArrayRank", 1)));
                }
                case Rt.TPtr:
                    return new PointerTypeSignature(Sig(Rt.TypeOf(Rt.Call(Rt.TypeObject(type), "GetElementType")), module));
                case Rt.TVar:
                case Rt.TMVar:
                {
                    int pos = Rt.CallInt(Rt.TypeObject(type), "get_GenericParameterPosition", 0);
                    return new GenericParameterSignature(module, kind == Rt.TVar ? GenericParameterType.Type : GenericParameterType.Method, Math.Max(0, pos));
                }
                default:
                    Problem($"unknown type kind 0x{kind:X}");
                    return f.Object;
            }
        }

        private static ITypeDefOrRef Ref(TypeDefinition td, ModuleDefinition module) =>
            td.DeclaringModule == module ? td : module.DefaultImporter.ImportType(td);

        private static ITypeDefOrRef DefOrRef(TypeSignature sig) =>
            sig is TypeDefOrRefSignature tdr ? tdr.Type : sig.ToTypeDefOrRef();

        // Marker attributes

        private Injected Inject(ModuleDefinition module, AssemblyDefinition corlib)
        {
            var attrBase = corlib.ManifestModule.TopLevelTypes.FirstOrDefault(t => t.Namespace == "System" && t.Name == "Attribute");
            ITypeDefOrRef baseRef = attrBase == null ? null : Ref(attrBase, module);
            IMethodDefOrRef Make(string name, params string[] fields)
            {
                var t = new TypeDefinition("Cpp2ILInjected", name, TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit, baseRef);
                foreach (var f in fields) t.Fields.Add(new FieldDefinition(f, FieldAttributes.Public, module.CorLibTypeFactory.String));
                var ctor = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RuntimeSpecialName,
                    MethodSignature.CreateInstance(module.CorLibTypeFactory.Void));
                t.Methods.Add(ctor);
                module.TopLevelTypes.Add(t);
                return ctor;
            }
            return new Injected
            {
                Token = Make("TokenAttribute", "Token"),
                Address = Make("AddressAttribute", "RVA", "Offset"),
                FieldOffset = Make("FieldOffsetAttribute", "Offset"),
            };
        }

        private static CustomAttribute Attr(ModuleDefinition module, IMethodDefOrRef ctor, params (string name, string value)[] fields)
        {
            var str = module.CorLibTypeFactory.String;
            var named = fields.Select(f => new CustomAttributeNamedArgument(CustomAttributeArgumentMemberType.Field, f.name, str, new CustomAttributeArgument(str, f.value)));
            return new CustomAttribute((ICustomAttributeType)ctor, new CustomAttributeSignature(Array.Empty<CustomAttributeArgument>(), named));
        }

        [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string name);
    }

    internal sealed class MemoryResolver : IAssemblyResolver
    {
        private readonly Dictionary<string, AssemblyDefinition> _byName = new Dictionary<string, AssemblyDefinition>(StringComparer.OrdinalIgnoreCase);
        public void Add(AssemblyDefinition a) => _byName[a.Name.ToString()] = a;
        public AssemblyDefinition Resolve(AssemblyDescriptor assembly) => assembly != null && _byName.TryGetValue(assembly.Name.ToString(), out var a) ? a : null;
        public void AddToCache(AssemblyDescriptor descriptor, AssemblyDefinition definition) => _byName[descriptor.Name.ToString()] = definition;
        public bool RemoveFromCache(AssemblyDescriptor descriptor) => _byName.Remove(descriptor.Name.ToString());
        public bool HasCached(AssemblyDescriptor descriptor) => _byName.ContainsKey(descriptor.Name.ToString());
        public void ClearCache() => _byName.Clear();
    }

    internal sealed unsafe class Pe
    {
        public readonly IntPtr Base;
        private readonly long _size;
        private readonly List<(uint va, uint vsize, uint raw, uint rawSize)> _sections = new List<(uint, uint, uint, uint)>();

        public Pe(IntPtr module)
        {
            Base = module;
            if (module == IntPtr.Zero) return;
            byte* b = (byte*)module;
            int pe = *(int*)(b + 0x3C);
            int sections = *(ushort*)(b + pe + 6);
            int optSize = *(ushort*)(b + pe + 20);
            _size = *(uint*)(b + pe + 24 + 56);
            byte* sh = b + pe + 24 + optSize;
            for (int i = 0; i < sections; i++, sh += 40)
                _sections.Add((*(uint*)(sh + 12), *(uint*)(sh + 8), *(uint*)(sh + 20), *(uint*)(sh + 16)));
        }

        public bool Contains(IntPtr p) => Base != IntPtr.Zero && (long)p >= (long)Base && (long)p < (long)Base + _size;

        public bool FileOffset(long rva, out long offset)
        {
            foreach (var s in _sections)
                if (rva >= s.va && rva < s.va + Math.Max(s.vsize, s.rawSize)) { offset = rva - s.va + s.raw; return true; }
            offset = 0;
            return false;
        }
    }
}
