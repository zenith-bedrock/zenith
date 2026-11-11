using System.Text;
using Xunit;
using Zenith.NetherNet;

namespace Zenith.NetherNet.Tests;

public sealed class NetherNetFrameCodecTests
{
    [Fact]
    public void EncodeUnreliable_writes_zero_header_and_payload()
    {
        var frame = NetherNetFrameCodec.EncodeUnreliable([1, 2, 3], 10);
        Assert.Equal([0, 1, 2, 3], frame);
        Assert.True(NetherNetFrameCodec.TryDecodeUnreliable(frame, 10, out var payload));
        Assert.Equal([1, 2, 3], payload.ToArray());
    }

    [Fact]
    public void EncodeReliable_fragments_in_wire_order_and_reassembles()
    {
        var source = Encoding.UTF8.GetBytes("abcdefghij");
        var frames = NetherNetFrameCodec.EncodeReliable(source, 4, 20);

        Assert.Equal(3, frames.Count);
        Assert.Equal((byte)2, frames[0][0]);
        Assert.Equal((byte)1, frames[1][0]);
        Assert.Equal((byte)0, frames[2][0]);

        var reassembler = new NetherNetFrameCodec.ReliableReassembler(20);
        byte[]? result = null;
        foreach (var frame in frames)
            Assert.Equal(frame == frames[^1], reassembler.TryAppend(frame, out result));
        Assert.Equal(source, result);
    }

    [Fact]
    public void ReliableReassembler_rejects_out_of_order_frame_and_resets()
    {
        var reassembler = new NetherNetFrameCodec.ReliableReassembler(20);
        Assert.False(reassembler.TryAppend([2, 1], out _));
        Assert.Throws<FormatException>(() => reassembler.TryAppend([0, 2], out _));
        Assert.True(reassembler.TryAppend([0, 9], out var result));
        Assert.Equal([9], result);
    }

    [Fact]
    public void TryDecodeUnreliable_rejects_segmented_or_oversized_frame()
    {
        Assert.False(NetherNetFrameCodec.TryDecodeUnreliable([1, 9], 10, out _));
        Assert.False(NetherNetFrameCodec.TryDecodeUnreliable([0, 1, 2], 1, out _));
        Assert.False(NetherNetFrameCodec.TryDecodeUnreliable([0], 10, out _));
        Assert.False(NetherNetFrameCodec.TryDecodeUnreliable([0, 1], 0, out _));
    }

    [Fact]
    public void ReliableReassembler_invalid_frame_discards_partial_message()
    {
        var reassembler = new NetherNetFrameCodec.ReliableReassembler(2);
        Assert.False(reassembler.TryAppend([1, 1], out _));
        Assert.Throws<FormatException>(() => reassembler.TryAppend([0, 2, 3], out _));
        Assert.True(reassembler.TryAppend([0, 9], out var result));
        Assert.Equal([9], result);
    }

    [Fact]
    public void EncodeReliable_rejects_more_than_256_fragments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NetherNetFrameCodec.EncodeReliable(new byte[257], 1, 257));
    }
}
