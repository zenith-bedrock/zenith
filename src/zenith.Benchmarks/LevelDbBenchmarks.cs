using BenchmarkDotNet.Attributes;
using Zenith.LevelDB;

namespace Zenith.Benchmarks;

/// <summary>ZLDB single-threaded read/write/snapshot cost (ADR §115/§118).</summary>
[MemoryDiagnoser]
public class LevelDbBenchmarks
{
    private DB _db = null!;
    private string _dir = null!;
    private byte[][] _keys = null!;
    private byte[] _value = null!;

    [Params(1_000, 50_000)]
    public int EntryCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "zenith-leveldb-bench-" + Guid.NewGuid().ToString("N"));
        _db = new DB(new Options { CreateIfMissing = true }, _dir);
        _keys = new byte[EntryCount][];
        _value = new byte[64];
        for (var i = 0; i < EntryCount; i++)
        {
            _keys[i] = System.Text.Encoding.UTF8.GetBytes($"ov:{i}:0:0");
            _db.Put(_keys[i], _value);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _db.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>Zero-alloc (ADR §118) point read — no clone, returns the MemTable-owned array.</summary>
    [Benchmark]
    public bool TryGet_single() => _db.TryGet(_keys[EntryCount / 2], out _);

    [Benchmark]
    public void Put_single() => _db.Put(_keys[0], _value);

    /// <summary>Cost of materializing a full-dataset snapshot — the O(n) copy ADR §118 stopped
    /// cloning key/value bytes for (still copies the dictionary structure itself).</summary>
    [Benchmark]
    public int GetSnapshot_full()
    {
        var snap = _db.GetSnapshot();
        using var it = snap.CreateIterator();
        it.SeekToFirst();
        var count = 0;
        while (it.IsValid())
        {
            count++;
            it.Next();
        }

        return count;
    }
}

/// <summary>
/// Concurrent-read scaling (ADR §118): before the reader/writer lock, every <c>Get</c> serialized
/// on a single mutex — N threads reading concurrently took roughly N times as long as one. After,
/// reads should overlap. <c>ConcurrentTryGet</c>'s total work scales with <see cref="ThreadCount"/>
/// (each thread does the same fixed <see cref="ReadsPerThread"/>); if reads still run one-at-a-time
/// under the hood, wall time grows roughly linearly with <see cref="ThreadCount"/> — if they
/// genuinely overlap, it stays close to flat.
/// </summary>
[MemoryDiagnoser]
public class LevelDbConcurrencyBenchmarks
{
    private const int ReadsPerThread = 20_000;

    private DB _db = null!;
    private string _dir = null!;
    private byte[] _key = null!;

    [Params(1, 2, 4, 8)]
    public int ThreadCount { get; set; }

    private byte[][] _perThreadKeys = null!;

    [GlobalSetup]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "zenith-leveldb-bench-conc-" + Guid.NewGuid().ToString("N"));
        _db = new DB(new Options { CreateIfMissing = true }, _dir);
        _key = System.Text.Encoding.UTF8.GetBytes("hot-key");
        _db.Put(_key, new byte[64]);

        // Isolates lock overhead from same-key cache-line contention: each thread reads its own
        // key, so there is no shared tree-node data being touched by multiple cores at once.
        _perThreadKeys = new byte[16][];
        for (var t = 0; t < _perThreadKeys.Length; t++)
        {
            _perThreadKeys[t] = System.Text.Encoding.UTF8.GetBytes($"k{t}");
            _db.Put(_perThreadKeys[t], new byte[64]);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _db.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    [Benchmark]
    public void ConcurrentTryGet_SameKey()
    {
        var tasks = new Task[ThreadCount];
        for (var t = 0; t < ThreadCount; t++)
        {
            tasks[t] = Task.Run(() =>
            {
                for (var i = 0; i < ReadsPerThread; i++)
                    _db.TryGet(_key, out _);
            });
        }

        Task.WaitAll(tasks);
    }

    [Benchmark]
    public void ConcurrentTryGet_DistinctKeys()
    {
        var tasks = new Task[ThreadCount];
        for (var t = 0; t < ThreadCount; t++)
        {
            var key = _perThreadKeys[t % _perThreadKeys.Length];
            tasks[t] = Task.Run(() =>
            {
                for (var i = 0; i < ReadsPerThread; i++)
                    _db.TryGet(key, out _);
            });
        }

        Task.WaitAll(tasks);
    }
}
