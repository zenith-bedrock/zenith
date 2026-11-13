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

    [Fact]
    public void Gameplay_reaches_the_client_only_through_ports_not_session_or_protocol()
    {
        AssertNoReferences(
            gameplayType => IsInNamespace(gameplayType, "Zenith.Gameplay"),
            referencedType => IsInAnyNamespace(referencedType, "Zenith.Session", "Zenith.Protocol"));
    }

    /// <summary>
    /// Single-writer rule made executable: the pose, vitals and lifecycle scalars on <c>Player</c> keep public setters (the class is
    /// a plain state holder), so which code may write each one is pinned here. Adding a writer means adding it to this table on
    /// purpose; the review question is then "does this type own that state?". Tests are not scanned: they arrange state freely.
    /// </summary>
    [Fact]
    public void Player_state_is_written_only_by_its_owners()
    {
        const string Movement = "Zenith.Gameplay.Survival.MovementSystem";
        const string Hunger = "Zenith.Gameplay.Survival.HungerSystem";
        const string Minecart = "Zenith.Gameplay.Entities.MinecartSystem";
        const string Login = "Zenith.Session.Handler.LoginSessionHandler";
        const string InGame = "Zenith.Session.Handler.InGameSessionHandler";
        const string PlayerType = "Zenith.Player.Player";

        var owners = new Dictionary<string, string[]>
        {
            // Pose: MovementSystem applies client input on the tick; a mounted rider mirrors its vehicle; login places the player.
            ["PositionX"] = [Movement, Minecart, Login],
            ["PositionY"] = [Movement, Minecart, Login],
            ["PositionZ"] = [Movement, Minecart, Login],
            ["Pitch"] = [Movement, Login],
            ["Yaw"] = [Movement, Login],
            ["HeadYaw"] = [Movement, Login],
            ["IsOnGround"] = [Movement],
            ["IsSneaking"] = [Movement, PlayerType],
            ["IsSprinting"] = [Movement, PlayerType],
            ["FallPeakY"] = [Movement, PlayerType],
            // Vitals: HungerSystem owns the food model; mining and damage are exhaustion sources it prices.
            ["Hunger"] = [Hunger, PlayerType],
            ["Saturation"] = [Hunger, PlayerType],
            ["Exhaustion"] = [Hunger, "Zenith.Gameplay.Survival.PlayerDamage", "Zenith.Gameplay.WorldInteraction.BlockEditSystem", PlayerType],
            // Cross-system signals with exactly one producer.
            ["RidingEntityId"] = [Minecart],
            ["LastVillagerAttack"] = [PlayerType, "Zenith.Gameplay.Entities.VillagerSystem"],
            // Hotbar selection is the documented rule-6 exception (ADR §80): an atomic scalar the handler writes directly.
            ["SelectedHotbarSlot"] = [InGame],
            // Connection data owned by the session boundary.
            ["SkinRgba"] = [Login, InGame],
            ["SkinWidth"] = [Login, InGame],
            ["SkinHeight"] = [Login, InGame],
            // Connection-lifecycle gates flipped by the session state machine (ADR §97): handler enable/disable and close.
            ["IsInGame"] = [InGame, "Zenith.Session.NetworkSession"],
            ["IsSpawning"] = [InGame, "Zenith.Session.Handler.SpawnResponseSessionHandler"],
        };

        var playerType = typeof(Zenith.Player.Player);
        var tracked = owners.Keys.ToHashSet();
        var untracked = playerType.GetProperties()
            .Where(p => p.SetMethod is { IsPublic: true } && !tracked.Contains(p.Name))
            .Select(p => p.Name)
            .ToList();
        Assert.True(untracked.Count == 0,
            "Player has public setters with no declared owner (add them to the table): " + string.Join(", ", untracked));

        var violations = new List<string>();
        foreach (var type in typeof(RecipeRegistry).Assembly.GetTypes())
        {
            var outermost = type;
            while (outermost.DeclaringType is { } parent) outermost = parent;

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                                   BindingFlags.Static | BindingFlags.DeclaredOnly).Cast<MethodBase>()
                         .Concat(type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                                      BindingFlags.Static)))
            {
                foreach (var callee in EnumerateCalledMethods(method))
                {
                    if (callee.DeclaringType != playerType || !callee.Name.StartsWith("set_", StringComparison.Ordinal))
                        continue;
                    var property = callee.Name["set_".Length..];
                    if (owners.TryGetValue(property, out var allowed) && !allowed.Contains(outermost.FullName))
                        violations.Add($"{outermost.FullName}.{method.Name} writes Player.{property}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            "Player state written outside its owners:\n" + string.Join("\n", violations.Distinct().OrderBy(x => x)));
    }

    private static IEnumerable<MethodBase> EnumerateCalledMethods(MethodBase method)
    {
        var body = method.GetMethodBody();
        if (body?.GetILAsByteArray() is not { } il)
            yield break;

        for (var offset = 0; offset < il.Length;)
        {
            var opcode = ReadOpCode(il, ref offset);
            var operandOffset = offset;
            OperandSize(opcode.OperandType, il, ref offset);
            if (opcode.OperandType != OperandType.InlineMethod)
                continue;

            MethodBase? callee;
            try
            {
                callee = method.Module.ResolveMethod(BitConverter.ToInt32(il, operandOffset),
                    method.DeclaringType?.GetGenericArguments(),
                    method is MethodInfo info ? info.GetGenericArguments() : null);
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (callee is not null)
                yield return callee;
        }
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
