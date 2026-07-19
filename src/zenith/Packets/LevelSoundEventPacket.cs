using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>
/// LevelSoundEvent (0x7b) — protocol 1001 / PM BedrockProtocol 58 string sound names.
/// Server-authored place/break/hit (ADR §59); inbound stays quiet ACK.
/// </summary>
sealed class LevelSoundEventPacket : DataPacket
{
    public const string SoundPlace = "place";
    public const string SoundBreak = "break";
    public const string SoundHit = "hit";

    public override int Id => (int)ProtocolInfo.LEVEL_SOUND_EVENT_PACKET;

    public string Sound { get; set; } = "";
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }
    public int ExtraData { get; set; } = -1;
    public string EntityType { get; set; } = ":";
    public bool IsBabyMob { get; set; }
    public bool IsGlobal { get; set; }
    public long ActorUniqueId { get; set; } = -1;
    public bool HasFirePosition { get; set; }
    public float FirePositionX { get; set; }
    public float FirePositionY { get; set; }
    public float FirePositionZ { get; set; }

    public override void Decode(ref BinaryStream stream)
    {
        Sound = stream.ReadVarString();
        PositionX = stream.ReadFloat(BinaryStream.Endianess.Little);
        PositionY = stream.ReadFloat(BinaryStream.Endianess.Little);
        PositionZ = stream.ReadFloat(BinaryStream.Endianess.Little);
        ExtraData = stream.ReadVarInt();
        EntityType = stream.ReadVarString();
        IsBabyMob = stream.ReadBool();
        IsGlobal = stream.ReadBool();
        ActorUniqueId = stream.ReadLong(BinaryStream.Endianess.Little);
        HasFirePosition = stream.ReadBool();
        if (HasFirePosition)
        {
            FirePositionX = stream.ReadFloat(BinaryStream.Endianess.Little);
            FirePositionY = stream.ReadFloat(BinaryStream.Endianess.Little);
            FirePositionZ = stream.ReadFloat(BinaryStream.Endianess.Little);
        }
    }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteVarString(Sound);
        writer.WriteFloat(PositionX, BinaryStream.Endianess.Little);
        writer.WriteFloat(PositionY, BinaryStream.Endianess.Little);
        writer.WriteFloat(PositionZ, BinaryStream.Endianess.Little);
        writer.WriteVarInt(ExtraData);
        writer.WriteVarString(EntityType);
        writer.WriteBool(IsBabyMob);
        writer.WriteBool(IsGlobal);
        writer.WriteLong(ActorUniqueId, BinaryStream.Endianess.Little);
        writer.WriteBool(HasFirePosition);
        if (HasFirePosition)
        {
            writer.WriteFloat(FirePositionX, BinaryStream.Endianess.Little);
            writer.WriteFloat(FirePositionY, BinaryStream.Endianess.Little);
            writer.WriteFloat(FirePositionZ, BinaryStream.Endianess.Little);
        }

        return writer.GetBufferDisposing();
    }
}
