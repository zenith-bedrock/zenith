using Zenith.Raknet.Stream;

namespace Zenith.Packets;

sealed class StopSoundPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.STOP_SOUND_PACKET;

    public string SoundName { get; set; }

    public bool StopAllSounds { get; set; }
    public bool StopMusic { get; set; }

    public override void Decode(ref BinaryStream stream)
    {
        SoundName = stream.ReadVarString();

        StopAllSounds = stream.ReadBool();
        StopMusic = stream.ReadBool();
    }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();

        writer.WriteVarString(SoundName);
        writer.WriteBool(StopAllSounds);
        writer.WriteBool(StopMusic);

        return writer.GetBufferDisposing();
    }
}
