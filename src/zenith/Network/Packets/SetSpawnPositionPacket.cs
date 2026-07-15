using Zenith.Raknet.Stream;

namespace Zenith.Network.Packets;

/// <summary>
/// Tells the client where the player's (or world's) spawn/respawn point is - used for the
/// compass and the respawn screen. Cheap to send alongside the rest of the spawn sequence,
/// even before there's a real bed/respawn-anchor system.
/// </summary>
class SetSpawnPositionPacket : DataPacket
{
    public override int Id => (int)ProtocolInfo.SET_SPAWN_POSITION_PACKET;

    public const int TYPE_PLAYER_SPAWN = 0;
    public const int TYPE_WORLD_SPAWN = 1;

    public int SpawnType { get; set; } = TYPE_PLAYER_SPAWN;
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public int Dimension { get; set; } = DimensionId.Overworld;

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteVarInt(SpawnType);
        writer.WriteVarInt(X);
        writer.WriteVarInt(Y);
        writer.WriteVarInt(Z);
        writer.WriteVarInt(Dimension);
        // "Causing block position" (bed/respawn anchor) - no such system yet, so it just
        // mirrors the spawn position itself.
        writer.WriteVarInt(X);
        writer.WriteVarInt(Y);
        writer.WriteVarInt(Z);
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream) { }
}
