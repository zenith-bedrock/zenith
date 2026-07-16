using Zenith.Session;
using Zenith.World;

namespace Zenith.Protocol;

/// <summary>
/// Empacota LevelChunk + overlays via Protocol. Partilhado por PreSpawn e ChunkStreamSystem.
/// </summary>
static class ColumnSend
{
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
    }

    /// <summary>Re-send current overlays for known columns (join catch-up, §14).</summary>
    public static void EmitOverlaysToSession(
        NetworkSession session,
        World.World world,
        IReadOnlyList<(int X, int Z)> columns)
    {
        if (columns.Count == 0) return;

        var updates = new List<(int X, int Y, int Z, int BlockRuntimeId)>();
        for (var i = 0; i < columns.Count; i++)
        {
            var (cx, cz) = columns[i];
            var overlays = world.GetOverlaysInColumn(cx, cz);
            for (var j = 0; j < overlays.Count; j++)
            {
                var o = overlays[j];
                updates.Add((o.X, o.Y, o.Z, o.BlockRuntimeId));
            }
        }

        if (updates.Count > 0)
            session.Protocol.World.PublishUpdateBlocks(updates);
    }
}
