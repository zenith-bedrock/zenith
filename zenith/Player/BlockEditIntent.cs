namespace Zenith.Player;

/// <summary>Intenção de bloco pendente — escrita só no handler; mutação no BlockSystem.</summary>
readonly struct BlockEditIntent
{
    public bool HasValue { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Z { get; init; }
    public int BlockRuntimeId { get; init; }

    public static BlockEditIntent Set(int x, int y, int z, int blockRuntimeId) => new()
    {
        HasValue = true,
        X = x,
        Y = y,
        Z = z,
        BlockRuntimeId = blockRuntimeId
    };

    public bool IsInWorldBounds() =>
        Y is >= -64 and <= 320 &&
        X is > -30_000_000 and < 30_000_000 &&
        Z is > -30_000_000 and < 30_000_000;
}
