using Zenith.Raknet.Extension;
using Zenith.Binary;
using static Zenith.Binary.BinaryStream;

namespace Zenith.Raknet.Network.Protocol;

public class UnconnectedPing : IPacket
{
    public byte Id => (byte)Enumerator.MessageIdentifier.UnconnectedPing;
    
    public ulong Time { get; set; }
    public byte[] Magic { get; set; } = []; // 16 bytes
    public ulong ClientGuid { get; set; }

    void IPacket.Decode(ref BinaryStream stream)
    {
        Time = stream.ReadULong();
        Magic = stream.ReadMagic();
        ClientGuid = stream.ReadULong();
    }
}