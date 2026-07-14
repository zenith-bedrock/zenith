namespace Zenith.World;

/// <summary>
/// Ordem Bedrock-safe por coluna: LevelChunk (base) e só então UpdateBlock dos overlays.
/// Extraído do PreSpawn para permitir teste de unidade da sequência sem NetworkSession.
/// </summary>
static class ColumnTerrainEmitter
{
    public static void Emit(
        in ColumnReadResult column,
        Action<ChunkColumnData> sendLevelChunk,
        Action<int, int, int, int> sendUpdateBlock)
    {
        sendLevelChunk(column.Base);
        var overlays = column.Overlays;
        for (var i = 0; i < overlays.Count; i++)
        {
            var o = overlays[i];
            sendUpdateBlock(o.X, o.Y, o.Z, o.BlockRuntimeId);
        }
    }
}
