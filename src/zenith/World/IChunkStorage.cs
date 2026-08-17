namespace Zenith.World;

readonly record struct ChunkCoord(int X, int Z);

/// <summary>World generator identity — see <see cref="IChunkStorage.GetWorldMetadataAsync"/>.</summary>
readonly record struct WorldMetadata(string Terrain, int Seed);

/// <summary>
/// Coluna de chunk pronta pra transmissão (payload de LevelChunk).
/// Imutável após publicação — leituras fora do tick são thread-safe.
/// </summary>
sealed class ChunkColumnData
{
    public ChunkCoord Coord { get; }
    public int DimensionId { get; }
    public int SubChunkCount { get; }
    public byte[] ExtraPayload { get; }

    public ChunkColumnData(ChunkCoord coord, int dimensionId, int subChunkCount, byte[] extraPayload)
    {
        Coord = coord;
        DimensionId = dimensionId;
        SubChunkCount = subChunkCount;
        ExtraPayload = extraPayload;
    }
}

/// <summary>
/// Seam de storage. <see cref="ValueTask"/> desde o dia 1.
/// Overlay esparso (<c>ov:</c>) + chests (<c>ct:</c>) + inventário (<c>inv:</c>) + playerdata (<c>pd:</c>) — ADR §39 / §60.
/// </summary>
interface IChunkStorage
{
    ValueTask<ChunkColumnData?> GetAsync(ChunkCoord coord, CancellationToken cancellationToken = default);
    ValueTask PutAsync(ChunkColumnData column, CancellationToken cancellationToken = default);

    ValueTask PutOverlayAsync(int x, int y, int z, int blockRuntimeId, CancellationToken cancellationToken = default);
    ValueTask DeleteOverlayAsync(int x, int y, int z, CancellationToken cancellationToken = default);
    ValueTask ForEachOverlayAsync(Action<int, int, int, int> visitor, CancellationToken cancellationToken = default);

    /// <summary>Per-chunk overlay read (ADR §114) — resident-chunk-state hydrate path.</summary>
    ValueTask<IReadOnlyList<BlockOverride>> LoadOverlaysForChunkAsync(int chunkX, int chunkZ, CancellationToken cancellationToken = default);

    ValueTask PutChestAsync(int x, int y, int z, byte[] blob, CancellationToken cancellationToken = default);
    ValueTask DeleteChestAsync(int x, int y, int z, CancellationToken cancellationToken = default);
    ValueTask ForEachChestAsync(Action<int, int, int, byte[]> visitor, CancellationToken cancellationToken = default);

    /// <summary>Per-chunk chest read (ADR §114) — resident-chunk-state hydrate path.</summary>
    ValueTask<IReadOnlyList<(int X, int Y, int Z, byte[] Blob)>> LoadChestsForChunkAsync(int chunkX, int chunkZ, CancellationToken cancellationToken = default);

    ValueTask PutInventoryAsync(Guid uuid, byte[] blob, CancellationToken cancellationToken = default);
    ValueTask<byte[]?> GetInventoryAsync(Guid uuid, CancellationToken cancellationToken = default);

    ValueTask PutArmorAsync(Guid uuid, byte[] blob, CancellationToken cancellationToken = default);
    ValueTask<byte[]?> GetArmorAsync(Guid uuid, CancellationToken cancellationToken = default);

    ValueTask PutPlayerDataAsync(Guid uuid, byte[] blob, CancellationToken cancellationToken = default);
    ValueTask<byte[]?> GetPlayerDataAsync(Guid uuid, CancellationToken cancellationToken = default);

    /// <summary>
    /// World identity (generator mode + seed) — set once, the first time a world is ever opened, and
    /// never overwritten after (Phase XXIV: seed is world identity, not a per-restart config knob).
    /// Null means this is a brand-new world with no committed generator identity yet.
    /// </summary>
    ValueTask<WorldMetadata?> GetWorldMetadataAsync(CancellationToken cancellationToken = default);
    ValueTask PutWorldMetadataAsync(WorldMetadata metadata, CancellationToken cancellationToken = default);

    /// <summary>Await in-flight persistence (chest/inv/pd/overlay). Shutdown only — never GameLoop (ADR §41).</summary>
    ValueTask FlushAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Backend-specific operational property (ADR §114/§121) — e.g. <c>Zenith.LevelDB</c>'s
    /// <c>"zldb.num-entries"</c>/<c>"zldb.approximate-memory-usage"</c> via <c>DB.GetProperty</c>.
    /// Diagnostics only, never gameplay logic. False for an unrecognized name or a backend with no
    /// such properties (never throws).
    /// </summary>
    bool TryGetProperty(string property, out string value);
}

/// <summary>
/// Cache thread-safe; chunks tratados como imutáveis após Put.
/// Overlay/chest/inv/pd em RAM para testes (ADR §39 / §60).
/// </summary>
sealed class InMemoryChunkStorage : IChunkStorage
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<ChunkCoord, ChunkColumnData> _chunks = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(int X, int Y, int Z), int> _overlays = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(int X, int Y, int Z), byte[]> _chests = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, byte[]> _inventories = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, byte[]> _armor = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, byte[]> _playerData = new();
    private WorldMetadata? _worldMetadata;

    /// <summary>Column Put count — tests for ADR §45 sparse flat.</summary>
    public int PutCount { get; private set; }

    public ValueTask<ChunkColumnData?> GetAsync(ChunkCoord coord, CancellationToken cancellationToken = default)
    {
        _chunks.TryGetValue(coord, out var column);
        return ValueTask.FromResult<ChunkColumnData?>(column);
    }

    public ValueTask PutAsync(ChunkColumnData column, CancellationToken cancellationToken = default)
    {
        PutCount++;
        _chunks[column.Coord] = column;
        return ValueTask.CompletedTask;
    }

    public ValueTask PutOverlayAsync(int x, int y, int z, int blockRuntimeId, CancellationToken cancellationToken = default)
    {
        _overlays[(x, y, z)] = blockRuntimeId;
        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteOverlayAsync(int x, int y, int z, CancellationToken cancellationToken = default)
    {
        _overlays.TryRemove((x, y, z), out _);
        return ValueTask.CompletedTask;
    }

    public ValueTask ForEachOverlayAsync(Action<int, int, int, int> visitor, CancellationToken cancellationToken = default)
    {
        foreach (var (key, id) in _overlays)
            visitor(key.X, key.Y, key.Z, id);
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<BlockOverride>> LoadOverlaysForChunkAsync(int chunkX, int chunkZ, CancellationToken cancellationToken = default)
    {
        var results = new List<BlockOverride>();
        foreach (var (key, id) in _overlays)
        {
            if (ChunkMath.BlockToChunk(key.X) == chunkX && ChunkMath.BlockToChunk(key.Z) == chunkZ)
                results.Add(new BlockOverride(key.X, key.Y, key.Z, id));
        }
        return ValueTask.FromResult<IReadOnlyList<BlockOverride>>(results);
    }

    public ValueTask PutChestAsync(int x, int y, int z, byte[] blob, CancellationToken cancellationToken = default)
    {
        _chests[(x, y, z)] = blob;
        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteChestAsync(int x, int y, int z, CancellationToken cancellationToken = default)
    {
        _chests.TryRemove((x, y, z), out _);
        return ValueTask.CompletedTask;
    }

    public ValueTask ForEachChestAsync(Action<int, int, int, byte[]> visitor, CancellationToken cancellationToken = default)
    {
        foreach (var (key, blob) in _chests)
            visitor(key.X, key.Y, key.Z, blob);
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<(int X, int Y, int Z, byte[] Blob)>> LoadChestsForChunkAsync(int chunkX, int chunkZ, CancellationToken cancellationToken = default)
    {
        var results = new List<(int, int, int, byte[])>();
        foreach (var (key, blob) in _chests)
        {
            if (ChunkMath.BlockToChunk(key.X) == chunkX && ChunkMath.BlockToChunk(key.Z) == chunkZ)
                results.Add((key.X, key.Y, key.Z, blob));
        }
        return ValueTask.FromResult<IReadOnlyList<(int X, int Y, int Z, byte[] Blob)>>(results);
    }

    public ValueTask PutInventoryAsync(Guid uuid, byte[] blob, CancellationToken cancellationToken = default)
    {
        _inventories[uuid] = blob;
        return ValueTask.CompletedTask;
    }

    public ValueTask<byte[]?> GetInventoryAsync(Guid uuid, CancellationToken cancellationToken = default)
    {
        _inventories.TryGetValue(uuid, out var blob);
        return ValueTask.FromResult<byte[]?>(blob);
    }

    public ValueTask PutArmorAsync(Guid uuid, byte[] blob, CancellationToken cancellationToken = default)
    {
        _armor[uuid] = blob;
        return ValueTask.CompletedTask;
    }

    public ValueTask<byte[]?> GetArmorAsync(Guid uuid, CancellationToken cancellationToken = default)
    {
        _armor.TryGetValue(uuid, out var blob);
        return ValueTask.FromResult<byte[]?>(blob);
    }

    public ValueTask PutPlayerDataAsync(Guid uuid, byte[] blob, CancellationToken cancellationToken = default)
    {
        _playerData[uuid] = blob;
        return ValueTask.CompletedTask;
    }

    public ValueTask<byte[]?> GetPlayerDataAsync(Guid uuid, CancellationToken cancellationToken = default)
    {
        _playerData.TryGetValue(uuid, out var blob);
        return ValueTask.FromResult<byte[]?>(blob);
    }

    public ValueTask FlushAsync(CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public ValueTask<WorldMetadata?> GetWorldMetadataAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(_worldMetadata);

    public ValueTask PutWorldMetadataAsync(WorldMetadata metadata, CancellationToken cancellationToken = default)
    {
        _worldMetadata = metadata;
        return ValueTask.CompletedTask;
    }

    public bool TryGetProperty(string property, out string value)
    {
        value = "";
        return false;
    }
}
