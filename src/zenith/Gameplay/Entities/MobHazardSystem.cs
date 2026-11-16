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

            if (FluidAt(pos, HeadHeight) == FluidType.Water)
            {
                var air = _air.GetValueOrDefault(id, AirSupplySystem.MaxAirSupply) - 1;
                if (air <= AirSupplySystem.DrownThreshold)
                {
                    air = 0;
                    damage.TryApplyDamage(id, DamageSource.Drown, AirSupplySystem.DrownDamage, online, clock.CurrentTick);
                }
                _air[id] = air;
            }
            else if (_air.Remove(id, out var left) && left < AirSupplySystem.MaxAirSupply)
            {
                var refilled = Math.Min(AirSupplySystem.MaxAirSupply, Math.Max(0, left) + 4);
                if (refilled < AirSupplySystem.MaxAirSupply) _air[id] = refilled;
            }
        }

        // Entities that are gone leave their breath behind; sweep rarely.
        if (clock.CurrentTick % 200 == 0 && _air.Count > 0)
            _air.Keys.Where(k => !stores.Health.Has(k)).ToList().ForEach(k => _air.Remove(k));
    }

    private FluidType FluidAt(Position pos, float heightAboveFeet) =>
        world.TryGetBlock((int)MathF.Floor(pos.X), (int)MathF.Floor(pos.Y + heightAboveFeet), (int)MathF.Floor(pos.Z), out var block)
            ? Blocks.FluidOf(block)
            : FluidType.None;
}
