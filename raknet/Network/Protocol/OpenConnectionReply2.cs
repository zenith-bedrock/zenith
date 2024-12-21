using System.Net;
using Zenith.Raknet.Stream;

namespace Zenith.Raknet.Network.Protocol;

public class OpenConnectionReply2 : IPacket
{
    public byte Id => (byte)Enumerator.MessageIdentifier.OpenConnectionReply2;

    public ulong ServerGuid { get; set; }
    public IPEndPoint ClientAddress { get; set; }
    public ushort MTUSize { get; set; }
    public bool ServerSecurity { get; set; }

    public byte[] Encode()
    {
        var writer = new BinaryStreamWriter();
        writer.WriteByte(Id);
        writer.WriteMagic();
        writer.WriteUInt64BE(ServerGuid);
        writer.WriteAddress(ClientAddress);
        writer.WriteUInt16BE(MTUSize);
        writer.WriteBool(ServerSecurity);
        return writer.GetBufferDisposing();
    }

    public void Decode(BinaryStreamReader stream) { }
}