// MemberRef audit: enumerate every MemberRef in <consumer.dll> that resolves into
// assembly "sts2", then verify a matching MethodDef/FieldDef exists in <target sts2.dll>.
// Usage: audit <consumer.dll> <target-sts2.dll> [scopeAssemblyName=sts2]
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: audit <consumer.dll> <target-sts2.dll> [scopeName]");
    return 2;
}
string scopeName = args.Length > 2 ? args[2] : "sts2";

using var consumerPe = new PEReader(File.OpenRead(args[0]));
var c = consumerPe.GetMetadataReader();
using var targetPe = new PEReader(File.OpenRead(args[1]));
var t = targetPe.GetMetadataReader();

var provider = new SigStringProvider();

// ---- index target: full type name -> members --------------------------------
var targetTypes = new Dictionary<string, TargetType>();
foreach (var tdHandle in t.TypeDefinitions)
{
    var td = t.GetTypeDefinition(tdHandle);
    string full = Sig.TypeDefFullName(t, td);
    if (!targetTypes.TryGetValue(full, out var bucket))
    {
        bucket = new TargetType((td.Attributes & TypeAttributes.Interface) != 0);
        targetTypes[full] = bucket;
    }
    foreach (var mh in td.GetMethods())
    {
        var md = t.GetMethodDefinition(mh);
        var sig = md.DecodeSignature(provider, null);
        string key = t.GetString(md.Name) + "|" + Sig.Key(sig);
        bucket.Methods.Add(key);
        if ((md.Attributes & MethodAttributes.Abstract) != 0)
            bucket.RequiredInterfaceMethods.Add(key);
    }
    foreach (var fh in td.GetFields())
    {
        var fd = t.GetFieldDefinition(fh);
        bucket.Fields.Add(
            t.GetString(fd.Name) + "|" + Sig.Strip(fd.DecodeSignature(provider, null))
        );
    }
    foreach (var ih in td.GetInterfaceImplementations())
    {
        var implementation = t.GetInterfaceImplementation(ih);
        string? inherited = ReferencedTypeFullName(t, implementation.Interface, provider, out _);
        if (inherited != null)
            bucket.Interfaces.Add(inherited);
    }
}

// ---- walk consumer MemberRefs ------------------------------------------------
int total = 0,
    missing = 0;
foreach (var mrHandle in c.MemberReferences)
{
    var mr = c.GetMemberReference(mrHandle);
    string? typeName = ParentTypeFullName(c, mr.Parent, provider, out string? scope);
    if (typeName == null || scope != scopeName)
        continue;
    total++;

    string memberName = c.GetString(mr.Name);
    bool found;
    string detail;
    if (mr.GetKind() == MemberReferenceKind.Method)
    {
        var sig = mr.DecodeMethodSignature(provider, null);
        detail = Render(memberName, sig);
        found =
            targetTypes.TryGetValue(typeName, out var bucket)
            && bucket.Methods.Contains(memberName + "|" + Sig.Key(sig));
    }
    else
    {
        string fieldType = Sig.Strip(mr.DecodeFieldSignature(provider, null));
        detail = fieldType + " " + memberName;
        found =
            targetTypes.TryGetValue(typeName, out var bucket)
            && bucket.Fields.Contains(memberName + "|" + fieldType);
    }

    if (!found)
    {
        missing++;
        Console.WriteLine($"MISSING  {typeName} :: {detail}");
    }
}

// A class compiled against an older interface has no MemberRef for members that
// were added later. The runtime nevertheless validates every current abstract
// interface slot while building the class VTable, which is why this gap escaped
// the original audit and surfaced as TypeLoadException in issue #86.
int interfaceTypes = 0;
foreach (var tdHandle in c.TypeDefinitions)
{
    var td = c.GetTypeDefinition(tdHandle);
    foreach (var ih in td.GetInterfaceImplementations())
    {
        var implementation = c.GetInterfaceImplementation(ih);
        string? interfaceName = ReferencedTypeFullName(
            c,
            implementation.Interface,
            provider,
            out string? interfaceScope
        );
        if (interfaceName == null || interfaceScope != scopeName)
            continue;

        interfaceTypes++;
        string consumerType = Sig.TypeDefFullName(c, td);
        if (
            !targetTypes.TryGetValue(interfaceName, out var targetInterface)
            || !targetInterface.IsInterface
        )
        {
            missing++;
            Console.WriteLine($"MISSING_INTERFACE  {consumerType} :: {interfaceName}");
            continue;
        }

        var required = RequiredInterfaceMethods(interfaceName, targetTypes);
        var provided = BindableMethods(c, tdHandle, provider);
        foreach (string key in required.Order())
        {
            if (provided.Contains(key))
                continue;
            missing++;
            string detail = RenderKey(key);
            Console.WriteLine(
                $"MISSING_INTERFACE_SLOT  {consumerType} : {interfaceName} :: {detail}"
            );
        }
    }
}

Console.WriteLine(
    $"--- audited {total} {scopeName}-scoped MemberRefs and "
        + $"{interfaceTypes} implemented interfaces, {missing} missing ---"
);
return missing == 0 ? 0 : 1;

// ---- helpers -------------------------------------------------------------------
static string Render(string name, MethodSignature<string> s) =>
    $"{Sig.Strip(s.ReturnType)} {name}{(s.GenericParameterCount > 0 ? $"`{s.GenericParameterCount}" : "")}({string.Join(", ", s.ParameterTypes.Select(Sig.Strip))})";

static string RenderKey(string key)
{
    int separator = key.IndexOf('|');
    return separator < 0 ? key : key[..separator] + key[(separator + 1)..];
}

static HashSet<string> RequiredInterfaceMethods(
    string interfaceName,
    Dictionary<string, TargetType> targetTypes
)
{
    var required = new HashSet<string>();
    var pending = new Stack<string>();
    pending.Push(interfaceName);
    while (pending.Count > 0)
    {
        string current = pending.Pop();
        if (!targetTypes.TryGetValue(current, out var target))
            continue;
        required.UnionWith(target.RequiredInterfaceMethods);
        foreach (string inherited in target.Interfaces)
            pending.Push(inherited);
    }
    return required;
}

static HashSet<string> BindableMethods(
    MetadataReader reader,
    TypeDefinitionHandle classHandle,
    SigStringProvider provider
)
{
    var methods = new HashSet<string>();
    var visited = new HashSet<TypeDefinitionHandle>();
    var pending = new Stack<TypeDefinitionHandle>();
    pending.Push(classHandle);

    while (pending.Count > 0)
    {
        var currentHandle = pending.Pop();
        if (!visited.Add(currentHandle))
            continue;
        var current = reader.GetTypeDefinition(currentHandle);
        foreach (var mh in current.GetMethods())
        {
            var method = reader.GetMethodDefinition(mh);
            if (
                (method.Attributes & MethodAttributes.Virtual) == 0
                || (method.Attributes & MethodAttributes.Static) != 0
            )
                continue;
            var sig = method.DecodeSignature(provider, null);
            methods.Add(reader.GetString(method.Name) + "|" + Sig.Key(sig));
        }

        if (current.BaseType.Kind == HandleKind.TypeDefinition)
            pending.Push((TypeDefinitionHandle)current.BaseType);
    }
    return methods;
}

static string? ParentTypeFullName(
    MetadataReader r,
    EntityHandle parent,
    SigStringProvider provider,
    out string? scope
)
{
    scope = null;
    switch (parent.Kind)
    {
        case HandleKind.TypeReference:
            return Sig.TypeRefFullName(r, (TypeReferenceHandle)parent, out scope);
        case HandleKind.TypeSpecification:
        {
            var ts = r.GetTypeSpecification((TypeSpecificationHandle)parent);
            string decoded = ts.DecodeSignature(provider, null); // e.g. "Ns.Type`1@sts2<Boolean>"
            int at = decoded.IndexOf('@');
            if (at < 0)
                return null;
            string full = decoded[..at];
            int end = at + 1;
            while (
                end < decoded.Length
                && decoded[end] != '<'
                && decoded[end] != ','
                && decoded[end] != '>'
                && decoded[end] != '['
                && decoded[end] != '&'
            )
                end++;
            scope = decoded[(at + 1)..end];
            return full;
        }
        default:
            return null; // MethodDef / ModuleRef parents: not cross-assembly, skip
    }
}

static string? ReferencedTypeFullName(
    MetadataReader reader,
    EntityHandle handle,
    SigStringProvider provider,
    out string? scope
)
{
    switch (handle.Kind)
    {
        case HandleKind.TypeReference:
            return Sig.TypeRefFullName(reader, (TypeReferenceHandle)handle, out scope);
        case HandleKind.TypeSpecification:
            return ParentTypeFullName(reader, handle, provider, out scope);
        case HandleKind.TypeDefinition:
            scope = null;
            return Sig.TypeDefFullName(
                reader,
                reader.GetTypeDefinition((TypeDefinitionHandle)handle)
            );
        default:
            scope = null;
            return null;
    }
}

sealed class TargetType(bool isInterface)
{
    public bool IsInterface { get; } = isInterface;
    public HashSet<string> Methods { get; } = new();
    public HashSet<string> RequiredInterfaceMethods { get; } = new();
    public HashSet<string> Fields { get; } = new();
    public HashSet<string> Interfaces { get; } = new();
}

static class Sig
{
    public static string Key(MethodSignature<string> s) =>
        $"g{s.GenericParameterCount}({string.Join(",", s.ParameterTypes.Select(Strip))}):{Strip(s.ReturnType)}";

    // remove "@scope" suffixes for stable cross-side compare
    public static string Strip(string s)
    {
        int at;
        while ((at = s.IndexOf('@')) >= 0)
        {
            int end = at + 1;
            while (
                end < s.Length
                && s[end] != '<'
                && s[end] != ','
                && s[end] != '>'
                && s[end] != '['
                && s[end] != '&'
                && s[end] != '*'
                && s[end] != '+'
                && s[end] != ')'
            )
                end++;
            s = s[..at] + s[end..];
        }
        return s;
    }

    public static string TypeDefFullName(MetadataReader r, TypeDefinition td)
    {
        string name = r.GetString(td.Name);
        var declaring = td.GetDeclaringType();
        if (!declaring.IsNil)
            return TypeDefFullName(r, r.GetTypeDefinition(declaring)) + "+" + name;
        string ns = r.GetString(td.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    public static string TypeRefFullName(MetadataReader r, TypeReferenceHandle h, out string? scope)
    {
        var tr = r.GetTypeReference(h);
        string name = r.GetString(tr.Name);
        switch (tr.ResolutionScope.Kind)
        {
            case HandleKind.AssemblyReference:
                scope = r.GetString(
                    r.GetAssemblyReference((AssemblyReferenceHandle)tr.ResolutionScope).Name
                );
                string ns = r.GetString(tr.Namespace);
                return ns.Length == 0 ? name : ns + "." + name;
            case HandleKind.TypeReference: // nested type
                string outer = TypeRefFullName(r, (TypeReferenceHandle)tr.ResolutionScope, out scope);
                return outer + "+" + name;
            default:
                scope = null;
                return name;
        }
    }
}

// Renders types as strings. TypeRefs carry an "@scope" suffix so TypeSpec parents can
// recover their resolution scope; Sig.Strip removes those for comparisons.
class SigStringProvider : ISignatureTypeProvider<string, object?>
{
    public string GetPrimitiveType(PrimitiveTypeCode code) => code.ToString();

    public string GetTypeFromDefinition(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        byte rawTypeKind
    ) => Sig.TypeDefFullName(reader, reader.GetTypeDefinition(handle));

    public string GetTypeFromReference(
        MetadataReader reader,
        TypeReferenceHandle handle,
        byte rawTypeKind
    )
    {
        string full = Sig.TypeRefFullName(reader, handle, out string? scope);
        return scope is { Length: > 0 } ? full + "@" + scope : full;
    }

    public string GetSZArrayType(string elementType) => elementType + "[]";

    public string GetArrayType(string elementType, ArrayShape shape) =>
        elementType + "[" + new string(',', shape.Rank - 1) + "]";

    public string GetByReferenceType(string elementType) => elementType + "&";

    public string GetPointerType(string elementType) => elementType + "*";

    public string GetGenericInstantiation(
        string genericType,
        System.Collections.Immutable.ImmutableArray<string> typeArguments
    ) => genericType + "<" + string.Join(",", typeArguments) + ">";

    public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;

    public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;

    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) =>
        (isRequired ? "modreq(" : "modopt(") + modifier + ")" + unmodifiedType;

    public string GetPinnedType(string elementType) => elementType;

    public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";

    public string GetTypeFromSpecification(
        MetadataReader reader,
        object? genericContext,
        TypeSpecificationHandle handle,
        byte rawTypeKind
    ) => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
}
