using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: patch-target-audit <target.dll> <rules.tsv>");
    return 2;
}

using var pe = new PEReader(File.OpenRead(args[0]));
var reader = pe.GetMetadataReader();
var typeProvider = new TypeNameProvider();
var types = reader.TypeDefinitions.ToDictionary(
    handle => TypeDefFullName(reader, reader.GetTypeDefinition(handle)),
    handle => handle
);

int checkedRules = 0;
int optionalMisses = 0;
int failures = 0;

foreach (
    var (line, lineNumber) in File.ReadLines(args[1]).Select((line, index) => (line, index + 1))
)
{
    if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
        continue;
    string[] columns = line.Split('\t');
    if (columns.Length != 6)
    {
        Console.Error.WriteLine(
            $"invalid rule at line {lineNumber}: expected 6 tab-separated columns"
        );
        return 2;
    }

    var rule = new Rule(columns[0], columns[1], columns[2], columns[3], columns[4], columns[5]);
    checkedRules++;
    string? problem = CheckRule(rule, pe, reader, types, typeProvider);
    if (problem == null)
    {
        Console.WriteLine($"OK  {rule.Kind} {rule.TypeName}::{rule.MemberName}");
        continue;
    }

    if (rule.Severity == "optional")
    {
        optionalMisses++;
        Console.WriteLine($"OPTIONAL_MISSING  {problem}");
    }
    else
    {
        failures++;
        Console.WriteLine($"MISSING_PATCH_TARGET  {problem}");
    }
}

Console.WriteLine(
    $"--- audited {checkedRules} patch/reflection rules, "
        + $"{failures} required failures, {optionalMisses} optional degradations ---"
);
return failures == 0 ? 0 : 1;

static string? CheckRule(
    Rule rule,
    PEReader pe,
    MetadataReader reader,
    Dictionary<string, TypeDefinitionHandle> types,
    TypeNameProvider provider
)
{
    if (!types.TryGetValue(rule.TypeName, out var typeHandle))
        return $"{rule.TypeName} :: type not found";
    if (rule.Kind == "type")
        return null;

    var type = reader.GetTypeDefinition(typeHandle);
    switch (rule.Kind)
    {
        case "method":
        {
            var methods = FindMethods(reader, typeHandle, rule.MemberName, provider).ToList();
            return CheckMethodSelection(rule, methods);
        }
        case "property":
        {
            bool found = type.GetProperties()
                .Any(handle =>
                    reader.GetString(reader.GetPropertyDefinition(handle).Name) == rule.MemberName
                );
            return found ? null : $"{rule.TypeName}::{rule.MemberName} property not found";
        }
        case "field":
        {
            bool found = type.GetFields()
                .Any(handle =>
                    reader.GetString(reader.GetFieldDefinition(handle).Name) == rule.MemberName
                );
            return found ? null : $"{rule.TypeName}::{rule.MemberName} field not found";
        }
        case "il-call":
        {
            var methods = FindMethods(reader, typeHandle, rule.MemberName, provider).ToList();
            string? selectionProblem = CheckMethodSelection(rule, methods);
            if (selectionProblem != null)
                return selectionProblem;
            string[] requiredTarget = rule.Requires.Split("::", 2, StringSplitOptions.None);
            if (requiredTarget.Length != 2)
                return $"{rule.TypeName}::{rule.MemberName} invalid il-call requirement '{rule.Requires}'";
            bool found = methods
                .Where(method => ArityMatches(rule.Arity, method.Arity))
                .Any(method =>
                    EnumerateCalls(pe, reader, method.Handle, provider)
                        .Any(call =>
                            call.TypeName == requiredTarget[0]
                            && call.MethodName == requiredTarget[1]
                        )
                );
            return found
                ? null
                : $"{rule.TypeName}::{rule.MemberName} IL no longer calls {rule.Requires}";
        }
        default:
            return $"{rule.TypeName}::{rule.MemberName} unknown rule kind '{rule.Kind}'";
    }
}

static string? CheckMethodSelection(Rule rule, List<MethodCandidate> methods)
{
    if (rule.Arity == "bare")
    {
        if (methods.Count == 0)
            return $"{rule.TypeName}::{rule.MemberName} method not found";
        if (methods.Count != 1)
            return $"{rule.TypeName}::{rule.MemberName} bare lookup is ambiguous ({methods.Count} overloads)";
        return null;
    }

    if (!int.TryParse(rule.Arity, out int arity))
        return $"{rule.TypeName}::{rule.MemberName} invalid arity '{rule.Arity}'";
    return methods.Any(method => method.Arity == arity)
        ? null
        : $"{rule.TypeName}::{rule.MemberName}/{arity} method not found";
}

static bool ArityMatches(string selector, int arity) =>
    selector == "bare" || (int.TryParse(selector, out int expected) && expected == arity);

static IEnumerable<MethodCandidate> FindMethods(
    MetadataReader reader,
    TypeDefinitionHandle initial,
    string name,
    TypeNameProvider provider
)
{
    var pending = new Stack<TypeDefinitionHandle>();
    var visited = new HashSet<TypeDefinitionHandle>();
    var seenSignatures = new HashSet<string>();
    pending.Push(initial);
    while (pending.Count > 0)
    {
        var handle = pending.Pop();
        if (!visited.Add(handle))
            continue;
        var type = reader.GetTypeDefinition(handle);
        foreach (var methodHandle in type.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if (reader.GetString(method.Name) != name)
                continue;
            var signature = method.DecodeSignature(provider, null);
            string signatureKey =
                $"g{signature.GenericParameterCount}({string.Join(",", signature.ParameterTypes)}):{signature.ReturnType}";
            if (seenSignatures.Add(signatureKey))
                yield return new MethodCandidate(
                    methodHandle,
                    signature.ParameterTypes.Length,
                    signatureKey
                );
        }
        if (type.BaseType.Kind == HandleKind.TypeDefinition)
            pending.Push((TypeDefinitionHandle)type.BaseType);
    }
}

static IEnumerable<CalledMethod> EnumerateCalls(
    PEReader pe,
    MetadataReader reader,
    MethodDefinitionHandle methodHandle,
    TypeNameProvider provider
)
{
    var method = reader.GetMethodDefinition(methodHandle);
    if (method.RelativeVirtualAddress == 0)
        yield break;
    var body = pe.GetMethodBody(method.RelativeVirtualAddress);
    byte[] il = body.GetILBytes()?.ToArray() ?? Array.Empty<byte>();
    int offset = 0;
    while (offset < il.Length)
    {
        ushort value = il[offset++];
        if (value == 0xfe && offset < il.Length)
            value = (ushort)(0xfe00 | il[offset++]);
        if (!OpCodeLookup.Map.TryGetValue(value, out var opCode))
            yield break;

        if (opCode.OperandType == OperandType.InlineMethod && offset + 4 <= il.Length)
        {
            int token = BitConverter.ToInt32(il, offset);
            var called = ResolveCalledMethod(reader, token, provider);
            if (called != null)
                yield return called.Value;
        }

        int operandSize = OperandSize(opCode.OperandType, il, offset);
        if (operandSize < 0 || offset + operandSize > il.Length)
            yield break;
        offset += operandSize;
    }
}

static CalledMethod? ResolveCalledMethod(
    MetadataReader reader,
    int token,
    TypeNameProvider provider
)
{
    EntityHandle handle;
    try
    {
        handle = MetadataTokens.EntityHandle(token);
    }
    catch (ArgumentException)
    {
        return null;
    }

    switch (handle.Kind)
    {
        case HandleKind.MethodDefinition:
        {
            var method = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
            string typeName = TypeDefFullName(
                reader,
                reader.GetTypeDefinition(method.GetDeclaringType())
            );
            return new CalledMethod(typeName, reader.GetString(method.Name));
        }
        case HandleKind.MemberReference:
        {
            var member = reader.GetMemberReference((MemberReferenceHandle)handle);
            string? typeName = EntityTypeName(reader, member.Parent, provider);
            return typeName == null
                ? null
                : new CalledMethod(typeName, reader.GetString(member.Name));
        }
        case HandleKind.MethodSpecification:
        {
            var specification = reader.GetMethodSpecification((MethodSpecificationHandle)handle);
            return ResolveCalledMethod(
                reader,
                MetadataTokens.GetToken(specification.Method),
                provider
            );
        }
        default:
            return null;
    }
}

static string? EntityTypeName(MetadataReader reader, EntityHandle handle, TypeNameProvider provider)
{
    return handle.Kind switch
    {
        HandleKind.TypeDefinition => TypeDefFullName(
            reader,
            reader.GetTypeDefinition((TypeDefinitionHandle)handle)
        ),
        HandleKind.TypeReference => TypeRefFullName(reader, (TypeReferenceHandle)handle),
        HandleKind.TypeSpecification => reader
            .GetTypeSpecification((TypeSpecificationHandle)handle)
            .DecodeSignature(provider, null),
        _ => null,
    };
}

static int OperandSize(OperandType operandType, byte[] il, int offset)
{
    return operandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar =>
            1,
        OperandType.InlineVar => 2,
        OperandType.InlineBrTarget
        or OperandType.InlineField
        or OperandType.InlineI
        or OperandType.InlineMethod
        or OperandType.InlineSig
        or OperandType.InlineString
        or OperandType.InlineTok
        or OperandType.InlineType
        or OperandType.ShortInlineR => 4,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch when offset + 4 <= il.Length => 4
            + (BitConverter.ToInt32(il, offset) * 4),
        _ => -1,
    };
}

static string TypeDefFullName(MetadataReader reader, TypeDefinition type)
{
    string name = reader.GetString(type.Name);
    var declaring = type.GetDeclaringType();
    if (!declaring.IsNil)
        return TypeDefFullName(reader, reader.GetTypeDefinition(declaring)) + "+" + name;
    string ns = reader.GetString(type.Namespace);
    return ns.Length == 0 ? name : ns + "." + name;
}

static string TypeRefFullName(MetadataReader reader, TypeReferenceHandle handle)
{
    var type = reader.GetTypeReference(handle);
    string name = reader.GetString(type.Name);
    if (type.ResolutionScope.Kind == HandleKind.TypeReference)
        return TypeRefFullName(reader, (TypeReferenceHandle)type.ResolutionScope) + "+" + name;
    string ns = reader.GetString(type.Namespace);
    return ns.Length == 0 ? name : ns + "." + name;
}

readonly record struct Rule(
    string Severity,
    string Kind,
    string TypeName,
    string MemberName,
    string Arity,
    string Requires
);

readonly record struct MethodCandidate(MethodDefinitionHandle Handle, int Arity, string Signature);

readonly record struct CalledMethod(string TypeName, string MethodName);

static class OpCodeLookup
{
    public static readonly Dictionary<ushort, OpCode> Map = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode))
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(opCode => unchecked((ushort)opCode.Value));
}

sealed class TypeNameProvider : ISignatureTypeProvider<string, object?>
{
    public string GetPrimitiveType(PrimitiveTypeCode code) => code.ToString();

    public string GetTypeFromDefinition(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        byte rawTypeKind
    ) => DefName(reader, reader.GetTypeDefinition(handle));

    public string GetTypeFromReference(
        MetadataReader reader,
        TypeReferenceHandle handle,
        byte rawTypeKind
    ) => RefName(reader, handle);

    public string GetSZArrayType(string elementType) => elementType + "[]";

    public string GetArrayType(string elementType, ArrayShape shape) =>
        elementType + "[" + new string(',', shape.Rank - 1) + "]";

    public string GetByReferenceType(string elementType) => elementType + "&";

    public string GetPointerType(string elementType) => elementType + "*";

    public string GetGenericInstantiation(
        string genericType,
        System.Collections.Immutable.ImmutableArray<string> typeArguments
    ) => genericType;

    public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;

    public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;

    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) =>
        unmodifiedType;

    public string GetPinnedType(string elementType) => elementType;

    public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";

    public string GetTypeFromSpecification(
        MetadataReader reader,
        object? genericContext,
        TypeSpecificationHandle handle,
        byte rawTypeKind
    ) => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    private static string DefName(MetadataReader reader, TypeDefinition type)
    {
        string name = reader.GetString(type.Name);
        var declaring = type.GetDeclaringType();
        if (!declaring.IsNil)
            return DefName(reader, reader.GetTypeDefinition(declaring)) + "+" + name;
        string ns = reader.GetString(type.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    private static string RefName(MetadataReader reader, TypeReferenceHandle handle)
    {
        var type = reader.GetTypeReference(handle);
        string name = reader.GetString(type.Name);
        if (type.ResolutionScope.Kind == HandleKind.TypeReference)
            return RefName(reader, (TypeReferenceHandle)type.ResolutionScope) + "+" + name;
        string ns = reader.GetString(type.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }
}
