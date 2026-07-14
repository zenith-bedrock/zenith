using System.IO.Compression;
using Xunit;
using Zenith.Network.Packets;
using Zenith.Network.Protocol;
using Zenith.World;

namespace Zenith.Tests;

public class SpawnPathWireTests
{
    [Fact]
    public void Empty_biome_definition_list_is_two_zero_counts()
    {
        var bytes = new BiomeDefinitionListPacket().Encode().ToArray();
        // packet id (0x7a varuint) + defs=0 + strings=0
        Assert.Equal(0x7a, bytes[0]);
        Assert.Equal(0, bytes[1]);
        Assert.Equal(0, bytes[2]);
        Assert.Equal(3, bytes.Length);
    }

    [Fact]
    public void GamePacket_under_threshold_uses_compression_none()
    {
        var gp = new GamePacket
        {
            Compression = PacketCompression.ZLIB,
            CompressionThreshold = 256,
            Packets = [new PlayStatusPacket { Status = 0 }]
        };
        var wire = gp.Encode().ToArray();
        Assert.Equal(0xfe, wire[0]); // MessageIdentifier.Game
        Assert.Equal(PacketCompression.NONE, wire[1]);
    }

    [Fact]
    public void GamePacket_over_threshold_uses_zlib_flate()
    {
        var big = new byte[400];
        // Build via LevelChunk with large payload
        var gp = new GamePacket
        {
            Compression = PacketCompression.ZLIB,
            CompressionThreshold = 256,
            Packets =
            [
                new LevelChunkPacket
                {
                    ChunkX = 0,
                    ChunkZ = 0,
                    SubChunkCount = 1,
                    ExtraPayload = big
                }
            ]
        };
        var wire = gp.Encode().ToArray();
        Assert.Equal(0xfe, wire[0]);
        Assert.Equal(PacketCompression.ZLIB, wire[1]);

        // Must be raw deflate-decompressible (Vedrock flate framing).
        using var ms = new MemoryStream(wire, 2, wire.Length - 2);
        using var inflate = new DeflateStream(ms, CompressionMode.Decompress);
        using var outMs = new MemoryStream();
        inflate.CopyTo(outMs);
        Assert.True(outMs.Length > 0);
    }

    [Fact]
    public void Level_chunk_batch_size_matches_vedrock_flush()
    {
        Assert.Equal(4, WorldProtocol.LevelChunkBatchSize);
    }

    [Fact]
    public void StartGame_eye_height_offset_constant()
    {
        Assert.Equal(1.62f, Blocks.PlayerEyeHeight);
        Assert.Equal(-60, Blocks.FlatSpawnY);
    }
}
