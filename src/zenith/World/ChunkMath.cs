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

    /// <summary>Blocks along one chunk (and section) edge.</summary>
    public const int Edge = 1 << ChunkSizeShift;

    private const int LocalMask = Edge - 1;

    public static int BlockToChunk(int block) => block >> ChunkSizeShift;

    public static int BlockToChunk(float block) => (int)Math.Floor(block / Edge);

    /// <summary>The block coordinate of a chunk's lowest corner along one axis.</summary>
    public static int ChunkOrigin(int chunk) => chunk << ChunkSizeShift;

    /// <summary>A block coordinate's position inside its chunk (0..15), also for negative coordinates.</summary>
    public static int ToLocal(int block) => block & LocalMask;
}
