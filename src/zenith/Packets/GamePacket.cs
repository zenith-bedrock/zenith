using System.IO;
using System.IO.Compression;
using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Network;
using Zenith.Raknet.Stream;

namespace Zenith.Packets;

class GamePacket : IPacket
{
    public byte Id => (byte)MessageIdentifier.Game;

    /// <summary>
    /// Wire compression algorithm preference. After NetworkSettings, typically
    /// <see cref="PacketCompression.ZLIB"/>; under-threshold batches use
    /// <see cref="PacketCompression.NONE"/> (0xff) + raw payload.
    /// </summary>
    public byte Compression = PacketCompression.NOT_PRESENT;

    /// <summary>Batch size at which ZLIB/flate kicks in (from NetworkSettings).</summary>
    public int CompressionThreshold = 256;

    public List<DataPacket> Packets = new();

    public Span<byte> Encode()
    {
        var writer = new BinaryStream();
        writer.WriteByte(Id);

        var payloadWriter = new BinaryStream();
        foreach (var packet in Packets)
        {
            var buffer = packet.Encode();
            payloadWriter.WriteUnsignedVarInt(buffer.Length);
            payloadWriter.Write(buffer);
        }

        var uncompressed = payloadWriter.GetBufferDisposing();

        if (Compression == PacketCompression.NOT_PRESENT)
        {
            // Pré-NetworkSettings: sem byte de algoritmo.
            writer.Write(uncompressed);
        }
        else if (Compression == PacketCompression.ZLIB && uncompressed.Length < CompressionThreshold)
        {
            // Below threshold: algorithm 0xff + uncompressed batch (no flate).
            writer.WriteByte(PacketCompression.NONE);
            writer.Write(uncompressed);
        }
        else if (Compression == PacketCompression.ZLIB)
        {
            writer.WriteByte(PacketCompression.ZLIB);
            using var ms = new MemoryStream();
            using (var deflate = new DeflateStream(ms, CompressionLevel.Fastest, leaveOpen: true))
            {
                deflate.Write(uncompressed);
            }

            writer.Write(ms.GetBuffer().AsSpan(0, (int)ms.Length));
        }
        else
        {
            writer.WriteByte(Compression);
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

