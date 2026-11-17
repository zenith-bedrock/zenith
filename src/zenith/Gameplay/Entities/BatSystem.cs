using Zenith.Ecs;
using Zenith.Gameplay.Runtime;
using Zenith.Gameplay.Survival;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.Entities;

/// <summary>
/// The flying mob. Position, health, identity and despawn tracking are the shared <see cref="EntityRuntime"/> components and
/// showing it to players is the shared <see cref="ActorReplication"/>; only <see cref="BatState"/> (its 3D wander heading) and the
/// flight rule are its own. Movement is bespoke 3D flight, not <see cref="GroundMobMovement"/>: a bat has no supporting-block
/// requirement and wanders on a vertical axis no ground mob uses.
/// </summary>
[GameSystem("bat")]
sealed class BatSystem : MobSystem<BatState>
{
    private const float MovePerTick = 0.1f;
    private const int MinWanderTicks = 30;
    private const int MaxWanderTicks = 80;
    private const float WanderVerticalBias = 0.5f; // Keeps the random heading from drifting mostly sideways.
    private const float AttackDamage = 4f;

    private readonly Random _random;

    public BatSystem(World.World world, PlayerManager players, EntityRuntime stores, ItemPalette itemPalette, Random? random = null)
        : base(MobDefinitions.Bat, world, players, stores, itemPalette, withVelocity: false)
    {
        _random = random ?? new Random();
    }

    internal IReadOnlyList<EntityId> Bats => Entities;
    internal ComponentStore<BatState> BatStates => States;

    internal EntityId SpawnBat(float x, float y, float z) => SpawnWith(x, y, z);

    protected override void Think(EntityId id, GameClock clock, IReadOnlyList<Player.Player> online)
    {
        Wander(id, clock);
    }

    /// <summary>
    /// Bespoke 3D wander: picks a random heading including a vertical component, walks it, retries sooner if blocked. The one real
    /// behavioural difference from every ground mob's wander is the Y axis being live, not fixed to a sampled ground height.
    /// </summary>
    private void Wander(EntityId id, GameClock clock)
    {
        if (clock.CurrentTick >= States.GetRef(id).WanderChangeAtTick)
            PickNewHeading(id, clock);

        var state = States.GetRef(id);
        if (!_stores.Positions.TryGet(id, out var pos)) return;
        if (TryFly(id, pos.X + state.WanderDirectionX * MovePerTick, pos.Y + state.WanderDirectionY * MovePerTick, pos.Z + state.WanderDirectionZ * MovePerTick))
        {
            ref var p = ref _stores.Positions.GetRef(id);
            p.Yaw = LookMath.MoveYawTowards(p.Yaw, LookMath.YawTowards(state.WanderDirectionX, state.WanderDirectionZ), LookMath.DefaultMaxTurnDegreesPerTick);
        }
        else
            States.GetRef(id).WanderChangeAtTick = clock.CurrentTick; // blocked — choose a fresh heading next tick
    }

    private void PickNewHeading(EntityId id, GameClock clock)
    {
        ref var state = ref States.GetRef(id);
        var yaw = _random.NextSingle() * MathF.PI * 2f;
        var horizontal = MathF.Cos(_random.NextSingle() * MathF.PI * WanderVerticalBias);
        state.WanderDirectionX = MathF.Cos(yaw) * horizontal;
        state.WanderDirectionZ = MathF.Sin(yaw) * horizontal;
        state.WanderDirectionY = (_random.NextSingle() * 2f - 1f) * (1f - horizontal);
        state.WanderChangeAtTick = clock.CurrentTick + (ulong)_random.Next(MinWanderTicks, MaxWanderTicks);
    }

    /// <summary>
    /// Flight-validity probe: a flying mob needs body clearance but, unlike every ground mob, no solid block underneath.
    /// </summary>
    private bool TryFly(EntityId id, float x, float y, float z)
    {
        var blockX = (int)MathF.Floor(x);
        var blockY = (int)MathF.Floor(y);
        var blockZ = (int)MathF.Floor(z);
        if (_world.GetBlock(blockX, blockY, blockZ) != World.World.AirRuntimeId ||
            _world.GetBlock(blockX, blockY + 1, blockZ) != World.World.AirRuntimeId)
            return false;

        ref var p = ref _stores.Positions.GetRef(id);
        p.X = x;
        p.Y = y;
        p.Z = z;
        return true;
    }
}
