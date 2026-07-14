namespace Zenith.Network.Packets;

/// <summary>Bits / camadas do abilities payload Bedrock (wire).</summary>
static class AbilityBits
{
    public const int Build = 0;
    public const int Mine = 1;
    public const int DoorsAndSwitches = 2;
    public const int OpenContainers = 3;
    public const int AttackPlayers = 4;
    public const int AttackMobs = 5;
    public const int WalkSpeed = 14;

    public const int Count = 19;
    public const ushort LayerBase = 1;

    public const byte PlayerPermissionMember = 1;
    public const byte CommandPermissionNormal = 0;

    public static uint Bit(int index) => 1u << index;
}
