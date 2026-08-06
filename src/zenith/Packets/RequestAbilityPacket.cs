using Zenith.Packets.Generation;

namespace Zenith.Packets;

/// <summary>
/// RequestAbility (0xB8) — client toggle (FLYING). Decode only; AbilityValue always reads type+bool+float.
/// </summary>
[GamePacket((int)ProtocolInfo.REQUEST_ABILITY_PACKET)]
sealed partial class RequestAbilityPacket : DataPacket
{
    public const byte ValueTypeBool = 1;
    public const byte ValueTypeFloat = 2;

    [WireVar]
    public int Ability { get; set; }

    [Wire]
    public byte ValueType { get; set; }

    [Wire]
    public bool BoolValue { get; set; }

    [Wire(Zenith.Raknet.Stream.BinaryStream.Endianess.Little)]
    public float FloatValue { get; set; }
}
