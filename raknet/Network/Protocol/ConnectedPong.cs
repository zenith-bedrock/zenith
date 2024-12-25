using Zenith.Raknet.Stream;

namespace Zenith.Raknet.Network.Protocol;

public class ConnectedPong : IPacket
{
    public byte Id => (byte)Enumerator.MessageIdentifier.ConnectedPong;

    public ulong SendPingTime { get; set; }
    public ulong SendPongTime { get; set; }

    public Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteByte(Id);
        writer.WriteULong(SendPingTime);
        writer.WriteULong(SendPongTime);
        return writer.GetBufferDisposing();
    }

    public void Decode(BinaryStream stream) { }
}