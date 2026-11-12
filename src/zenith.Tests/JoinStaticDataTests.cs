using Xunit;
using Zenith.Packets;
using Zenith.Server;

namespace Zenith.Tests;

public class JoinStaticDataTests
{
    [Fact]
    public void The_embedded_set_was_captured_for_the_protocol_this_server_speaks()
    {
        Assert.Equal(ServerIdentity.ProtocolVersion, JoinStaticData.Protocol);
        Assert.False(string.IsNullOrEmpty(JoinStaticData.BedrockVersion));
    }

    [Theory]
    [InlineData(JoinStaticNames.VoxelShapes, 1)]
    [InlineData(JoinStaticNames.EntityProperties, 13)]
    [InlineData(JoinStaticNames.EntityIdentifiers, 1)]
    [InlineData(JoinStaticNames.PlayerFog, 1)]
    [InlineData(JoinStaticNames.CameraPresets, 1)]
    [InlineData(JoinStaticNames.CameraAimAssistPresets, 1)]
    [InlineData(JoinStaticNames.CameraSpline, 1)]
    [InlineData(JoinStaticNames.TrimData, 1)]
    public void Every_name_the_join_sequence_sends_has_its_captured_packets(string name, int count)
    {
        var packets = JoinStaticData.Packets(name).ToArray();

        Assert.Equal(count, packets.Length);
        foreach (var packet in packets)
        {
            var bytes = packet.Encode().ToArray();
            Assert.True(bytes.Length > 1);
        }
    }

    [Fact]
    public void An_encoded_packet_is_its_id_varint_followed_by_the_captured_body()
    {
        var entity = JoinStaticData.All.First(e => e.Name == JoinStaticNames.EntityIdentifiers);
        var encoded = new StaticDataPacket(entity.Id, entity.Body).Encode().ToArray();

        // id 119 is one varint byte; the body follows untouched.
        Assert.Equal(119, entity.Id);
        Assert.Equal(119, encoded[0]);
        Assert.Equal(entity.Body, encoded[1..]);
    }

    [Fact]
    public void An_unknown_name_fails_instead_of_sending_nothing()
    {
        Assert.Throws<InvalidOperationException>(() => JoinStaticData.Packets("no_such_packet").ToArray());
    }
}
