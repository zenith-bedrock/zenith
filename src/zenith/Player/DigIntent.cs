using Zenith.World;

namespace Zenith.Player;

/// <summary>
/// Dig start/abort queued on the receive thread; <see cref="Gameplay.Systems.BlockDigSystem"/>
/// applies <c>BeginBreak</c>/<c>AbortBreak</c> + crack/swing fan-out on tick (§54).
/// </summary>
readonly struct DigIntent
{
    public bool HasValue { get; init; }
    public bool IsAbort { get; init; }
    public bool IsActivity { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Z { get; init; }
    public int RequiredTicks { get; init; }
    public ulong StartedTick { get; init; }
    /// <summary>Held stack at dig start (ADR §55).</summary>
    public StackId HeldStackId { get; init; }

    public static DigIntent Start(
        int x, int y, int z, ulong startedTick, int requiredTicks, StackId heldStackId = default) => new()
    {
        HasValue = true,
        IsAbort = false,
        X = x,
        Y = y,
        Z = z,
        StartedTick = startedTick,
        RequiredTicks = requiredTicks,
        HeldStackId = heldStackId
    };

    public static DigIntent Abort(int x, int y, int z) => new()
    {
        HasValue = true,
        IsAbort = true,
        X = x,
        Y = y,
        Z = z
    };

    public static DigIntent Activity(int x, int y, int z, ulong tick) => new()
    {
        HasValue = true,
        IsActivity = true,
        X = x,
        Y = y,
        Z = z,
        StartedTick = tick
    };
}
