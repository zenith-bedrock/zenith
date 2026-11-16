using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text;

namespace Zenith.World;

readonly record struct ChunkCoord(int X, int Z);

/// <summary>World generator identity — see <see cref="ChunkStorageExtensions.GetWorldMetadataAsync"/>.</summary>
readonly record struct WorldMetadata(string Terrain, int Seed);

/// <summary>
/// The world's storage seam (ADR §168): records addressed by <see cref="RecordKey"/>, values opaque to the store.
/// What a record means is in <see cref="ChunkStorageExtensions"/>; how it reaches the disk is in the implementation.
/// <see cref="ValueTask"/> from day one.
/// </summary>
interface IChunkStorage
{
    /// <summary>The stored bytes, or null when the record was never written.</summary>
    ValueTask<byte[]?> GetAsync(RecordKey key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Write-behind: completes once the record is accepted, not when the disk has it. The array is kept by
    /// reference, so the caller must not change it afterwards.
    /// </summary>
    ValueTask PutAsync(RecordKey key, byte[] blob, CancellationToken cancellationToken = default);

    /// <summary>Await in-flight persistence. Shutdown only — never GameLoop (ADR §41).</summary>
    ValueTask FlushAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Backend operational property (ADR §114/§121), e.g. <c>"zldb.num-entries"</c>. Diagnostics only, never
    /// gameplay logic. False for an unknown name or a backend with none (never throws).
    /// </summary>
    bool TryGetProperty(string property, out string value);
}

/// <summary>What each <see cref="RecordKind"/> means, as calls the game can read.</summary>
static class ChunkStorageExtensions
{
    /// <summary>The stored blob of a column, or null when it was never saved (the world then generates it).</summary>
    public static ValueTask<byte[]?> GetColumnAsync(this IChunkStorage storage, ChunkCoord coord, CancellationToken ct = default) =>
        storage.GetAsync(RecordKey.Column(coord), ct);

    public static ValueTask PutColumnAsync(this IChunkStorage storage, ChunkCoord coord, byte[] blob, CancellationToken ct = default) =>
        storage.PutAsync(RecordKey.Column(coord), blob, ct);

    public static ValueTask<byte[]?> GetInventoryAsync(this IChunkStorage storage, Guid uuid, CancellationToken ct = default) =>
        storage.GetAsync(RecordKey.Player(RecordKind.Inventory, uuid), ct);

    public static ValueTask PutInventoryAsync(this IChunkStorage storage, Guid uuid, byte[] blob, CancellationToken ct = default) =>
        storage.PutAsync(RecordKey.Player(RecordKind.Inventory, uuid), blob, ct);

    public static ValueTask<byte[]?> GetArmorAsync(this IChunkStorage storage, Guid uuid, CancellationToken ct = default) =>
        storage.GetAsync(RecordKey.Player(RecordKind.Armor, uuid), ct);

    public static ValueTask PutArmorAsync(this IChunkStorage storage, Guid uuid, byte[] blob, CancellationToken ct = default) =>
        storage.PutAsync(RecordKey.Player(RecordKind.Armor, uuid), blob, ct);

    public static ValueTask<byte[]?> GetOffhandAsync(this IChunkStorage storage, Guid uuid, CancellationToken ct = default) =>
        storage.GetAsync(RecordKey.Player(RecordKind.Offhand, uuid), ct);

    public static ValueTask PutOffhandAsync(this IChunkStorage storage, Guid uuid, byte[] blob, CancellationToken ct = default) =>
        storage.PutAsync(RecordKey.Player(RecordKind.Offhand, uuid), blob, ct);

    public static ValueTask<byte[]?> GetPlayerDataAsync(this IChunkStorage storage, Guid uuid, CancellationToken ct = default) =>
        storage.GetAsync(RecordKey.Player(RecordKind.PlayerData, uuid), ct);

    public static ValueTask PutPlayerDataAsync(this IChunkStorage storage, Guid uuid, byte[] blob, CancellationToken ct = default) =>
        storage.PutAsync(RecordKey.Player(RecordKind.PlayerData, uuid), blob, ct);

    /// <summary>The world's game rules in <see cref="GameRuleCodec"/> bytes; null until a rule is first changed.</summary>
    public static ValueTask<byte[]?> GetGameRulesAsync(this IChunkStorage storage, CancellationToken ct = default) =>
        storage.GetAsync(RecordKey.Singleton(RecordKind.GameRules), ct);

    public static ValueTask PutGameRulesAsync(this IChunkStorage storage, byte[] blob, CancellationToken ct = default) =>
        storage.PutAsync(RecordKey.Singleton(RecordKind.GameRules), blob, ct);

    /// <summary>A module's opaque state; null until the module first saves.</summary>
    public static ValueTask<byte[]?> GetModuleStateAsync(this IChunkStorage storage, string module, CancellationToken ct = default) =>
        storage.GetAsync(RecordKey.Module(module), ct);

    public static ValueTask PutModuleStateAsync(this IChunkStorage storage, string module, byte[] blob, CancellationToken ct = default) =>
        storage.PutAsync(RecordKey.Module(module), blob, ct);

    /// <summary>
    /// World identity (generator mode + seed): set once, the first time a world is opened, and never overwritten
    /// (Phase XXIV: the seed is world identity, not a per-restart config knob). Null means a brand-new world.
    /// </summary>
    public static async ValueTask<WorldMetadata?> GetWorldMetadataAsync(this IChunkStorage storage, CancellationToken ct = default)
    {
        var bytes = await storage.GetAsync(RecordKey.Singleton(RecordKind.WorldMetadata), ct).ConfigureAwait(false);
        if (bytes is null || bytes.Length < sizeof(int) + 1) return null;
        return new WorldMetadata(
            Encoding.UTF8.GetString(bytes.AsSpan(sizeof(int))),
            BinaryPrimitives.ReadInt32LittleEndian(bytes));
    }

    public static ValueTask PutWorldMetadataAsync(this IChunkStorage storage, WorldMetadata metadata, CancellationToken ct = default)
    {
        var terrain = Encoding.UTF8.GetBytes(metadata.Terrain);
        var blob = new byte[sizeof(int) + terrain.Length];
        BinaryPrimitives.WriteInt32LittleEndian(blob, metadata.Seed);
        terrain.CopyTo(blob.AsSpan(sizeof(int)));
        return storage.PutAsync(RecordKey.Singleton(RecordKind.WorldMetadata), blob, ct);
    }
}

/// <summary>Thread-safe, RAM only: the storage tests run against (ADR §39 / §60).</summary>
sealed class InMemoryChunkStorage : IChunkStorage
{
    private readonly ConcurrentDictionary<RecordKey, byte[]> _records = new();
    private int _putCount;

    /// <summary>Column puts so far — tests for ADR §45 (a column nobody edited is never written).</summary>
    public int PutCount => Volatile.Read(ref _putCount);

    public ValueTask<byte[]?> GetAsync(RecordKey key, CancellationToken cancellationToken = default)
    {
        _records.TryGetValue(key, out var blob);
        return ValueTask.FromResult<byte[]?>(blob);
    }

    public ValueTask PutAsync(RecordKey key, byte[] blob, CancellationToken cancellationToken = default)
    {
        if (key.Kind == RecordKind.Column) Interlocked.Increment(ref _putCount);
        _records[key] = blob;
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// An independent copy of what has been stored so far: what a restart after a crash would find on disk. Tests use it to boot a
    /// second world from the bytes that had already been written, and none that had not.
    /// </summary>
    public InMemoryChunkStorage Snapshot()
    {
        var copy = new InMemoryChunkStorage();
        foreach (var (key, blob) in _records) copy._records[key] = blob;
        return copy;
    }

    public ValueTask FlushAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public bool TryGetProperty(string property, out string value)
    {
        value = "";
        return false;
    }
}
