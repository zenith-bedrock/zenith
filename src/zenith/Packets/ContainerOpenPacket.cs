using Zenith.Packets.Generation;

namespace Zenith.Packets;

/// <summary>ContainerOpen (0x2e) — abre UI de inventário / container.</summary>
[GamePacket((int)ProtocolInfo.CONTAINER_OPEN_PACKET)]
sealed partial class ContainerOpenPacket : DataPacket
{
    public const byte WindowTypeChest = 0;
    /// <summary>gophertunnel <c>ContainerTypeWorkbench</c> = 1 (ADR §139).</summary>
    public const byte WindowTypeWorkbench = 1;
    public const byte WindowTypeInventory = 0xff;

    [Wire]
    public byte WindowId { get; set; }

    [Wire]
    public byte WindowType { get; set; } = WindowTypeInventory;

    [WireVar]
    public int BlockX { get; set; }

    [WireVar]
    public int BlockY { get; set; }

    [WireVar]
    public int BlockZ { get; set; }

    [WireVar]
    public long ActorUniqueId { get; set; } = -1;
}
