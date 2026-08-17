using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Zenith.Raknet.Log;

namespace Zenith.World;

/// <summary>
/// Mundo: colunas base via Dimension overworld (<see cref="ITerrainProvider"/>) + <see cref="IChunkStorage"/> + overlay esparso.
/// Overlay SoftCap (§36): refuse new keys at <see cref="OverrideSoftCap"/>; compact when rid == base;
/// overwrite / hydrate always allowed. Mutação nunca reescreve subchunk; só overlay + UpdateBlock.
/// Column index (§36 adendo): <see cref="GetOverlaysInColumn"/> O(bucket), not O(all overlays).
/// Terrain / storage / Dimension seams: ADR §62 / §71.
/// </summary>
sealed class World
{
    /// <summary>SoftCap for new overlay keys (same order as historical warn threshold).</summary>
    internal const int OverrideSoftCap = 10_000;

    private readonly IChunkStorage _storage;
    private readonly Dimension _overworld;
    private readonly ConcurrentDictionary<(int X, int Y, int Z), int> _blockOverrides = new();
    private readonly WorldGenerationBroker _generationBroker;
    private readonly int _generationWorkers;
    private readonly int _generationCacheColumns;
    private readonly ConcurrentDictionary<ChunkCoord, ChunkColumnData> _baseColumnCache = new();
    private readonly ConcurrentQueue<ChunkCoord> _baseColumnCacheOrder = new();
    private int _baseColumnCacheCount;
    /// <summary>
    /// Secondary index by chunk for column stream. SSOT for GetBlock remains <see cref="_blockOverrides"/>;
    /// both updated only via <see cref="StoreOverlay"/> / <see cref="RemoveOverlay"/> (no drift).
    /// </summary>
    private readonly ConcurrentDictionary<(int Cx, int Cz), ConcurrentDictionary<(int X, int Y, int Z), int>> _overlaysByChunk = new();
    /// <summary>
    /// Chunks whose overlays/chests have been loaded from <see cref="_storage"/> into RAM (ADR §114).
    /// Guarded against double-hydration races by <see cref="_generationBroker"/>'s existing
    /// single-flight coalescing for the same coordinate — no separate lock needed here.
    /// </summary>
    private readonly ConcurrentDictionary<(int Cx, int Cz), byte> _hydratedChunks = new();
    private readonly ILogger? _logger;
    private readonly WorldGenerationDiagnostics? _generationDiagnostics;
    private int _softCapWarned;

    public FloorDropStore FloorDrops { get; }
    public ChestStore Chests { get; }
    public GravityPendingStore GravityPending { get; }
    public FallingBlockStore FallingBlocks { get; }

    /// <summary>Per-chunk viewer refcount driving eviction (ADR §114) — see <see cref="ChunkResidencyIndex"/>.</summary>
    public ChunkResidencyIndex ChunkResidency { get; } = new();

    /// <summary>Default playable dimension (only one until Nether/End ADR).</summary>
    public Dimension Overworld => _overworld;

    public int OverrideCount => _blockOverrides.Count;

    /// <summary>Feet Y for join/respawn above base terrain at (x,z) — ignores overlays.</summary>
    public int SampleSpawnFeetY(int x, int z) => _overworld.Terrain.SampleSpawnFeetY(x, z);

    /// <summary>Spawn biome wire pair from overworld terrain (flat → plains).</summary>
    public SpawnBiome SampleSpawnBiome(int x, int z) => _overworld.Terrain.SampleSpawnBiome(x, z);

    public World(
        IChunkStorage storage,
        ILogger? logger = null,
        ITerrainProvider? terrain = null,
        WorldGenerationDiagnostics? generationDiagnostics = null,
        int generationWorkers = 1,
        int generationCacheColumns = 1024)
    {
        if (generationWorkers is < 1 or > 64)
            throw new ArgumentOutOfRangeException(nameof(generationWorkers), generationWorkers, "Generation workers must be 1..64.");
        if (generationCacheColumns is < 0 or > 65_536)
            throw new ArgumentOutOfRangeException(nameof(generationCacheColumns), generationCacheColumns, "Generation cache must be 0..65536 columns.");

        _storage = storage;
        _overworld = Dimension.CreateOverworld(terrain ?? FlatTerrainProvider.Instance);
        _logger = logger;
        _generationDiagnostics = generationDiagnostics;
        _generationWorkers = generationWorkers;
        _generationCacheColumns = generationCacheColumns;
        _generationBroker = new WorldGenerationBroker(generationWorkers, GenerateColumnCoreAsync, generationDiagnostics);
        FloorDrops = new FloorDropStore(logger);
        Chests = new ChestStore(logger);
        GravityPending = new GravityPendingStore(logger);
        FallingBlocks = new FallingBlockStore(logger);
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

    public void PersistArmor(Player.Player player)
    {
        if (!player.IdentityStable) return;
        _ = _storage.PutArmorAsync(player.Uuid, player.Inventory.PackArmorBlob());
    }

    public bool TryLoadArmor(Guid uuid, Player.PlayerInventory inventory)
    {
        var blob = _storage.GetArmorAsync(uuid).AsTask().GetAwaiter().GetResult();
        return blob is not null && inventory.TryLoadArmorFromBlob(blob);
    }

    /// <summary>Quit / gamemode Persist — skips unstable identity (ADR §60).</summary>
    public void PersistPlayerData(Player.Player player)
    {
        if (!player.IdentityStable) return;
        // A persisted health of 0 would fail TryUnpack's own corruption guard on the next load
        // (health must be positive — a hydrated player must never load already-dead). Disconnecting
        // mid-death-screen (before CompleteRespawn ran) is the one real path that could otherwise
        // hit this: persist as if already respawned rather than losing the whole blob (pose/XP too)
        // to a validation rejection.
        var health = player.IsDead ? player.MaxHealth : player.Health;
        var blob = PlayerDataBlob.Pack(
            player.PositionX,
            player.PositionY,
            player.PositionZ,
            player.Yaw,
            player.Pitch,
            player.GameMode,
            player.ExperienceLevel,
            player.ExperiencePoints,
            health,
            player.Hunger,
            player.Saturation,
            player.Exhaustion);
        _ = _storage.PutPlayerDataAsync(player.Uuid, blob);
    }

    /// <summary>
    /// Login hydrate. False = miss / corrupt / OOB → caller keeps terrain spawn + config GameMode.
    /// </summary>
    public bool TryLoadPlayerData(
        Guid uuid, out float x, out float y, out float z, out float yaw, out float pitch, out Player.GameMode mode,
        out int experienceLevel, out int experiencePoints,
        out float health, out float hunger, out float saturation, out float exhaustion)
    {
        x = y = z = yaw = pitch = 0;
        mode = Player.GameMode.Survival;
        experienceLevel = 0;
        experiencePoints = 0;
        health = 20f;
        hunger = 20f;
        saturation = 5f;
        exhaustion = 0f;
        var blob = _storage.GetPlayerDataAsync(uuid).AsTask().GetAwaiter().GetResult();
        return blob is not null && PlayerDataBlob.TryUnpack(
            blob, out x, out y, out z, out yaw, out pitch, out mode, out experienceLevel, out experiencePoints,
            out health, out hunger, out saturation, out exhaustion);
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
        ct.ThrowIfCancellationRequested();
        var generationScope = _generationDiagnostics is null
            ? default(WorldGenerationDiagnostics.ColumnScope)
            : _generationDiagnostics.BeginColumn();
        using (generationScope)
        {
            var request = await _generationBroker.RequestAsync(new ChunkCoord(chunkX, chunkZ)).ConfigureAwait(false);
            if (request.Coalesced) _generationDiagnostics?.RecordCoalesced();
            return await request.Task.WaitAsync(ct).ConfigureAwait(false);
        }
    }

    private async ValueTask<ColumnReadResult> GenerateColumnCoreAsync(ChunkCoord coord)
    {
        try
        {
            return await GetOrCreateColumnCoreAsync(coord.X, coord.Z, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch
        {
            _generationDiagnostics?.RecordFailed();
            throw;
        }
    }

    /// <summary>
    /// First-touch hydrate (ADR §114): loads this chunk's overlays/chests from <see cref="_storage"/>
    /// into RAM the first time it's requested, replacing the old "load everything at boot" behavior.
    /// Runs inside <see cref="_generationBroker"/>'s per-coordinate single-flight, so concurrent
    /// first touches of the same chunk can't double-hydrate.
    /// </summary>
    private async ValueTask EnsureHydratedAsync(int chunkX, int chunkZ, CancellationToken ct)
    {
        var key = (chunkX, chunkZ);
        if (_hydratedChunks.ContainsKey(key))
            return;

        var overlays = await _storage.LoadOverlaysForChunkAsync(chunkX, chunkZ, ct).ConfigureAwait(false);
        foreach (var o in overlays)
            StoreOverlay(o.X, o.Y, o.Z, o.BlockRuntimeId);

        var chests = await _storage.LoadChestsForChunkAsync(chunkX, chunkZ, ct).ConfigureAwait(false);
        foreach (var (x, y, z, blob) in chests)
            Chests.TryLoadFromBlob(x, y, z, blob);

        _hydratedChunks[key] = 0;
    }

    private async ValueTask<ColumnReadResult> GetOrCreateColumnCoreAsync(
        int chunkX, int chunkZ, CancellationToken ct)
    {
        await EnsureHydratedAsync(chunkX, chunkZ, ct).ConfigureAwait(false);

        var coord = new ChunkCoord(chunkX, chunkZ);
        if (_baseColumnCache.TryGetValue(coord, out var cached))
        {
            _generationDiagnostics?.RecordCacheHit();
            return new ColumnReadResult(cached, GetOverlaysInColumn(chunkX, chunkZ));
        }

        var existing = await _storage.GetAsync(coord, ct).ConfigureAwait(false);

        ChunkColumnData bas;
        if (existing is not null && existing.SubChunkCount > 0 && LooksLikeTerrainPayload(existing))
        {
            bas = existing;
            _generationDiagnostics?.RecordLoaded();
            CacheBaseColumn(bas);
        }
        else if (existing is null)
        {
            // ADR §45: miss → in-memory base only (do not materialize identical c:x:z blobs).
            _generationDiagnostics?.RecordGenerated();
            var terrain = _overworld.Terrain.GetBaseColumn(chunkX, chunkZ);
            bas = new ChunkColumnData(coord, dimensionId: _overworld.WireId, terrain.SubChunkCount, terrain.Payload);
            CacheBaseColumn(bas);
        }
        else
        {
            // Legacy empty/corrupt c: — regenerate base and Put so disk self-heals.
            _generationDiagnostics?.RecordGenerated();
            var terrain = _overworld.Terrain.GetBaseColumn(chunkX, chunkZ);
            bas = new ChunkColumnData(coord, dimensionId: _overworld.WireId, terrain.SubChunkCount, terrain.Payload);
            await _storage.PutAsync(bas, ct).ConfigureAwait(false);
            bas = (await _storage.GetAsync(coord, ct).ConfigureAwait(false)) ?? bas;
            CacheBaseColumn(bas);
        }

        var overlays = GetOverlaysInColumn(chunkX, chunkZ);
        return new ColumnReadResult(bas, overlays);
    }

    public async ValueTask<IReadOnlyList<ColumnReadResult>> GetRadiusAsync(int centerX, int centerZ, int radius, CancellationToken ct = default)
    {
        var results = new List<ColumnReadResult>();
        await foreach (var column in StreamRadiusAsync(centerX, centerZ, radius, ct)
                           .ConfigureAwait(false))
            results.Add(column);
        return results;
    }

    /// <summary>
    /// Reads a square radius in deterministic chunk order without materializing the complete
    /// radius. At most a small worker-sized batch is retained by this iterator; the caller's
    /// consumption rate therefore applies backpressure to generation and serialization stages.
    /// </summary>
    public async IAsyncEnumerable<ColumnReadResult> StreamRadiusAsync(
        int centerX,
        int centerZ,
        int radius,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (radius < 0)
            throw new ArgumentOutOfRangeException(nameof(radius), radius, "Radius must be non-negative.");

        var batch = new List<Task<ColumnReadResult>>(Math.Max(_generationWorkers * 2, 1));
        foreach (var (x, z) in EnumerateRadius(centerX, centerZ, radius))
        {
            ct.ThrowIfCancellationRequested();
            batch.Add(GetOrCreateColumnAsync(x, z, ct).AsTask());
            if (batch.Count < batch.Capacity)
                continue;

            var completed = await Task.WhenAll(batch).ConfigureAwait(false);
            foreach (var column in completed)
                yield return column;
            batch.Clear();
        }

        if (batch.Count == 0)
            yield break;

        var tail = await Task.WhenAll(batch).ConfigureAwait(false);
        foreach (var column in tail)
            yield return column;
    }

    /// <summary>Enumerates a square in center-out order without retaining the complete radius.</summary>
    private static IEnumerable<(int X, int Z)> EnumerateRadius(int centerX, int centerZ, int radius)
    {
        yield return (centerX, centerZ);
        for (var ring = 1; ring <= radius; ring++)
        {
            for (var dx = -ring; dx <= ring; dx++)
                yield return (centerX + dx, centerZ - ring);
            for (var dz = -ring + 1; dz <= ring; dz++)
                yield return (centerX + ring, centerZ + dz);
            for (var dx = ring - 1; dx >= -ring; dx--)
                yield return (centerX + dx, centerZ + ring);
            for (var dz = ring - 1; dz >= -ring + 1; dz--)
                yield return (centerX - ring, centerZ + dz);
        }
    }


    public ValueTask StopGenerationAsync() => _generationBroker.DisposeAsync();

    private void CacheBaseColumn(ChunkColumnData column)
    {
        if (_generationCacheColumns == 0 || !_baseColumnCache.TryAdd(column.Coord, column))
            return;

        _baseColumnCacheOrder.Enqueue(column.Coord);
        var count = Interlocked.Increment(ref _baseColumnCacheCount);
        while (count > _generationCacheColumns && _baseColumnCacheOrder.TryDequeue(out var evicted))
        {
            if (_baseColumnCache.TryRemove(evicted, out _))
                count = Interlocked.Decrement(ref _baseColumnCacheCount);
        }
    }

    public static int AirRuntimeId => Blocks.Air;

    /// <summary>
    /// True when <see cref="TrySetBlock"/> would not SoftCap-refuse this write
    /// (compact-to-base and overwrite of existing keys always OK).
    /// </summary>
    public bool CanAcceptBlockWrite(int x, int y, int z, int blockRuntimeId)
    {
        if (blockRuntimeId == _overworld.Terrain.SampleBaseBlock(x, y, z))
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
        var baseRid = _overworld.Terrain.SampleBaseBlock(x, y, z);
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

    /// <summary>Snapshot of chunk coords currently hydrated (ADR §114) — the sweep's candidate set.</summary>
    public List<(int X, int Z)> CopyHydratedChunks()
    {
        var list = new List<(int X, int Z)>(_hydratedChunks.Count);
        foreach (var key in _hydratedChunks.Keys)
            list.Add(key);
        return list;
    }

    /// <summary>
    /// Evicts chunk (chunkX, chunkZ)'s overlays/chests from RAM if nobody currently has it in view
    /// (ADR §114). Returns false without changing anything if the chunk has a viewer.
    /// Overlays are already fire-and-forget-persisted on every write (ADR §41 — GameLoop never waits
    /// on disk), so no explicit flush is needed here. Chests are NOT continuously persisted while
    /// their UI is open (only on close), so any chest is explicitly persisted just before eviction;
    /// a chest with an open UI is left resident regardless (belt-and-suspenders — its viewer implies
    /// someone's nearby even if residency bookkeeping somehow disagrees).
    /// </summary>
    public bool TryEvictChunk(int chunkX, int chunkZ)
    {
        if (ChunkResidency.HasViewers(chunkX, chunkZ))
            return false;

        foreach (var (x, y, z) in Chests.PositionsInChunk(chunkX, chunkZ).ToArray())
        {
            if (Chests.OpenerCount(x, y, z) > 0)
                continue;
            PersistChest(x, y, z);
            Chests.Evict(x, y, z);
        }

        if (_overlaysByChunk.TryRemove((chunkX, chunkZ), out var bucket))
        {
            foreach (var cell in bucket.Keys)
                _blockOverrides.TryRemove(cell, out _);
        }

        _hydratedChunks.TryRemove((chunkX, chunkZ), out _);
        return true;
    }

    /// <summary>
    /// Diagnostic line comparing resident-in-World overlay/chest counts against ZLDB's own always-
    /// resident dataset size (ADR §114/§121's <c>DB.GetProperty</c>). This is NOT evidence total RAM
    /// shrank — ZLDB's own memtable holds its whole dataset resident regardless, by design (ADR
    /// §11/§61) — just a residency ratio operators can watch (how much of the persisted set is
    /// currently duplicated in World's RAM vs. evicted).
    /// </summary>
    public void LogResidencySnapshot()
    {
        if (_logger is null) return;
        var zldbEntries = _storage.TryGetProperty("zldb.num-entries", out var entries) ? entries : "n/a";
        _logger.Info(
            $"Chunk residency: {_blockOverrides.Count} overlays / {Chests.Count} chests resident in RAM " +
            $"across {_hydratedChunks.Count} hydrated chunks (zldb.num-entries={zldbEntries}, mixes every key kind).");
    }

    /// <summary>Overlay se existir; senão amostra do terreno base (<see cref="ITerrainProvider"/>).</summary>
    public int GetBlock(int x, int y, int z)
    {
        if (_blockOverrides.TryGetValue((x, y, z), out var id))
            return id;
        return _overworld.Terrain.SampleBaseBlock(x, y, z);
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
        // Reads ChunkPayloads.SubChunkVersion instead of a local literal — a hardcoded second
        // copy of the subchunk version byte (previously 8, stale after ChunkPayloads moved to 9)
        // is exactly what let every stored column silently fail this check and get needlessly
        // re-generated/re-Put on every load. One source of truth, not two numbers that must be
        // kept in sync by hand.
        return column.ExtraPayload.Length >= 3 && column.ExtraPayload[0] == ChunkPayloads.SubChunkVersion;
    }

    private static int ToChunk(int block) => ChunkMath.BlockToChunk(block);
}
