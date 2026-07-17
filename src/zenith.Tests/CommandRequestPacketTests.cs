using Zenith.Packets;
using Zenith.Raknet.Stream;
using Xunit;

namespace Zenith.Tests;

public class CommandRequestPacketTests
{
    [Fact]
    public void CommandRequest_roundtrips_player_origin()
    {
        var uuid = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var original = new CommandRequestPacket
        {
            CommandLine = "/gamemode creative",
            Origin = new CommandOriginData
            {
                Origin = CommandOriginData.OriginPlayer,
                Uuid = uuid,
                RequestId = "",
                PlayerUniqueId = 2
            },
            Internal = false,
            Version = "1.26.33"
        };

        var encoded = original.Encode().ToArray();
        var stream = new BinaryStream(encoded);
        Assert.Equal((int)ProtocolInfo.COMMAND_REQUEST_PACKET, stream.ReadUnsignedVarInt());

        var decoded = new CommandRequestPacket();
        decoded.Decode(ref stream);
        stream.Dispose();

        Assert.Equal(original.CommandLine, decoded.CommandLine);
        Assert.Equal(CommandOriginData.OriginPlayer, decoded.Origin.Origin);
        Assert.Equal(uuid, decoded.Origin.Uuid);
        Assert.Equal("", decoded.Origin.RequestId);
        Assert.Equal(2, decoded.Origin.PlayerUniqueId);
        Assert.False(decoded.Internal);
        Assert.Equal("1.26.33", decoded.Version);
    }
}
