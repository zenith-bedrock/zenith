using System.IO.Compression;
using BenchmarkDotNet.Attributes;
using Zenith.Packets;
using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Network;
using Zenith.Raknet.Stream;

namespace Zenith.Benchmarks;

/// <summary>
/// Phase XXVII — representative pipeline measurements for the inbound hot path (Part 1 of the
/// phase brief), not just BinaryStream primitives in isolation. Covers A (FrameSet inbound decode)
/// and B (GamePacket inbound batch decode/dispatch) — the two paths this phase actually changed.
/// Frame.Decode payload materialization, outbound fragmentation and split reassembly were evaluated
/// (see docs/history/phases/phase-xxvii-network-hot-path-findings.md) but not changed this pass, so
/// they are not separately benchmarked here — their cost is unchanged from before this phase.
/// </summary>
[MemoryDiagnoser]
public class NetworkHotPathBenchmarks
{
    // --- A. FrameSet inbound decode ---

    private byte[] _frameSet1x64 = null!;
    private byte[] _frameSet8x256 = null!;
    private byte[] _frameSet32x1024 = null!;

    // --- B. GamePacket inbound batch decode/dispatch ---

    private byte[] _batch1 = null!;
    private byte[] _batch4 = null!;
    private byte[] _batch16 = null!;
    private byte[] _batch32 = null!;
    private byte[] _batchZlib32 = null!;

    [GlobalSetup]
    public void Setup()
    {
        _frameSet1x64 = BuildFrameSet(1, 64);
        _frameSet8x256 = BuildFrameSet(8, 256);
        _frameSet32x1024 = BuildFrameSet(32, 1024);

        _batch1 = BuildUncompressedBatch(1);
        _batch4 = BuildUncompressedBatch(4);
        _batch16 = BuildUncompressedBatch(16);
        _batch32 = BuildUncompressedBatch(32);
        _batchZlib32 = BuildZlibBatch(32);
    }

    [Benchmark(Description = "FrameSet decode: 1 frame x 64B")]
    public int FrameSetDecode_1x64() => DecodeFrameSet(_frameSet1x64);

    [Benchmark(Description = "FrameSet decode: 8 frames x 256B")]
    public int FrameSetDecode_8x256() => DecodeFrameSet(_frameSet8x256);

    [Benchmark(Description = "FrameSet decode: 32 frames x 1KiB")]
    public int FrameSetDecode_32x1024() => DecodeFrameSet(_frameSet32x1024);

    [Benchmark(Description = "GameBatch dispatch: 1 subpacket")]
    public int GameBatchDispatch_1() => DispatchBatch(_batch1);

    [Benchmark(Description = "GameBatch dispatch: 4 subpackets")]
    public int GameBatchDispatch_4() => DispatchBatch(_batch4);

    [Benchmark(Description = "GameBatch dispatch: 16 subpackets")]
    public int GameBatchDispatch_16() => DispatchBatch(_batch16);

    [Benchmark(Description = "GameBatch dispatch: 32 subpackets")]
    public int GameBatchDispatch_32() => DispatchBatch(_batch32);

    [Benchmark(Description = "GameBatch dispatch: 32 subpackets, ZLIB compressed")]
    public int GameBatchDispatch_32_Zlib() => DispatchZlibBatch(_batchZlib32);

    // --- helpers ---

    private static int DecodeFrameSet(byte[] data)
    {
        var stream = new BinaryStream(data, 1, data.Length - 1); // skip the BitFlags.Valid byte, matches RakNetSession.Incoming
        var frameSet = new FrameSet();
        frameSet.Decode(ref stream);
        return frameSet.Packets.Count;
    }

    /// <summary>
    /// Mirrors NetworkSession.HandleGamePacket's inbound loop for the uncompressed (NOT_PRESENT)
    /// case without needing a full NetworkSession/ServerContext — this benchmark targets the batch
    /// framing cost itself (ReadSubstream windows, header decode), not gameplay dispatch.
    /// </summary>
    private static int DispatchBatch(byte[] batch)
    {
        var stream = new BinaryStream(batch);
        var count = 0;
        while (!stream.IsEndOfFile)
        {
            var length = stream.ReadUnsignedVarInt();
            var subpacket = stream.ReadSubstream(length);
            var header = new DataPacket.HeaderInfo();
            header.Decode(ref subpacket);
            count += header.Id;
            subpacket.Dispose();
        }
        return count;
    }

    private static int DispatchZlibBatch(byte[] wireBatch)
    {
        var stream = new BinaryStream(wireBatch);
        var compressionType = stream.PeekByte();
        if (compressionType is PacketCompression.ZLIB or PacketCompression.SNAPPY or PacketCompression.NONE)
            stream.ReadByte();

        using var memoryStream = new MemoryStream(stream.Buffer, stream.Offset, stream.Length - stream.Offset, writable: false);
        using var inflater = new DeflateStream(memoryStream, CompressionMode.Decompress);
        var decompressed = Zenith.Session.NetworkSession.InflateToPooledBuffer(inflater, out var decompressedLength);
        try
        {
            var payload = new BinaryStream(decompressed, decompressedLength);
            var count = 0;
            while (!payload.IsEndOfFile)
            {
                var length = payload.ReadUnsignedVarInt();
                var subpacket = payload.ReadSubstream(length);
                var header = new DataPacket.HeaderInfo();
                header.Decode(ref subpacket);
                count += header.Id;
                subpacket.Dispose();
            }
            return count;
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(decompressed);
        }
    }

    private static byte[] BuildFrameSet(int frameCount, int payloadSize)
    {
        var frameSet = new FrameSet { Sequence = 1 };
        var rng = new Random(42);
        for (var i = 0; i < frameCount; i++)
        {
            var payload = new byte[payloadSize];
            rng.NextBytes(payload);
            frameSet.Packets.Add(new Frame
            {
                Reliability = Reliability.Unreliable,
                Buffer = payload
            });
        }

        return frameSet.Encode().ToArray();
    }

    private static byte[] BuildUncompressedBatch(int subpacketCount)
    {
        var writer = new BinaryStream();
        for (var i = 0; i < subpacketCount; i++)
        {
            var body = new BinaryStream();
            body.WriteUnsignedVarInt(10 + i); // fake DataPacket header id
            body.WriteByte((byte)i);
            body.WriteByte((byte)(i + 1));
            body.WriteByte((byte)(i + 2));
            var bodyBytes = body.TakeOwnedBuffer();

            writer.WriteUnsignedVarInt(bodyBytes.Length);
            writer.Write(bodyBytes);
        }

        return writer.TakeOwnedBuffer();
    }

    private static byte[] BuildZlibBatch(int subpacketCount)
    {
        var uncompressed = BuildUncompressedBatch(subpacketCount);
        using var compressedStream = new MemoryStream();
        using (var deflate = new DeflateStream(compressedStream, CompressionLevel.Fastest, leaveOpen: true))
            deflate.Write(uncompressed);

        var writer = new BinaryStream();
        writer.WriteByte(PacketCompression.ZLIB);
        writer.Write(compressedStream.ToArray());
        return writer.TakeOwnedBuffer();
    }
}
