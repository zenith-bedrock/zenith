namespace Zenith.LevelDB;

/// <summary>Opções de abertura do banco.</summary>
public sealed class Options
{
    public bool CreateIfMissing { get; set; } = true;
    public bool ErrorIfExists { get; set; }

    /// <summary>Approximate memtable size that triggers a flush to a new table. Only the memtable is written: the cost of a flush does not grow with the database.</summary>
    public int WriteBufferSize { get; set; } = 4 * 1024 * 1024;

    /// <summary>Target size of a table's data block. A value larger than this gets a block of its own.</summary>
    public int BlockSize { get; set; } = 16 * 1024;

    /// <summary>Bloom filter bits per key. Ten keeps false positives near 1%.</summary>
    public int BloomBitsPerKey { get; set; } = 10;

    /// <summary>Merge this many neighbouring tables of similar size into one (size-tiered compaction).</summary>
    public int CompactionFanIn { get; set; } = 4;

    /// <summary>Above this many tables the smallest neighbouring run is merged even when the sizes differ.</summary>
    public int MaxTableCount { get; set; } = 12;

    /// <summary>Compact on a background thread. Turn it off for deterministic tests; <see cref="DB.CompactNow"/> still works.</summary>
    public bool BackgroundCompaction { get; set; } = true;

    /// <summary>
    /// Observability hook: invoked with a human-readable message when WAL replay (during
    /// <c>Open</c>) detects genuine corruption — a structurally complete record with a CRC
    /// mismatch — as opposed to a normal crash-torn tail. Per-instance (ADR §119): a process can
    /// legitimately hold more than one <see cref="DB"/> open at once (multiple worlds, or
    /// <see cref="DB.Repair"/> opening one internally) — a shared static hook would let setting it
    /// on one instance silently affect every other <see cref="DB"/> in the process, and would only
    /// ever fire for whichever instance happened to still be replaying its WAL when set. Not wired
    /// to a logger by default — this is a leaf library with no dependency on `Zenith.Diagnostics`.
    /// </summary>
    public Action<string>? OnCorruption { get; set; }
}

/// <summary>Opções de leitura (v1 ignora verify_checksums).</summary>
public sealed class ReadOptions
{
    public static ReadOptions Default { get; } = new();
}

/// <summary>Opções de escrita no journal (WAL).</summary>
public sealed class WriteOptions
{
    public static WriteOptions Default { get; } = new();

    /// <summary>
    /// Se true, após append no journal chama <c>FileStream.Flush(true)</c> (fsync)
    /// antes de retornar. Default false = flush OS buffer apenas.
    /// </summary>
    public bool Sync { get; set; }
}
