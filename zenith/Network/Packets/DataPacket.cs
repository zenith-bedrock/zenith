using Zenith.Raknet.Stream;

namespace Zenith.Network.Packets;

abstract class DataPacket
{
    public const int PID_MASK = 0x3ff;

    public const int SUBCLIENT_ID_MASK = 0x03;
    public const int SENDER_SUBCLIENT_ID_SHIFT = 10;
    public const int RECIPIENT_SUBCLIENT_ID_SHIFT = 12;

    public class HeaderInfo
    {
        public int Id { get; set; } = 0;
        public int SenderSubId = 0;
        public int RecipientSubId = 0;

        public void Decode(ref BinaryStream stream)
        {
            var header = stream.ReadUnsignedVarInt();
            Id = header & PID_MASK;
            SenderSubId = (header >> SENDER_SUBCLIENT_ID_SHIFT) & SUBCLIENT_ID_MASK;
            RecipientSubId = (header >> RECIPIENT_SUBCLIENT_ID_SHIFT) & SUBCLIENT_ID_MASK;
        }
    }

    public abstract int Id { get; }

    public HeaderInfo Header = new();

    public DataPacket()
    {
        Header.Id = Id;
    }

    public virtual Span<byte> Encode()
    {
        return Array.Empty<byte>();
    }

    public abstract void Decode(ref BinaryStream stream);

    public static T From<T>(ref BinaryStream stream) where T : DataPacket, new()
    {
        var packet = new T();
        packet.Decode(ref stream);
        stream.Dispose();
        return packet;
    }

}