namespace Zenith.World;

readonly record struct ChunkCoord(int X, int Z);

/// <summary>World generator identity — see <see cref="IChunkStorage.GetWorldMetadataAsync"/>.</summary>
readonly record struct WorldMetadata(string Terrain, int Seed);

/// <summary>
/// Seam de storage. <see cref="ValueTask"/> desde o dia 1.
/// Colunas (<c>c:</c>, one <see cref="ColumnCodec"/> blob each) + inventário (<c>inv:</c>) + playerdata (<c>pd:</c>) — ADR §167.
/// </summary>
interface IChunkStorage
{
    /// <summary>The stored blob of a column, or null when it was never saved (the world then generates it).</summary>
    ValueTask<byte[]?> GetColumnAsync(ChunkCoord coord, CancellationToken cancellationToken = default);

    /// <summary>Write-behind: completes once the blob is queued, not when the disk has it.</summary>
    ValueTask PutColumnAsync(ChunkCoord coord, byte[] blob, CancellationToken cancellationToken = default);

    ValueTask PutInventoryAsync(Guid uuid, byte[] blob, CancellationToken cancellationToken = default);
    ValueTask<byte[]?> GetInventoryAsync(Guid uuid, CancellationToken cancellationToken = default);

    ValueTask PutArmorAsync(Guid uuid, byte[] blob, CancellationToken cancellationToken = default);
    ValueTask<byte[]?> GetArmorAsync(Guid uuid, CancellationToken cancellationToken = default);
    ValueTask PutOffhandAsync(Guid uuid, byte[] blob, CancellationToken cancellationToken = default);
    ValueTask<byte[]?> GetOffhandAsync(Guid uuid, CancellationToken cancellationToken = default);

    ValueTask PutPlayerDataAsync(Guid uuid, byte[] blob, CancellationToken cancellationToken = default);
    ValueTask<byte[]?> GetPlayerDataAsync(Guid uuid, CancellationToken cancellationToken = default);

    /// <summary>
    /// World identity (generator mode + seed) — set once, the first time a world is ever opened, and
    /// never overwritten after (Phase XXIV: seed is world identity, not a per-restart config knob).
    /// Null means this is a brand-new world with no committed generator identity yet.
    /// </summary>
    ValueTask<WorldMetadata?> GetWorldMetadataAsync(CancellationToken cancellationToken = default);
    ValueTask PutWorldMetadataAsync(WorldMetadata metadata, CancellationToken cancellationToken = default);

    /// <summary>The world's game rules in <see cref="GameRuleCodec"/> bytes; null until a rule is first changed.</summary>
    ValueTask<byte[]?> GetGameRulesAsync(CancellationToken cancellationToken = default);
    ValueTask PutGameRulesAsync(byte[] blob, CancellationToken cancellationToken = default);

    /// <summary>Await in-flight persistence (column/inv/pd). Shutdown only — never GameLoop (ADR §41).</summary>
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
/// Thread-safe.
/// Colunas/inv/pd em RAM para testes (ADR §39 / §60).
/// </summary>
sealed class InMemoryChunkStorage : IChunkStorage
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<ChunkCoord, byte[]> _columns = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, byte[]> _inventories = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, byte[]> _armor = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, byte[]> _offhand = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, byte[]> _playerData = new();
    private WorldMetadata? _worldMetadata;
    private byte[]? _gameRules;

    /// <summary>Column Put count — tests for ADR §45 (a column nobody edited is never written).</summary>
    public int PutCount { get; private set; }

    public ValueTask<byte[]?> GetColumnAsync(ChunkCoord coord, CancellationToken cancellationToken = default)
    {
        _columns.TryGetValue(coord, out var blob);
        return ValueTask.FromResult<byte[]?>(blob);
    }

    public ValueTask PutColumnAsync(ChunkCoord coord, byte[] blob, CancellationToken cancellationToken = default)
    {
        PutCount++;
        _columns[coord] = blob;
        return ValueTask.CompletedTask;
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

    public ValueTask PutOffhandAsync(Guid uuid, byte[] blob, CancellationToken cancellationToken = default)
    {
        _offhand[uuid] = blob;
        return ValueTask.CompletedTask;
    }

    public ValueTask<byte[]?> GetOffhandAsync(Guid uuid, CancellationToken cancellationToken = default)
    {
        _offhand.TryGetValue(uuid, out var blob);
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

    public ValueTask<byte[]?> GetGameRulesAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(_gameRules);

    public ValueTask PutGameRulesAsync(byte[] blob, CancellationToken cancellationToken = default)
    {
        _gameRules = blob;
        return ValueTask.CompletedTask;
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
