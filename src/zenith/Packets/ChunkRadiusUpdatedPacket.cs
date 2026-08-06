using Zenith.Packets.Generation;

namespace Zenith.Packets;

/// <summary>
/// Server's reply to <see cref="RequestChunkRadiusPacket"/>, confirming the view radius
/// (in chunks) that will actually be used - may be lower than what the client asked for.
/// </summary>
[GamePacket((int)ProtocolInfo.CHUNK_RADIUS_UPDATED_PACKET)]
sealed partial class ChunkRadiusUpdatedPacket : DataPacket
{
    [WireVar]
    public int Radius { get; set; }
}
