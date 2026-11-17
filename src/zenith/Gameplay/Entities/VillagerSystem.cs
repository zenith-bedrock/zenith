using Zenith.Gameplay.Inventory;
using Zenith.Ecs;
using Zenith.Gameplay.Runtime;
using Zenith.Gameplay.Survival;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.Entities;

/// <summary>
/// Passive wandering NPC with one real trade (wheat in, emerald out) on top of the shared actor components: position, health, fall
/// speed and despawn tracking are <see cref="EntityRuntime"/> components, showing it to players is <see cref="ActorReplication"/>,
/// and only <see cref="VillagerState"/> (the offer, the trade cooldown, the wander heading) is its own. The trade is the first
/// consumer of <c>InventoryTransactionPacket.ActorInteract</c>; see <see cref="TryHandleInteraction"/>.
/// </summary>
[GameSystem("villager")]
sealed class VillagerSystem : MobSystem<VillagerState>
{
    private const float MovePerTick = 0.04f;
    private const int MinWanderTicks = 60;
    private const int MaxWanderTicks = 140;
    private const float AttackDamage = 4f;
    private const float InteractDistance = 2.25f; // Same reach as melee: no separate "interact range" concept exists yet.
    private const int TradeCooldownTicks = 200; // 10s @ 20 TPS: stops a held right-click from redeeming the same ware repeatedly.

    private readonly GroundMobBody _body;
    private readonly StackId _ware;
    private readonly StackId _requestedWare;
    private readonly Random _random;

    internal long TradeCount { get; private set; }

    public VillagerSystem(World.World world, PlayerManager players, EntityRuntime stores, ItemPalette itemPalette, Random? random = null)
        : base(MobDefinitions.Villager, world, players, stores, itemPalette)
    {
        _body = new GroundMobBody(world, stores, players);
        // The ware a trade gives back; independent of what a kill drops (the loot table).
        _ware = StackId.FromItem(itemPalette.Require(ItemNames.Emerald));
        _requestedWare = StackId.FromItem(itemPalette.Require(ItemNames.Wheat));
        _random = random ?? new Random();
    }

    internal IReadOnlyList<EntityId> Villagers => Entities;
    internal ComponentStore<VillagerState> VillagerStates => States;

    internal EntityId SpawnVillager(float x, float y, float z)
    {
        var id = _mob.Spawn(x, y, z);
        States.Set(id, new VillagerState { Ware = _ware, RequestedWare = _requestedWare });
        return id;
    }

    protected override void Think(EntityId id, GameClock clock, IReadOnlyList<Player.Player> online)
    {
        TryHandleInteraction(id, online, clock);
        Wander(id, clock);
        _body.ApplyGravity(id);
    }

    /// <summary>
    /// A real trade: consumes one <see cref="VillagerState.RequestedWare"/> from the player's held stack and gives one
    /// <see cref="VillagerState.Ware"/> back, all-or-nothing. Reach-checked like a melee attack, but it consumes an interact intent
    /// (a different wire action with a different payload), so it does not reuse the attack helper.
    /// </summary>
    private void TryHandleInteraction(EntityId id, IReadOnlyList<Player.Player> online, GameClock clock)
    {
        var state = States.GetRef(id);
        if (clock.CurrentTick < state.NextTradeTick) return;
        if (!_stores.Positions.TryGet(id, out var pos)) return;
        if (!_stores.Identities.TryGet(id, out var identity)) return;

        var reachSquared = InteractDistance * InteractDistance;
        foreach (var player in online)
        {
            if (!player.IsInGame || player.IsDead) continue;
            var dx = pos.X - player.PositionX;
            var dz = pos.Z - player.PositionZ;
            if (dx * dx + dz * dz > reachSquared) continue;
            if (!player.Intents.TryConsumeInteractIntent(checked((long)identity.ActorRuntimeId))) continue;

            if (TryTrade(state, player))
                States.GetRef(id).NextTradeTick = clock.CurrentTick + TradeCooldownTicks;
            return; // At most one interact resolved per tick: the intent itself is already one-shot.
        }
    }

    /// <summary>All-or-nothing exchange: the player's payment is refunded if the villager's ware can't fit their inventory.</summary>
    private bool TryTrade(VillagerState offer, Player.Player player)
    {
        if (!player.Inventory.TryConsume(offer.RequestedWare, 1)) return false;

        if (!player.Inventory.TryAdd(offer.Ware, 1))
        {
            player.Inventory.TryAdd(offer.RequestedWare, 1); // Refund: the slot this just vacated can always take it back.
            return false;
        }

        player.Client.Inventory.SendInventoryContent(player.Inventory);
        player.Level.Persistence.PersistInventory(player);
        TradeCount++;
        return true;
    }

    /// <summary>Concrete Villager health/removal operation: no retaliation, but a hit is remembered for the golem's aggro rule.</summary>
    public override bool TryApplyDamage(EntityId id, DamageSource source, float amount, IReadOnlyList<Player.Player> online, ulong currentTick)
    {
        // Vanilla-parity golem aggro trigger (see Player.LastVillagerAttack): recorded whether or not the hit is accepted below, since
        // a player swinging at a villager is provocative even if e.g. loot capacity later refuses it.
        if (source.OwnerRuntimeId is { } attackerId && _stores.Positions.TryGet(id, out var pos) &&
            _players.GetByRuntimeId(attackerId) is { } attacker)
            attacker.LastVillagerAttack = (currentTick, pos.X, pos.Z);

        return base.TryApplyDamage(id, source, amount, online, currentTick);
    }

    /// <summary>Peaceful AI: pick a random heading periodically, walk it, retry sooner if blocked. Same shape as Cow's wander.</summary>
    private void Wander(EntityId id, GameClock clock)
    {
        if (clock.CurrentTick >= States.GetRef(id).WanderChangeAtTick)
            PickNewHeading(id, clock);

        var state = States.GetRef(id);
        if (!_stores.Positions.TryGet(id, out var pos)) return;
        if (!_body.TryMove(id, pos.X + state.WanderDirectionX * MovePerTick, pos.Z + state.WanderDirectionZ * MovePerTick))
        {
            States.GetRef(id).WanderChangeAtTick = clock.CurrentTick; // blocked: choose a fresh heading next tick
            return;
        }

        ref var p = ref _stores.Positions.GetRef(id);
        p.Yaw = LookMath.MoveYawTowards(p.Yaw, LookMath.YawTowards(state.WanderDirectionX, state.WanderDirectionZ), LookMath.DefaultMaxTurnDegreesPerTick);
    }

    private void PickNewHeading(EntityId id, GameClock clock)
    {
        ref var state = ref States.GetRef(id);
        var angle = _random.NextSingle() * MathF.PI * 2f;
        state.WanderDirectionX = MathF.Cos(angle);
        state.WanderDirectionZ = MathF.Sin(angle);
        state.WanderChangeAtTick = clock.CurrentTick + (ulong)_random.Next(MinWanderTicks, MaxWanderTicks);
    }
}
