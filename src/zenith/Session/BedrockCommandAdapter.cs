using Zenith.Gameplay.Commands;
using Zenith.Packets;

namespace Zenith.Session;

/// <summary>Bedrock-only projection of the protocol-free command catalog.</summary>
static class BedrockCommandAdapter
{
    private const uint Valid = 0x100000;
    private const uint Enum = 0x200000;
    private const uint Int = 1;
    private const uint Target = 8;
    private const uint String = 58;

    /// <summary>The command list one player is told about: only what <paramref name="holds"/> says they may use.</summary>
    public static AvailableCommandsPacket CreateMetadata(CommandCatalog catalog, Func<string, bool> holds)
    {
        var packet = new AvailableCommandsPacket();
        foreach (var definition in catalog.Definitions)
        {
            if (!CommandCatalog.IsUsable(definition, holds)) continue;
            var aliasIndex = uint.MaxValue;
            if (definition.Aliases.Count > 0)
            {
                aliasIndex = (uint)packet.Enums.Count;
                packet.Enums.Add(AddEnum(packet, $"{definition.Name}Aliases", definition.Aliases));
            }
            // A name and a selector are the same thing to the client (a target), so overloads that differ only there are one overload.
            var seen = new HashSet<string>();
            var overloads = definition.Overloads.Where(overload => seen.Add(Signature(overload))).Select(overload => new AvailableCommandOverload(
                overload.Arguments.Select(argument => ProjectArgument(packet, argument)).ToArray())).ToArray();
            packet.Commands.Add(new AvailableCommand(definition.Name, definition.Description,
                "any", aliasIndex, overloads));
        }
        return packet;
    }

    private static string Signature(CommandOverload overload) => string.Join(' ', overload.Arguments.Select(argument =>
        $"{argument.Name}:{argument switch
        {
            EnumCommandArgument e => "enum=" + string.Join('|', e.Values.Keys),
            IntegerCommandArgument => "int",
            PlayerCommandArgument or SelectorCommandArgument => "target",
            _ => "string"
        }}{(argument.Optional ? "?" : "")}"));

    private static AvailableCommandParameter ProjectArgument(AvailableCommandsPacket packet, CommandArgument argument)
    {
        var type = argument switch
        {
            EnumCommandArgument commandEnum => Valid | Enum | (uint)AddFixedEnum(packet, argument.Name, commandEnum.Values.Keys),
            IntegerCommandArgument => Valid | Int,
            PlayerCommandArgument or SelectorCommandArgument => Valid | Target,
            StringCommandArgument => Valid | String,
            _ => throw new InvalidOperationException($"Unsupported command argument {argument.GetType().Name}.")
        };
        return new AvailableCommandParameter(argument.Name, type, argument.Optional);
    }

    private static int AddFixedEnum(AvailableCommandsPacket packet, string name, IEnumerable<string> values)
    {
        var index = packet.Enums.Count;
        packet.Enums.Add(AddEnum(packet, name, values));
        return index;
    }

    private static AvailableCommandEnum AddEnum(AvailableCommandsPacket packet, string name, IEnumerable<string> values)
    {
        var indices = new List<int>();
        foreach (var value in values)
        {
            var index = packet.EnumValues.IndexOf(value);
            if (index < 0) { index = packet.EnumValues.Count; packet.EnumValues.Add(value); }
            indices.Add(index);
        }
        return new AvailableCommandEnum(name, indices);
    }
}
