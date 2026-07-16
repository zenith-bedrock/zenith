using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>
/// PlaySound (0x56) — server → client. Position is SoundPos (BlockPos of world*8).
/// Optional handle is fixed LE uint64 (gophertunnel).
/// </summary>
sealed class PlaySoundPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.PLAY_SOUND_PACKET;

    public string SoundName { get; set; } = "";

    /// <summary>World X; Encode writes (int)(X * 8) as BlockPos.</summary>
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }

    public float Volume { get; set; } = 1f;
    public float Pitch { get; set; } = 1f;

    public ulong? ServerSoundHandle { get; set; }

    public override void Decode(ref BinaryStream stream)
    {
        SoundName = stream.ReadVarString();
        PositionX = stream.ReadVarInt() / 8f;
        PositionY = stream.ReadVarInt() / 8f;
        PositionZ = stream.ReadVarInt() / 8f;
        Volume = stream.ReadFloat(BinaryStream.Endianess.Little);
        Pitch = stream.ReadFloat(BinaryStream.Endianess.Little);
        if (stream.ReadBool())
            ServerSoundHandle = stream.ReadULong(BinaryStream.Endianess.Little);
        else
            ServerSoundHandle = null;
    }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteVarString(SoundName);
        writer.WriteVarInt((int)(PositionX * 8f));
        writer.WriteVarInt((int)(PositionY * 8f));
        writer.WriteVarInt((int)(PositionZ * 8f));
        writer.WriteFloat(Volume, BinaryStream.Endianess.Little);
        writer.WriteFloat(Pitch, BinaryStream.Endianess.Little);
        if (ServerSoundHandle is { } handle)
        {
            writer.WriteBool(true);
            writer.WriteULong(handle, BinaryStream.Endianess.Little);
        }
        else
        {
            writer.WriteBool(false);
        }

        return writer.GetBufferDisposing();
    }
}
