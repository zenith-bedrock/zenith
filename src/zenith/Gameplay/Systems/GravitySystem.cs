using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.Systems;

/// <summary>
/// Cell-tick sand/gravel falls (ADR §57). Runs after <see cref="BlockSystem"/>.
/// Discrete UpdateBlock moves — no falling_block actor wire.
/// </summary>
sealed class GravitySystem : IGameSystem
{
    private readonly World.World _world;
    private readonly List<(int X, int Y, int Z, int BlockRuntimeId)> _updatesScratch = new();

    public GravitySystem(World.World world) => _world = world;

    public void Tick(GameClock clock, IReadOnlyList<global::Zenith.Player.Player> online)
    {
        _ = clock;
        var pending = _world.GravityPending;
        if (pending.Count == 0) return;

        pending.PromoteDeferred();

        var updates = _updatesScratch;
        updates.Clear();
        var steps = 0;
        while (steps < GravityPendingStore.MaxStepsPerTick &&
               pending.TryDequeue(out var x, out var y, out var z))
        {
            steps++;
            TryFallCell(x, y, z, updates, pending, deferCascade: true);
        }

        if (updates.Count == 0 || online.Count == 0) return;

        foreach (var peer in online)
        {
            if (!peer.IsInGame) continue;
            peer.Session.Protocol.World.PublishUpdateBlocks(updates);
        }
    }

    /// <summary>
    /// Drain remaining pending falls into settled overlays before persistence flush (graceful stop).
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
            TryFallCell(x, y, z, updates, pending, deferCascade: false);
        }
        // No peer fan-out on shutdown — clients disconnect; overlays persist.
    }

    private void TryFallCell(
        int x,
        int y,
        int z,
        List<(int X, int Y, int Z, int BlockRuntimeId)> updates,
        GravityPendingStore pending,
        bool deferCascade)
    {
        var rid = _world.GetBlock(x, y, z);
        if (!Blocks.IsGravity(rid))
            return;
        if (!IsUnsupported(x, y, z))
            return;

        var landY = y - 1;
        while (landY >= Blocks.FlatMinY && _world.GetBlock(x, landY, z) == World.World.AirRuntimeId)
            landY--;

        // Void: only air down to FlatMinY — destroy (no floor drop).
        if (landY < Blocks.FlatMinY)
        {
            if (!_world.TrySetBlock(x, y, z, World.World.AirRuntimeId))
                return;
            updates.Add((x, y, z, World.World.AirRuntimeId));
            EnqueueAbove(x, y, z, pending, deferCascade);
            return;
        }

        var destY = landY + 1;
        if (destY >= y)
            return;

        if (!_world.CanAcceptBlockWrite(x, destY, z, rid) ||
            !_world.CanAcceptBlockWrite(x, y, z, World.World.AirRuntimeId))
            return;

        if (!_world.TrySetBlock(x, y, z, World.World.AirRuntimeId))
            return;
        if (!_world.TrySetBlock(x, destY, z, rid))
        {
            // Best-effort rollback source so we do not delete the block.
            _ = _world.TrySetBlock(x, y, z, rid);
            return;
        }

        updates.Add((x, y, z, World.World.AirRuntimeId));
        updates.Add((x, destY, z, rid));
        EnqueueAbove(x, y, z, pending, deferCascade);
        if (IsUnsupported(x, destY, z))
            EnqueueFallCell(x, destY, z, pending, deferCascade);
    }

    private bool IsUnsupported(int x, int y, int z)
    {
        var belowY = y - 1;
        if (belowY < Blocks.FlatMinY)
            return true;
        return _world.GetBlock(x, belowY, z) == World.World.AirRuntimeId;
    }

    private void EnqueueAbove(int x, int y, int z, GravityPendingStore pending, bool defer)
    {
        var aboveY = y + 1;
        if (aboveY > 320) return;
        if (Blocks.IsGravity(_world.GetBlock(x, aboveY, z)))
            EnqueueFallCell(x, aboveY, z, pending, defer);
    }

    private static void EnqueueFallCell(int x, int y, int z, GravityPendingStore pending, bool defer)
    {
        if (defer)
            pending.TryEnqueueDeferred(x, y, z);
        else
            pending.TryEnqueue(x, y, z);
    }
}
