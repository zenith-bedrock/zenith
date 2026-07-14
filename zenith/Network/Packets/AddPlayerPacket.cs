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
        BedrockWire.WriteUuid(ref writer, Uuid);
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
        BedrockWire.WriteAirItem(ref writer);
        writer.WriteVarInt(GameMode);
        BedrockWire.WriteVisibleNameMetadata(ref writer, Username);
        BedrockWire.WriteEmptyPropertySync(ref writer);
        BedrockWire.WriteMinimalAbilities(ref writer, (long)ActorRuntimeId);
        writer.WriteUnsignedVarInt(0); // links
        writer.WriteVarString(DeviceId);
        writer.WriteInt(BuildPlatform, BinaryStream.Endianess.Little);
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream) { }
}
