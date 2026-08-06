using Zenith.Packets.Generation;

namespace Zenith.Packets;

/// <summary>
/// BlockEvent (0x1a) — S→C block-local FX (chest lid). ADR §28 adendo.
/// </summary>
[GamePacket((int)ProtocolInfo.BLOCK_EVENT_PACKET)]
sealed partial class BlockEventPacket : DataPacket
{
    /// <summary>gophertunnel / PM: ChangeChestState — EventData 1 open, 0 close.</summary>
    public const int EventChangeChestState = 1;

    public const int ChestStateClosed = 0;
    public const int ChestStateOpen = 1;

    [WireVar]
    public int X { get; set; }

    [WireVar]
    public int Y { get; set; }

    [WireVar]
    public int Z { get; set; }

    [WireVar]
    public int EventType { get; set; }

    [WireVar]
    public int EventData { get; set; }
}
