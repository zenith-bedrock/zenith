using Zenith.Raknet.Stream;

namespace Zenith.Raknet.Network;

public interface IPacket
{
    public byte Id { get; }

    public byte[] Encode()
    {
        return Array.Empty<byte>();
    }
    
    void Decode(BinaryStreamReader stream);

    public static T From<T>(BinaryStreamReader stream) where T : IPacket
    {
        var packet = (T) Activator.CreateInstance(typeof(T))!;
        packet.Decode(stream);
        stream.Dispose();
        return packet;
    }
}