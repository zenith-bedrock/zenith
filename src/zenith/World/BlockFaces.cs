namespace Zenith.World;

/// <summary>Block-face arithmetic for the Bedrock face index (0 down, 1 up, 2 north, 3 south, 4 west, 5 east).</summary>
static class BlockFaces
{
    /// <summary>The cell a click on <paramref name="face"/> of (x, y, z) points into.</summary>
    public static (int X, int Y, int Z) Offset(int x, int y, int z, byte face) => face switch
    {
        0 => (x, y - 1, z),
        1 => (x, y + 1, z),
        2 => (x, y, z - 1),
        3 => (x, y, z + 1),
        4 => (x - 1, y, z),
        5 => (x + 1, y, z),
        _ => (x, y, z)
    };
}
