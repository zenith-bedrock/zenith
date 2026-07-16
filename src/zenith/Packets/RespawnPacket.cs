using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>
/// Respawn (0x2d) — death → spawn handshake (ADR §40).
/// Server: Searching / Ready; client: ClientReady.
/// </summary>
sealed class RespawnPacket : DataPacket
{
    public const byte StateSearchingForSpawn = 0;
    public const byte StateReadyToSpawn = 1;
    public const byte StateClientReadyToSpawn = 2;

    public override int Id => (int)ProtocolInfo.RESPAWN_PACKET;

    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }
    public byte State { get; set; }
    public ulong EntityRuntimeId { get; set; }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteFloat(PositionX, BinaryStream.Endianess.Little);
        writer.WriteFloat(PositionY, BinaryStream.Endianess.Little);
        writer.WriteFloat(PositionZ, BinaryStream.Endianess.Little);
        writer.WriteByte(State);
        writer.WriteUnsignedVarLong((long)EntityRuntimeId);
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream)
    {
        PositionX = stream.ReadFloat(BinaryStream.Endianess.Little);
        PositionY = stream.ReadFloat(BinaryStream.Endianess.Little);
        PositionZ = stream.ReadFloat(BinaryStream.Endianess.Little);
        State = stream.ReadByte();
        EntityRuntimeId = (ulong)stream.ReadUnsignedVarLong();
    }
}
