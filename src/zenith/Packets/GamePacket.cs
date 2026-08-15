using System.IO;
using System.IO.Compression;
using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Stream;

namespace Zenith.Packets;

/// <summary>
/// Outbound Bedrock batch envelope only (ADR §… Phase XXVII). Inbound batch framing is parsed
/// directly by <see cref="Zenith.Session.NetworkSession.HandleGamePacket"/> via bounded
/// <see cref="BinaryStream.ReadSubstream"/> windows over the same backing buffer — each contained
/// DataPacket used to get its own <c>byte[]</c> copy here just to be handed to a handler that
/// immediately wrapped it in a fresh <see cref="BinaryStream"/> and consumed it synchronously; that
/// per-subpacket array was pure copy pressure with no ownership need. Encode/decode were never a
/// symmetrical pair to begin with (outbound built <see cref="DataPacket"/> objects, inbound only
/// ever needed raw byte windows), so splitting them is not a design compromise.
/// </summary>
class GamePacket
{
    public const byte GameId = (byte)Zenith.Raknet.Enumerator.MessageIdentifier.Game;

    /// <summary>
    /// Wire compression algorithm preference. After NetworkSettings, typically
    /// <see cref="PacketCompression.ZLIB"/>; under-threshold batches use
    /// <see cref="PacketCompression.NONE"/> (0xff) + raw payload.
    /// </summary>
    public byte Compression = PacketCompression.NOT_PRESENT;

    /// <summary>Batch size at which ZLIB/flate kicks in (from NetworkSettings).</summary>
    public int CompressionThreshold = 256;

    public List<DataPacket> Packets = new();

    public Span<byte> Encode() => EncodeOwned();

    /// <summary>Owned buffer for Frame send — avoids <c>Encode().ToArray()</c> double copy.</summary>
    public byte[] EncodeOwned()
    {
        var writer = new BinaryStream();
        writer.WriteByte(GameId);

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

        return writer.TakeOwnedBuffer();
    }

}

