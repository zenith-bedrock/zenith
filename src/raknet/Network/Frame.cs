using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Stream;

namespace Zenith.Raknet.Network;

public class Frame
{
    public record SplitPacketInfo(int Count, short Id, int Index);

    public const byte RELIABILITY_SHIFT = 5;
    public const byte RELIABILITY_FLAGS = 0b111 << RELIABILITY_SHIFT;

    public const byte SPLIT_FLAG = 0b00010000;
    public const int SPLIT_INFO_LENGTH = 4 + 2 + 4;

    public const byte MAX_ORDER_CHANNELS = 32;

    /// <summary>Single source of truth for the OrderChannel bound both the send path
    /// (SendFrameLocked) and the receive path (HandleFrame) must agree on - the
    /// OutputOrderIndex/OutputSequenceIndex/InputOrderIndex/InputHighestSequenceIndex arrays are
    /// all sized exactly MAX_ORDER_CHANNELS, so an out-of-range channel indexes past the end.
    /// The two call sites used to compare against this bound with different, independently
    /// hand-written expressions (`&gt; 31` vs `&gt;= MAX_ORDER_CHANNELS`) that could silently
    /// desync if this constant ever changed.</summary>
    public static bool IsValidOrderChannel(byte channel) => channel < MAX_ORDER_CHANNELS;

    public Reliability Reliability { get; set; }

    public uint MessageIndex { get; set; }
    public uint SequenceIndex { get; set; }

    public uint OrderIndex { get; set; }
    public byte OrderChannel { get; set; }

    public SplitPacketInfo? SplitInfo { get; set; }

    public ReadOnlyMemory<byte> Buffer { get; set; } = ReadOnlyMemory<byte>.Empty;

    public bool IsSplit() => SplitInfo is not null;

    public int GetByteLength()
    {
        var length = Buffer.Length + 3;
        if (IsReliable(Reliability)) length += 3;
        if (IsSequenced(Reliability)) length += 3;
        if (IsOrdered(Reliability)) length += 4;
        if (IsSplit()) length += 10;
        return length;
    }

    public static bool IsReliable(Reliability reliability)
    {
        return reliability switch
        {
            Reliability.Reliable or Reliability.ReliableOrdered or Reliability.ReliableSequenced or
                Reliability.ReliableWithAckReceipt or Reliability.ReliableOrderedWithAckReceipt => true,
            _ => false
        };
    }

    public static bool IsSequenced(Reliability reliability)
    {
        return reliability switch
        {
            Reliability.UnreliableSequenced or Reliability.ReliableSequenced => true,
            _ => false
        };
    }

    public static bool IsOrdered(Reliability reliability)
    {
        return reliability switch
        {
            Reliability.ReliableOrdered or Reliability.ReliableOrderedWithAckReceipt => true,
            _ => false
        };
    }

    public static bool IsSequencedOrOrdered(Reliability reliability)
    {
        return reliability switch
        {
            Reliability.UnreliableSequenced or Reliability.ReliableOrdered or
                Reliability.ReliableSequenced or Reliability.ReliableOrderedWithAckReceipt => true,
            _ => false
        };
    }

    public Span<byte> Encode()
    {
        var writer = new BinaryStream();

        var flags = (byte)((byte)Reliability << RELIABILITY_SHIFT);
        if (SplitInfo is not null)
        {
            flags |= SPLIT_FLAG;
        }

        writer.WriteByte(flags);
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
            writer.WriteShort(SplitInfo.Id);
            writer.WriteInt(SplitInfo.Index);
        }

        writer.Write(Buffer.Span);

        return writer.GetBufferDisposing();
    }

    public void Decode(ref BinaryStream stream)
    {
        var flags = stream.ReadByte();
        Reliability = (Reliability)((flags & RELIABILITY_FLAGS) >> RELIABILITY_SHIFT);
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