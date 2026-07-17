using System.IO.Compression;
using Xunit;
using Zenith.Session;

namespace Zenith.Tests;

public class InflateCapTests
{
    [Fact]
    public void InflateToPooledBuffer_rejects_over_2MiB()
    {
        // Raw deflate (same as Bedrock zlib payload after compression byte).
        var huge = new byte[NetworkSession.MaxDecompressedSize + 64 * 1024];
        Random.Shared.NextBytes(huge);

        using var compressed = new MemoryStream();
        using (var deflate = new DeflateStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            deflate.Write(huge);
        var payload = compressed.ToArray();

        using var input = new MemoryStream(payload);
        using var inflater = new DeflateStream(input, CompressionMode.Decompress);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            NetworkSession.InflateToPooledBuffer(inflater, out _));
        Assert.Contains("maximum size", ex.Message);
        Assert.Contains(NetworkSession.MaxDecompressedSize.ToString(), ex.Message);
    }
}
