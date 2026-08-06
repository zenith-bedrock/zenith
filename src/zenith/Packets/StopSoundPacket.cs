using Zenith.Packets.Generation;

namespace Zenith.Packets;

/// <summary>StopSound (0x57) — server → client stop named / all / music sounds.</summary>
[GamePacket((int)ProtocolInfo.STOP_SOUND_PACKET)]
sealed partial class StopSoundPacket : DataPacket
{
    [WireString]
    public string SoundName { get; set; } = "";

    [Wire]
    public bool StopAllSounds { get; set; }

    [Wire]
    public bool StopMusic { get; set; }
}
