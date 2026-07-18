using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>
/// BlockEvent (0x1a) — S→C block-local FX (chest lid). ADR §28 adendo.
/// </summary>
sealed class BlockEventPacket : DataPacket
{
    /// <summary>gophertunnel / PM: ChangeChestState — EventData 1 open, 0 close.</summary>
    public const int EventChangeChestState = 1;

    public const int ChestStateClosed = 0;
    public const int ChestStateOpen = 1;

    public override int Id => (int)ProtocolInfo.BLOCK_EVENT_PACKET;

    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public int EventType { get; set; }
    public int EventData { get; set; }

    public override Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(Id);
        writer.WriteVarInt(X);
        writer.WriteVarInt(Y);
        writer.WriteVarInt(Z);
        writer.WriteVarInt(EventType);
        writer.WriteVarInt(EventData);
        return writer.GetBufferDisposing();
    }

    public override void Decode(ref BinaryStream stream)
    {
        X = stream.ReadVarInt();
        Y = stream.ReadVarInt();
        Z = stream.ReadVarInt();
        EventType = stream.ReadVarInt();
        EventData = stream.ReadVarInt();
    }
}
