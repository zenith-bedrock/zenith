using Xunit;
using Zenith.Raknet.Stream;

namespace raknet.Tests;

public class BinaryStreamTests
{
    [Fact]
    public void Default_constructor_starts_empty()
    {
        var stream = new BinaryStream();
        Assert.Equal(0, stream.Length);
        Assert.Equal(0, stream.Offset);
        Assert.True(stream.IsEndOfFile);
        stream.Dispose();
    }

    [Fact]
    public void Buffer_constructor_sets_Length_from_buffer()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var stream = new BinaryStream(data);
        Assert.Equal(5, stream.Length);
        Assert.Equal(0, stream.Offset);
        Assert.Same(data, stream.Buffer);
        stream.Dispose();
    }

    [Fact]
    public void Buffer_with_length_constructor_uses_explicit_length()
    {
        var data = new byte[] { 1, 2, 3, 4, 5, 0, 0, 0 };
        var stream = new BinaryStream(data, length: 5);
        Assert.Equal(5, stream.Length);
        Assert.Equal(0, stream.Offset);
        Assert.Same(data, stream.Buffer);
        stream.Dispose();
    }

    [Fact]
    public void Buffer_with_zero_length_creates_empty_stream()
    {
        var data = new byte[] { 1, 2, 3 };
        var stream = new BinaryStream(data, length: 0);
        Assert.Equal(0, stream.Length);
        Assert.True(stream.IsEndOfFile);
        stream.Dispose();
    }

    [Fact]
    public void ReadSpan_returns_correct_bytes_and_advances_offset()
    {
        var data = new byte[] { 10, 20, 30, 40, 50 };
        var stream = new BinaryStream(data);

        var span = stream.ReadSpan(2);
        Assert.Equal(2, span.Length);
        Assert.Equal(10, span[0]);
        Assert.Equal(20, span[1]);
        Assert.Equal(2, stream.Offset);

        span = stream.ReadSpan(3);
        Assert.Equal(30, span[0]);
        Assert.Equal(50, span[2]);
        Assert.Equal(5, stream.Offset);
        Assert.True(stream.IsEndOfFile);
        stream.Dispose();
    }

    [Fact]
    public void ReadSpan_with_zero_returns_empty()
    {
        var data = new byte[] { 1, 2, 3 };
        var stream = new BinaryStream(data);
        var span = stream.ReadSpan(0);
        Assert.Equal(0, span.Length);
        Assert.Equal(0, stream.Offset);
        stream.Dispose();
    }

    [Fact]
    public void ReadSpan_past_end_throws()
    {
        var stream = new BinaryStream([1, 2, 3]);
        stream.ReadSpan(3);
        Assert.True(stream.IsEndOfFile);

        try
        {
            stream.ReadSpan(1);
            Assert.Fail("Expected InvalidOperationException");
        }
        catch (InvalidOperationException)
        {
            // Expected
        }

        stream.Dispose();
    }

    [Fact]
    public void ReadByte_returns_value_and_advances()
    {
        var stream = new BinaryStream([42]);
        Assert.Equal(42, stream.ReadByte());
        Assert.Equal(1, stream.Offset);
        stream.Dispose();
    }

    [Fact]
    public void ReadRemaining_returns_all_from_offset()
    {
        var data = new byte[] { 10, 20, 30, 40 };
        var stream = new BinaryStream(data);
        stream.ReadSpan(1);
        var remaining = stream.ReadRemaining();
        Assert.Equal(3, remaining.Length);
        Assert.Equal(20, remaining[0]);
        Assert.Equal(40, remaining[2]);
        Assert.True(stream.IsEndOfFile);
        stream.Dispose();
    }

    [Fact]
    public void WriteByte_appends_and_grows_buffer()
    {
        var stream = new BinaryStream();
        stream.WriteByte(1);
        stream.WriteByte(2);
        stream.WriteByte(3);
        Assert.Equal(3, stream.Length);
        Assert.Equal(0, stream.Offset);

        stream.Rewind();
        Assert.Equal(1, stream.ReadByte());
        Assert.Equal(2, stream.ReadByte());
        Assert.Equal(3, stream.ReadByte());
        stream.Dispose();
    }

    [Fact]
    public void Write_grows_via_EnsureCapacity()
    {
        var stream = new BinaryStream();
        var big = new byte[200];
        big.AsSpan().Fill(0xAB);
        stream.Write(big);
        Assert.Equal(200, stream.Length);
        stream.Rewind();
        var read = stream.ReadSpan(200);
        Assert.Equal(0xAB, read[0]);
        Assert.Equal(0xAB, read[199]);
        stream.Dispose();
    }

    [Fact]
    public void Write_and_read_roundtrip()
    {
        var stream = new BinaryStream();
        stream.WriteByte(0x10);
        stream.WriteUnsignedVarInt(300);
        stream.WriteInt(-42, BinaryStream.Endianess.Little);
        stream.WriteFloat(3.14f, BinaryStream.Endianess.Big);

        stream.Rewind();
        Assert.Equal(0x10, stream.ReadByte());
        Assert.Equal(300u, (uint)stream.ReadUnsignedVarInt());
        Assert.Equal(-42, stream.ReadInt(BinaryStream.Endianess.Little));
        Assert.Equal(3.14f, stream.ReadFloat(BinaryStream.Endianess.Big));
        Assert.True(stream.IsEndOfFile);
        stream.Dispose();
    }

    [Fact]
    public void GetBufferDisposing_returns_written_data()
    {
        var stream = new BinaryStream();
        stream.WriteByte(7);
        stream.WriteByte(8);
        stream.WriteByte(9);

        var span = stream.GetBufferDisposing();
        Assert.Equal(3, span.Length);
        Assert.Equal(7, span[0]);
        Assert.Equal(9, span[2]);
        Assert.Equal(0, stream.Length);
        Assert.Empty(stream.Buffer);
        stream.Dispose();
    }

    [Fact]
    public void Dispose_resets_state()
    {
        var stream = new BinaryStream([1, 2, 3]);
        stream.ReadSpan(1);
        stream.Dispose();
        Assert.Equal(0, stream.Length);
        Assert.Equal(0, stream.Offset);
        Assert.Empty(stream.Buffer);
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        var stream = new BinaryStream([1, 2, 3]);
        stream.Dispose();
        stream.Dispose();
        Assert.Equal(0, stream.Length);
        Assert.Equal(0, stream.Offset);
    }

    [Fact]
    public void ReadSpan_after_Dispose_throws_ObjectDisposed()
    {
        var stream = new BinaryStream([1, 2, 3]);
        stream.Dispose();

        try
        {
            stream.ReadSpan(1);
            Assert.Fail("Expected ObjectDisposedException");
        }
        catch (ObjectDisposedException)
        {
            // Expected
        }
    }

    [Fact]
    public void Rewind_resets_offset()
    {
        var stream = new BinaryStream([10, 20, 30]);
        stream.ReadSpan(2);
        Assert.Equal(2, stream.Offset);
        stream.Rewind();
        Assert.Equal(0, stream.Offset);
        Assert.Equal(10, stream.ReadByte());
        stream.Dispose();
    }

    [Fact]
    public void Write_after_read_switches_to_write_mode()
    {
        var stream = new BinaryStream();
        stream.WriteByte(1);
        stream.Rewind();
        Assert.Equal(1, stream.ReadByte());
        stream.WriteByte(2);
        Assert.Equal(2, stream.Length);
        stream.Rewind();
        Assert.Equal(1, stream.ReadByte());
        Assert.Equal(2, stream.ReadByte());
        stream.Dispose();
    }
}
