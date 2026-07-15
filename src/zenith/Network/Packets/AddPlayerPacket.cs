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
    public NetworkItemStack HeldItem { get; set; } = NetworkItemStack.Empty;

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
        HeldItem.WriteLegacyItemInstance(ref writer);
        writer.WriteVarInt(GameMode);
        EntityMetadataWriter.WriteVisibleNameMetadata(ref writer, Username);
        writer.WriteUnsignedVarInt(0); // property sync ints
        writer.WriteUnsignedVarInt(0); // property sync floats
        WriteMinimalAbilities(ref writer, (long)ActorRuntimeId);
        writer.WriteUnsignedVarInt(0); // links
        writer.WriteVarString(DeviceId);
        writer.WriteInt(BuildPlatform, BinaryStream.Endianess.Little);
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream) { }

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
