using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>
/// Sent by the client right after StartGame to ask for a view distance (in chunks). The
/// client won't leave the loading screen until it gets a <see cref="ChunkRadiusUpdatedPacket"/>
/// reply plus enough chunk data around the spawn point.
/// </summary>
class RequestChunkRadiusPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.REQUEST_CHUNK_RADIUS_PACKET;

    public int Radius { get; set; }
    public byte MaxRadius { get; set; }

    public override void Decode(ref BinaryStream stream)
    {
        Radius = stream.ReadVarInt();
        MaxRadius = stream.ReadByte();
    }
}
