namespace Zenith.World;

/// <summary>Diferença esparsa sobre o terreno base de uma coluna.</summary>
readonly record struct BlockOverride(int X, int Y, int Z, int BlockRuntimeId);

/// <summary>
/// Leitura mesclada: payload base do storage + overlays da coluna (RAM / LevelDB <c>ov:</c>).
/// </summary>
readonly record struct ColumnReadResult(ChunkColumnData Base, IReadOnlyList<BlockOverride> Overlays);
