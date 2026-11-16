using Zenith.Ecs;
using Zenith.Gameplay.Runtime;
using Zenith.Gameplay.Survival;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.Entities;

/// <summary>
/// What liquid does to the mobs (ADR §171), the counterpart of <see cref="AirSupplySystem"/> for actors in the ECS: standing
/// in lava hurts every <see cref="LavaIntervalTicks"/>; a head kept under water runs the breath down exactly as it does for
/// a player and drowns the mob when it is spent. Damage goes through <see cref="DamageDispatch"/>, so each species keeps its own
/// death consequences. Aquatic species are exempt (<paramref name="isAquatic"/>); a corpse takes nothing.
/// </summary>
sealed class MobHazardSystem(
    World.World world,
    EntityRuntime stores,
    DamageDispatch damage,
    Func<EntityId, bool> isAquatic) : IGameSystem
{
    public const int LavaIntervalTicks = 10;
    public const float LavaDamage = 4f;

    /// <summary>Mobs differ in height; a point a little over a block up stands for the head, and a floating mob at the surface keeps it dry.</summary>
    private const float HeadHeight = 1.0f;
    private const float FeetOffset = 0.1f;

    /// <summary>How often the breath of entities that no longer exist is forgotten.</summary>
    private const int SweepEveryTicks = 200;

    private readonly Dictionary<EntityId, int> _air = [];
    private readonly List<EntityId> _scratch = [];

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        _scratch.Clear();
        foreach (var id in Query.With(stores.Health, stores.Positions)) _scratch.Add(id);

        foreach (var id in _scratch)
        {
            if (stores.Corpses.Has(id) || isAquatic(id)) continue;
            if (!stores.Positions.TryGet(id, out var pos)) continue;

            if (FluidAt(pos, FeetOffset) == FluidType.Lava && clock.CurrentTick % LavaIntervalTicks == 0)
            {
                damage.TryApplyDamage(id, DamageSource.Lava, LavaDamage, online, clock.CurrentTick);
                continue;
            }

            var underwater = FluidAt(pos, HeadHeight) == FluidType.Water;
            if (!underwater && !_air.ContainsKey(id)) continue; // nothing to breathe back in

            var air = AirSupplySystem.Breathe(_air.GetValueOrDefault(id, AirSupplySystem.MaxAirSupply), underwater, out var drowning);
            if (drowning) damage.TryApplyDamage(id, DamageSource.Drown, AirSupplySystem.DrownDamage, online, clock.CurrentTick);
            if (air >= AirSupplySystem.MaxAirSupply) _air.Remove(id);
            else _air[id] = air;
        }

        // Entities that are gone leave their breath behind; sweep rarely.
        if (clock.CurrentTick % SweepEveryTicks == 0 && _air.Count > 0)
            _air.Keys.Where(k => !stores.Health.Has(k)).ToList().ForEach(k => _air.Remove(k));
    }

    private FluidType FluidAt(Position pos, float heightAboveFeet) =>
        world.TryGetBlock((int)MathF.Floor(pos.X), (int)MathF.Floor(pos.Y + heightAboveFeet), (int)MathF.Floor(pos.Z), out var block)
            ? Blocks.FluidOf(block)
            : FluidType.None;
}
