using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>Interact (0x21) — open-inventory and target identity.</summary>
sealed class InteractPacket : DataPacket
{
    public const int ActionOpenInventory = 6;

    public override int Id => (int)ProtocolInfo.INTERACT_PACKET;

    public int Action { get; set; }
    public long TargetActorRuntimeId { get; set; }

    public override Span<byte> Encode() => Array.Empty<byte>();

    public override void Decode(ref BinaryStream stream)
    {
        Action = stream.ReadByte();
        TargetActorRuntimeId = stream.ReadUnsignedVarLong();
        if (stream.ReadBool())
        {
            _ = stream.ReadFloat(BinaryStream.Endianess.Little);
            _ = stream.ReadFloat(BinaryStream.Endianess.Little);
            _ = stream.ReadFloat(BinaryStream.Endianess.Little);
        }
    }
}
