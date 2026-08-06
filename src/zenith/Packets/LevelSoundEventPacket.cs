using Zenith.Packets.Generation;
using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>
/// LevelSoundEvent (0x7b) — protocol 1001 / PM BedrockProtocol 58 string sound names.
/// Server-authored place/break/hit (ADR §59); inbound stays quiet ACK.
/// </summary>
[GamePacket((int)ProtocolInfo.LEVEL_SOUND_EVENT_PACKET)]
sealed partial class LevelSoundEventPacket : DataPacket
{
    public const string SoundPlace = "place";
    public const string SoundBreak = "break";
    public const string SoundHit = "hit";

    [WireString]
    public string Sound { get; set; } = "";

    [Wire(BinaryStream.Endianess.Little)]
    public float PositionX { get; set; }

    [Wire(BinaryStream.Endianess.Little)]
    public float PositionY { get; set; }

    [Wire(BinaryStream.Endianess.Little)]
    public float PositionZ { get; set; }

    [WireVar]
    public int ExtraData { get; set; } = -1;

    [WireString]
    public string EntityType { get; set; } = ":";

    [Wire]
    public bool IsBabyMob { get; set; }

    [Wire]
    public bool IsGlobal { get; set; }

    [Wire(BinaryStream.Endianess.Little)]
    public long ActorUniqueId { get; set; } = -1;

    [Wire]
    public bool HasFirePosition { get; set; }

    [Wire(BinaryStream.Endianess.Little)]
    [WireWhen(nameof(HasFirePosition), true)]
    public float FirePositionX { get; set; }

    [Wire(BinaryStream.Endianess.Little)]
    [WireWhen(nameof(HasFirePosition), true)]
    public float FirePositionY { get; set; }

    [Wire(BinaryStream.Endianess.Little)]
    [WireWhen(nameof(HasFirePosition), true)]
    public float FirePositionZ { get; set; }
}
