using System.Reflection;
using System.Reflection.Emit;
using Xunit;
using Zenith.Gameplay.Inventory;

namespace Zenith.Tests;

public class ArchitectureBoundaryTests
{
    private static readonly IReadOnlyDictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(opcode => opcode.Value);

    [Fact]
    public void Packets_do_not_reference_domain_or_protocol_layers()
    {
        AssertNoReferences(
            packetType => IsInNamespace(packetType, "Zenith.Packets"),
            referencedType => IsInAnyNamespace(
                referencedType,
                "Zenith.Event",
                "Zenith.Gameplay",
                "Zenith.Log",
                "Zenith.Player",
                "Zenith.Protocol",
                "Zenith.Server",
                "Zenith.Session",
                "Zenith.World"));
    }

    [Fact]
    public void Gameplay_does_not_reference_bedrock_wire_models()
    {
        AssertNoReferences(
            gameplayType => IsInNamespace(gameplayType, "Zenith.Gameplay"),
            referencedType => IsInNamespace(referencedType, "Zenith.Packets"));
    }

    private static void AssertNoReferences(
        Func<Type, bool> subject,
        Func<Type, bool> forbidden)
    {
        var violations = new List<string>();
        foreach (var type in typeof(RecipeRegistry).Assembly.GetTypes().Where(subject))
        {
            foreach (var reference in EnumerateReferences(type))
            {
                if (forbidden(reference.Type))
                    violations.Add($"{type.FullName} {reference.Member} → {reference.Type.FullName}");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Architecture boundary violations:\n" + string.Join("\n", violations.OrderBy(x => x)));
    }

    private static IEnumerable<(Type Type, string Member)> EnumerateReferences(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                  BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var field in type.GetFields(flags))
            foreach (var reference in Flatten(field.FieldType))
                yield return (reference, $"field {field.Name}");

        foreach (var property in type.GetProperties(flags))
            foreach (var reference in Flatten(property.PropertyType))
                yield return (reference, $"property {property.Name}");

        foreach (var method in type.GetMethods(flags).Cast<MethodBase>().Concat(type.GetConstructors(flags)))
        {
            if (method is MethodInfo methodInfo)
            {
                foreach (var reference in Flatten(methodInfo.ReturnType))
                    yield return (reference, $"signature {method.Name}");
            }
            foreach (var parameter in method.GetParameters())
                foreach (var reference in Flatten(parameter.ParameterType))
                    yield return (reference, $"signature {method.Name}");
            foreach (var reference in EnumerateBodyReferences(method))
                yield return (reference, $"body {method.Name}");
        }
    }

    private static IEnumerable<Type> EnumerateBodyReferences(MethodBase method)
    {
        var body = method.GetMethodBody();
        if (body?.GetILAsByteArray() is not { } il)
            yield break;

        for (var offset = 0; offset < il.Length;)
        {
            var opcode = ReadOpCode(il, ref offset);
            var operandOffset = offset;
            var operandSize = OperandSize(opcode.OperandType, il, ref offset);
            if (opcode.OperandType is not (OperandType.InlineField or OperandType.InlineMethod or
                OperandType.InlineTok or OperandType.InlineType) || operandSize != sizeof(int))
                continue;

            var token = BitConverter.ToInt32(il, operandOffset);
            MemberInfo? member;
            try
            {
                member = method.Module.ResolveMember(token, method.DeclaringType?.GetGenericArguments(),
                    method is MethodInfo info ? info.GetGenericArguments() : null);
            }
            catch (ArgumentException)
            {
                continue;
            }

            var referenced = member switch
            {
                Type referencedType => referencedType,
                _ => member?.DeclaringType
            };
            if (referenced is not null)
                yield return referenced;
        }
    }

    private static OpCode ReadOpCode(byte[] il, ref int offset)
    {
        short value = il[offset++];
        if (value == 0xfe)
            value = (short)(0xfe00 | il[offset++]);
        return OpCodesByValue[value];
    }

    private static int OperandSize(OperandType operandType, byte[] il, ref int offset)
    {
        var size = operandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineI or OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineMethod or
                OperandType.InlineSig or OperandType.InlineString or OperandType.InlineTok or OperandType.InlineType => 4,
            OperandType.ShortInlineR => 4,
            OperandType.InlineI8 or OperandType.InlineR => 8,
            OperandType.InlineSwitch => sizeof(int) + BitConverter.ToInt32(il, offset) * sizeof(int),
            _ => throw new InvalidOperationException($"Unknown IL operand type {operandType}.")
        };
        offset += size;
        return size;
    }

    private static IEnumerable<Type> Flatten(Type type)
    {
        if (type.HasElementType && type.GetElementType() is { } element)
            return Flatten(element);

        return type.IsGenericType
            ? [type, .. type.GetGenericArguments().SelectMany(Flatten)]
            : [type];
    }

    private static bool IsInAnyNamespace(Type type, params string[] namespaces) =>
        namespaces.Any(@namespace => IsInNamespace(type, @namespace));

    private static bool IsInNamespace(Type type, string @namespace) =>
        type.Namespace == @namespace || type.Namespace?.StartsWith(@namespace + ".", StringComparison.Ordinal) == true;
}
