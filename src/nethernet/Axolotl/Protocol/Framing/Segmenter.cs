namespace Zenith.NetherNet.Axolotl.Protocol.Framing;

/// <summary>Splits a payload into countdown-prefixed segments (port of <c>Segmenter</c>).</summary>
public sealed class Segmenter
{
    public const int HeaderSize = 1;
    public const int MaxSegmentPayloadSize = 262143;
    public const int MaxSegments = 255;
    public const int MaxPayloadSize = MaxSegmentPayloadSize * MaxSegments;

    public int MaxSegmentPayload { get; }

    public Segmenter(int maxSegmentPayloadSize = MaxSegmentPayloadSize)
    {
        if (maxSegmentPayloadSize < 1 || maxSegmentPayloadSize > MaxSegmentPayloadSize)
            throw new ArgumentException($"Segment payload size must be between 1 and {MaxSegmentPayloadSize}, got {maxSegmentPayloadSize}");
        MaxSegmentPayload = maxSegmentPayloadSize;
    }

    public IReadOnlyList<byte[]> Segment(ReadOnlySpan<byte> payload)
    {
        var length = payload.Length;
        if (length == 0)
            return Array.Empty<byte[]>();

        var total = (length - 1) / MaxSegmentPayload + 1;
        if (total > MaxSegments)
            throw new FramingException($"Payload of {length} bytes needs {total} segments, but at most {MaxSegments} can be framed");

        var segments = new List<byte[]>(total);
        var remaining = total - 1;
        for (var offset = 0; offset < length; offset += MaxSegmentPayload)
        {
            var chunk = payload.Slice(offset, Math.Min(MaxSegmentPayload, length - offset));
            var seg = new byte[chunk.Length + 1];
            seg[0] = (byte)remaining;
            chunk.CopyTo(seg.AsSpan(1));
            segments.Add(seg);
            remaining--;
        }
        return segments;
    }
}
