using Xunit;
using Zenith.Raknet.Stream;

namespace raknet.Tests;

/// <summary>
/// Phase XXVII — the 3-arg window constructor and <see cref="BinaryStream.ReadSubstream"/> are the
/// zero-copy replacements for array-range syntax (<c>buffer[1..]</c>) and per-subpacket
/// <c>.ToArray()</c>. These tests prove the window shares the backing array (zero-copy) and that its
/// bounds are a real security boundary, not just a copy-avoidance convenience.
/// </summary>
public class BinaryStreamWindowTests
{
    [Fact]
    public void Window_starting_at_zero_reads_like_the_plain_constructor()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var window = new BinaryStream(data, 0, 5);
        Assert.Same(data, window.Buffer);
        Assert.Equal(0, window.Offset);
        Assert.Equal(5, window.Length);
        Assert.Equal(1, window.ReadByte());
    }

    [Fact]
    public void Window_starting_at_one_shares_the_backing_array()
    {
        var data = new byte[] { 0xAA, 1, 2, 3, 4 };
        var window = new BinaryStream(data, 1, data.Length - 1);

        Assert.Same(data, window.Buffer); // zero-copy: identity, not just equal contents
        Assert.Equal(1, window.Offset);
        Assert.Equal(data.Length, window.Length);
        Assert.Equal(1, window.ReadByte());
        Assert.Equal(2, window.ReadByte());
    }

    [Fact]
    public void Window_starting_deep_in_the_array_reads_from_the_correct_position()
    {
        var data = new byte[] { 9, 9, 9, 9, 9, 42, 43, 44 };
        var window = new BinaryStream(data, 5, 3);
        Assert.Equal(42, window.ReadByte());
        Assert.Equal(43, window.ReadByte());
        Assert.Equal(44, window.ReadByte());
        Assert.True(window.IsEndOfFile);
    }

    [Fact]
    public void Window_exact_end_reports_end_of_file_at_the_window_boundary_not_the_array_end()
    {
        var data = new byte[] { 1, 2, 3, 4, 5, 6 };
        var window = new BinaryStream(data, 2, 2); // covers indices 2,3 only
        Assert.False(window.IsEndOfFile);
        _ = window.ReadByte();
        Assert.False(window.IsEndOfFile);
        _ = window.ReadByte();
        Assert.True(window.IsEndOfFile); // even though the backing array has more bytes after
    }

    [Fact]
    public void Window_count_overrunning_the_array_throws()
    {
        var data = new byte[] { 1, 2, 3 };
        Assert.Throws<ArgumentOutOfRangeException>(() => new BinaryStream(data, 2, 5));
    }

    [Fact]
    public void Window_offset_past_the_array_throws()
    {
        var data = new byte[] { 1, 2, 3 };
        Assert.Throws<ArgumentOutOfRangeException>(() => new BinaryStream(data, 10, 0));
    }

    [Fact]
    public void Window_reading_past_its_own_bound_throws_even_though_the_array_has_more_bytes()
    {
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var window = new BinaryStream(data, 0, 3);
        _ = window.ReadSpan(3);

        try
        {
            window.ReadByte();
            Assert.Fail("Expected InvalidOperationException");
        }
        catch (InvalidOperationException)
        {
            // Expected
        }
    }

    [Fact]
    public void Zero_length_window_is_immediately_end_of_file()
    {
        var data = new byte[] { 1, 2, 3 };
        var window = new BinaryStream(data, 1, 0);
        Assert.True(window.IsEndOfFile);
    }

    [Fact]
    public void PeekByte_reads_at_the_window_offset_not_absolute_index_zero()
    {
        var data = new byte[] { 0xAA, 0xBB, 0xCC };
        var window = new BinaryStream(data, 1, 2);
        // The historical bug this guards: `stream.Buffer[0]` would read 0xAA (the byte before the
        // window) instead of 0xBB (the window's actual first byte).
        Assert.Equal(0xBB, window.PeekByte());
        Assert.Equal(0xBB, window.ReadByte()); // peek must not have advanced Offset
    }

    [Fact]
    public void PeekByte_at_end_of_window_throws()
    {
        var data = new byte[] { 1, 2, 3 };
        var window = new BinaryStream(data, 0, 0);

        try
        {
            window.PeekByte();
            Assert.Fail("Expected InvalidOperationException");
        }
        catch (InvalidOperationException)
        {
            // Expected
        }
    }

    // --- ReadSubstream: the bounded sub-reader used to eliminate per-DataPacket .ToArray() ---

    [Fact]
    public void ReadSubstream_child_shares_the_same_backing_array()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var parent = new BinaryStream(data);
        var child = parent.ReadSubstream(3);
        Assert.Same(data, child.Buffer); // zero-copy
    }

    [Fact]
    public void ReadSubstream_advances_the_parent_past_the_consumed_window()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var parent = new BinaryStream(data);
        _ = parent.ReadSubstream(3);
        Assert.Equal(3, parent.Offset);
        Assert.Equal(4, parent.ReadByte());
    }

    [Fact]
    public void ReadSubstream_child_starts_at_the_expected_byte()
    {
        var data = new byte[] { 10, 20, 30, 40, 50 };
        var parent = new BinaryStream(data);
        _ = parent.ReadByte(); // consume the first byte first
        var child = parent.ReadSubstream(2);
        Assert.Equal(20, child.ReadByte());
        Assert.Equal(30, child.ReadByte());
    }

    /// <summary>
    /// The security boundary Part 13 of the phase brief calls out explicitly: a malformed subpacket
    /// declaring a shorter length than it actually tries to read must not be able to read into the
    /// next subpacket's bytes.
    /// </summary>
    [Fact]
    public void ReadSubstream_child_cannot_over_read_into_the_next_packet()
    {
        // [length A=2][packet A bytes][length B=2][packet B bytes]
        byte[] data = [2, 0xAA, 0xAA, 2, 0xBB, 0xBB];
        var parent = new BinaryStream(data);
        var declaredLength = parent.ReadByte();
        var packetA = parent.ReadSubstream(declaredLength);

        _ = packetA.ReadByte();
        _ = packetA.ReadByte();
        // Packet A's own declared length is exhausted — even though the backing array still has
        // packet B's bytes right after it, packetA must not be able to read them.
        try
        {
            packetA.ReadByte();
            Assert.Fail("Expected InvalidOperationException");
        }
        catch (InvalidOperationException)
        {
            // Expected
        }

        // The parent, unaffected by packetA's attempted over-read, still finds packet B intact.
        var lengthB = parent.ReadByte();
        var packetB = parent.ReadSubstream(lengthB);
        Assert.Equal(0xBB, packetB.ReadByte());
        Assert.Equal(0xBB, packetB.ReadByte());
    }

    [Fact]
    public void ReadSubstream_declared_length_greater_than_remaining_throws_before_creating_a_window()
    {
        var data = new byte[] { 1, 2, 3 };
        var parent = new BinaryStream(data);
        try
        {
            parent.ReadSubstream(10);
            Assert.Fail("Expected InvalidOperationException");
        }
        catch (InvalidOperationException)
        {
            // Expected
        }
    }

    [Fact]
    public void ReadSubstream_zero_length_packet_is_valid_and_immediately_at_end_of_file()
    {
        var data = new byte[] { 5, 5, 5 };
        var parent = new BinaryStream(data);
        var child = parent.ReadSubstream(0);
        Assert.True(child.IsEndOfFile);
        Assert.Equal(0, parent.Offset); // parent did not advance for a zero-length window
    }

    [Fact]
    public void Nested_substreams_each_bound_correctly()
    {
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var outer = new BinaryStream(data, 1, 6); // window over indices 1..6 (values 2..7)
        var middle = outer.ReadSubstream(4); // values 2,3,4,5
        var inner = middle.ReadSubstream(2); // values 2,3

        Assert.Equal(2, inner.ReadByte());
        Assert.Equal(3, inner.ReadByte());
        Assert.True(inner.IsEndOfFile);

        // middle still has its own remaining bytes (4,5) independent of inner's bound
        Assert.Equal(4, middle.ReadByte());
        Assert.Equal(5, middle.ReadByte());
        Assert.True(middle.IsEndOfFile);

        // outer still has its own remaining bytes (6,7) independent of middle's consumption
        Assert.Equal(6, outer.ReadByte());
        Assert.Equal(7, outer.ReadByte());
        Assert.True(outer.IsEndOfFile);
    }

    [Fact]
    public void Root_offset_non_zero_window_over_a_pooled_style_buffer_with_explicit_valid_length()
    {
        // Simulates an ArrayPool.Rent buffer where capacity > actual valid data.
        var rented = new byte[64];
        rented[10] = 7;
        rented[11] = 8;
        var window = new BinaryStream(rented, 10, 2); // only bytes [10,12) are valid
        Assert.Equal(12, window.Length);
        Assert.Equal(7, window.ReadByte());
        Assert.Equal(8, window.ReadByte());
        Assert.True(window.IsEndOfFile);
    }
}
