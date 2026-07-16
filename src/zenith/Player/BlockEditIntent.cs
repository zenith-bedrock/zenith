namespace Zenith.Player;

/// <summary>Intenção de bloco pendente — escrita só no handler; mutação no BlockSystem.</summary>
readonly struct BlockEditIntent
{
    public bool HasValue { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Z { get; init; }
    public int BlockRuntimeId { get; init; }

    /// <summary>Slot do hotbar a consumir no place; ignorado no break (−1).</summary>
    public int HotbarSlot { get; init; }

    /// <summary>
    /// Survival dig auth snapshotted at queue time (§27). When true, timing uses
    /// <see cref="DigStartedTick"/> / <see cref="DigRequiredTicks"/> — not live <c>HasBreakTarget</c>.
    /// </summary>
    public bool DigAuthorized { get; init; }
    public ulong DigStartedTick { get; init; }
    public int DigRequiredTicks { get; init; }

    public static BlockEditIntent Set(int x, int y, int z, int blockRuntimeId, int hotbarSlot = -1) => new()
    {
        HasValue = true,
        X = x,
        Y = y,
        Z = z,
        BlockRuntimeId = blockRuntimeId,
        HotbarSlot = hotbarSlot
    };

    /// <summary>Survival break with dig progress frozen into the intent (receive-thread retarget-safe).</summary>
    public static BlockEditIntent BreakWithDig(
        int x,
        int y,
        int z,
        ulong digStartedTick,
        int digRequiredTicks) => new()
    {
        HasValue = true,
        X = x,
        Y = y,
        Z = z,
        BlockRuntimeId = World.World.AirRuntimeId,
        HotbarSlot = -1,
        DigAuthorized = true,
        DigStartedTick = digStartedTick,
        DigRequiredTicks = digRequiredTicks
    };

    public bool IsInWorldBounds() =>
        Y is >= -64 and <= 320 &&
        X is > -30_000_000 and < 30_000_000 &&
        Z is > -30_000_000 and < 30_000_000;
}
