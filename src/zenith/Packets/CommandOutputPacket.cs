using Zenith.Raknet.Stream;

namespace Zenith.Packets;

internal readonly struct CommandOutputMessage
{
    public bool IsInternal { get; init; }
    public string MessageId { get; init; }
    public string[] Parameters { get; init; }

    public static CommandOutputMessage Read(BinaryStream stream)
    {
        var isInternal = stream.ReadBool();
        var messageId = stream.ReadVarString();
        var count = stream.ReadUnsignedVarInt();
        var parameters = new string[count];
        for (var i = 0; i < count; i++)
            parameters[i] = stream.ReadVarString();

        return new CommandOutputMessage
        {
            IsInternal = isInternal,
            MessageId = messageId,
            Parameters = parameters
        };
    }

    public void Write(BinaryStream writer)
    {
        writer.WriteBool(IsInternal);
        writer.WriteVarString(MessageId);

        writer.WriteUnsignedVarInt(Parameters.Length);
        foreach (var parameter in Parameters)
        {
            writer.WriteVarString(parameter);
        }
    }
}

internal sealed class CommandOutputPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.COMMAND_OUTPUT_PACKET;

    public CommandOriginData OriginData { get; set; }
    public CommandOutputType OutputType { get; set; }
    public int SuccessCount { get; set; }
    public CommandOutputMessage[] Messages { get; set; } = [];
    public string CommandData { get; set; }

    public override void Decode(ref BinaryStream stream)
    {
        OriginData = CommandOriginData.Read(stream);
        OutputType = (CommandOutputType)stream.ReadByte();
        SuccessCount = stream.ReadUnsignedVarInt();

        var count = stream.ReadUnsignedVarInt();
        var messages = new CommandOutputMessage[count];
        for (var i = 0; i < count; i++)
            messages[i] = CommandOutputMessage.Read(stream);
        Messages = messages;

        if (OutputType == CommandOutputType.TYPE_DATA_SET)
            CommandData = stream.ReadVarString();
        else
            CommandData = string.Empty;
    }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        OriginData.Write(writer);
        writer.WriteByte((byte)OutputType);
        writer.WriteUnsignedVarInt(SuccessCount);

        writer.WriteUnsignedVarInt(Messages.Length);
        foreach (var m in Messages)
            m.Write(writer);

        if (OutputType == CommandOutputType.TYPE_DATA_SET)
            writer.WriteVarString(CommandData);

        return writer.GetBufferDisposing();
    }
}

internal enum CommandOutputType : byte
{
    TYPE_LAST,
    TYPE_SILENT,
    TYPE_ALL,
    TYPE_DATA_SET
}
