using Zenith.Player;
using Zenith.Session;
using Zenith.World;

namespace Zenith.Protocol;

/// <summary>
/// Empacota LevelChunk + overlays via Protocol. Partilhado por PreSpawn e ChunkStreamSystem.
/// </summary>
static class ColumnSend
{
    [ThreadStatic]
    private static List<BlockOverride>? t_overlayScratch;

    [ThreadStatic]
    private static List<(int X, int Y, int Z, int BlockRuntimeId)>? t_updatesScratch;

    /// <summary>
    /// <paramref name="column"/>'s <c>Overlays</c> field is deliberately ignored here — it was
    /// captured back when the column was first read, which for a background-streamed join (large
    /// view radius, small spawn-ready radius) can be seconds before this actually sends, sitting in
    /// a bounded channel / worker queue the whole time. A block edited by another player in that
    /// window would silently never reach this joining client for this cell — a real, reproducible
    /// ghost-block desync on any populated server, not a hypothetical. Floor items are published
    /// separately on the owner after this worker acknowledges column transmission (ADR §163).
    /// Overlays are re-read live, right before sending, so this always reflects the true current
    /// RAM state — the same freshness guarantee <see cref="EmitOverlaysToSession"/>'s join-catchup
    /// path already had.
    /// </summary>
    public static void EmitToSession(NetworkSession session, in ColumnReadResult column, byte orderChannel)
    {
        var world = session.Context.World;
        var overlayScratch = t_overlayScratch ??= new List<BlockOverride>(64);
        world.FillOverlaysInColumn(column.Base.Coord.X, column.Base.Coord.Z, overlayScratch);

        ColumnTerrainEmitter.Emit(
            column.Base,
            overlayScratch,
            sendLevelChunk: bas => session.Protocol.World.SendLevelChunk(new ChunkColumn(
                bas.Coord.X,
                bas.Coord.Z,
                bas.DimensionId,
                bas.SubChunkCount,
                bas.ExtraPayload), orderChannel),
            sendUpdateBlock: (x, y, z, runtimeId) =>
                session.Protocol.World.SendUpdateBlock(x, y, z, runtimeId, orderChannel: orderChannel));

        // Item catch-up follows the worker's publication acknowledgement on the tick (ADR §163).
    }

    /// <summary>Re-send current overlays for known columns (join catch-up, §14).</summary>
    public static void EmitOverlaysToSession(
        NetworkSession session,
        World.World world,
        IReadOnlyList<(int X, int Z)> columns)
    {
        if (columns.Count == 0) return;

        var overlayScratch = t_overlayScratch ??= new List<BlockOverride>(64);
        var updates = t_updatesScratch ??= new List<(int X, int Y, int Z, int BlockRuntimeId)>(64);
        updates.Clear();
        for (var i = 0; i < columns.Count; i++)
        {
            var (cx, cz) = columns[i];
            world.FillOverlaysInColumn(cx, cz, overlayScratch);
            for (var j = 0; j < overlayScratch.Count; j++)
            {
                var o = overlayScratch[j];
                updates.Add((o.X, o.Y, o.Z, o.BlockRuntimeId));
            }

            EmitFloorDropsInColumn(session, world, cx, cz);
        }

        if (updates.Count > 0)
            session.Protocol.World.PublishUpdateBlocks(updates);
    }

    /// <summary>AddItemActor for floor drops in one column (late join / stream catch-up, §26 wire).</summary>
    public static void EmitFloorDropsInColumn(NetworkSession session, World.World world, int chunkX, int chunkZ)
    {
        foreach (var drop in world.FloorDrops.SnapshotViews())
        {
            if (PlayerChunkTracker.BlockToChunk(drop.Pose.X) != chunkX ||
                PlayerChunkTracker.BlockToChunk(drop.Pose.Z) != chunkZ)
                continue;
            if (drop.EntityRuntimeId == 0) continue;

            var item = session.Protocol.Inventory.DescribeStack(drop.Id, drop.Count);
            if (item.NetworkId == 0) continue; // invalid/air — do not spawn AddItemActor
            session.Protocol.Entity.SendAddItemActor(
                drop.EntityRuntimeId,
                item,
                drop.Pose.X,
                drop.Pose.Y + EntityHitboxes.ItemNetworkOffset,
                drop.Pose.Z,
                drop.Pose.VelocityX, drop.Pose.VelocityY, drop.Pose.VelocityZ);
        }
    }
}
