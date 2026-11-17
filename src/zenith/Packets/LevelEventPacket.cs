using Zenith.Packets.Generation;
using Zenith.Binary;

namespace Zenith.Packets;

/// <summary>LevelEvent (0x19) — particles / sounds / block crack feedback.</summary>
[GamePacket((int)ProtocolInfo.LEVEL_EVENT_PACKET)]
sealed partial class LevelEventPacket : DataPacket
{
    public const int EventStartRain = 3001;
    public const int EventStartThunder = 3002;
    public const int EventStopRain = 3003;
    public const int EventStopThunder = 3004;
    /// <summary>Intensity sent with a start-rain or start-thunder event (0 to 65535).</summary>
    public const int WeatherIntensity = 50000;
    public const int EventStartBlockCracking = 3600;
    public const int EventStopBlockCracking = 3601;
    public const int EventBlockBreakSpeed = 3602;
    /// <summary>Experience orb pickup sound (LevelEvent sound id, same in PocketMine and Dragonfly).</summary>
    public const int EventSoundExperienceOrbPickup = 1051;
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
