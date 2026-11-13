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
sealed class VillagerSystem : IGameSystem
{
    private const float MovePerTick = 0.04f;
    private const int MinWanderTicks = 60;
    private const int MaxWanderTicks = 140;
    private const float AttackDamage = 4f;
    private const float AttackDistance = 2.25f;
    private const float InteractDistance = 2.25f; // Same reach as melee: no separate "interact range" concept exists yet.
    private const int TradeCooldownTicks = 200; // 10s @ 20 TPS: stops a held right-click from redeeming the same ware repeatedly.
    private const float DespawnRadius = 64f;

    private readonly PlayerManager _players;
    private readonly EntityRuntime _stores;
    private readonly ComponentStore<VillagerState> _villagers;
    private readonly MobCore _mob;
    private readonly GroundMobBody _body;
    private readonly ActorReplication _replication;
    private readonly StackId _ware;
    private readonly StackId _requestedWare;
    private readonly Random _random;
    private readonly List<EntityId> _tickScratch = [];

    internal long TradeCount { get; private set; }

    public VillagerSystem(World.World world, PlayerManager players, EntityRuntime stores, ItemPalette itemPalette, Random? random = null)
    {
        _players = players;
        _stores = stores;
        _mob = new MobCore(MobDefinitions.Villager, world, players, stores, itemPalette);
        _replication = _mob.Replication;
        _body = new GroundMobBody(world, stores, players);
        _villagers = new ComponentStore<VillagerState>(stores.Entities);
        // The ware is the same emerald a kill drops: one concrete item, two paths to it.
        _ware = StackId.FromItem(itemPalette.Require(MobDefinitions.Villager.LootItemName));
        _requestedWare = StackId.FromItem(itemPalette.Require("minecraft:wheat"));
        _random = random ?? new Random();
    }

    internal IReadOnlyList<EntityId> Villagers => _villagers.Entities;
    internal EntityRuntime Stores => _stores;
    internal ComponentStore<VillagerState> VillagerStates => _villagers;

    /// <summary>Cross-species dispatch seam: "is this ECS entity a villager," nothing more.</summary>
    internal bool Owns(EntityId id) => _villagers.Has(id);
    internal long ReplicatedSpawnCount => _replication.SpawnCount;
    internal long ReplicatedMoveCount => _replication.MoveCount;
    internal long ReplicatedMoveSkippedCount => _replication.MoveSkippedCount;
    internal long ReplicatedRemovalCount => _replication.RemovalCount;
    internal long DespawnCount => _replication.DespawnCount;

    internal EntityId SpawnVillager(float x, float y, float z)
    {
        var id = _mob.Spawn(x, y, z);
        _villagers.Set(id, new VillagerState { Ware = _ware, RequestedWare = _requestedWare });
        return id;
    }

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        _replication.BeginTick(online);
        if (online.Count == 0) return;

        _tickScratch.Clear();
        _tickScratch.AddRange(_villagers.Entities);

        foreach (var id in _tickScratch)
        {
            if (!_stores.Entities.IsAlive(id)) continue;
            if (_replication.TryDespawn(id, clock, online, DespawnRadius)) continue;
            _replication.Reconcile(id, online);
            DamageableActorCombat.ApplyPlayerMeleeAttacks(id, _stores, online, AttackDistance, AttackDamage, clock.CurrentTick, TryApplyDamage);
            if (!_stores.Entities.IsAlive(id)) continue;
            TryHandleInteraction(id, online, clock);
            Wander(id, clock);
            _body.ApplyGravity(id);
            _replication.Reconcile(id, online);
        }

        _replication.Prune(_villagers.Entities, online);
        _replication.ReplicateMoves(_villagers.Entities, online);
    }

    /// <summary>
    /// A real trade: consumes one <see cref="VillagerState.RequestedWare"/> from the player's held stack and gives one
    /// <see cref="VillagerState.Ware"/> back, all-or-nothing. Reach-checked like a melee attack, but it consumes an interact intent
    /// (a different wire action with a different payload), so it does not reuse the attack helper.
    /// </summary>
    private void TryHandleInteraction(EntityId id, IReadOnlyList<Player.Player> online, GameClock clock)
    {
        var state = _villagers.GetRef(id);
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
                _villagers.GetRef(id).NextTradeTick = clock.CurrentTick + TradeCooldownTicks;
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
        player.Level.PersistInventory(player);
        TradeCount++;
        return true;
    }

    /// <summary>Concrete Villager health/removal operation: no retaliation, but a hit is remembered for the golem's aggro rule.</summary>
    public bool TryApplyDamage(EntityId id, DamageSource source, float amount, IReadOnlyList<Player.Player> online, ulong currentTick)
    {
        // Vanilla-parity golem aggro trigger (see Player.LastVillagerAttack): recorded whether or not the hit is accepted below, since
        // a player swinging at a villager is provocative even if e.g. loot capacity later refuses it.
        if (source.OwnerRuntimeId is { } attackerId && _stores.Positions.TryGet(id, out var pos) &&
            _players.GetByRuntimeId(attackerId) is { } attacker)
            attacker.LastVillagerAttack = (currentTick, pos.X, pos.Z);

        return _mob.TryApplyDamage(id, source, amount, online, currentTick);
    }

    /// <summary>Peaceful AI: pick a random heading periodically, walk it, retry sooner if blocked. Same shape as Cow's wander.</summary>
    private void Wander(EntityId id, GameClock clock)
    {
        if (clock.CurrentTick >= _villagers.GetRef(id).WanderChangeAtTick)
            PickNewHeading(id, clock);

        var state = _villagers.GetRef(id);
        if (!_stores.Positions.TryGet(id, out var pos)) return;
        if (!_body.TryMove(id, pos.X + state.WanderDirectionX * MovePerTick, pos.Z + state.WanderDirectionZ * MovePerTick))
        {
            _villagers.GetRef(id).WanderChangeAtTick = clock.CurrentTick; // blocked: choose a fresh heading next tick
            return;
        }

        ref var p = ref _stores.Positions.GetRef(id);
        p.Yaw = LookMath.MoveYawTowards(p.Yaw, LookMath.YawTowards(state.WanderDirectionX, state.WanderDirectionZ), LookMath.DefaultMaxTurnDegreesPerTick);
    }

    private void PickNewHeading(EntityId id, GameClock clock)
    {
        ref var state = ref _villagers.GetRef(id);
        var angle = _random.NextSingle() * MathF.PI * 2f;
        state.WanderDirectionX = MathF.Cos(angle);
        state.WanderDirectionZ = MathF.Sin(angle);
        state.WanderChangeAtTick = clock.CurrentTick + (ulong)_random.Next(MinWanderTicks, MaxWanderTicks);
    }
}
