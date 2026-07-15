using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>
/// Server's reply to <see cref="RequestChunkRadiusPacket"/>, confirming the view radius
/// (in chunks) that will actually be used - may be lower than what the client asked for.
/// </summary>
class ChunkRadiusUpdatedPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.CHUNK_RADIUS_UPDATED_PACKET;

    public int Radius { get; set; }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteVarInt(Radius);
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream) { }
}
