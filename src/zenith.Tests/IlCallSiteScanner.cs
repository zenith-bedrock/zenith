using System.Reflection;
using System.Reflection.Emit;

namespace Zenith.Tests;

/// <summary>
/// Finds, in the compiled production assembly, every place that calls (or takes a pointer to) a given method. It reads the IL, so
/// comments and unreachable text never count, and lambdas, closures, local functions and async state machines are attributed to the
/// type that wrote them. It cannot see reflection or dynamic invocation; callers that need that guarantee must not use either.
/// </summary>
static class IlCallSiteScanner
{
    private static readonly IReadOnlyDictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(opcode => opcode.Value);

    /// <summary>One call: the outermost type that contains the calling code, the method it sits in, and the method called.</summary>
    public readonly record struct Site(Type Caller, string Method, MethodBase Target);

    /// <summary>Every call site in <paramref name="assembly"/> whose callee satisfies <paramref name="isTarget"/>.</summary>
    public static IReadOnlyList<Site> Find(Assembly assembly, Func<MethodBase, bool> isTarget)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                   BindingFlags.Static | BindingFlags.DeclaredOnly;
        var sites = new List<Site>();
        foreach (var type in assembly.GetTypes())
        {
            foreach (var method in type.GetMethods(flags).Cast<MethodBase>().Concat(type.GetConstructors(flags)))
            {
                foreach (var target in Callees(method))
                    if (isTarget(target))
                        sites.Add(new Site(Outermost(type), method.Name, target));
            }
        }

        return sites;
    }

    /// <summary>The type a compiler-generated type (closure, state machine) belongs to; the type itself otherwise.</summary>
    public static Type Outermost(Type type)
    {
        while (type.DeclaringType is { } outer) type = outer;
        return type;
    }

    private static IEnumerable<MethodBase> Callees(MethodBase method)
    {
        var body = method.GetMethodBody();
        if (body?.GetILAsByteArray() is not { } il) yield break;

        for (var offset = 0; offset < il.Length;)
        {
            var opcode = ReadOpCode(il, ref offset);
            var operandOffset = offset;
            offset += OperandSize(opcode.OperandType, il, offset);
            // call, callvirt, newobj, ldftn and ldvirtftn all name their method by metadata token.
            if (opcode.OperandType != OperandType.InlineMethod) continue;

            MethodBase? callee;
            try
            {
                callee = method.Module.ResolveMethod(
                    BitConverter.ToInt32(il, operandOffset),
                    method.DeclaringType?.GetGenericArguments(),
                    method is MethodInfo info && info.IsGenericMethod ? info.GetGenericArguments() : null);
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (callee is not null) yield return callee;
        }
    }

    private static OpCode ReadOpCode(byte[] il, ref int offset)
    {
        short value = il[offset++];
        if (value == 0xfe) value = (short)(0xfe00 | il[offset++]);
        return OpCodesByValue[value];
    }

    private static int OperandSize(OperandType operandType, byte[] il, int offset) => operandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI or OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineMethod or
            OperandType.InlineSig or OperandType.InlineString or OperandType.InlineTok or OperandType.InlineType or
            OperandType.ShortInlineR => 4,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => sizeof(int) + BitConverter.ToInt32(il, offset) * sizeof(int),
        _ => throw new InvalidOperationException($"Unknown IL operand type {operandType}.")
    };
}
