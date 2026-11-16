using Zenith.World;

namespace Zenith.Gameplay.WorldInteraction.Fire;

/// <summary>The fire block's one property, in terms the rules use.</summary>
static class FireBlocks
{
    public const int MaxAge = 15;

    public static int AgeOf(int id) => Blocks.Catalog.States.TryGet(id, StateKeys.FireAge, out var age) ? age : 0;

    public static int WithAge(int age)
    {
        var id = Blocks.Fire;
        Blocks.Catalog.States.TrySet(ref id, StateKeys.FireAge, Math.Clamp(age, 0, MaxAge));
        return id;
    }
}

/// <summary>
/// The life of one fire cell (ADR §172), run when its scheduled tick falls due. Fire ages; it dies when nothing burnable
/// is beside it and it is not standing on something solid, when it is old and its floor does not burn, or when water is
/// next to it. Each tick it may set a flammable neighbour alight (or burn it to nothing), and may jump to nearby air that
/// borders something that invites fire. All of it is data: <see cref="BlockFamily.Flammability"/> and
/// <see cref="BlockFamily.FlameEncouragement"/>. A neighbour in a chunk that is not resident is never read as open and never loaded.
/// </summary>
sealed class FireSpread(World.World world, Random random, List<(int X, int Y, int Z, int BlockRuntimeId)> changes)
{
    /// <summary>Ticks until a fire cell next acts: 30 to 39.</summary>
    public const int MinDelay = 30;
    public const int DelayRange = 10;

    /// <summary>Fire stays on while it is this young with solid ground under it, even with nothing burnable beside it.</summary>
    private const int YoungestUnfuelledAge = 3;

    /// <summary>Old fire over a floor that does not burn goes out with this chance (one in N) each step.</summary>
    private const int BurnOutOneIn = 4;

    /// <summary>Open cells it can jump to: this many to each side and below by one, up to <see cref="ReachUp"/> above.</summary>
    private const int ReachSide = 1;
    private const int ReachDown = 1;
    private const int ReachUp = 4;

    /// <summary>The roll that decides a jump is out of this, plus this much more for each cell above the first.</summary>
    private const int JumpRoll = 100;

    /// <summary>Added to a neighbour's encouragement before dividing by the fire's age, so young fire spreads fastest.</summary>
    private const int EncouragementBase = 40;
    private const int AgeBase = 30;

    /// <summary>Burning a neighbour turns it into fire when a roll of (age + this) lands under <see cref="FireTurnThreshold"/>, else it is just gone.</summary>
    private const int FireTurnRollBase = 10;
    private const int FireTurnThreshold = 5;

    private const int SideBurnWeight = 300;
    private const int VerticalBurnWeight = 250;

    public static int NextDelay(Random random) => MinDelay + random.Next(DelayRange);

    /// <summary>One step of the fire at the cell; true while it is still burning (so the caller schedules the next one).</summary>
    public bool Tick(int x, int y, int z)
    {
        if (!world.TryGetBlock(x, y, z, out var id) || !Blocks.IsFire(id)) return false;

        foreach (var (dx, dy, dz) in BlockFaces.Neighbours)
            if (world.TryGetBlock(x + dx, y + dy, z + dz, out var side) && Blocks.FluidOf(side) == FluidType.Water)
                return Remove(x, y, z);

        var age = FireBlocks.AgeOf(id);
        var floorKnown = world.TryGetBlock(x, y - 1, z, out var floor);
        var onFuel = floorKnown && Blocks.FlammabilityOf(floor) > 0;
        var fuelBeside = HasFlammableNeighbour(x, y, z);

        if (!fuelBeside && floorKnown && (!Blocks.CanSupportGroundActor(floor) || age > YoungestUnfuelledAge)) return Remove(x, y, z);
        if (!onFuel && age == FireBlocks.MaxAge && random.Next(BurnOutOneIn) == 0) return Remove(x, y, z);

        var aged = Math.Min(FireBlocks.MaxAge, age + random.Next(3) / 2);
        if (aged != age) Write(x, y, z, FireBlocks.WithAge(aged));
        age = aged;

        foreach (var (dx, dy, dz) in BlockFaces.Neighbours)
            TryCatchFire(x + dx, y + dy, z + dz, dy == 0 ? SideBurnWeight : VerticalBurnWeight, age);

        SpreadToAir(x, y, z, age);
        return world.TryGetBlock(x, y, z, out var now) && Blocks.IsFire(now);
    }

    private void TryCatchFire(int x, int y, int z, int weight, int sourceAge)
    {
        if (!world.TryGetBlock(x, y, z, out var id)) return;
        var flammability = Blocks.FlammabilityOf(id);
        if (flammability <= 0 || random.Next(weight) >= flammability) return;

        // A burning block either becomes fire in its turn or is simply gone.
        if (random.Next(sourceAge + FireTurnRollBase) < FireTurnThreshold)
            Write(x, y, z, FireBlocks.WithAge(sourceAge + random.Next(5) / 4));
        else
            Write(x, y, z, World.World.AirRuntimeId);
    }

    private void SpreadToAir(int x, int y, int z, int age)
    {
        for (var dx = -ReachSide; dx <= ReachSide; dx++)
            for (var dz = -ReachSide; dz <= ReachSide; dz++)
                for (var dy = -ReachDown; dy <= ReachUp; dy++)
                {
                    if (dx == 0 && dy == 0 && dz == 0) continue;
                    var (tx, ty, tz) = (x + dx, y + dy, z + dz);
                    if (!world.TryGetBlock(tx, ty, tz, out var target) || !Blocks.IsAir(target)) continue;

                    var encouragement = StrongestEncouragement(tx, ty, tz);
                    if (encouragement <= 0) continue;

                    // Higher cells are harder to reach.
                    var reluctance = dy > 1 ? JumpRoll + (dy - 1) * JumpRoll : JumpRoll;
                    if (random.Next(reluctance) > (encouragement + EncouragementBase) / (age + AgeBase)) continue;
                    if (HasWaterNeighbour(tx, ty, tz)) continue;

                    Write(tx, ty, tz, FireBlocks.WithAge(age + random.Next(5) / 4));
                }
    }

    private int StrongestEncouragement(int x, int y, int z)
    {
        var best = 0;
        foreach (var (dx, dy, dz) in BlockFaces.Neighbours)
            if (world.TryGetBlock(x + dx, y + dy, z + dz, out var id))
                best = Math.Max(best, Blocks.FlameEncouragementOf(id));
        return best;
    }

    private bool HasFlammableNeighbour(int x, int y, int z)
    {
        foreach (var (dx, dy, dz) in BlockFaces.Neighbours)
            if (world.TryGetBlock(x + dx, y + dy, z + dz, out var id) && Blocks.FlammabilityOf(id) > 0)
                return true;
        return false;
    }

    private bool HasWaterNeighbour(int x, int y, int z)
    {
        foreach (var (dx, dy, dz) in BlockFaces.Neighbours)
            if (world.TryGetBlock(x + dx, y + dy, z + dz, out var id) && Blocks.FluidOf(id) == FluidType.Water)
                return true;
        return false;
    }

    private bool Remove(int x, int y, int z)
    {
        Write(x, y, z, World.World.AirRuntimeId);
        return false;
    }

    private void Write(int x, int y, int z, int id)
    {
        if (world.TrySetBlock(x, y, z, id)) changes.Add((x, y, z, id));
    }
}
