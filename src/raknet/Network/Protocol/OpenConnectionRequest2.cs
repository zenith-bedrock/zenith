using System.Net;
using Zenith.Raknet.Extension;
using Zenith.Raknet.Stream;

namespace Zenith.Raknet.Network.Protocol;

public class OpenConnectionRequest2 : IPacket
{
    public byte Id => (byte)Enumerator.MessageIdentifier.OpenConnectionRequest2;

    public byte[] Magic { get; set; } = [];
    public IPEndPoint ServerAddress { get; set; } = new(IPAddress.Any, 0);
    public ushort MTUSize { get; set; }
    public ulong ClientGuid { get; set; }

    public void Decode(ref BinaryStream stream)
    {
        Magic = stream.ReadMagic();
        ServerAddress = stream.ReadIPEndPoint();
        MTUSize = stream.ReadUShort();
        ClientGuid = stream.ReadULong();
    }
}