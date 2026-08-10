using Zenith.Packets;
using Zenith.Raknet.Stream;
using Xunit;

namespace Zenith.Tests;

/// <summary>
/// Locks the ResourcePackClientResponse status values to the real 0-indexed wire enum
/// (Mojang ResourcePackResponse: Cancel/Downloading/DownloadingFinished/ResourcePackStackFinished
/// = 0..3; cross-checked against gophertunnel and minecraft-data). Regression coverage for the
/// off-by-one that silently stalled every join at the resource-pack handshake — found by running
/// zenith-smoke-bot against a live server, not by static review.
/// </summary>
public class ResourcePackClientResponseStatusTests
{
    [Fact]
    public void Status_constants_are_zero_indexed()
    {
        Assert.Equal(0, ResourcePackClientResponsePacket.STATUS_REFUSED);
        Assert.Equal(1, ResourcePackClientResponsePacket.STATUS_SEND_PACKS);
        Assert.Equal(2, ResourcePackClientResponsePacket.STATUS_HAVE_ALL_PACKS);
        Assert.Equal(3, ResourcePackClientResponsePacket.STATUS_COMPLETED);
    }

    [Fact]
    public void Decode_reads_status_name_and_completed_has_no_pack_ids()
    {
        var w = new BinaryStream();
        w.WriteUnsignedVarInt(ResourcePackClientResponsePacket.STATUS_COMPLETED);
        w.WriteVarString("completed");

        var stream = new BinaryStream(w.GetBufferDisposing().ToArray());
        var packet = new ResourcePackClientResponsePacket();
        packet.Decode(ref stream);

        Assert.Equal(ResourcePackClientResponsePacket.STATUS_COMPLETED, packet.Status);
        Assert.Empty(packet.ResourcePackIds);
        Assert.True(stream.IsEndOfFile);
    }

    [Fact]
    public void Decode_send_packs_reads_pack_id_array()
    {
        var w = new BinaryStream();
        w.WriteUnsignedVarInt(ResourcePackClientResponsePacket.STATUS_SEND_PACKS);
        w.WriteVarString("send_packs");
        w.WriteUnsignedVarInt(2);
        w.WriteVarString("pack-a_1.0.0");
        w.WriteVarString("pack-b_2.0.0");

        var stream = new BinaryStream(w.GetBufferDisposing().ToArray());
        var packet = new ResourcePackClientResponsePacket();
        packet.Decode(ref stream);

        Assert.Equal(ResourcePackClientResponsePacket.STATUS_SEND_PACKS, packet.Status);
        Assert.Equal(["pack-a_1.0.0", "pack-b_2.0.0"], packet.ResourcePackIds);
        Assert.True(stream.IsEndOfFile);
    }
}
