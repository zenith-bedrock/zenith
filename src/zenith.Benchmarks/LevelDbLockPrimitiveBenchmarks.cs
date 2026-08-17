using BenchmarkDotNet.Attributes;

namespace Zenith.Benchmarks;

/// <summary>
/// Isolates whether <c>ReaderWriterLockSlim</c> (ADR §118's read-path lock) is actually the right
/// primitive for ZLDB's real access pattern — many very short, cheap point reads — or whether its
/// own per-acquisition bookkeeping overhead outweighs the "readers don't exclude each other"
/// benefit for this specific shape of workload. Same read-only <see cref="SortedDictionary{TKey,TValue}"/>
/// lookup, guarded two different ways, run at the same thread counts as
/// <see cref="LevelDbConcurrencyBenchmarks"/> for a direct, apples-to-apples comparison.
/// </summary>
[MemoryDiagnoser]
public class LevelDbLockPrimitiveBenchmarks
{
    private const int ReadsPerThread = 20_000;

    private readonly object _mutex = new();
    private readonly ReaderWriterLockSlim _rwLock = new(LockRecursionPolicy.NoRecursion);
    private SortedDictionary<byte[], byte[]> _data = null!;
    private byte[] _key = null!;

    [Params(1, 2, 4, 8)]
    public int ThreadCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _data = new SortedDictionary<byte[], byte[]>(Comparer<byte[]>.Create(static (a, b) =>
        {
            var n = Math.Min(a.Length, b.Length);
            for (var i = 0; i < n; i++)
            {
                var d = a[i].CompareTo(b[i]);
                if (d != 0) return d;
            }

            return a.Length.CompareTo(b.Length);
        }));
        _key = System.Text.Encoding.UTF8.GetBytes("hot-key");
        _data[_key] = new byte[64];
    }

    [Benchmark(Baseline = true)]
    public void PlainLock()
    {
        var tasks = new Task[ThreadCount];
        for (var t = 0; t < ThreadCount; t++)
        {
            tasks[t] = Task.Run(() =>
            {
                for (var i = 0; i < ReadsPerThread; i++)
                {
                    lock (_mutex)
                    {
                        _data.TryGetValue(_key, out _);
                    }
                }
            });
        }

        Task.WaitAll(tasks);
    }

    [Benchmark]
    public void ReaderWriterLockSlim()
    {
        var tasks = new Task[ThreadCount];
        for (var t = 0; t < ThreadCount; t++)
        {
            tasks[t] = Task.Run(() =>
            {
                for (var i = 0; i < ReadsPerThread; i++)
                {
                    _rwLock.EnterReadLock();
                    try
                    {
                        _data.TryGetValue(_key, out _);
                    }
                    finally
                    {
                        _rwLock.ExitReadLock();
                    }
                }
            });
        }

        Task.WaitAll(tasks);
    }

    /// <summary>No lock at all — the theoretical ceiling. Not correct for ZLDB as-is (a concurrent
    /// write could still mutate the dictionary's internal tree structure mid-traversal), included
    /// only to show how much of the cost is locking overhead versus the read work itself.</summary>
    [Benchmark]
    public void NoLock()
    {
        var tasks = new Task[ThreadCount];
        for (var t = 0; t < ThreadCount; t++)
        {
            tasks[t] = Task.Run(() =>
            {
                for (var i = 0; i < ReadsPerThread; i++)
                    _data.TryGetValue(_key, out _);
            });
        }

        Task.WaitAll(tasks);
    }
}
