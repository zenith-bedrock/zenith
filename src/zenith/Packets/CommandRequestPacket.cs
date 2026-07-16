using Zenith.Raknet.Stream;

namespace Zenith.Packets;

readonly struct CommandOriginData
{
    public string CommandType { get; init; }
    public string CommandUuid { get; init; }
    public string RequestId { get; init; }
    public long PlayerActorUniqueId { get; init; }


    public static CommandOriginData Read(BinaryStream stream)
    {
        var commandType = stream.ReadVarString();
        var commandUuid = stream.ReadUuid();
        var requestId = stream.ReadVarString();
        var playerActorUniqueId = 0L;

        if (commandType is CommandRequestPacket.COMMAND_TYPE_DEV_CONSOLE or CommandRequestPacket.COMMAND_TYPE_TEST)
        {
            playerActorUniqueId = stream.ReadVarLong();
        }

        return new CommandOriginData
        {
            CommandType = commandType,
            CommandUuid = commandUuid,
            RequestId = requestId,
            PlayerActorUniqueId = playerActorUniqueId
        };
    }

    public void Write(BinaryStream writer)
    {
        writer.WriteVarString(CommandType);
        writer.WriteUuid(Guid.Parse(CommandUuid));
        writer.WriteVarString(RequestId);

        if (CommandType is CommandRequestPacket.COMMAND_TYPE_DEV_CONSOLE or CommandRequestPacket.COMMAND_TYPE_TEST)
        {
            writer.WriteVarLong(PlayerActorUniqueId);
        }
    }
}

sealed class CommandRequestPacket : DataPacket
{
    public const string COMMAND_TYPE_PLAYER = "Player";
    public const string COMMAND_TYPE_TEST = "Test";
    public const string COMMAND_TYPE_AUTOMATION_PLAYER = "AutomationPlayer";
    public const string COMMAND_TYPE_DEV_CONSOLE = "DevConsole";

    public override int Id => (int)ProtocolInfo.COMMAND_REQUEST_PACKET;

    public string Command { get; set; }
    public CommandOriginData OriginData { get; set; }
    public bool IsInternal { get; set; }
    public string Version { get; set; }

    public override void Decode(ref BinaryStream stream)
    {
        Command = stream.ReadVarString();
        OriginData = CommandOriginData.Read(stream);
        IsInternal = stream.ReadBool();
        Version = stream.ReadVarString();
    }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();

        writer.WriteVarString(Command);

        OriginData.Write(writer);

        writer.WriteBool(IsInternal);
        writer.WriteVarString(Version);

        return writer.GetBufferDisposing();
    }
}
