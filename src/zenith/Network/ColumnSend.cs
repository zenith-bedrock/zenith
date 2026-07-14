using Zenith.Network.Protocol;
using Zenith.Network.Session;
using Zenith.World;

namespace Zenith.Network;

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
}
