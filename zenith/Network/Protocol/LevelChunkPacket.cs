using Zenith.Raknet.Stream;

namespace Zenith.Network.Protocol;

/// <summary>
/// Carries one 16x16 chunk column's terrain data to the client. Zenith has no real
/// World/Chunk model yet, so every instance sent today wraps a fake, fully empty (air)
/// column built by <see cref="ChunkUtils.BuildEmptyOverworldPayload"/> - just enough for the
/// client to stop waiting on the "Loading world" screen. Replace ExtraPayload's source once
/// a real per-chunk block/biome model exists.
/// </summary>
class LevelChunkPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.LEVEL_CHUNK_PACKET;

    public int ChunkX { get; set; }
    public int ChunkZ { get; set; }
    public int DimensionId { get; set; } = 0;

    /// <summary>Number of block subchunks encoded at the start of ExtraPayload. 0 = fully empty column.</summary>
    public int SubChunkCount { get; set; } = 0;

    /// <summary>
    /// Raw pre-built payload: block subchunks (SubChunkCount of them), then one biome entry
    /// per subchunk index the dimension expects, then border blocks, then tiles. See
    /// <see cref="ChunkUtils"/> for the only shape currently produced (fully empty).
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
