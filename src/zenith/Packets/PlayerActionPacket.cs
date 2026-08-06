using Zenith.Packets.Generation;

namespace Zenith.Packets;

/// <summary>PlayerAction (0x24) — destroy predict/creative sufficient for break path.</summary>
[GamePacket((int)ProtocolInfo.PLAYER_ACTION_PACKET)]
sealed partial class PlayerActionPacket : DataPacket
{
    public const int ActionCreativeDestroy = 13;
    public const int ActionPredictDestroy = 26;
    /// <summary>Bedrock RESPAWN — death-screen client ready (ADR §40).</summary>
    public const int ActionRespawn = 7;
    /// <summary>Bedrock START_ITEM_USE_ON — expected noise; not logged at Debug.</summary>
    public const int ActionStartItemUseOn = 28;
    /// <summary>Bedrock STOP_ITEM_USE_ON — expected noise; not logged at Debug.</summary>
    public const int ActionStopItemUseOn = 29;

    [WireVar]
    public ulong ActorRuntimeId { get; set; }

    [WireVar]
    public int Action { get; set; }

    [WireVar]
    public int BlockX { get; set; }

    [WireVar]
    public int BlockY { get; set; }

    [WireVar]
    public int BlockZ { get; set; }

    [WireVar]
    public int ResultX { get; set; }

    [WireVar]
    public int ResultY { get; set; }

    [WireVar]
    public int ResultZ { get; set; }

    [WireVar]
    public int Face { get; set; }
}
