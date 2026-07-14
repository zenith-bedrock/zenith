using Zenith.World;

namespace Zenith.Player;

/// <summary>
/// Quais colunas já foram (ou estão a ser) enviadas a este jogador.
/// Usado por PreSpawn + ChunkStreamSystem — não é VisibilitySystem.
/// </summary>
sealed class PlayerChunkTracker
{
    private readonly object _gate = new();
    private readonly HashSet<(int X, int Z)> _known = new();

    /// <summary>Raio de view em chunks (confirmado ao cliente).</summary>
    public int Radius { get; set; }

    public int LastPublisherChunkX { get; private set; } = int.MinValue;
    public int LastPublisherChunkZ { get; private set; } = int.MinValue;

    /// <summary>Marca coluna como em voo/enviada. False se já conhecida.</summary>
    public bool TryBegin(int chunkX, int chunkZ)
    {
        lock (_gate)
            return _known.Add((chunkX, chunkZ));
    }

    public void Forget(int chunkX, int chunkZ)
    {
        lock (_gate)
            _known.Remove((chunkX, chunkZ));
    }

    public void RememberMany(IEnumerable<(int X, int Z)> coords)
    {
        lock (_gate)
        {
            foreach (var c in coords)
                _known.Add(c);
        }
    }

    public bool PublisherCenterChanged(int chunkX, int chunkZ)
    {
        if (chunkX == LastPublisherChunkX && chunkZ == LastPublisherChunkZ)
            return false;
        LastPublisherChunkX = chunkX;
        LastPublisherChunkZ = chunkZ;
        return true;
    }

    public static int BlockToChunk(float block) => (int)Math.Floor(block / 16f);

    public static void ForEachInSquare(int centerX, int centerZ, int radius, Action<int, int> visit)
    {
        for (var x = centerX - radius; x <= centerX + radius; x++)
        for (var z = centerZ - radius; z <= centerZ + radius; z++)
            visit(x, z);
    }
}
