using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.Systems;

/// <summary>
/// Sand/gravel falls as a real, visible falling_block entity (ADR §95) — replaces §57's instant
/// column-teleport (block vanished from the top of the column and reappeared at the bottom in the
/// same tick, with no wire actor at all). Runs after <see cref="BlockEditSystem"/>.
/// </summary>
sealed class GravitySystem : IGameSystem
{
    /// <summary>Per-tick acceleration, matches vanilla-parity sand/gravel (PowerNukkitX <c>EntityFallingBlock</c>).</summary>
    private const float Gravity = 0.04f;

    /// <summary>Per-tick velocity retention factor (drag = 0.02).</summary>
    private const float DragFactor = 0.98f;

    private readonly World.World _world;
    private readonly PlayerManager _players;
    private readonly List<(int X, int Y, int Z, int BlockRuntimeId)> _updatesScratch = new();
    private readonly List<FallingBlockEntry> _landedScratch = new();
    private readonly List<FallingBlockEntry> _voidedScratch = new();

    public GravitySystem(World.World world, PlayerManager players)
    {
        _world = world;
        _players = players;
    }

    public void Tick(GameClock clock, IReadOnlyList<global::Zenith.Player.Player> online)
    {
        _ = clock;

        var updates = _updatesScratch;
        updates.Clear();

        BeginNewFalls(online, updates);
        AdvanceActiveFalls(online, updates);

        if (updates.Count == 0 || online.Count == 0) return;
        foreach (var peer in online)
        {
            if (!peer.IsInGame) continue;
            peer.Session.Protocol.World.PublishUpdateBlocks(updates);
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
        var pending = _world.GravityPending;
        var guard = GravityPendingStore.SoftCap * 2;
        while (guard-- > 0)
        {
            pending.PromoteDeferred();
            if (!pending.TryDequeue(out var x, out var y, out var z))
                break;
            BeginFall(x, y, z, updates, deferCascade: false, out var entry);
            if (entry is not null)
                LandAtCurrentSupport(entry, updates, deferCascade: false);
        }

        foreach (var entry in _world.FallingBlocks.Active.ToArray())
            LandAtCurrentSupport(entry, updates, deferCascade: false);
        // No peer fan-out on shutdown — clients disconnect; overlays persist.
    }

    private void BeginNewFalls(
        IReadOnlyList<global::Zenith.Player.Player> online,
        List<(int X, int Y, int Z, int BlockRuntimeId)> updates)
    {
        var pending = _world.GravityPending;
        if (pending.Count == 0) return;

        pending.PromoteDeferred();

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
        if (!IsUnsupported(x, y, z))
            return;
        if (!_world.CanAcceptBlockWrite(x, y, z, World.World.AirRuntimeId))
            return;
        if (!_world.TrySetBlock(x, y, z, World.World.AirRuntimeId))
            return;

        updates.Add((x, y, z, World.World.AirRuntimeId));
        EnqueueAbove(x, y, z, deferCascade);

        var entityId = _players.AllocateRuntimeId();
        var entry = new FallingBlockEntry
        {
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
            // SoftCap refuse — resolve at the current support instantly rather than lose the block.
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
            var cellY = (int)MathF.Floor(entry.Y);

            // Someone else already claimed our current cell while we were airborne — rest one
            // cell above it instead of overwriting (matches vanilla: a falling block that finds
            // its target occupied settles on top rather than replacing what landed first).
            if (cellY >= Blocks.FlatMinY && _world.GetBlock(entry.LandX, cellY, entry.LandZ) != World.World.AirRuntimeId)
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

            if (_world.GetBlock(entry.LandX, belowY, entry.LandZ) != World.World.AirRuntimeId)
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
                peer.Session.Protocol.Entity.SendMoveActorAbsoluteRaw(
                    entry.RuntimeId, entry.LandX + 0.5f, entry.Y, entry.LandZ + 0.5f);
            }
        }

        foreach (var entry in _voidedScratch)
        {
            _world.FallingBlocks.Remove(entry);
            foreach (var peer in online)
            {
                if (!peer.IsInGame) continue;
                peer.Session.Protocol.Entity.SendRemoveActor(entry.EntityId);
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
        while (landY - 1 >= Blocks.FlatMinY && _world.GetBlock(entry.LandX, landY - 1, entry.LandZ) == World.World.AirRuntimeId)
            landY--;

        if (landY - 1 < Blocks.FlatMinY)
        {
            // No support all the way down — void, destroy without placing (matches the per-tick path).
            _world.FallingBlocks.Remove(entry);
            return;
        }

        entry.LandY = landY;
        Land(entry, updates, deferCascade, online: null);
    }

    private void Land(
        FallingBlockEntry entry,
        List<(int X, int Y, int Z, int BlockRuntimeId)> updates,
        bool deferCascade,
        IReadOnlyList<global::Zenith.Player.Player>? online)
    {
        _world.FallingBlocks.Remove(entry);

        if (online is not null)
        {
            foreach (var peer in online)
            {
                if (!peer.IsInGame) continue;
                peer.Session.Protocol.Entity.SendRemoveActor(entry.EntityId);
            }
        }

        if (!_world.CanAcceptBlockWrite(entry.LandX, entry.LandY, entry.LandZ, entry.BlockRuntimeId) ||
            !_world.TrySetBlock(entry.LandX, entry.LandY, entry.LandZ, entry.BlockRuntimeId))
            return; // Best-effort: something else claimed the cell mid-fall — block is lost (rare, §95 non-goal).

        updates.Add((entry.LandX, entry.LandY, entry.LandZ, entry.BlockRuntimeId));
        if (IsUnsupported(entry.LandX, entry.LandY, entry.LandZ))
            EnqueueFallCell(entry.LandX, entry.LandY, entry.LandZ, deferCascade);
    }

    private static void SpawnFanout(IReadOnlyList<global::Zenith.Player.Player> online, FallingBlockEntry entry)
    {
        foreach (var peer in online)
        {
            if (!peer.IsInGame) continue;
            peer.Session.Protocol.Entity.SendAddFallingBlock(
                entry.EntityId, entry.RuntimeId, entry.BlockRuntimeId,
                entry.LandX + 0.5f, entry.Y, entry.LandZ + 0.5f);
        }
    }

    private bool IsUnsupported(int x, int y, int z)
    {
        var belowY = y - 1;
        if (belowY < Blocks.FlatMinY)
            return true;
        return _world.GetBlock(x, belowY, z) == World.World.AirRuntimeId;
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
        if (defer)
            _world.GravityPending.TryEnqueueDeferred(x, y, z);
        else
            _world.GravityPending.TryEnqueue(x, y, z);
    }
}
