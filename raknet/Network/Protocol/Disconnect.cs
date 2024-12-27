using Zenith.Raknet.Stream;

namespace Zenith.Raknet.Network.Protocol;

public class Disconnect : IPacket
{
    public byte Id => (byte)Enumerator.MessageIdentifier.ConnectedPong;

    public Span<byte> Encode()
    {
        return new byte[] { Id };
    }

    public void Decode(BinaryStream stream) { }
}