namespace Zenith.Player;

/// <summary>
/// Dig start/abort queued on the receive thread; <see cref="Gameplay.Systems.BlockSystem"/>
/// applies <c>BeginBreak</c>/<c>AbortBreak</c> + crack/swing fan-out on tick (§54).
/// </summary>
readonly struct DigIntent
{
    public bool HasValue { get; init; }
    public bool IsAbort { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Z { get; init; }
    public int RequiredTicks { get; init; }
    public ulong StartedTick { get; init; }

    public static DigIntent Start(int x, int y, int z, ulong startedTick, int requiredTicks) => new()
    {
        HasValue = true,
        IsAbort = false,
        X = x,
        Y = y,
        Z = z,
        StartedTick = startedTick,
        RequiredTicks = requiredTicks
    };

    public static DigIntent Abort(int x, int y, int z) => new()
    {
        HasValue = true,
        IsAbort = true,
        X = x,
        Y = y,
        Z = z
    };
}
