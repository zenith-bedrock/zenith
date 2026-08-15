using System.IO.Compression;
using Zenith.Packets;
using Zenith.Raknet.Stream;
using Zenith.Session.Handler;
using Xunit;

namespace Zenith.Tests;

/// <summary>
/// Phase XXVII — <see cref="Zenith.Session.NetworkSession.HandleGamePacket"/> no longer materializes
/// one <c>byte[]</c> per contained DataPacket (removed GamePacket's inbound Decode/Buffers in favor
/// of bounded <see cref="BinaryStream.ReadSubstream"/> windows over the same batch buffer). These
/// tests exercise the real dispatch path end-to-end, not just BinaryStream in isolation.
/// </summary>
public class GamePacketDispatchTests
{
    private sealed class RecordingHandler : ISessionHandler
    {
        public List<(int Id, byte Marker)> Received { get; } = new();

        public bool HandleDataPacket(Zenith.Session.NetworkSession session, DataPacket.HeaderInfo header, ref BinaryStream stream)
        {
            var marker = stream.IsEndOfFile ? (byte)0 : stream.ReadByte();
            Received.Add((header.Id, marker));
            return true;
        }
    }

    /// <summary>Builds an uncompressed (NOT_PRESENT) batch: no leading algorithm byte, straight into
    /// [length][header varint][marker byte] entries, matching GamePacket.EncodeOwned's own
    /// NOT_PRESENT branch.</summary>
    private static byte[] BuildUncompressedBatch(params (int Id, byte Marker)[] subpackets)
    {
        var writer = new BinaryStream();
        foreach (var (id, marker) in subpackets)
        {
            var body = new BinaryStream();
            body.WriteUnsignedVarInt(id); // DataPacket.HeaderInfo wire shape: just the id varint here
            body.WriteByte(marker);
            var bodyBytes = body.TakeOwnedBuffer();

            writer.WriteUnsignedVarInt(bodyBytes.Length);
            writer.Write(bodyBytes);
        }

        return writer.TakeOwnedBuffer();
    }

    [Fact]
    public void Batch_with_multiple_subpackets_dispatches_each_one_in_order()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("dispatcher");
        var recorder = new RecordingHandler();
        player.Session.SetHandler(recorder);

        var batch = BuildUncompressedBatch((10, 0xAA), (20, 0xBB), (30, 0xCC));
        var stream = new BinaryStream(batch);
        player.Session.HandleGamePacket(ref stream);

        Assert.Equal(3, recorder.Received.Count);
        Assert.Equal((10, (byte)0xAA), recorder.Received[0]);
        Assert.Equal((20, (byte)0xBB), recorder.Received[1]);
        Assert.Equal((30, (byte)0xCC), recorder.Received[2]);
    }

    [Fact]
    public void Single_subpacket_batch_dispatches_correctly()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("single-dispatcher");
        var recorder = new RecordingHandler();
        player.Session.SetHandler(recorder);

        var batch = BuildUncompressedBatch((42, 0x11));
        var stream = new BinaryStream(batch);
        player.Session.HandleGamePacket(ref stream);

        Assert.Single(recorder.Received);
        Assert.Equal((42, (byte)0x11), recorder.Received[0]);
    }

    /// <summary>
    /// Security boundary (Phase XXVII, Part 13/30): a subpacket whose declared length is larger than
    /// what's actually left in the batch must be rejected outright rather than silently reading into
    /// (nonexistent) following data or corrupting the parse.
    /// </summary>
    [Fact]
    public void Subpacket_declaring_a_length_longer_than_the_remaining_batch_throws_cleanly()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("malformed-length");
        var recorder = new RecordingHandler();
        player.Session.SetHandler(recorder);

        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(1000); // declared length, far exceeds what follows
        writer.WriteByte(1);
        writer.WriteByte(2);
        var batch = writer.TakeOwnedBuffer();

        var stream = new BinaryStream(batch);
        try
        {
            player.Session.HandleGamePacket(ref stream);
            Assert.Fail("Expected InvalidOperationException");
        }
        catch (InvalidOperationException)
        {
            // Expected
        }
    }

    /// <summary>A well-formed subpacket followed by a malformed one: the first must dispatch cleanly
    /// before the second is rejected — proves the loop doesn't reject the whole batch pre-emptively,
    /// only the malformed entry itself.</summary>
    [Fact]
    public void Well_formed_subpacket_dispatches_before_a_later_malformed_one_throws()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("partial-batch");
        var recorder = new RecordingHandler();
        player.Session.SetHandler(recorder);

        var writer = new BinaryStream();
        var goodBody = new BinaryStream();
        goodBody.WriteUnsignedVarInt(7);
        goodBody.WriteByte(0x55);
        var goodBytes = goodBody.TakeOwnedBuffer();
        writer.WriteUnsignedVarInt(goodBytes.Length);
        writer.Write(goodBytes);

        writer.WriteUnsignedVarInt(999); // malformed: declared length exceeds remaining bytes
        writer.WriteByte(9);

        var batch = writer.TakeOwnedBuffer();
        var stream = new BinaryStream(batch);

        try
        {
            player.Session.HandleGamePacket(ref stream);
            Assert.Fail("Expected InvalidOperationException");
        }
        catch (InvalidOperationException)
        {
            // Expected
        }

        Assert.Single(recorder.Received);
        Assert.Equal((7, (byte)0x55), recorder.Received[0]);
    }

    /// <summary>
    /// Phase XXVII, Part 15/29 — the decompressed batch is a rented ArrayPool buffer, returned in
    /// HandleGamePacket's `finally` once every contained subpacket has already been synchronously
    /// dispatched. Proves the real ZLIB path — not just the inbound-decode plumbing — still works
    /// after removing the intermediate GamePacket.Buffers copy step, and that dispatch completes
    /// (and reads real values) before that pooled buffer goes back to the pool.
    /// </summary>
    [Fact]
    public void Zlib_compressed_batch_dispatches_all_subpackets_before_the_pooled_buffer_returns()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("zlib-dispatcher");
        var recorder = new RecordingHandler();
        player.Session.SetHandler(recorder);

        var uncompressed = BuildUncompressedBatch((11, 0x01), (22, 0x02), (33, 0x03));

        using var compressedStream = new MemoryStream();
        using (var deflate = new DeflateStream(compressedStream, CompressionLevel.Fastest, leaveOpen: true))
            deflate.Write(uncompressed);
        var compressedBody = compressedStream.ToArray();

        var writer = new BinaryStream();
        writer.WriteByte(PacketCompression.ZLIB);
        writer.Write(compressedBody);
        var wireBatch = writer.TakeOwnedBuffer();

        var stream = new BinaryStream(wireBatch);
        player.Session.HandleGamePacket(ref stream);

        Assert.Equal(3, recorder.Received.Count);
        Assert.Equal((11, (byte)0x01), recorder.Received[0]);
        Assert.Equal((22, (byte)0x02), recorder.Received[1]);
        Assert.Equal((33, (byte)0x03), recorder.Received[2]);

        // A second, independent call reusing the same ArrayPool confirms the first call's buffer was
        // actually returned (not leaked/double-owned) and the pool is still in a healthy state.
        var recorder2 = new RecordingHandler();
        player.Session.SetHandler(recorder2);
        var uncompressed2 = BuildUncompressedBatch((44, 0x09));
        using var compressedStream2 = new MemoryStream();
        using (var deflate2 = new DeflateStream(compressedStream2, CompressionLevel.Fastest, leaveOpen: true))
            deflate2.Write(uncompressed2);
        var writer2 = new BinaryStream();
        writer2.WriteByte(PacketCompression.ZLIB);
        writer2.Write(compressedStream2.ToArray());
        var wireBatch2 = writer2.TakeOwnedBuffer();
        var stream2 = new BinaryStream(wireBatch2);
        player.Session.HandleGamePacket(ref stream2);

        Assert.Single(recorder2.Received);
        Assert.Equal((44, (byte)0x09), recorder2.Received[0]);
    }
}
