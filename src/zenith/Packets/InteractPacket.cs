using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>Interact (0x21) — decode focado em open_inventory.</summary>
sealed class InteractPacket : DataPacket
{
    public const int ActionOpenInventory = 6;

    public override int Id => (int)ProtocolInfo.INTERACT_PACKET;

    public int Action { get; set; }

    public override Span<byte> Encode() => Array.Empty<byte>();

    public override void Decode(ref BinaryStream stream)
    {
        Action = stream.ReadByte();
        stream.ReadUnsignedVarLong(); // target actor runtime
        if (stream.ReadBool())
        {
            _ = stream.ReadFloat(BinaryStream.Endianess.Little);
            _ = stream.ReadFloat(BinaryStream.Endianess.Little);
            _ = stream.ReadFloat(BinaryStream.Endianess.Little);
        }
    }
}
