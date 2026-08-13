using Zenith.Packets.Generation;
using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>LevelEvent (0x19) — particles / sounds / block crack feedback.</summary>
[GamePacket((int)ProtocolInfo.LEVEL_EVENT_PACKET)]
sealed partial class LevelEventPacket : DataPacket
{
    public const int EventStartBlockCracking = 3600;
    public const int EventStopBlockCracking = 3601;
    public const int EventBlockBreakSpeed = 3602;
    /// <summary>Stable Bedrock particle id — Creeper explosion visual (Phase XV).</summary>
    public const int EventParticleExplosion = 2013;

    [WireVar]
    public int EventType { get; set; }

    [Wire(BinaryStream.Endianess.Little)]
    public float X { get; set; }

    [Wire(BinaryStream.Endianess.Little)]
    public float Y { get; set; }

    [Wire(BinaryStream.Endianess.Little)]
    public float Z { get; set; }

    [WireVar]
    public int EventData { get; set; }
}
