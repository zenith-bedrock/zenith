using System.Collections.Concurrent;
using System.Threading;
using Zenith.Raknet.Log;

namespace Zenith.World;

/// <summary>
/// Mundo: colunas base via <see cref="IChunkStorage"/> + overlay esparso permanente.
/// Overlay warn-once at threshold (ADR §36) — sem refuse/eviction nesta leva.
/// Mutação nunca reescreve subchunk; só overlay + UpdateBlock.
/// Column index (§36 adendo): <see cref="GetOverlaysInColumn"/> O(bucket), not O(all overlays).
/// </summary>
sealed class World
{
    /// <summary>Wire Overworld — keep equal to Network.Packets.DimensionId.Overworld (dual layer SSOT).</summary>
    internal const int OverworldDimensionId = 0;

    internal const int OverrideWarnThreshold = 10_000;

    private readonly IChunkStorage _storage;
    private readonly byte[] _flatOverworldPayload;
    private readonly int _flatSubChunkCount;
    private readonly ConcurrentDictionary<(int X, int Y, int Z), int> _blockOverrides = new();
    /// <summary>
    /// Secondary index by chunk for column stream. SSOT for GetBlock remains <see cref="_blockOverrides"/>;
    /// both updated only via <see cref="StoreOverlay"/> (no drift).
    /// </summary>
    private readonly ConcurrentDictionary<(int Cx, int Cz), ConcurrentDictionary<(int X, int Y, int Z), int>> _overlaysByChunk = new();
    private readonly ILogger? _logger;
    private int _overrideWarned;

    public FloorDropStore FloorDrops { get; }
    public ChestStore Chests { get; }

    public int OverrideCount => _blockOverrides.Count;

    public World(IChunkStorage storage, ILogger? logger = null)
    {
        _storage = storage;
        _logger = logger;
        FloorDrops = new FloorDropStore(logger);
        Chests = new ChestStore(logger);
        (_flatSubChunkCount, _flatOverworldPayload) = ChunkPayloads.BuildFlatOverworld();
        storage.ForEachOverlayAsync(StoreOverlay)
            .AsTask()
            .GetAwaiter()
            .GetResult();
        storage.ForEachChestAsync((x, y, z, blob) => Chests.TryLoadFromBlob(x, y, z, blob))
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }

    public void PersistChest(int x, int y, int z)
    {
        var blob = Chests.PackBlob(x, y, z);
        if (blob is null) return;
        _ = _storage.PutChestAsync(x, y, z, blob);
    }

    public void DeletePersistedChest(int x, int y, int z) =>
        _ = _storage.DeleteChestAsync(x, y, z);

    public void PersistInventory(Guid uuid, Player.PlayerInventory inventory) =>
        _ = _storage.PutInventoryAsync(uuid, inventory.PackMainBlob());

    public bool TryLoadInventory(Guid uuid, Player.PlayerInventory inventory)
    {
        var blob = _storage.GetInventoryAsync(uuid).AsTask().GetAwaiter().GetResult();
        return blob is not null && inventory.TryLoadMainFromBlob(blob);
    }

    /// <summary>Await in-flight chest/inv/overlay writes — shutdown only (ADR §41).</summary>
    public ValueTask FlushPersistenceAsync(CancellationToken cancellationToken = default) =>
        _storage.FlushAsync(cancellationToken);

    /// <summary>
    /// Lê payload base (gera flat on miss / migra empty legado) e aplica overlays da coluna na leitura.
    /// Seguro para chamar da thread de rede.
    /// </summary>
    public async ValueTask<ColumnReadResult> GetOrCreateColumnAsync(int chunkX, int chunkZ, CancellationToken ct = default)
    {
        var coord = new ChunkCoord(chunkX, chunkZ);
        var existing = await _storage.GetAsync(coord, ct).ConfigureAwait(false);

        ChunkColumnData bas;
        if (existing is not null && existing.SubChunkCount > 0 && LooksLikeTerrainPayload(existing))
        {
            bas = existing;
        }
        else if (existing is null)
        {
            // ADR §45: miss → in-memory flat only (do not materialize identical c:x:z blobs).
            bas = new ChunkColumnData(coord, dimensionId: OverworldDimensionId, _flatSubChunkCount, _flatOverworldPayload);
        }
        else
        {
            // Legacy empty/corrupt c: — regenerate flat and Put so disk self-heals.
            bas = new ChunkColumnData(coord, dimensionId: OverworldDimensionId, _flatSubChunkCount, _flatOverworldPayload);
            await _storage.PutAsync(bas, ct).ConfigureAwait(false);
            bas = (await _storage.GetAsync(coord, ct).ConfigureAwait(false)) ?? bas;
        }

        var overlays = GetOverlaysInColumn(chunkX, chunkZ);
        return new ColumnReadResult(bas, overlays);
    }

    public async ValueTask<IReadOnlyList<ColumnReadResult>> GetRadiusAsync(int centerX, int centerZ, int radius, CancellationToken ct = default)
    {
        var list = new List<ColumnReadResult>((radius * 2 + 1) * (radius * 2 + 1));
        for (var x = centerX - radius; x <= centerX + radius; x++)
        {
            for (var z = centerZ - radius; z <= centerZ + radius; z++)
                list.Add(await GetOrCreateColumnAsync(x, z, ct).ConfigureAwait(false));
        }

        return list;
    }

    public static int AirRuntimeId => Blocks.Air;

    /// <summary>
    /// Autoridade em RAM no tick. Persistência de overlay é fire-and-forget
    /// (fila no storage) — o GameLoop nunca espera disco.
    /// </summary>
    public void SetBlock(int x, int y, int z, int blockRuntimeId)
    {
        StoreOverlay(x, y, z, blockRuntimeId);
        _ = _storage.PutOverlayAsync(x, y, z, blockRuntimeId);

        if (_blockOverrides.Count >= OverrideWarnThreshold &&
            Interlocked.Exchange(ref _overrideWarned, 1) == 0)
        {
            _logger?.Warning(
                $"World overlays crossed OverrideWarnThreshold ({OverrideWarnThreshold}). " +
                "No refuse/eviction — persistence redesign needed; continuing unbounded.");
        }
    }

    /// <summary>
    /// Single write path for flat map + chunk index (SetBlock + LevelDB hydrate).
    /// Break → air overwrites in place (no TryRemove) — same as pre-index behavior.
    /// </summary>
    private void StoreOverlay(int x, int y, int z, int blockRuntimeId)
    {
        var cell = (x, y, z);
        _blockOverrides[cell] = blockRuntimeId;
        var chunk = (ToChunk(x), ToChunk(z));
        var bucket = _overlaysByChunk.GetOrAdd(chunk, static _ => new ConcurrentDictionary<(int X, int Y, int Z), int>());
        bucket[cell] = blockRuntimeId;
    }

    /// <summary>Overlay se existir; senão amostra do terreno base flat.</summary>
    public int GetBlock(int x, int y, int z)
    {
        if (_blockOverrides.TryGetValue((x, y, z), out var id))
            return id;
        return SampleBaseBlock(x, y, z);
    }

    public IReadOnlyList<BlockOverride> GetOverlaysInColumn(int chunkX, int chunkZ)
    {
        if (!_overlaysByChunk.TryGetValue((chunkX, chunkZ), out var bucket) || bucket.IsEmpty)
            return Array.Empty<BlockOverride>();

        var list = new List<BlockOverride>(bucket.Count);
        foreach (var kv in bucket)
        {
            var (bx, by, bz) = kv.Key;
            list.Add(new BlockOverride(bx, by, bz, kv.Value));
        }

        return list;
    }

    private static int SampleBaseBlock(int x, int y, int z)
    {
        _ = x;
        _ = z;
        if (y >= Blocks.FlatMinY && y <= Blocks.FlatStoneTopY) return Blocks.Stone;
        if (y == Blocks.FlatGrassY) return Blocks.GrassBlock;
        return Blocks.Air;
    }

    /// <summary>
    /// SubChunkCount&gt;0 with a tiny payload is almost always corrupt legacy / empty biomes-only
    /// leftovers — regenerate flat so clients never get all-air columns that stick in LevelDB.
    /// </summary>
    private static bool LooksLikeTerrainPayload(ChunkColumnData column)
    {
        // Flat: at least version + layers + one palette header byte.
        return column.ExtraPayload.Length >= 3 && column.ExtraPayload[0] == 8;
    }

    private static int ToChunk(int block) => block >> 4;
}
