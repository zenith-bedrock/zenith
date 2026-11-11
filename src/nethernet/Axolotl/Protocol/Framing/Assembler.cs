namespace Zenith.NetherNet.Axolotl.Protocol.Framing;

/// <summary>Reassembles countdown-prefixed segments (port of <c>Assembler</c>).</summary>
public sealed class Assembler
{
    private readonly MemoryStream _buffer = new();
    private int _remaining;
    private readonly bool _fragmentationAllowed;
    private readonly int _maxPayloadSize;

    public Assembler(bool fragmentationAllowed, int maxPayloadSize = Segmenter.MaxPayloadSize)
    {
        if (maxPayloadSize < 1)
            throw new ArgumentException($"Maximum payload size must be positive, got {maxPayloadSize}");
        _fragmentationAllowed = fragmentationAllowed;
        _maxPayloadSize = maxPayloadSize;
    }

    private void Reset()
    {
        _buffer.SetLength(0);
        _remaining = 0;
    }

    /// <summary>Feeds one segment; returns the complete payload when the countdown reaches zero, else null.</summary>
    public byte[]? Accept(ReadOnlySpan<byte> segment)
    {
        var length = segment.Length;
        if (length < 2)
        {
            Reset();
            throw new FramingException($"Segment must have a counter byte and at least one payload byte, got {length} bytes");
        }

        int remaining = segment[0];
        if (!_fragmentationAllowed && remaining != 0)
        {
            Reset();
            throw new FramingException($"Channel does not support fragmentation, but received a segment with remaining count {remaining}");
        }
        if (_remaining > 0 && remaining != _remaining - 1)
        {
            var expected = _remaining - 1;
            Reset();
            throw new FramingException($"Expected segment countdown {expected}, got {remaining}");
        }
        if (_buffer.Length + length - 1 > _maxPayloadSize)
        {
            Reset();
            throw new FramingException($"Assembled payload would exceed the {_maxPayloadSize} byte limit");
        }

        _buffer.Write(segment[1..]);
        _remaining = remaining;
        if (remaining != 0)
            return null;

        var payload = _buffer.ToArray();
        _buffer.SetLength(0);
        return payload;
    }
}
