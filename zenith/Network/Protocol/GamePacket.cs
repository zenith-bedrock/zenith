using System.IO;
using System.IO.Compression;
using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Network;
using Zenith.Raknet.Stream;
using Zenith.Session;

namespace Zenith.Network.Protocol;

class GamePacket : IPacket
{
    public byte Id => (byte)MessageIdentifier.Game;

    public byte Compression = PacketCompression.NOT_PRESENT;
    public List<DataPacket> Packets = new();

    public Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteByte(Id);
        if (Compression != PacketCompression.NOT_PRESENT) writer.WriteByte(Compression);

        var payloadWriter = new BinaryStream();
        foreach (var packet in Packets)
        {
            var buffer = packet.Encode();
            payloadWriter.WriteUnsignedVarInt(buffer.Length);
            payloadWriter.Write(buffer);
        }

        var uncompressed = payloadWriter.GetBufferDisposing();

        if (Compression == PacketCompression.ZLIB)
        {
            using var ms = new MemoryStream();
            using (var zlib = new ZLibStream(ms, CompressionLevel.Fastest))
            {
                zlib.Write(uncompressed);
            }
            writer.Write(ms.ToArray());
        }
        else
        {
            writer.Write(uncompressed);
        }

        return writer.GetBufferDisposing();
    }

    public void Decode(ref BinaryStream stream)
    {
        while (!stream.IsEndOfFile)
        {
            var length = stream.ReadUnsignedVarInt();
            Buffers.Add(stream.ReadSpan(length).ToArray());
        }
        stream.Dispose();
    }

    /// <summary>Sub-pacotes decodificados de um GamePacket recebido. Só populado por Decode.</summary>
    public List<byte[]> Buffers = new();
}

