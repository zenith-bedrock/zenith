using Zenith.Packets;
using Zenith.Raknet.Stream;
using Xunit;

namespace Zenith.Tests;

public class LevelChunkPacketTests
{
    [Fact]
    public void Encode_writes_Cereal_optional_and_always_present_cache_metadata()
    {
        var payload = new byte[] { 1, 2, 3 };
        var packet = new LevelChunkPacket
        {
            ChunkX = 4,
            ChunkZ = -5,
            SubChunkCount = 6,
            ExtraPayload = payload,
        };

        var bytes = packet.Encode().ToArray();
        var stream = new BinaryStream(bytes);

        Assert.Equal((int)ProtocolInfo.LEVEL_CHUNK_PACKET, stream.ReadUnsignedVarInt());
        Assert.Equal(4, stream.ReadVarInt());
        Assert.Equal(-5, stream.ReadVarInt());
        Assert.Equal(DimensionId.Overworld, stream.ReadVarInt());
        Assert.Equal(6, (int)stream.ReadUnsignedVarInt());
        Assert.False(stream.ReadBool()); // ClientRequestSubChunkLimit presence (Cereal optional, ADR §79/§88)
        Assert.False(stream.ReadBool()); // CacheEnabled
        Assert.Equal(0, (int)stream.ReadUnsignedVarInt()); // CacheMetadata count - always present since 2168
        Assert.Equal(payload.Length, (int)stream.ReadUnsignedVarInt());
        Assert.Equal(payload, stream.ReadSpan(payload.Length).ToArray());
    }
}
