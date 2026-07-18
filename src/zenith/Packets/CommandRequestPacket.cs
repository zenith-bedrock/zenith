using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>
/// Command origin blob (protocol 1001): origin string + UUID + requestId + Int64 unique id.
/// </summary>
readonly struct CommandOriginData
{
    public const string OriginPlayer = "player";
    public const string OriginDevConsole = "devconsole";
    public const string OriginTest = "test";

    public string Origin { get; init; }
    public Guid Uuid { get; init; }
    public string RequestId { get; init; }
    public long PlayerUniqueId { get; init; }

    public static CommandOriginData Read(ref BinaryStream stream)
    {
        var origin = stream.ReadVarString();
        var uuid = stream.ReadUuid();
        var requestId = stream.ReadVarString();
        var playerUniqueId = stream.ReadLong(BinaryStream.Endianess.Little);
        return new CommandOriginData
        {
            Origin = origin,
            Uuid = uuid,
            RequestId = requestId,
            PlayerUniqueId = playerUniqueId
        };
    }

    public void Write(ref BinaryStream writer)
    {
        writer.WriteVarString(Origin);
        writer.WriteUuid(Uuid);
        writer.WriteVarString(RequestId);
        writer.WriteLong(PlayerUniqueId, BinaryStream.Endianess.Little);
    }
}

/// <summary>CommandRequest (0x4D) — client slash command line (ADR §52 adendo).</summary>
sealed class CommandRequestPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.COMMAND_REQUEST_PACKET;

    public string CommandLine { get; set; } = "";
    public CommandOriginData Origin { get; set; }
    public bool Internal { get; set; }
    public string Version { get; set; } = "";

    public override void Decode(ref BinaryStream stream)
    {
        CommandLine = stream.ReadVarString();
        Origin = CommandOriginData.Read(ref stream);
        Internal = stream.ReadBool();
        Version = stream.ReadVarString();
    }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteVarString(CommandLine);
        Origin.Write(ref writer);
        writer.WriteBool(Internal);
        writer.WriteVarString(Version);
        return writer.GetBufferDisposing();
    }
}
