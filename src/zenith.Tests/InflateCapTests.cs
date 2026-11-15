using System.IO.Compression;
using Xunit;
using Zenith.Session;
using System.Globalization;
using System.Buffers;

namespace Zenith.Tests;

public class InflateCapTests
{
    private sealed class TrackingPool : ArrayPool<byte>
    {
        public readonly HashSet<byte[]> Outstanding = new();
        public override byte[] Rent(int minimumLength) { var b=new byte[minimumLength]; Outstanding.Add(b); return b; }
        public override void Return(byte[] array, bool clearArray=false) { Assert.True(Outstanding.Remove(array)); }
    }
    private sealed class BrokenStream : Stream
    {
        public override bool CanRead=>true; public override bool CanSeek=>false; public override bool CanWrite=>false;
        public override long Length=>throw new NotSupportedException();
        public override long Position { get=>throw new NotSupportedException(); set=>throw new NotSupportedException(); }
        public override int Read(byte[] buffer,int offset,int count)=>throw new IOException("read failed");
        public override void Flush()=>throw new NotSupportedException();
        public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
        public override void SetLength(long value)=>throw new NotSupportedException();
        public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    }

    [Fact]
    public void Inflate_failure_returns_the_rented_buffer_without_double_return()
    {
        var pool=new TrackingPool();
        using var input=new BrokenStream(); using var inflater=new DeflateStream(input,CompressionMode.Decompress);
        Assert.Throws<IOException>(()=>NetworkSession.InflateToPooledBuffer(inflater,out _,pool));
        Assert.Empty(pool.Outstanding);
    }

    [Fact]
    public void Successful_inflate_transfers_exactly_one_buffer_to_the_caller()
    {
        var pool=new TrackingPool(); using var compressed=new MemoryStream();
        using(var d=new DeflateStream(compressed,CompressionLevel.Fastest,true)) d.Write(new byte[10000]);
        using var input=new MemoryStream(compressed.ToArray());
        using var inflater=new DeflateStream(input,CompressionMode.Decompress);
        var bytes=NetworkSession.InflateToPooledBuffer(inflater,out var length,pool);
        Assert.Equal(10000,length); Assert.Same(bytes,Assert.Single(pool.Outstanding));
        pool.Return(bytes); Assert.Empty(pool.Outstanding);
    }

    [Fact]
    public void Size_limit_failure_returns_all_growth_buffers_exactly_once()
    {
        var pool=new TrackingPool(); using var compressed=new MemoryStream();
        using(var d=new DeflateStream(compressed,CompressionLevel.Fastest,true))
            d.Write(new byte[NetworkSession.MaxDecompressedSize+1]);
        using var input=new MemoryStream(compressed.ToArray());
        using var inflater=new DeflateStream(input,CompressionMode.Decompress);
        Assert.Throws<InvalidOperationException>(()=>NetworkSession.InflateToPooledBuffer(inflater,out _,pool));
        Assert.Empty(pool.Outstanding);
    }
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
        Assert.Contains(NetworkSession.MaxDecompressedSize.ToString(CultureInfo.InvariantCulture), ex.Message);
    }
}
