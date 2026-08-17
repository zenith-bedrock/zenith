namespace Zenith.LevelDB;

/// <summary>
/// Wraps a storage-layer I/O failure (disk full, permission denied, path issues) encountered
/// during durability-critical work — WAL append/flush, snapshot write — so a caller can catch one
/// clear type instead of needing to know which raw BCL exception (<see cref="IOException"/>,
/// <see cref="UnauthorizedAccessException"/>, ...) a given failure mode happens to throw. The
/// original exception is always preserved as <see cref="Exception.InnerException"/>.
/// Not used for logical/format errors (missing/corrupt <c>CURRENT</c>, corrupted snapshot, bad
/// batch encoding) — those already have clear, specific types
/// (<see cref="InvalidOperationException"/>/<see cref="InvalidDataException"/>) and are left as-is.
/// </summary>
public sealed class LevelDbIOException : IOException
{
    public LevelDbIOException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
