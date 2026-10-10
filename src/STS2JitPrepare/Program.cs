using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;

if (args.Length != 4)
    throw new ArgumentException("Usage: STS2JitPrepare <harmony|corelib> <input> <support.dll> <output>");
if (Path.GetFullPath(args[1]) == Path.GetFullPath(args[3]))
    throw new ArgumentException("The original assembly must remain unchanged.");
string[] expectedHashes = args[0] switch
{
    "harmony" => ["ef1898322c9f5c86dc1b0758b272a9c440823b4a41ca9a0b82a3aa6b3d206387"],
    // .NET 9.0.20 CoreLib from the ios-arm64 and iossimulator-arm64 runtime packs.
    "corelib" => ["2c04207e84e2b4fff5407d1687e1c85e56db71ad540a6aead1baf00b4a691089",
                  "24671033ec93244f5d3998e376dc00666f33ae3fd3e9f887537564978656f560"],
    _ => throw new ArgumentException("Unknown assembly adapter.")
};
using (var input = File.OpenRead(args[1]))
    if (!expectedHashes.Contains(Convert.ToHexString(SHA256.HashData(input)), StringComparer.OrdinalIgnoreCase))
        throw new NotSupportedException("Assembly checksum differs from the tested input; review the adapter before updating it.");

using var assembly = AssemblyDefinition.ReadAssembly(args[1]);
var module = assembly.MainModule;
MethodDefinition Method(string type, string name) => module.GetType(type).Methods.Single(m => m.Name == name);
ILProcessor Replace(MethodDefinition method)
{
    method.Body = new MethodBody(method);
    return method.Body.GetILProcessor();
}

if (args[0] == "corelib")
{
    var il = Replace(Method("System.Runtime.CompilerServices.RuntimeFeature", "get_IsDynamicCodeCompiled"));
    il.Emit(OpCodes.Call, Method("System.Runtime.CompilerServices.RuntimeFeature", "get_IsDynamicCodeSupported"));
    il.Emit(OpCodes.Ret);
}
else if (args[0] == "harmony")
{
    if (assembly.Name.Version != new Version(2, 4, 2, 0))
        throw new NotSupportedException("The iOS backend targets Harmony 2.4.2.0.");
    const string system = "MonoMod.Core.Platforms.Systems.MacOSSystem";
    var arm64 = (int)module.GetType("MonoMod.Utils.ArchitectureKind").Fields.Single(f => f.Name == "Arm64").Constant;
    var archIl = Replace(Method("MonoMod.Utils.PlatformDetection", "get_Architecture"));
    archIl.Emit(OpCodes.Ldc_I4, arm64);
    archIl.Emit(OpCodes.Ret);
    // Reuse Darwin's ARM64 ABI and region queries; executable writes stay in native code.
    var il = Replace(Method("MonoMod.Core.Platforms.PlatformTriple", "CreateCurrentSystem"));
    il.Emit(OpCodes.Newobj, Method(system, ".ctor"));
    il.Emit(OpCodes.Ret);
    il = Replace(Method(system, "get_NativeExceptionHelper"));
    il.Emit(OpCodes.Ldnull);
    il.Emit(OpCodes.Ret);
    using var support = AssemblyDefinition.ReadAssembly(args[2]);
    var patch = support.MainModule.GetType("STS2JitSupport.NativePatch").Methods.Single(m => m.Name == "PatchData");
    il = Replace(Method(system, "PatchData"));
    for (int i = 1; i <= 4; i++) il.Emit(OpCodes.Ldarg, i);
    il.Emit(OpCodes.Call, module.ImportReference(patch));
    il.Emit(OpCodes.Ret);

    var fix = support.MainModule.GetType("STS2JitSupport.SignatureFix").Methods.Single(m => m.Name == "PreserveReturnModifiers");
    var importer = module.GetType("MonoMod.Utils.MMReflectionImporter").Methods.Single(m =>
        m.Name == "_ImportReference" && m.Parameters[0].ParameterType.FullName == "System.Reflection.MethodBase");
    var setter = importer.Body.Instructions.Single(i => i.Operand is MethodReference called && called.Name == "set_ReturnType");
    var processor = importer.Body.GetILProcessor();
    var cursor = setter;
    void Append(Instruction instruction) { processor.InsertAfter(cursor, instruction); cursor = instruction; }
    Append(Instruction.Create(OpCodes.Ldarg_0));
    Append(Instruction.Create(OpCodes.Ldarg_1));
    Append(Instruction.Create(OpCodes.Ldloc, importer.Body.Variables.First(v => v.VariableType.FullName == "Mono.Cecil.MethodReference")));
    Append(Instruction.Create(OpCodes.Call, module.ImportReference(fix)));
}
else throw new ArgumentException("Unknown assembly adapter.");

string temporary = args[3] + "." + Guid.NewGuid().ToString("N") + ".tmp";
try
{
    assembly.Write(temporary);
    File.Move(temporary, args[3], overwrite: true);
}
finally { if (File.Exists(temporary)) File.Delete(temporary); }
Console.WriteLine($"Prepared {args[0]} for the iOS JIT runtime.");
