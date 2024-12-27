using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Network;
using Zenith.Raknet.Stream;

namespace zenith.Network.Protocol;

class GamePacket : IPacket
{
    public byte Id => (byte)MessageIdentifier.Game;

    public List<byte[]> Buffers = new();

    public Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteByte(Id);
        foreach (var buffer in Buffers)
        {
            writer.WriteUnsignedVarInt(buffer.Length);
            writer.Write(buffer);
        }
        return writer.GetBufferDisposing();
    }

    public void Decode(BinaryStream stream)
    {
        while (!stream.IsEndOfFile)
        {
            var length = stream.ReadUnsignedVarInt();
            Buffers.Add(stream.ReadSpan(length).ToArray());
        }
        stream.Dispose();
    }
}

