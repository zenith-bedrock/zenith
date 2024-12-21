using System.Net;
using Zenith.Raknet.Stream;

namespace Zenith.Raknet.Network.Protocol;

public class OpenConnectionRequest2 : IPacket
{
    public byte Id => (byte)Enumerator.MessageIdentifier.OpenConnectionRequest2;

    public byte[] Magic { get; set; }
    public IPEndPoint ServerAddress { get; set; }
    public ushort MTUSize { get; set; }
    public ulong ClientGuid { get; set; }

    public void Decode(BinaryStreamReader stream)
    {
        Magic = stream.ReadMagic();
        ServerAddress = stream.ReadAddress();
        MTUSize = stream.ReadUInt16BE();
        ClientGuid = stream.ReadUInt64BE();
    }
}