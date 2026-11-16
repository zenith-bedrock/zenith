using Zenith.Gameplay.Policy;
using Zenith.Gameplay.Runtime;
using Zenith.Gameplay.WorldInteraction.Fluids;
using Zenith.Logging;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.WorldInteraction;

/// <summary>
/// Sand/gravel falls as a real, visible falling_block entity (ADR §95) — replaces §57's instant
/// column-teleport (block vanished from the top of the column and reappeared at the bottom in the
/// same tick, with no wire actor at all). Runs after <see cref="BlockEditSystem"/>.
/// <para/>
/// A block that starts to fall exists as exactly one thing at every tick boundary: the block at its origin, one falling entry (with
/// a record saved in its column), or the block at its resting place. It leaves that set only by landing, or through the void
/// (plan 002, phase 4a). Landing never overwrites a cell that does not accept the block; the block rests above it instead.
/// <para/>
/// Gravity is an environment change (cause <see cref="EnvironmentCause.Gravity"/>, plan 002 phase 4b). A cell the policies refuse
/// behaves like ground: a block never starts to fall into it, never passes through it and never lands in it, it rests above. Vetoing
/// the origin leaves the block where it is (nothing replays when a protection is lifted). No veto ever removes a block: when no
/// cell may take it, the entry stays alive and is retried.
/// </summary>
sealed class GravitySystem : IGameSystem
{
    /// <summary>Per-tick acceleration, matches vanilla-parity sand/gravel (PowerNukkitX <c>EntityFallingBlock</c>).</summary>
    private const float Gravity = 0.04f;

    /// <summary>Per-tick velocity retention factor (drag = 0.02).</summary>
    private const float DragFactor = 0.98f;

    private readonly World.World _world;
    private readonly PlayerManager _players;
    private readonly ILogger? _logger;
    private readonly EnvironmentChangePolicies _environment;
    private ulong _now;
    private readonly List<WorldColumn> _columnsToRetryAdopting = new();
    private readonly List<(int X, int Y, int Z, int BlockRuntimeId)> _updatesScratch = new();
    private readonly List<FallingBlockEntry> _landedScratch = new();
    private readonly List<FallingBlockEntry> _voidedScratch = new();

    /// <summary>Ticks between attempts to place a block that no cell accepts yet. Paces the retry and the log only; it never ends the entry.</summary>
    internal const int StuckRetryTicks = 20;

    public GravitySystem(World.World world, PlayerManager players, EnvironmentChangePolicies environment, ILogger? logger = null)
    {
        _world = world;
        _players = players;
        _environment = environment;
        _logger = logger ?? world.Logger;
    }

    /// <summary>
    /// What a falling block may occupy and land in: air, fire, a liquid, ground-cover plants and torches (what a liquid washes
    /// away too). Everything else (every block that blocks movement, and open doors, trapdoors and gates) stops the fall and is
    /// never overwritten.
    /// </summary>
    internal static bool Accepts(int blockRuntimeId) => Blocks.IsFluid(blockRuntimeId) || FluidFlow.CanHold(blockRuntimeId);

    /// <summary>Can a falling block occupy this cell: it accepts one, and no environment policy refuses gravity there.</summary>
    private bool CanPass(int x, int y, int z, int fallingBlock)
    {
        var current = _world.GetBlock(x, y, z);
        return Accepts(current) && _environment.Allows(EnvironmentCause.Gravity, _now, x, y, z, current, fallingBlock);
    }

    public void Tick(GameClock clock, IReadOnlyList<global::Zenith.Player.Player> online)
    {
        _now = clock.CurrentTick;

        var updates = _updatesScratch;
        updates.Clear();

        AdoptSavedFalls(online);
        BeginNewFalls(online, updates);
        AdvanceActiveFalls(online, updates);

        if (updates.Count == 0 || online.Count == 0) return;
        foreach (var peer in online)
        {
            if (!peer.IsInGame) continue;
            peer.Client.World.PublishUpdateBlocks(updates);
        }
    }

    /// <summary>
    /// Drain remaining pending/active falls into settled overlays before persistence flush
    /// (graceful stop) — snaps every in-flight entity straight to its current-support landing
    /// cell, no animation.
    /// </summary>
    public void SettleAllPending()
    {
        var updates = _updatesScratch;
        updates.Clear();
        AdoptSavedFalls(Array.Empty<global::Zenith.Player.Player>());

        // Entries already in flight land first: that frees store room before any new fall starts.
        foreach (var entry in _world.FallingBlocks.Active.ToArray())
            LandAtCurrentSupport(entry, updates, deferCascade: false);

        var pending = _world.GravityPending;
        var guard = GravityPendingStore.SoftCap * 2;
        while (guard-- > 0)
        {
            pending.PromoteAll();
            if (!pending.TryDequeue(out var x, out var y, out var z))
                break;
            BeginFall(x, y, z, updates, deferCascade: false, out var entry);
            if (entry is not null)
                LandAtCurrentSupport(entry, updates, deferCascade: false);
        }
        // No peer fan-out on shutdown — clients disconnect; overlays persist.
    }

    private void BeginNewFalls(
        IReadOnlyList<global::Zenith.Player.Player> online,
        List<(int X, int Y, int Z, int BlockRuntimeId)> updates)
    {
        var pending = _world.GravityPending;
        if (pending.Count == 0) return;

        pending.StampNoticed(_now, GravityPendingStore.StartDelayTicks);
        pending.PromoteDue(_now);

        var steps = 0;
        while (steps < GravityPendingStore.MaxStepsPerTick &&
               pending.TryDequeue(out var x, out var y, out var z))
        {
            steps++;
            BeginFall(x, y, z, updates, deferCascade: true, out var entry);
            if (entry is not null)
                SpawnFanout(online, entry);
        }
    }

    /// <summary>Vacates the source cell (if still unsupported gravity) and spawns a falling entry.</summary>
    private void BeginFall(
        int x,
        int y,
        int z,
        List<(int X, int Y, int Z, int BlockRuntimeId)> updates,
        bool deferCascade,
        out FallingBlockEntry? spawned)
    {
        spawned = null;
        var rid = _world.GetBlock(x, y, z);
        if (!Blocks.IsGravity(rid))
            return;
        if (!IsUnsupported(x, y, z, rid))
            return;

        // Vetoed origin: the block stays where it is, quietly. Nothing is queued, so nothing replays when the protection is lifted.
        if (!_environment.Allows(EnvironmentCause.Gravity, _now, x, y, z, rid, World.World.AirRuntimeId))
            return;

        // No room to track another fall (capacity is transient): the block stays and is looked at again later, instead of being
        // cleared and force-landed.
        if (!_world.FallingBlocks.HasRoom)
        {
            EnqueueFallCell(x, y, z, defer: true);
            return;
        }

        // The record is written into the column before the cell is cleared: no image of the column can show the empty cell without it.
        var record = _world.RecordFall(x, y, z, rid);
        if (!_world.TrySetBlock(x, y, z, World.World.AirRuntimeId))
        {
            _world.ClearFall(x, z, record);
            return;
        }

        record.IsAdopted = true;
        updates.Add((x, y, z, World.World.AirRuntimeId));
        EnqueueAbove(x, y, z, deferCascade);

        var entityId = _players.AllocateRuntimeId();
        var entry = new FallingBlockEntry
        {
            Record = record,
            EntityId = entityId,
            RuntimeId = (ulong)entityId,
            BlockRuntimeId = rid,
            LandX = x,
            LandY = y,
            LandZ = z,
            Y = y,
            VelocityY = 0f
        };

        if (!_world.FallingBlocks.TrySpawn(entry))
        {
            // Room was checked above; if it vanished anyway, resolve at the current support rather than lose the block.
            LandAtCurrentSupport(entry, updates, deferCascade);
            return;
        }

        spawned = entry;
    }

    private void AdvanceActiveFalls(
        IReadOnlyList<global::Zenith.Player.Player> online,
        List<(int X, int Y, int Z, int BlockRuntimeId)> updates)
    {
        var active = _world.FallingBlocks.Active;
        if (active.Count == 0) return;

        _landedScratch.Clear();
        _voidedScratch.Clear();
        foreach (var entry in active)
        {
            if (entry.IsStuck)
            {
                if (--entry.RetryIn <= 0)
                {
                    entry.RetryIn = StuckRetryTicks;
                    _landedScratch.Add(entry);
                }

                continue;
            }

            var cellY = (int)MathF.Floor(entry.Y);

            // Someone else already claimed our current cell while we were airborne: rest above it
            // instead of overwriting it; Land searches upward for the first cell that accepts the block.
            // Phase XXIX: water no longer counts, a block falling into a lake keeps sinking.
            if (cellY >= Blocks.FlatMinY && !CanPass(entry.LandX, cellY, entry.LandZ, entry.BlockRuntimeId))
            {
                entry.LandY = cellY + 1;
                _landedScratch.Add(entry);
                continue;
            }

            var belowY = cellY - 1;
            if (belowY < Blocks.FlatMinY)
            {
                // Nothing left below the world's floor to land on — falls into the void.
                _voidedScratch.Add(entry);
                continue;
            }

            if (!CanPass(entry.LandX, belowY, entry.LandZ, entry.BlockRuntimeId))
            {
                entry.LandY = cellY;
                _landedScratch.Add(entry);
                continue;
            }

            entry.VelocityY += Gravity;
            entry.VelocityY *= DragFactor;
            entry.Y -= entry.VelocityY;

            foreach (var peer in online)
            {
                if (!peer.IsInGame) continue;
                peer.Client.Entity.SendMoveActorAbsoluteRaw(
                    entry.RuntimeId, entry.LandX + 0.5f, entry.Y, entry.LandZ + 0.5f);
            }
        }

        foreach (var entry in _voidedScratch)
        {
            Resolve(entry);
            foreach (var peer in online)
            {
                if (!peer.IsInGame) continue;
                peer.Client.Entity.SendRemoveActor(entry.EntityId);
            }
        }

        foreach (var entry in _landedScratch)
            Land(entry, updates, deferCascade: true, online);
    }

    /// <summary>Shutdown-time force landing — recomputes the live support column once, no animation.</summary>
    private void LandAtCurrentSupport(
        FallingBlockEntry entry,
        List<(int X, int Y, int Z, int BlockRuntimeId)> updates,
        bool deferCascade)
    {
        var landY = entry.LandY;
        while (landY - 1 >= Blocks.FlatMinY && CanPass(entry.LandX, landY - 1, entry.LandZ, entry.BlockRuntimeId))
            landY--;

        if (landY - 1 < Blocks.FlatMinY)
        {
            // No support all the way down: void, destroy without placing (matches the per-tick path).
            Resolve(entry);
            return;
        }

        entry.LandY = landY;
        Land(entry, updates, deferCascade, online: null);
    }

    /// <summary>
    /// Puts the block down at the first cell from its landing cell upward that accepts it, then ends the fall in the same step.
    /// Block and record change in one column, so no saved image holds the landed block and its record together, or neither.
    /// When no cell accepts it the entry stays alive and is retried; it is never discarded.
    /// </summary>
    private void Land(
        FallingBlockEntry entry,
        List<(int X, int Y, int Z, int BlockRuntimeId)> updates,
        bool deferCascade,
        IReadOnlyList<global::Zenith.Player.Player>? online)
    {
        var restY = FindRestingCell(entry.LandX, entry.LandY, entry.LandZ, entry.BlockRuntimeId);
        if (restY is null || !_world.TrySetBlock(entry.LandX, restY.Value, entry.LandZ, entry.BlockRuntimeId))
        {
            MarkStuck(entry);
            return;
        }

        entry.LandY = restY.Value;
        Resolve(entry);

        if (online is not null)
        {
            foreach (var peer in online)
            {
                if (!peer.IsInGame) continue;
                peer.Client.Entity.SendRemoveActor(entry.EntityId);
            }
        }

        updates.Add((entry.LandX, entry.LandY, entry.LandZ, entry.BlockRuntimeId));
        if (IsUnsupported(entry.LandX, entry.LandY, entry.LandZ, entry.BlockRuntimeId))
            EnqueueFallCell(entry.LandX, entry.LandY, entry.LandZ, deferCascade);
    }

    /// <summary>The lowest cell at or above <paramref name="fromY"/> that accepts a falling block, or null when none up to the world's top does.</summary>
    private int? FindRestingCell(int x, int fromY, int z, int fallingBlock)
    {
        for (var y = Math.Max(fromY, Blocks.FlatMinY); y <= WorldColumn.MaxY; y++)
            if (CanPass(x, y, z, fallingBlock))
                return y;
        return null;
    }

    private void MarkStuck(FallingBlockEntry entry)
    {
        entry.RetryIn = StuckRetryTicks;
        if (entry.IsStuck) return;
        entry.IsStuck = true;
        _logger?.Error(
            $"Falling block {entry.BlockRuntimeId} at {entry.LandX},{entry.LandZ} has no cell that accepts it; " +
            $"it stays in flight and is retried every {StuckRetryTicks} ticks.");
    }

    /// <summary>Ends a fall: the entry leaves the store and its record leaves the column, together. The only callers are a landing and the void.</summary>
    private void Resolve(FallingBlockEntry entry)
    {
        _world.FallingBlocks.Remove(entry);
        if (entry.Record is { } record)
            _world.ClearFall(entry.LandX, entry.LandZ, record);
    }

    /// <summary>
    /// Re-creates the falling entries of columns that were loaded carrying falls that were in flight when they were saved, then lets
    /// them fall again from their origin. Each record is adopted at most once; the blocks above an origin are queued again, since the
    /// queue that held them was memory only.
    /// </summary>
    private void AdoptSavedFalls(IReadOnlyList<global::Zenith.Player.Player> online)
    {
        while (_world.TryTakeColumnWithSavedFalls(out var column))
            _columnsToRetryAdopting.Add(column);
        if (_columnsToRetryAdopting.Count == 0) return;

        var columns = _columnsToRetryAdopting.ToArray();
        _columnsToRetryAdopting.Clear();
        foreach (var column in columns)
            AdoptColumn(column, online);
    }

    private void AdoptColumn(WorldColumn column, IReadOnlyList<global::Zenith.Player.Player> online)
    {
        if (!_world.TryGetLoadedColumn(column.Coord.X, column.Coord.Z, out var resident) || !ReferenceEquals(resident, column))
            return;

        foreach (var record in column.PendingFalls.ToArray())
        {
            if (record.IsAdopted) continue;

            var x = column.Coord.X * 16 + record.LocalX;
            var z = column.Coord.Z * 16 + record.LocalZ;
            var entityId = _players.AllocateRuntimeId();
            var entry = new FallingBlockEntry
            {
                Record = record,
                EntityId = entityId,
                RuntimeId = (ulong)entityId,
                BlockRuntimeId = record.BlockRuntimeId,
                LandX = x,
                LandY = record.OriginY,
                LandZ = z,
                Y = record.OriginY,
                VelocityY = 0f
            };

            if (!_world.FallingBlocks.TrySpawn(entry))
            {
                _columnsToRetryAdopting.Add(column);
                return;
            }

            record.IsAdopted = true;
            EnqueueAbove(x, record.OriginY, z, defer: true);
            SpawnFanout(online, entry);
        }
    }

    private static void SpawnFanout(IReadOnlyList<global::Zenith.Player.Player> online, FallingBlockEntry entry)
    {
        foreach (var peer in online)
        {
            if (!peer.IsInGame) continue;
            peer.Client.Entity.SendAddFallingBlock(
                entry.EntityId, entry.RuntimeId, entry.BlockRuntimeId,
                entry.LandX + 0.5f, entry.Y, entry.LandZ + 0.5f);
        }
    }

    /// <summary>True when the cell below would let the block fall: it accepts one and no policy refuses. A refused cell is ground.</summary>
    private bool IsUnsupported(int x, int y, int z, int fallingBlock)
    {
        var belowY = y - 1;
        if (belowY < Blocks.FlatMinY)
            return true;
        return CanPass(x, belowY, z, fallingBlock);
    }

    private void EnqueueAbove(int x, int y, int z, bool defer)
    {
        var aboveY = y + 1;
        if (aboveY > 320) return;
        if (Blocks.IsGravity(_world.GetBlock(x, aboveY, z)))
            EnqueueFallCell(x, aboveY, z, defer);
    }

    private void EnqueueFallCell(int x, int y, int z, bool defer)
    {
        // Deferred (the normal path): the cell starts to fall after the vanilla delay. Not deferred (shutdown): at once.
        _world.GravityPending.TryEnqueue(x, y, z, defer ? _now + GravityPendingStore.StartDelayTicks : 0);
    }
}
