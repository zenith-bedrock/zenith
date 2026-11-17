using Zenith.World;

namespace Zenith.Gameplay.Survival;

/// <summary>
/// The rules of being on fire, shared by players and mobs (plan 004 item 8). Fire or lava sets a body alight for a while; an
/// alight body takes <see cref="Damage"/> every <see cref="DamageIntervalTicks"/> until the time runs out, and water at the feet
/// puts it out at once. Contact renews the time but never shortens it.
/// </summary>
static class Burning
{
    public const int FireIgniteTicks = 160;
    public const int LavaIgniteTicks = 300;
    public const int DamageIntervalTicks = 20;
    public const float Damage = 1f;

    /// <summary>What the cells around a body's feet do to it: <see cref="Extinguish"/>, 0 for nothing, or the ticks it is set alight for.</summary>
    public const int Extinguish = -1;

    public static int ContactAt(World.World world, float x, float y, float z, bool alight = false)
    {
        var (bx, bz) = ((int)MathF.Floor(x), (int)MathF.Floor(z));
        var feet = (int)MathF.Floor(y + 0.1f);
        if (world.TryGetBlock(bx, feet, bz, out var below))
        {
            var fluid = Blocks.FluidOf(below);
            if (fluid == FluidType.Water) return Extinguish;
            if (fluid == FluidType.Lava) return LavaIgniteTicks;
            if (Blocks.IsFire(below)) return FireIgniteTicks;
        }

        if (world.TryGetBlock(bx, feet + 1, bz, out var above) && Blocks.IsFire(above)) return FireIgniteTicks;

        // Rain falling on a body that is alight puts it out (only asked of a burning body: it scans the sky above).
        return alight && world.Weather.Raining && LightModel.SeesSky(world, bx, feet + 1, bz) ? Extinguish : 0;
    }

    /// <summary>One tick for a body that has <paramref name="remaining"/> ticks of burning: the new time and whether it takes its damage now.</summary>
    public static int Step(int remaining, int contact, ulong tick, out bool hurt)
    {
        if (contact == Extinguish) remaining = 0;
        else if (contact > remaining) remaining = contact;

        hurt = remaining > 0 && tick % DamageIntervalTicks == 0;
        return remaining > 0 ? remaining - 1 : 0;
    }
}
