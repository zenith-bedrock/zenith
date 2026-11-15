using System.IO.Compression;
using Xunit;
using Zenith.Nbt;
using Zenith.Packets;
using Zenith.Protocol;
using Zenith.World;

namespace Zenith.Tests;

public class SpawnPathWireTests
{
    [Fact]
    public void Captured_biomes_follow_2225_optional_gates_and_string_table_to_EOF()
    {
        var reader = new Zenith.Binary.BinaryStream(BiomeDefinitionListBlob.WireBody.ToArray());
        var count = reader.ReadUnsignedVarInt();
        Assert.Equal(89, count);
        var stringReferences = new List<ushort>();
        for (var n = 0; n < count; n++)
        {
            stringReferences.Add(reader.ReadUShort(Zenith.Binary.BinaryStream.Endianess.Little)); // name-table map key
            _ = reader.ReadUShort(Zenith.Binary.BinaryStream.Endianess.Little); // numeric biome ID, may be 65535
            for (var f = 0; f < 5; f++) _ = reader.ReadFloat(Zenith.Binary.BinaryStream.Endianess.Little);
            _ = reader.ReadInt(Zenith.Binary.BinaryStream.Endianess.Little); // water color
            Assert.InRange(reader.ReadByte(), (byte)0, (byte)1); // rain
            var tags = reader.ReadByte();
            Assert.InRange(tags, (byte)0, (byte)1);
            if (tags == 1)
            {
                var tagCount = reader.ReadUnsignedVarInt();
                for (var t = 0; t < tagCount; t++)
                    stringReferences.Add(reader.ReadUShort(Zenith.Binary.BinaryStream.Endianess.Little));
            }
            Assert.Equal(0, reader.ReadByte()); // this capture has no client chunk-generation payload
        }
        var strings = reader.ReadUnsignedVarInt();
        Assert.Equal(187, strings);
        for (var n = 0; n < strings; n++) _ = reader.ReadVarString();
        Assert.All(stringReferences, index => Assert.InRange((int)index, 0, strings - 1));
        Assert.True(reader.IsEndOfFile);
    }

    [Fact]
    public void Biome_definition_list_embeds_non_empty_vanilla_body()
    {
        Assert.True(BiomeDefinitionListBlob.ByteLength > 2,
            "embedded biome_definitions.bin must be non-empty (ADR §70)");

        var bytes = new BiomeDefinitionListPacket().Encode().ToArray();
        Assert.Equal(0x7a, bytes[0]); // packet id varuint
        // Body length = encode length - 1 (id fits in one byte for 0x7a).
        Assert.Equal(1 + BiomeDefinitionListBlob.ByteLength, bytes.Length);
        // BDS 1.26.52.3 supplies 89 definitions; this is the captured wire body.
        Assert.Equal(0x59, bytes[1]);
    }

    [Fact]
    public void Jigsaw_structure_data_encodes_the_vanilla_network_nbt_compound()
    {
        Assert.True(JigsawStructureDataBlob.ByteLength > 2,
            "embedded jigsaw_structures_data.nbt must contain the vanilla rule compound");

        var bytes = new JigsawStructureDataPacket().Encode().ToArray();
        var stream = new Zenith.Binary.BinaryStream(bytes);
        Assert.Equal(313, (int)stream.ReadUnsignedVarInt());

        var decoded = NbtCodec.Decode(
            stream.Buffer.AsSpan(stream.Offset, stream.Length - stream.Offset), NbtEncoding.Network);
        Assert.Equal(JigsawStructureDataBlob.ByteLength, decoded.BytesConsumed);
        Assert.Equal(string.Empty, decoded.Root.Name);
        Assert.True(decoded.Root.Tag.TryGetCompound(out var rules));
        Assert.True(rules.TryGet("jigsaws", out var jigsawRulesTag));
        Assert.True(jigsawRulesTag.TryGetList(out var jigsawRules));
        Assert.Equal(NbtType.Compound, jigsawRules.ElementType);
        Assert.True(jigsawRules.Count > 0);
        stream.Offset += decoded.BytesConsumed;
        Assert.True(stream.IsEndOfFile);
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

        // Must be raw deflate-decompressible (algorithm 0xff framing under threshold).
        using var ms = new MemoryStream(wire, 2, wire.Length - 2);
        using var inflate = new DeflateStream(ms, CompressionMode.Decompress);
        using var outMs = new MemoryStream();
        inflate.CopyTo(outMs);
        Assert.True(outMs.Length > 0);
    }

    [Fact]
    public void Level_chunk_batch_size_is_one_per_envelope()
    {
        // Noise columns are large — one LevelChunk per GamePacket (ADR §14 adendo).
        Assert.Equal(1, WorldProtocol.LevelChunkBatchSize);
    }

    [Fact]
    public void Default_spawn_ready_radius_is_smaller_than_default_view()
    {
        // ADR §70: leave loading with a small ready-disk; ChunkStream fills the rest.
        var config = new Zenith.Server.ServerConfig();
        Assert.Equal(2, config.World.SpawnReadyRadius);
        Assert.Equal(4, config.World.SpawnChunkRadius);
        Assert.True(config.World.SpawnReadyRadius <= config.World.SpawnChunkRadius);
    }

    [Fact]
    public void StartGame_eye_height_offset_constant()
    {
        Assert.Equal(1.62f, Blocks.PlayerEyeHeight);
        Assert.Equal(-60, Blocks.FlatSpawnY);
    }
}
