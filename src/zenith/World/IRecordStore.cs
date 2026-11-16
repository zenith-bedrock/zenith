namespace Zenith.World;

/// <summary>
/// Synchronous, durable key-value backend under <see cref="WriteBehindStorage"/> (ADR §168). It knows records and
/// batches, nothing about threads, queues or async: the layer above owns those.
/// </summary>
interface IRecordStore : IDisposable
{
    byte[]? Get(RecordKey key);

    /// <summary>Applies every entry atomically. <paramref name="durable"/> asks for an fsync before returning.</summary>
    void Write(ReadOnlySpan<KeyValuePair<RecordKey, byte[]>> entries, bool durable);

    /// <summary>Operational counter for diagnostics (never gameplay). False for an unknown name.</summary>
    bool TryGetProperty(string property, out string value);
}
