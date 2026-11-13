using Zenith.Binary;

namespace Zenith.Raknet.Network;

public interface IPacket
{
    public byte Id { get; }

    public Span<byte> Encode()
    {
        return Array.Empty<byte>();
    }
    
    void Decode(ref BinaryStream stream);

    public static T From<T>(ref BinaryStream stream) where T : IPacket
    {
        var packet = Activator.CreateInstance<T>();
        packet.Decode(ref stream);
        stream.Dispose();
        return packet;
    }
}