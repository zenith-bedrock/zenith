using Zenith.Packets.Generation;

namespace Zenith.Packets;

/// <summary>
/// Sent by the client right after StartGame to ask for a view distance (in chunks). The
/// client won't leave the loading screen until it gets a <see cref="ChunkRadiusUpdatedPacket"/>
/// reply plus enough chunk data around the spawn point.
/// </summary>
[GamePacket((int)ProtocolInfo.REQUEST_CHUNK_RADIUS_PACKET)]
sealed partial class RequestChunkRadiusPacket : DataPacket
{
    [WireVar]
    public int Radius { get; set; }

    [Wire]
    public byte MaxRadius { get; set; }
}
