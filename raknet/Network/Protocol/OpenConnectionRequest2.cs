using Zenith.Raknet.Extension;
using Zenith.Raknet.Stream;

namespace Zenith.Raknet.Network.Protocol;

public class OpenConnectionRequest2 : IPacket
{
    public byte Id => (byte)Enumerator.MessageIdentifier.OpenConnectionRequest2;

    public byte[] Magic { get; set; }
    public System.Net.IPEndPoint ServerAddress { get; set; }
    public ushort MTUSize { get; set; }
    public ulong ClientGuid { get; set; }

    public void Decode(BinaryStream stream)
    {
        Magic = stream.ReadMagic();
        ServerAddress = stream.ReadIPEndPoint();
        MTUSize = stream.ReadUShort();
        ClientGuid = stream.ReadULong();
    }
}