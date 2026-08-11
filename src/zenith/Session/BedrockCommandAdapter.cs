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

    public static AvailableCommandsPacket CreateMetadata(CommandCatalog catalog)
    {
        var packet = new AvailableCommandsPacket();
        foreach (var definition in catalog.Definitions)
        {
            var aliasIndex = uint.MaxValue;
            if (definition.Aliases.Count > 0)
            {
                aliasIndex = (uint)packet.Enums.Count;
                packet.Enums.Add(AddEnum(packet, $"{definition.Name}Aliases", definition.Aliases));
            }
            var overloads = definition.Overloads.Select(overload => new AvailableCommandOverload(
                overload.Arguments.Select(argument => ProjectArgument(packet, argument)).ToArray())).ToArray();
            packet.Commands.Add(new AvailableCommand(definition.Name, definition.Description,
                definition.RequiredPermission == CommandPermission.Operator ? "operator" : "any", aliasIndex, overloads));
        }
        return packet;
    }

    private static AvailableCommandParameter ProjectArgument(AvailableCommandsPacket packet, CommandArgument argument)
    {
        var type = argument switch
        {
            EnumCommandArgument commandEnum => Enum | (uint)AddFixedEnum(packet, argument.Name, commandEnum.Values.Keys),
            IntegerCommandArgument => Valid | Int,
            PlayerCommandArgument => Valid | Target,
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
