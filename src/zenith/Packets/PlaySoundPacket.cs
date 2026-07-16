using Zenith.Raknet.Stream;

namespace Zenith.Packets;

sealed class PlaySoundPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.PLAY_SOUND_PACKET;

    public string SoundName { get; set; }

    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }

    public float Volume { get; set; }
    public float Pitch { get; set; }

    public float Loop { get; set; }

    public long? ServerSoundHandle { get; set; } = null;

    public override void Decode(ref BinaryStream stream)
    {
        SoundName = stream.ReadVarString();

        PositionX = stream.ReadFloat(BinaryStream.Endianess.Little);
        PositionY = stream.ReadFloat(BinaryStream.Endianess.Little);
        PositionZ = stream.ReadFloat(BinaryStream.Endianess.Little);

        Volume = stream.ReadFloat(BinaryStream.Endianess.Little);
        Pitch = stream.ReadFloat(BinaryStream.Endianess.Little);

        Loop = stream.ReadFloat(BinaryStream.Endianess.Little);

        if (stream.ReadBool())
            ServerSoundHandle = stream.ReadUnsignedVarLong();
    }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();

        writer.WriteVarString(SoundName);
        writer.WriteFloat(PositionX);
        writer.WriteFloat(PositionY);
        writer.WriteFloat(PositionZ);
        writer.WriteFloat(Volume);
        writer.WriteFloat(Pitch);
        writer.WriteFloat(Loop);

        if (ServerSoundHandle != null)
        {
            writer.WriteBool(true);
            writer.WriteUnsignedVarLong((long) ServerSoundHandle);
        }
        else
        {
            writer.WriteBool(false);
        }

        return writer.GetBufferDisposing();
    }
}
