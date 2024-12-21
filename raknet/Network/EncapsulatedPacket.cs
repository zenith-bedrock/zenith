using Zenith.Raknet.Stream;

namespace Zenith.Raknet.Network;

public class EncapsulatedPacket
{
    public record SplitPacketInfo(int Count, short ID, int Index);

    public const byte RELIABILITY_SHIFT = 5;
    public const byte RELIABILITY_FLAGS = 0b111 << RELIABILITY_SHIFT;

    public const byte SPLIT_FLAG = 0b00010000;

    public const int SPLIT_INFO_LENGTH = 4 + 2 + 4;

    public const byte UNRELIABLE = 0;
    public const byte UNRELIABLE_SEQUENCED = 1;
    public const byte RELIABLE = 2;
    public const byte RELIABLE_ORDERED = 3;
    public const byte RELIABLE_SEQUENCED = 4;

    public const byte UNRELIABLE_WITH_ACK_RECEIPT = 5;
    public const byte RELIABLE_WITH_ACK_RECEIPT = 6;
    public const byte RELIABLE_ORDERED_WITH_ACK_RECEIPT = 7;

    public const byte MAX_ORDER_CHANNELS = 32;

    public byte Reliability { get; set; }

    public uint MessageIndex { get; set; }
    public uint SequenceIndex { get; set; }

    public uint OrderIndex { get; set; }
    public byte OrderChannel { get; set; }

    public SplitPacketInfo? SplitInfo { get; set; } = null;

    public byte[] Buffer { get; set; }

    public static bool IsReliable(byte reliability)
    {
        return (
            reliability == RELIABLE ||
            reliability == RELIABLE_ORDERED ||
            reliability == RELIABLE_SEQUENCED ||
            reliability == RELIABLE_WITH_ACK_RECEIPT ||
            reliability == RELIABLE_ORDERED_WITH_ACK_RECEIPT
        );
    }

    public static bool IsSequenced(int reliability)
    {
        return (
            reliability == UNRELIABLE_SEQUENCED ||

            reliability == RELIABLE_SEQUENCED
        );
    }

    public static bool IsOrdered(int reliability)
    {
        return (
            reliability == RELIABLE_ORDERED ||
            reliability == RELIABLE_ORDERED_WITH_ACK_RECEIPT
        );
    }

    public static bool IsSequencedOrOrdered(int reliability)
    {
        return (
            reliability == UNRELIABLE_SEQUENCED ||
            reliability == RELIABLE_ORDERED ||
            reliability == RELIABLE_SEQUENCED ||
            reliability == RELIABLE_ORDERED_WITH_ACK_RECEIPT
        );
    }

    public Span<byte> Encode()
    {
        var writer = new BinaryStream();

        var flags = Reliability << RELIABILITY_SHIFT;
        if (SplitInfo is not null)
        {
            flags |= SPLIT_FLAG;
        }
        writer.WriteByte((byte)flags);
        writer.WriteShort((short)(Buffer.Length << 3));

        if (IsReliable(Reliability))
        {
            writer.WriteTriad(MessageIndex, BinaryStream.Endianess.Little);
        }

        if (IsSequenced(Reliability))
        {
            writer.WriteTriad(SequenceIndex, BinaryStream.Endianess.Little);
        }

        if (IsSequencedOrOrdered(Reliability))
        {
            writer.WriteTriad(OrderIndex, BinaryStream.Endianess.Little);
            writer.WriteByte(OrderChannel);
        }

        if (SplitInfo is not null)
        {
            writer.WriteInt(SplitInfo.Count);
            writer.WriteShort(SplitInfo.ID);
            writer.WriteInt(SplitInfo.Index);
        }

        writer.Write(Buffer);

        return writer.GetBufferDisposing();
    }

    public void Decode(BinaryStream stream)
    {
        var flags = stream.ReadByte();
        Reliability = (byte)((flags & RELIABILITY_FLAGS) >> RELIABILITY_SHIFT);
        var hasSplit = (flags & SPLIT_FLAG) != 0;

        var length = (int)Math.Ceiling((double)stream.ReadShort() / 8);

        if (IsReliable(Reliability))
        {
            MessageIndex = stream.ReadTriad(BinaryStream.Endianess.Little);
        }

        if (IsSequenced(Reliability))
        {
            SequenceIndex = stream.ReadTriad(BinaryStream.Endianess.Little);
        }

        if (IsSequencedOrOrdered(Reliability))
        {
            OrderIndex = stream.ReadTriad(BinaryStream.Endianess.Little);
            OrderChannel = stream.ReadByte();
        }

        if (hasSplit)
        {
            var splitCount = stream.ReadInt();
            var splitID = stream.ReadShort();
            var splitIndex = stream.ReadInt();
            SplitInfo = new(splitCount, splitID, splitIndex);
        }

        Buffer = stream.ReadSpan(length).ToArray();
    }
}