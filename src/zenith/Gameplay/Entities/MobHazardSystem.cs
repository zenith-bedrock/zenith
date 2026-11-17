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
[GameSystem("mob-hazard")]
sealed class MobHazardSystem(
    World.World world,
    EntityRuntime stores,
    DamageDispatch damage,
    Func<EntityId, bool> isAquatic,
    Func<EntityId, bool>? isUndead = null) : IGameSystem
{
    public const int LavaIntervalTicks = 10;
    public const float LavaDamage = 4f;

    /// <summary>The sun burns undead while it takes at most this many levels off the sky (full day and the edges of it).</summary>
    public const int SunBurnMaxDarkness = 3;

    /// <summary>How often a mob is checked for standing in the sun.</summary>
    private const int SunCheckEvery = 10;

    /// <summary>Mobs differ in height; a point a little over a block up stands for the head, and a floating mob at the surface keeps it dry.</summary>
    private const float HeadHeight = 1.0f;
    private const float FeetOffset = 0.1f;

    /// <summary>How often the breath of entities that no longer exist is forgotten.</summary>
    private const int SweepEveryTicks = 200;

    private readonly Dictionary<EntityId, int> _air = [];
    private readonly Dictionary<EntityId, int> _burn = [];
    private readonly List<EntityId> _scratch = [];

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        _scratch.Clear();
        foreach (var id in Query.With(stores.Health, stores.Positions)) _scratch.Add(id);

        foreach (var id in _scratch)
        {
            if (stores.Corpses.Has(id) || isAquatic(id)) continue;
            if (!stores.Positions.TryGet(id, out var pos)) continue;

            var sunlit = isUndead is not null && clock.CurrentTick % SunCheckEvery == 0 && isUndead(id) && InSun(pos, clock.WorldTime);
            Burn(id, pos, clock.CurrentTick, online, sunlit);

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

        // Entities that are gone leave their breath and flames behind; sweep rarely.
        if (clock.CurrentTick % SweepEveryTicks == 0)
        {
            if (_air.Count > 0) _air.Keys.Where(k => !stores.Health.Has(k)).ToList().ForEach(k => _air.Remove(k));
            if (_burn.Count > 0) _burn.Keys.Where(k => !stores.Health.Has(k)).ToList().ForEach(k => _burn.Remove(k));
        }
    }

    /// <summary>Fire and lava light a mob, water puts it out, and flames hurt it once a second (see <see cref="Burning"/>).</summary>
    private void Burn(EntityId id, Position pos, ulong tick, IReadOnlyList<Player.Player> online, bool sunlit = false)
    {
        var before = _burn.GetValueOrDefault(id);
        var contact = Burning.ContactAt(world, pos.X, pos.Y, pos.Z, alight: before > 0);
        if (contact == 0 && sunlit) contact = Burning.FireIgniteTicks;
        if (before == 0 && contact <= 0) return;

        var remaining = Burning.Step(before, contact, tick, out var hurt);
        if (remaining > 0) _burn[id] = remaining;
        else _burn.Remove(id);
        if (hurt) damage.TryApplyDamage(id, DamageSource.Fire, Burning.Damage, online, tick);

        if ((before > 0) == (remaining > 0)) return;
        if (!stores.Identities.TryGet(id, out var identity)) return;
        foreach (var peer in online)
            if (peer.IsInGame) peer.Client.Entity.SendActorBurning(identity.ActorRuntimeId, remaining > 0);
    }

    /// <summary>Daylight and nothing but clear air overhead: the cell the mob stands in sees the sun.</summary>
    private bool InSun(Position pos, int worldTime) =>
        !world.Weather.Raining && LightModel.SkyDarkness(worldTime) <= SunBurnMaxDarkness &&
        LightModel.SeesSky(world, (int)MathF.Floor(pos.X), (int)MathF.Floor(pos.Y + .5f), (int)MathF.Floor(pos.Z));

    /// <summary>Ticks of burning left on a mob (0 when it is not alight).</summary>
    public int BurnTicksOf(EntityId id) => _burn.GetValueOrDefault(id);

    private FluidType FluidAt(Position pos, float heightAboveFeet) =>
        world.TryGetBlock((int)MathF.Floor(pos.X), (int)MathF.Floor(pos.Y + heightAboveFeet), (int)MathF.Floor(pos.Z), out var block)
            ? Blocks.FluidOf(block)
            : FluidType.None;
}
