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

    public Reliability Reliability { get; set; }

    public uint MessageIndex { get; set; }
    public uint SequenceIndex { get; set; }

    public uint OrderIndex { get; set; }
    public byte OrderChannel { get; set; }

    public SplitPacketInfo? SplitInfo { get; set; }

    public byte[] Buffer { get; private set; } = Array.Empty<byte>();

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
        
        writer.Write(Buffer);
        
        return writer.GetBufferDisposing();
    }

    public void Decode(BinaryStream stream)
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