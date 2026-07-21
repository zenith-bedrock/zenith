using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Zenith.Raknet.Log;

namespace Zenith.World;

/// <summary>
/// Mundo: colunas base via <see cref="ITerrainProvider"/> + <see cref="IChunkStorage"/> + overlay esparso.
/// Overlay SoftCap (§36): refuse new keys at <see cref="OverrideSoftCap"/>; compact when rid == base;
/// overwrite / hydrate always allowed. Mutação nunca reescreve subchunk; só overlay + UpdateBlock.
/// Column index (§36 adendo): <see cref="GetOverlaysInColumn"/> O(bucket), not O(all overlays).
/// Terrain / storage seams: ADR §62.
/// </summary>
sealed class World
{
    /// <summary>Wire Overworld — keep equal to Network.Packets.DimensionId.Overworld (dual layer SSOT).</summary>
    internal const int OverworldDimensionId = 0;

    /// <summary>SoftCap for new overlay keys (same order as historical warn threshold).</summary>
    internal const int OverrideSoftCap = 10_000;

    private readonly IChunkStorage _storage;
    private readonly ITerrainProvider _terrain;
    private readonly ConcurrentDictionary<(int X, int Y, int Z), int> _blockOverrides = new();
    /// <summary>
    /// Secondary index by chunk for column stream. SSOT for GetBlock remains <see cref="_blockOverrides"/>;
    /// both updated only via <see cref="StoreOverlay"/> / <see cref="RemoveOverlay"/> (no drift).
    /// </summary>
    private readonly ConcurrentDictionary<(int Cx, int Cz), ConcurrentDictionary<(int X, int Y, int Z), int>> _overlaysByChunk = new();
    private readonly ILogger? _logger;
    private int _softCapWarned;

    public FloorDropStore FloorDrops { get; }
    public ChestStore Chests { get; }
    public GravityPendingStore GravityPending { get; }

    public int OverrideCount => _blockOverrides.Count;

    /// <summary>Feet Y for join/respawn above base terrain at (x,z) — ignores overlays.</summary>
    public int SampleSpawnFeetY(int x, int z) => _terrain.SampleSpawnFeetY(x, z);

    /// <summary>Spawn biome wire pair from terrain provider (flat → plains).</summary>
    public SpawnBiome SampleSpawnBiome(int x, int z) => _terrain.SampleSpawnBiome(x, z);

    public World(IChunkStorage storage, ILogger? logger = null, ITerrainProvider? terrain = null)
    {
        _storage = storage;
        _terrain = terrain ?? FlatTerrainProvider.Instance;
        _logger = logger;
        FloorDrops = new FloorDropStore(logger);
        Chests = new ChestStore(logger);
        GravityPending = new GravityPendingStore(logger);
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

    public void PersistInventory(Player.Player player)
    {
        if (!player.IdentityStable) return;
        PersistInventory(player.Uuid, player.Inventory);
    }

    /// <summary>Raw Put for tests / loaders — prefer <see cref="PersistInventory(Player.Player)"/> on live sessions.</summary>
    public void PersistInventory(Guid uuid, Player.PlayerInventory inventory) =>
        _ = _storage.PutInventoryAsync(uuid, inventory.PackMainBlob());

    public bool TryLoadInventory(Guid uuid, Player.PlayerInventory inventory)
    {
        var blob = _storage.GetInventoryAsync(uuid).AsTask().GetAwaiter().GetResult();
        return blob is not null && inventory.TryLoadMainFromBlob(blob);
    }

    /// <summary>Quit / gamemode Persist — skips unstable identity (ADR §60).</summary>
    public void PersistPlayerData(Player.Player player)
    {
        if (!player.IdentityStable) return;
        var blob = PlayerDataBlob.Pack(
            player.PositionX,
            player.PositionY,
            player.PositionZ,
            player.Yaw,
            player.Pitch,
            player.GameMode);
        _ = _storage.PutPlayerDataAsync(player.Uuid, blob);
    }

    /// <summary>
    /// Login hydrate. False = miss / corrupt / OOB → caller keeps terrain spawn + config GameMode.
    /// </summary>
    public bool TryLoadPlayerData(Guid uuid, out float x, out float y, out float z, out float yaw, out float pitch, out Player.GameMode mode)
    {
        x = y = z = yaw = pitch = 0;
        mode = Player.GameMode.Survival;
        var blob = _storage.GetPlayerDataAsync(uuid).AsTask().GetAwaiter().GetResult();
        return blob is not null && PlayerDataBlob.TryUnpack(blob, out x, out y, out z, out yaw, out pitch, out mode);
    }

    /// <summary>
    /// True when feet + eye cell are air (standing room). Used to heal flat-era pd: into noise hills.
    /// </summary>
    public bool IsSpawnFeetClear(int blockX, int feetY, int blockZ) =>
        GetBlock(blockX, feetY, blockZ) == AirRuntimeId &&
        GetBlock(blockX, feetY + 1, blockZ) == AirRuntimeId;

    /// <summary>
    /// If saved pose is buried / flooded, snap feet to <see cref="SampleSpawnFeetY"/> at the same XZ.
    /// Returns true when pose changed (caller should Persist).
    /// </summary>
    public bool TryHealSpawnFeet(Player.Player player)
    {
        var bx = (int)MathF.Floor(player.PositionX);
        var by = (int)MathF.Floor(player.PositionY);
        var bz = (int)MathF.Floor(player.PositionZ);
        if (IsSpawnFeetClear(bx, by, bz))
            return false;

        var healedY = SampleSpawnFeetY(bx, bz);
        if (healedY == by && IsSpawnFeetClear(bx, healedY, bz))
            return false;

        player.PositionY = healedY;
        return true;
    }

    /// <summary>Await in-flight chest/inv/pd/overlay writes — shutdown only (ADR §41).</summary>
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
            // ADR §45: miss → in-memory base only (do not materialize identical c:x:z blobs).
            var terrain = _terrain.GetBaseColumn(chunkX, chunkZ);
            bas = new ChunkColumnData(coord, dimensionId: OverworldDimensionId, terrain.SubChunkCount, terrain.Payload);
        }
        else
        {
            // Legacy empty/corrupt c: — regenerate base and Put so disk self-heals.
            var terrain = _terrain.GetBaseColumn(chunkX, chunkZ);
            bas = new ChunkColumnData(coord, dimensionId: OverworldDimensionId, terrain.SubChunkCount, terrain.Payload);
            await _storage.PutAsync(bas, ct).ConfigureAwait(false);
            bas = (await _storage.GetAsync(coord, ct).ConfigureAwait(false)) ?? bas;
        }

        var overlays = GetOverlaysInColumn(chunkX, chunkZ);
        return new ColumnReadResult(bas, overlays);
    }

    public async ValueTask<IReadOnlyList<ColumnReadResult>> GetRadiusAsync(int centerX, int centerZ, int radius, CancellationToken ct = default)
    {
        var span = radius * 2 + 1;
        var count = span * span;
        var coords = new (int X, int Z)[count];
        var index = 0;
        for (var x = centerX - radius; x <= centerX + radius; x++)
        {
            for (var z = centerZ - radius; z <= centerZ + radius; z++)
                coords[index++] = (x, z);
        }

        var results = new ColumnReadResult[count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, count),
            new ParallelOptions
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = Environment.ProcessorCount
            },
            async (i, token) =>
            {
                var (x, z) = coords[i];
                results[i] = await GetOrCreateColumnAsync(x, z, token).ConfigureAwait(false);
            }).ConfigureAwait(false);

        return results;
    }

    public static int AirRuntimeId => Blocks.Air;

    /// <summary>
    /// True when <see cref="TrySetBlock"/> would not SoftCap-refuse this write
    /// (compact-to-base and overwrite of existing keys always OK).
    /// </summary>
    public bool CanAcceptBlockWrite(int x, int y, int z, int blockRuntimeId)
    {
        if (blockRuntimeId == _terrain.SampleBaseBlock(x, y, z))
            return true;
        if (_blockOverrides.ContainsKey((x, y, z)))
            return true;
        return _blockOverrides.Count < OverrideSoftCap;
    }

    /// <summary>
    /// Autoridade em RAM no tick. Persistência de overlay é fire-and-forget
    /// (fila no storage) — o GameLoop nunca espera disco.
    /// SoftCap: refuse only when inserting a <b>new</b> key at <see cref="OverrideSoftCap"/>.
    /// Writing the flat base rid removes the overlay (compaction). Existing keys always overwrite.
    /// </summary>
    public bool TrySetBlock(int x, int y, int z, int blockRuntimeId)
    {
        var cell = (x, y, z);
        var baseRid = _terrain.SampleBaseBlock(x, y, z);
        var had = _blockOverrides.ContainsKey(cell);

        if (blockRuntimeId == baseRid)
        {
            if (had)
            {
                RemoveOverlay(x, y, z);
                _ = _storage.DeleteOverlayAsync(x, y, z);
            }

            return true;
        }

        if (!had && _blockOverrides.Count >= OverrideSoftCap)
        {
            if (Interlocked.Exchange(ref _softCapWarned, 1) == 0)
            {
                _logger?.Warning(
                    $"World overlays at SoftCap ({OverrideSoftCap}): refusing new overlay cells " +
                    "(overwrite / compact-to-base still allowed — ADR §36).");
            }

            return false;
        }

        StoreOverlay(x, y, z, blockRuntimeId);
        _ = _storage.PutOverlayAsync(x, y, z, blockRuntimeId);
        return true;
    }

    /// <summary>Legacy void API — prefers <see cref="TrySetBlock"/> when refuse matters.</summary>
    public void SetBlock(int x, int y, int z, int blockRuntimeId) =>
        _ = TrySetBlock(x, y, z, blockRuntimeId);

    /// <summary>
    /// Single write path for flat map + chunk index (SetBlock + LevelDB hydrate).
    /// Hydrate bypasses SoftCap (world already on disk).
    /// </summary>
    private void StoreOverlay(int x, int y, int z, int blockRuntimeId)
    {
        var cell = (x, y, z);
        _blockOverrides[cell] = blockRuntimeId;
        var chunk = (ToChunk(x), ToChunk(z));
        var bucket = _overlaysByChunk.GetOrAdd(chunk, static _ => new ConcurrentDictionary<(int X, int Y, int Z), int>());
        bucket[cell] = blockRuntimeId;
    }

    private void RemoveOverlay(int x, int y, int z)
    {
        var cell = (x, y, z);
        _blockOverrides.TryRemove(cell, out _);
        var chunk = (ToChunk(x), ToChunk(z));
        if (_overlaysByChunk.TryGetValue(chunk, out var bucket))
        {
            bucket.TryRemove(cell, out _);
            if (bucket.IsEmpty)
                _overlaysByChunk.TryRemove(chunk, out _);
        }
    }

    /// <summary>Overlay se existir; senão amostra do terreno base (<see cref="ITerrainProvider"/>).</summary>
    public int GetBlock(int x, int y, int z)
    {
        if (_blockOverrides.TryGetValue((x, y, z), out var id))
            return id;
        return _terrain.SampleBaseBlock(x, y, z);
    }

    public IReadOnlyList<BlockOverride> GetOverlaysInColumn(int chunkX, int chunkZ)
    {
        var list = new List<BlockOverride>();
        FillOverlaysInColumn(chunkX, chunkZ, list);
        return list.Count == 0 ? Array.Empty<BlockOverride>() : list;
    }

    /// <summary>
    /// Clear <paramref name="buffer"/> and fill with overlays in the column (no per-call List alloc
    /// when the caller reuses the buffer — §54 Phase 3).
    /// </summary>
    public void FillOverlaysInColumn(int chunkX, int chunkZ, List<BlockOverride> buffer)
    {
        buffer.Clear();
        if (!_overlaysByChunk.TryGetValue((chunkX, chunkZ), out var bucket) || bucket.IsEmpty)
            return;

        foreach (var kv in bucket)
        {
            var (bx, by, bz) = kv.Key;
            buffer.Add(new BlockOverride(bx, by, bz, kv.Value));
        }
    }

    /// <summary>Visit overlays in a column without allocating a list.</summary>
    public void ForEachOverlayInColumn(int chunkX, int chunkZ, Action<BlockOverride> visitor)
    {
        if (!_overlaysByChunk.TryGetValue((chunkX, chunkZ), out var bucket) || bucket.IsEmpty)
            return;

        foreach (var kv in bucket)
        {
            var (bx, by, bz) = kv.Key;
            visitor(new BlockOverride(bx, by, bz, kv.Value));
        }
    }

    /// <summary>
    /// SubChunkCount&gt;0 with a tiny payload is almost always corrupt legacy / empty biomes-only
    /// leftovers — regenerate base so clients never get all-air columns that stick in LevelDB.
    /// </summary>
    private static bool LooksLikeTerrainPayload(ChunkColumnData column)
    {
        // Subchunk network version 8 header (flat and noise columns).
        return column.ExtraPayload.Length >= 3 && column.ExtraPayload[0] == 8;
    }

    private static int ToChunk(int block) => block >> 4;
}
