using Zenith.Raknet.Stream;

namespace Zenith.Raknet.Network.Protocol;

public class OpenConnectionRequest1 : IPacket
{
    public byte Id => (byte)Enumerator.MessageIdentifier.OpenConnectionRequest1;

    public byte[] Magic { get; set; }
    public byte Protocol { get; set; }
    public ushort MTUSize { get; set; }

    public void Decode(BinaryStreamReader stream)
    {
        Magic = stream.ReadMagic();
        Protocol = stream.ReadByte();
        MTUSize = (ushort)(stream.Length - 17);
    }
}