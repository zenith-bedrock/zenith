using Zenith.Raknet.Stream;

namespace Zenith.Network.Packets;

/// <summary>PlayerAction (0x24) — destroy predict/creative sufficient for break path.</summary>
class PlayerActionPacket : DataPacket
{
    public const int ActionCreativeDestroy = 13;
    public const int ActionPredictDestroy = 26;

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
