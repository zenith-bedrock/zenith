using Zenith.World;

namespace Zenith.Gameplay.Survival;

/// <summary>Vanilla slows digging five-fold with the head in water and five-fold again in the air; the two stack (plan 004 item 7).</summary>
static class DigPenalty
{
    public const double Factor = 5.0;

    public static double DivisorFor(Player.Player player)
    {
        var divisor = 1.0;
        if (SubmersionProbe.AtHead(player) == FluidType.Water) divisor *= Factor;
        if (!player.IsOnGround) divisor *= Factor;
        return divisor;
    }

    /// <summary>Dig ticks of <paramref name="block"/> for what <paramref name="player"/> holds and is doing right now (-1: cannot be dug).</summary>
    public static int BreakTicksFor(Player.Player player, int block, StackId held) =>
        Blocks.BreakTicks(block, held, DivisorFor(player));
}
