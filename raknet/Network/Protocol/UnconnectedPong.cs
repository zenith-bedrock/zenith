using Zenith.Raknet.Stream;
using static Zenith.Raknet.Stream.BinaryStream;

namespace Zenith.Raknet.Network.Protocol;

public class UnconnectedPong : IPacket
{
    public byte Id => (byte)Enumerator.MessageIdentifier.UnconnectedPong;
    
    public ulong Time { get; set; }
    public ulong ServerGuid { get; set; }
    public required string Message { get; set; }

    public Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteByte(Id);
        writer.WriteULong(Time);
        writer.WriteULong(ServerGuid);
        writer.WriteMagic();
        writer.WriteString(Message);
        return writer.GetBufferDisposing();
    }

    public void Decode(BinaryStream stream) {}
}