using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>
/// Carries one 16x16 chunk column's terrain data to the client.
/// Payload vem de <see cref="World.ChunkPayloads"/> (flat base + biomes); edits via UpdateBlock/overlay.
/// </summary>
class LevelChunkPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.LEVEL_CHUNK_PACKET;

    public int ChunkX { get; set; }
    public int ChunkZ { get; set; }
    public int DimensionId { get; set; } = global::Zenith.Packets.DimensionId.Overworld;

    /// <summary>Number of block subchunks encoded at the start of ExtraPayload.</summary>
    public int SubChunkCount { get; set; } = 0;

    /// <summary>
    /// Raw pre-built payload: block subchunks (SubChunkCount of them), then biome entries,
    /// then border blocks / tiles.
    /// </summary>
    public byte[] ExtraPayload { get; set; } = Array.Empty<byte>();

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteVarInt(ChunkX);
        writer.WriteVarInt(ChunkZ);
        writer.WriteVarInt(DimensionId);
        writer.WriteUnsignedVarInt(SubChunkCount);
        writer.WriteBool(false); // client-side chunk cache (blob hashing) - not supported yet
        writer.WriteUnsignedVarInt(ExtraPayload.Length);
        writer.Write(ExtraPayload);
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream) { }
}
