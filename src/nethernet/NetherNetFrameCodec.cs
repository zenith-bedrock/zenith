namespace Zenith.NetherNet;

/// <summary>NetherNet data-channel framing from the Bedrock onboarding contract.</summary>
public static class NetherNetFrameCodec
{
    public static byte[] EncodeUnreliable(ReadOnlySpan<byte> payload, int maximumMessageSize)
    {
        ValidateMessageSize(payload.Length, maximumMessageSize);
        if (payload.Length == 0)
            throw new ArgumentException("NetherNet messages must contain a payload.", nameof(payload));
        var frame = new byte[payload.Length + 1];
        payload.CopyTo(frame.AsSpan(1));
        return frame;
    }

    public static IReadOnlyList<byte[]> EncodeReliable(ReadOnlySpan<byte> payload, int maximumFragmentPayloadSize, int maximumMessageSize)
    {
        ValidateMessageSize(payload.Length, maximumMessageSize);
        if (maximumFragmentPayloadSize is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(maximumFragmentPayloadSize));
        if (payload.Length == 0)
            throw new ArgumentException("NetherNet messages must contain a payload.", nameof(payload));

        var fragmentCount = 1 + (payload.Length - 1) / maximumFragmentPayloadSize;
        if (fragmentCount > 256)
            throw new ArgumentOutOfRangeException(nameof(payload), "NetherNet supports at most 256 fragments per message.");

        var frames = new List<byte[]>(fragmentCount);
        var offset = 0;
        for (var fragment = fragmentCount - 1; fragment >= 0; fragment--)
        {
            var length = Math.Min(maximumFragmentPayloadSize, payload.Length - offset);
            var frame = new byte[length + 1];
            frame[0] = (byte)fragment;
            payload.Slice(offset, length).CopyTo(frame.AsSpan(1));
            frames.Add(frame);
            offset += length;
        }
        return frames;
    }

    public static bool TryDecodeUnreliable(ReadOnlySpan<byte> frame, int maximumMessageSize, out ReadOnlyMemory<byte> payload)
    {
        if (maximumMessageSize < 1 || frame.Length <= 1 || frame[0] != 0 || frame.Length - 1 > maximumMessageSize)
        {
            payload = default;
            return false;
        }
        payload = frame[1..].ToArray();
        return true;
    }

    private static void ValidateMessageSize(int size, int maximumMessageSize)
    {
        if (maximumMessageSize < 1)
            throw new ArgumentOutOfRangeException(nameof(maximumMessageSize));
        if (size > maximumMessageSize)
            throw new ArgumentOutOfRangeException(nameof(size), "The NetherNet message exceeds the configured maximum.");
    }

    public sealed class ReliableReassembler
    {
        private readonly MemoryStream _buffer = new();
        private readonly int _maximumMessageSize;
        private int _remaining = -1;

        public ReliableReassembler(int maximumMessageSize)
        {
            if (maximumMessageSize < 1)
                throw new ArgumentOutOfRangeException(nameof(maximumMessageSize));
            _maximumMessageSize = maximumMessageSize;
        }

        public bool TryAppend(ReadOnlySpan<byte> frame, out byte[]? payload)
        {
            payload = null;
            if (frame.Length <= 1 || frame.Length - 1 > _maximumMessageSize)
            {
                Reset();
                throw new FormatException("Invalid or oversized reliable NetherNet frame.");
            }

            var remaining = frame[0];
            if (_remaining < 0)
                _remaining = remaining;
            else if (remaining != _remaining - 1)
            {
                Reset();
                throw new FormatException("Reliable NetherNet fragments arrived out of order.");
            }

            if (_buffer.Length + frame.Length - 1 > _maximumMessageSize)
            {
                Reset();
                throw new FormatException("Reliable NetherNet message exceeds the configured limit.");
            }
            _buffer.Write(frame[1..]);
            if (remaining != 0)
            {
                _remaining = remaining;
                return false;
            }

            payload = _buffer.ToArray();
            Reset();
            return true;
        }

        private void Reset()
        {
            _buffer.SetLength(0);
            _remaining = -1;
        }
    }
}
