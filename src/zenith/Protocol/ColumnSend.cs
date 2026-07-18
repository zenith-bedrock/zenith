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

    public static void EmitToSession(NetworkSession session, in ColumnReadResult column)
    {
        ColumnTerrainEmitter.Emit(
            column,
            sendLevelChunk: bas => session.Protocol.World.SendLevelChunk(new ChunkColumn(
                bas.Coord.X,
                bas.Coord.Z,
                bas.DimensionId,
                bas.SubChunkCount,
                bas.ExtraPayload)),
            sendUpdateBlock: (x, y, z, runtimeId) =>
                session.Protocol.World.SendUpdateBlock(x, y, z, runtimeId));

        EmitFloorDropsInColumn(session, session.Context.World, column.Base.Coord.X, column.Base.Coord.Z);
    }

    /// <summary>Re-send current overlays for known columns (join catch-up, §14).</summary>
    public static void EmitOverlaysToSession(
        NetworkSession session,
        World.World world,
        IReadOnlyList<(int X, int Z)> columns)
    {
        if (columns.Count == 0) return;

        var overlayScratch = t_overlayScratch ??= new List<BlockOverride>(64);
        var updates = new List<(int X, int Y, int Z, int BlockRuntimeId)>();
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
        foreach (var (pos, itemRid, count, entityId, _) in world.FloorDrops.Snapshot())
        {
            if (PlayerChunkTracker.BlockToChunk(pos.X) != chunkX ||
                PlayerChunkTracker.BlockToChunk(pos.Z) != chunkZ)
                continue;
            if (entityId == 0) continue;

            var item = session.Protocol.Inventory.DescribeStack(itemRid, count);
            if (item.NetworkId == 0) continue; // invalid/air — do not spawn AddItemActor
            session.Protocol.Entity.SendAddItemActor(
                entityId,
                item,
                pos.X + 0.5f,
                pos.Y + 0.125f,
                pos.Z + 0.5f);
        }
    }
}
