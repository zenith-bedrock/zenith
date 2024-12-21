using Zenith.Raknet.Stream;

namespace Zenith.Raknet.Network.Protocol;

public class UnconnectedPing : IPacket
{
    public byte Id => (byte)Enumerator.MessageIdentifier.UnconnectedPing;
    
    public ulong Time { get; set; }
    public byte[] Magic { get; set; } // 16 bytes
    public ulong ClientGuid { get; set; }

    void IPacket.Decode(BinaryStreamReader stream)
    {
        Time = stream.ReadUInt64BE();
        Magic = stream.ReadMagic();
        ClientGuid = stream.ReadUInt64BE();
    }
}