namespace Zenith.World;

/// <summary>
/// Single source of truth for block-to-chunk coordinate conversion. Previously implemented
/// independently in two places - World.ToChunk (int, arithmetic shift) and
/// PlayerChunkTracker.BlockToChunk (float, Math.Floor) - which happen to agree today (shifting
/// a power-of-2 is floor division) but had no shared authority to keep them agreeing if chunk
/// size or the int/float split ever changed.
/// </summary>
static class ChunkMath
{
    public const int ChunkSizeShift = 4; // 1 << 4 == 16 blocks per chunk edge.

    public static int BlockToChunk(int block) => block >> ChunkSizeShift;

    public static int BlockToChunk(float block) => (int)Math.Floor(block / 16f);
}
