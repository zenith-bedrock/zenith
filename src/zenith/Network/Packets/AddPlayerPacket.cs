using Zenith.Raknet.Stream;

namespace Zenith.Network.Packets;

/// <summary>AddPlayer (0x0c) — spawn visual de outro jogador no mundo do receptor.</summary>
class AddPlayerPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.ADD_PLAYER_PACKET;

    public Guid Uuid { get; set; }
    public string Username { get; set; } = "";
    public ulong ActorRuntimeId { get; set; }
    public string PlatformChatId { get; set; } = "";
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }
    public float MotionX { get; set; }
    public float MotionY { get; set; }
    public float MotionZ { get; set; }
    public float Pitch { get; set; }
    public float Yaw { get; set; }
    public float HeadYaw { get; set; }
    public int GameMode { get; set; }
    public string DeviceId { get; set; } = "";
    public int BuildPlatform { get; set; } = -1;

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteUuid(Uuid);
        writer.WriteVarString(Username);
        writer.WriteUnsignedVarLong((long)ActorRuntimeId);
        writer.WriteVarString(PlatformChatId);
        writer.WriteFloat(PositionX, BinaryStream.Endianess.Little);
        writer.WriteFloat(PositionY, BinaryStream.Endianess.Little);
        writer.WriteFloat(PositionZ, BinaryStream.Endianess.Little);
        writer.WriteFloat(MotionX, BinaryStream.Endianess.Little);
        writer.WriteFloat(MotionY, BinaryStream.Endianess.Little);
        writer.WriteFloat(MotionZ, BinaryStream.Endianess.Little);
        writer.WriteFloat(Pitch, BinaryStream.Endianess.Little);
        writer.WriteFloat(Yaw, BinaryStream.Endianess.Little);
        writer.WriteFloat(HeadYaw, BinaryStream.Endianess.Little);
        writer.WriteVarInt(0); // item air
        writer.WriteVarInt(GameMode);
        WriteVisibleNameMetadata(ref writer, Username);
        writer.WriteUnsignedVarInt(0); // property sync ints
        writer.WriteUnsignedVarInt(0); // property sync floats
        WriteMinimalAbilities(ref writer, (long)ActorRuntimeId);
        writer.WriteUnsignedVarInt(0); // links
        writer.WriteVarString(DeviceId);
        writer.WriteInt(BuildPlatform, BinaryStream.Endianess.Little);
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream) { }

    private static void WriteVisibleNameMetadata(ref BinaryStream writer, string name)
    {
        long flags =
            EntityFlag.Bit(EntityFlag.Breathing) |
            EntityFlag.Bit(EntityFlag.CanClimb) |
            EntityFlag.Bit(EntityFlag.HasCollision) |
            EntityFlag.Bit(EntityFlag.AffectedByGravity) |
            EntityFlag.Bit(EntityFlag.ShowName) |
            EntityFlag.Bit(EntityFlag.AlwaysShowName);

        writer.WriteUnsignedVarInt(8);

        writer.WriteUnsignedVarInt(EntityMetaKey.Flags);
        writer.WriteUnsignedVarInt(EntityMetaType.Long);
        writer.WriteVarLong(flags);

        writer.WriteUnsignedVarInt(EntityMetaKey.ColorIndex);
        writer.WriteUnsignedVarInt(EntityMetaType.Byte);
        writer.WriteByte(0);

        writer.WriteUnsignedVarInt(EntityMetaKey.Name);
        writer.WriteUnsignedVarInt(EntityMetaType.String);
        writer.WriteVarString(name);

        writer.WriteUnsignedVarInt(EntityMetaKey.EffectColor);
        writer.WriteUnsignedVarInt(EntityMetaType.Int);
        writer.WriteVarInt(0);

        writer.WriteUnsignedVarInt(EntityMetaKey.EffectAmbience);
        writer.WriteUnsignedVarInt(EntityMetaType.Byte);
        writer.WriteByte(0);

        writer.WriteUnsignedVarInt(EntityMetaKey.Width);
        writer.WriteUnsignedVarInt(EntityMetaType.Float);
        writer.WriteFloat(0.6f, BinaryStream.Endianess.Little);

        writer.WriteUnsignedVarInt(EntityMetaKey.Height);
        writer.WriteUnsignedVarInt(EntityMetaType.Float);
        writer.WriteFloat(1.8f, BinaryStream.Endianess.Little);

        writer.WriteUnsignedVarInt(EntityMetaKey.AlwaysShowNameTag);
        writer.WriteUnsignedVarInt(EntityMetaType.Byte);
        writer.WriteByte(1);
    }

    private static void WriteMinimalAbilities(ref BinaryStream writer, long targetUniqueId)
    {
        uint allSet = (1u << AbilityBits.Count) - 1;
        uint values =
            AbilityBits.Bit(AbilityBits.Build) |
            AbilityBits.Bit(AbilityBits.Mine) |
            AbilityBits.Bit(AbilityBits.DoorsAndSwitches) |
            AbilityBits.Bit(AbilityBits.OpenContainers) |
            AbilityBits.Bit(AbilityBits.AttackPlayers) |
            AbilityBits.Bit(AbilityBits.AttackMobs) |
            AbilityBits.Bit(AbilityBits.WalkSpeed);

        writer.WriteULong((ulong)targetUniqueId, BinaryStream.Endianess.Little);
        writer.WriteByte(AbilityBits.PlayerPermissionMember);
        writer.WriteByte(AbilityBits.CommandPermissionNormal);
        writer.WriteByte(1); // layer count
        writer.WriteUShort(AbilityBits.LayerBase, BinaryStream.Endianess.Little);
        writer.WriteUInt(allSet, BinaryStream.Endianess.Little);
        writer.WriteUInt(values, BinaryStream.Endianess.Little);
        writer.WriteFloat(0.05f, BinaryStream.Endianess.Little);
        writer.WriteFloat(1.0f, BinaryStream.Endianess.Little);
        writer.WriteFloat(0.1f, BinaryStream.Endianess.Little);
    }
}
