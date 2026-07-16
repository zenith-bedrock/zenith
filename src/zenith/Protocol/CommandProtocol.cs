using Zenith.Gameplay.Commands;
using Zenith.Packets;
using Zenith.Session;

namespace Zenith.Protocol;

/// <summary>Wire permission level (byte 0–5).</summary>
internal enum CommandPermissionLevel : byte
{
    Any = 0,
    GameDirectors = 1,
    Admin = 2,
    Host = 3,
    Owner = 4,
    Internal = 5
}

static class CommandPermissionLevelExtensions
{
    public static string ToWireString(this CommandPermissionLevel level) => level switch
    {
        CommandPermissionLevel.Any => "Any",
        CommandPermissionLevel.GameDirectors => "GameDirectors",
        CommandPermissionLevel.Admin => "Admin",
        CommandPermissionLevel.Host => "Host",
        CommandPermissionLevel.Owner => "Owner",
        CommandPermissionLevel.Internal => "Internal",
        _ => "Any"
    };
}

/// <summary>Transmite comandos. Apenas transmite — sem decidir.</summary>
sealed class CommandProtocol
{
    private readonly NetworkSession _session;

    public CommandProtocol(NetworkSession session) => _session = session;

    /// <summary>Transmite AvailableCommandsPacket com os comandos built-in (help).</summary>
    public void SendAvailableCommands()
    {
        var commands = new Commands[]
        {
            CreateCommand("about", "About the software")
        };

        _session.SendDataPacket(new AvaliableCommandsPacket
        {
            EnumValues = new CommandEnumValues { EnumValues = ["about"] },
            ChainedSubCommandValues = new ChainedSubCommandValues { ChainedValues = [] },
            PostFixes = new CommandPostFixes { PostFixes = [] },
            EnumData = new CommandEnumData
            {
                EnumDataValues =
                [
                    new CommandEnumValue { Name = "CommandNames", Values = [0] }
                ]
            },
            SubCommandData = [],
            CommandData = commands,
            SoftEnums = [],
            Constraints = []
        });
    }

    /// <summary>Transmite AvailableCommandsPacket com comandos, enums e soft enums.</summary>
    public void SendAvailableCommands(
        CommandEnumValues enumValues,
        ChainedSubCommandValues chainedSubCommandValues,
        CommandPostFixes postFixes,
        CommandEnumData enumData,
        CommandSubCommandData[] subCommandData,
        Commands[] commands,
        SoftEnumData[] softEnums,
        ConstrainedValueData[] constraints)
    {
        _session.SendDataPacket(new AvaliableCommandsPacket
        {
            EnumValues = enumValues,
            ChainedSubCommandValues = chainedSubCommandValues,
            PostFixes = postFixes,
            EnumData = enumData,
            SubCommandData = subCommandData,
            CommandData = commands,
            SoftEnums = softEnums,
            Constraints = constraints
        });
    }

    /// <summary>Transmite AvailableCommandsPacket a partir do CommandPalette.</summary>
    public void SendAvailableCommands(CommandPalette palette)
    {
        var all = palette.GetAll();

        var enumValues = new List<string>();
        var commandEntries = new List<Commands>();

        foreach (var cmd in all)
        {
            enumValues.Add(cmd.Name);
            commandEntries.Add(CreateCommand(
                cmd.Name, cmd.Description, cmd.PermissionLevel));
        }

        var valuesArr = enumValues.ToArray();
        var indices = new uint[valuesArr.Length];
        for (uint i = 0; i < indices.Length; i++)
            indices[i] = i;

        _session.SendDataPacket(new AvaliableCommandsPacket
        {
            EnumValues = new CommandEnumValues { EnumValues = valuesArr },
            ChainedSubCommandValues = new ChainedSubCommandValues { ChainedValues = [] },
            PostFixes = new CommandPostFixes { PostFixes = [] },
            EnumData = new CommandEnumData
            {
                EnumDataValues =
                [
                    new CommandEnumValue { Name = "CommandNames", Values = indices }
                ]
            },
            SubCommandData = [],
            CommandData = commandEntries.ToArray(),
            SoftEnums = [],
            Constraints = []
        });
    }

    /// <summary>Cria uma entrada Commands a partir de dados tipados.</summary>
    public static Commands CreateCommand(
        string name,
        string description,
        CommandPermissionLevel permissionLevel = CommandPermissionLevel.Any,
        ushort flags = 0,
        int alias = -1,
        ushort[]? subCommands = null,
        Commands.CommandOverload[]? overloads = null) =>
        new()
        {
            Name = name,
            Description = description,
            Flags = flags,
            PermissionLevel = permissionLevel.ToWireString(),
            Alias = alias,
            SubCommands = subCommands ?? [],
            Overloads = overloads ?? []
        };

    /// <summary>Cria um SoftEnumData a partir de nome e opções.</summary>
    public static SoftEnumData CreateSoftEnum(string name, string[] options) =>
        new() { Name = name, Options = options };

    /// <summary>
    /// Cria um parâmetro de overload.
    /// <c>symbol</c> no formato Bedrock: <c>(typeCode &lt;&lt; 16) | index</c> para enums,
    /// ou o valor bruto do símbolo para tipos primitivos.
    /// </summary>
    public static Commands.CommandOverloadParameter CreateParameter(
        string name,
        uint symbol,
        bool optional = false,
        byte options = 0) =>
        new()
        {
            Name = name,
            Symbol = symbol,
            Optional = optional,
            Options = options
        };

    /// <summary>Cria um overload com parâmetros.</summary>
    public static Commands.CommandOverload CreateOverload(
        bool chaining,
        Commands.CommandOverloadParameter[] parameters) =>
        new() { Chaining = chaining, Parameters = parameters };

    public void SendCommandOutputSuccess(string command, CommandOriginData origin, string message)
    {
        _session.SendDataPacket(new CommandOutputPacket
        {
            OriginData = origin,
            OutputType = CommandOutputType.TYPE_ALL,
            SuccessCount = 1,
            Messages =
            [
                new CommandOutputMessage
                {
                    IsInternal = false,
                    MessageId = message,
                    Parameters = []
                }
            ],
            CommandData = command
        });
    }

    public void SendCommandOutputError(string command, CommandOriginData origin, string message)
    {
        _session.SendDataPacket(new CommandOutputPacket
        {
            OriginData = origin,
            OutputType = CommandOutputType.TYPE_ALL,
            SuccessCount = 0,
            Messages =
            [
                new CommandOutputMessage
                {
                    IsInternal = false,
                    MessageId = message,
                    Parameters = []
                }
            ],
            CommandData = command
        });
    }
}
