using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>
/// Tells the client which area around a block position now has usable chunk data.
/// Without this packet the client ignores terrain even if LevelChunks arrive.
/// PreSpawn sends it before LevelChunks.
/// </summary>
class NetworkChunkPublisherUpdatePacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.NETWORK_CHUNK_PUBLISHER_UPDATE_PACKET;

    public int BlockX { get; set; }
    public int BlockY { get; set; }
    public int BlockZ { get; set; }

    /// <summary>Radius in blocks, not chunks (intentional on the protocol's side, not a typo).</summary>
    public int Radius { get; set; }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteVarInt(BlockX);
        writer.WriteVarInt(BlockY);
        writer.WriteVarInt(BlockZ);
        writer.WriteUnsignedVarInt(Radius);
        writer.WriteUInt(0, BinaryStream.Endianess.Little); // saved/cached chunk count - none
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream) { }
}
