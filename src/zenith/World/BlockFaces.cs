namespace Zenith.World;

/// <summary>Block-face arithmetic for the Bedrock face index (0 down, 1 up, 2 north, 3 south, 4 west, 5 east).</summary>
static class BlockFaces
{
    public const byte Down = 0;
    public const byte Up = 1;
    public const byte North = 2;
    public const byte South = 3;
    public const byte West = 4;
    public const byte East = 5;

    /// <summary>The six unit steps to a face-adjacent cell, indexed by face.</summary>
    public static readonly (int Dx, int Dy, int Dz)[] Neighbours =
    [
        (0, -1, 0), (0, 1, 0), (0, 0, -1), (0, 0, 1), (-1, 0, 0), (1, 0, 0)
    ];

    /// <summary>The four unit steps along the ground (north, south, west, east).</summary>
    public static readonly (int Dx, int Dz)[] Horizontal = [(0, -1), (0, 1), (-1, 0), (1, 0)];

    /// <summary>The cell a click on <paramref name="face"/> of (x, y, z) points into; the cell itself for an unknown face.</summary>
    public static (int X, int Y, int Z) Offset(int x, int y, int z, byte face)
    {
        if (face >= Neighbours.Length) return (x, y, z);
        var (dx, dy, dz) = Neighbours[face];
        return (x + dx, y + dy, z + dz);
    }
}
