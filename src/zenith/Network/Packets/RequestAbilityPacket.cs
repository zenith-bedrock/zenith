using Zenith.Raknet.Stream;

namespace Zenith.Network.Packets;

/// <summary>
/// RequestAbility (0xB8) — client toggle (FLYING). Decode only; AbilityValue always reads type+bool+float.
/// </summary>
sealed class RequestAbilityPacket : DataPacket
{
    public const byte ValueTypeBool = 1;
    public const byte ValueTypeFloat = 2;

    public override int Id => (int)ProtocolInfo.REQUEST_ABILITY_PACKET;

    public int Ability { get; set; }
    public byte ValueType { get; set; }
    public bool BoolValue { get; set; }
    public float FloatValue { get; set; }

    public override Span<byte> Encode() => [];

    public override void Decode(ref BinaryStream stream)
    {
        Ability = stream.ReadVarInt();
        ValueType = stream.ReadByte();
        BoolValue = stream.ReadBool();
        FloatValue = stream.ReadFloat(BinaryStream.Endianess.Little);
    }
}
