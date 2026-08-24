namespace Zenith.World;

/// <summary>
/// Ordem Bedrock-safe por coluna: LevelChunk (base) e só então UpdateBlock dos overlays.
/// Extraído do PreSpawn para permitir teste de unidade da sequência sem NetworkSession.
///
/// <paramref name="overlays"/> is deliberately a parameter, not read from <c>column.Overlays</c> —
/// see <see cref="Protocol.ColumnSend.EmitToSession"/>'s doc comment for why the caller must supply
/// a freshly-read list rather than whatever was captured when the column was originally read.
/// </summary>
static class ColumnTerrainEmitter
{
    public static void Emit(
        ChunkColumnData bas,
        IReadOnlyList<BlockOverride> overlays,
        Action<ChunkColumnData> sendLevelChunk,
        Action<int, int, int, int> sendUpdateBlock)
    {
        sendLevelChunk(bas);
        for (var i = 0; i < overlays.Count; i++)
        {
            var o = overlays[i];
            sendUpdateBlock(o.X, o.Y, o.Z, o.BlockRuntimeId);
        }
    }
}
