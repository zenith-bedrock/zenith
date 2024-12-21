using System.Net;
using Zenith.Raknet.Extension;
using Zenith.Raknet.Stream;

namespace Zenith.Raknet.Network.Protocol;

public class OpenConnectionReply2 : IPacket
{
    public byte Id => (byte)Enumerator.MessageIdentifier.OpenConnectionReply2;
    
    public ulong ServerGuid { get; set; }
    public IPEndPoint ClientAddress { get; set; }
    public ushort MTUSize { get; set; }
    public bool ServerSecurity { get; set; }

    public Span<byte> Encode()
    {
        var stream = new BinaryStream();
        stream.WriteByte(Id);
        stream.WriteMagic();
        stream.WriteULong(ServerGuid);
        stream.WriteIPEndPoint(ClientAddress);
        stream.WriteUShort(MTUSize);
        stream.WriteBool(ServerSecurity);
        return stream.GetBufferDisposing();
    }

    public void Decode(BinaryStream stream) {}
}