using Zenith.Raknet.Stream;

namespace Zenith.Raknet.Network.Protocol;

public class UnconnectedPong : IPacket
{
    public byte Id => (byte)Enumerator.MessageIdentifier.UnconnectedPong;
    
    public ulong Time { get; set; }
    public ulong ServerGuid { get; set; }
    public required string Message { get; set; }

    public byte[] Encode()
    {
        var writer = new BinaryStreamWriter();
        writer.WriteByte(Id);
        writer.WriteUInt64BE(Time);
        writer.WriteUInt64BE(ServerGuid);
        writer.WriteMagic();
        writer.WriteString(Message);
        return writer.GetBufferDisposing();
    }

    public void Decode(BinaryStreamReader stream) {}
}