using Zenith.Raknet.Stream;

namespace Zenith.Network.Packets;

/// <summary>
/// Prefixo seguro do AuthInput: pitch, yaw, position. O restante do payload é ignorado
/// para não depender de campos opcionais que mudam entre versões.
/// </summary>
class PlayerAuthInputPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.PLAYER_AUTH_INPUT_PACKET;

    public float Pitch { get; set; }
    public float Yaw { get; set; }
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }

    public override Span<byte> Encode() => Array.Empty<byte>();

    public override void Decode(ref BinaryStream stream)
    {
        Pitch = stream.ReadFloat(BinaryStream.Endianess.Little);
        Yaw = stream.ReadFloat(BinaryStream.Endianess.Little);
        PositionX = stream.ReadFloat(BinaryStream.Endianess.Little);
        PositionY = stream.ReadFloat(BinaryStream.Endianess.Little);
        PositionZ = stream.ReadFloat(BinaryStream.Endianess.Little);
        // restante do AuthInput: ignorado de propósito (prefix decode).
    }
}
