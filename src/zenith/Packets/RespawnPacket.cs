using Zenith.Packets.Generation;
using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>
/// Respawn (0x2d) — death → spawn handshake (ADR §40).
/// Server: Searching / Ready; client: ClientReady.
/// </summary>
[GamePacket((int)ProtocolInfo.RESPAWN_PACKET)]
sealed partial class RespawnPacket : DataPacket
{
    public const byte StateSearchingForSpawn = 0;
    public const byte StateReadyToSpawn = 1;
    public const byte StateClientReadyToSpawn = 2;

    [Wire(BinaryStream.Endianess.Little)]
    public float PositionX { get; set; }

    [Wire(BinaryStream.Endianess.Little)]
    public float PositionY { get; set; }

    [Wire(BinaryStream.Endianess.Little)]
    public float PositionZ { get; set; }

    [Wire]
    public byte State { get; set; }

    [WireVar]
    public ulong EntityRuntimeId { get; set; }
}
