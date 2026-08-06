using Zenith.Packets.Generation;
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
[GamePacket((int)ProtocolInfo.COMMAND_REQUEST_PACKET)]
sealed partial class CommandRequestPacket : DataPacket
{
    [WireString]
    public string CommandLine { get; set; } = "";

    [WireNested]
    public CommandOriginData Origin { get; set; }

    [Wire]
    public bool Internal { get; set; }

    [WireString]
    public string Version { get; set; } = "";
}
