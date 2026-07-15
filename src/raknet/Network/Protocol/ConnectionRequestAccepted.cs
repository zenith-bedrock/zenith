using System.Net;
using Zenith.Raknet.Extension;
using Zenith.Raknet.Stream;

namespace Zenith.Raknet.Network.Protocol;

public class ConnectionRequestAccepted : IPacket
{
    public byte Id => (byte)Enumerator.MessageIdentifier.ConnectionRequestAccepted;

    public IPEndPoint Address { get; set; } = new(IPAddress.Any, 0);
    public short SystemIndex { get; set; }
    public ulong SendPingTime { get; set; }
    public ulong SendPongTime { get; set; }

    public Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteByte(Id);
        writer.WriteIPEndPoint(Address);
        writer.WriteShort(SystemIndex);
        writer.WriteULong(SendPingTime);
        writer.WriteULong(SendPongTime);
        return writer.GetBufferDisposing();
    }

    public void Decode(ref BinaryStream stream) { }
}