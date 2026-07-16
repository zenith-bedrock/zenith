using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>PlayerAction (0x24) — destroy predict/creative sufficient for break path.</summary>
class PlayerActionPacket : DataPacket
{
    public const int ActionCreativeDestroy = 13;
    public const int ActionPredictDestroy = 26;
    /// <summary>Bedrock RESPAWN — death-screen client ready (ADR §40).</summary>
    public const int ActionRespawn = 7;
    /// <summary>Bedrock START_ITEM_USE_ON — expected noise; not logged at Debug.</summary>
    public const int ActionStartItemUseOn = 28;
    /// <summary>Bedrock STOP_ITEM_USE_ON — expected noise; not logged at Debug.</summary>
    public const int ActionStopItemUseOn = 29;

    public override int Id => (int)ProtocolInfo.PLAYER_ACTION_PACKET;

    public ulong ActorRuntimeId { get; set; }
    public int Action { get; set; }
    public int BlockX { get; set; }
    public int BlockY { get; set; }
    public int BlockZ { get; set; }
    public int ResultX { get; set; }
    public int ResultY { get; set; }
    public int ResultZ { get; set; }
    public int Face { get; set; }

    public override Span<byte> Encode() => Array.Empty<byte>();

    public override void Decode(ref BinaryStream stream)
    {
        ActorRuntimeId = (ulong)stream.ReadUnsignedVarLong();
        Action = stream.ReadVarInt();
        BlockX = stream.ReadVarInt();
        BlockY = stream.ReadVarInt();
        BlockZ = stream.ReadVarInt();
        ResultX = stream.ReadVarInt();
        ResultY = stream.ReadVarInt();
        ResultZ = stream.ReadVarInt();
        Face = stream.ReadVarInt();
    }
}
