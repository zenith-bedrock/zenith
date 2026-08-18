using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.Protocol;
using Zenith.World;

using Zenith.Gameplay.Survival;
namespace Zenith.Gameplay.Entities;

/// <summary>
/// Phase XVII, Priority 3 — passive wandering NPC, structurally near-identical to CowSystem's
/// wander/despawn/spawn/combat shape (that duplication is intentional and documented, not missed —
/// see docs/history/phases/phase-xvii-runtime-pressure-findings.md). Phase XVIII, Priority 1 added a real trade
/// interaction on top of <see cref="Villager.Wares"/> (wheat in, emerald out) — the first real use
/// of <c>InventoryTransactionPacket.ActorInteract</c>, previously decoded but never dispatched
/// anywhere. See <see cref="TryHandleInteraction"/> and
/// docs/history/phases/phase-xviii-runtime-pressure-findings.md for what wiring this actually revealed.
/// </summary>
sealed class VillagerSystem : IGameSystem
{
    private const float SpawnDistance = 4f;
    private const float MovePerTick = 0.04f;
    private const int MinWanderTicks = 60;
    private const int MaxWanderTicks = 140;
    private const float AttackDamage = 4f;
    private const float InteractDistance = 2.25f; // Same reach as melee — no separate "interact range" concept exists yet.
    private const int TradeCooldownTicks = 200; // 10s @ 20 TPS — prevents a single held-right-click from redeeming the same wares repeatedly.
    private const string LootItemName = "minecraft:emerald";
    /// <summary>Vanilla-parity NPC kill reward — same tier as a passive mob (Phase XI.4/XIV).</summary>
    private const int KillExperience = 1;
    private const float DespawnRadius = 64f;

    private readonly World.World _world;
    private readonly PlayerManager _players;
    private readonly VillagerStore _villagers;
    private readonly StackId _lootItem;
    private readonly StackId _wareItem;
    private readonly StackId _requestedWare;
    private readonly Random _random;
    private readonly HashSet<(long VillagerId, long PlayerId)> _replicated = new();
    private readonly Dictionary<(long VillagerId, long PlayerId), ProjectedPose> _lastProjected = new();
    private readonly List<RawActorPose> _moveBatch = [];
    private bool _bootstrapSpawned;
    private ulong _currentTick;

    internal long TradeCount { get; private set; }

    public VillagerSystem(World.World world, PlayerManager players, VillagerStore villagers, ItemPalette itemPalette, Random? random = null)
    {
        _world = world;
        _players = players;
        _villagers = villagers;
        _lootItem = StackId.FromItem(itemPalette.Require(LootItemName));
        _wareItem = _lootItem; // Traded ware is the same emerald a kill drops — one concrete item, two paths to it.
        _requestedWare = StackId.FromItem(itemPalette.Require("minecraft:wheat"));
        _random = random ?? new Random();
    }

    public VillagerStore Villagers => _villagers;
    internal long ReplicatedSpawnCount { get; private set; }
    internal long ReplicatedMoveCount { get; private set; }
    internal long ReplicatedMoveSkippedCount { get; private set; }
    internal long ReplicatedRemovalCount { get; private set; }
    internal long DespawnCount { get; private set; }

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        _currentTick = clock.CurrentTick;
        if (online.Count == 0) return;
        if (_villagers.Active.Count != 0)
            _bootstrapSpawned = true;
        EnsureBootstrapVillager(online);

        foreach (var villager in _villagers.Active.ToArray())
        {
            if (!villager.IsActive) continue;
            if (TryDespawn(villager, clock, online)) continue;
            ReconcileViewers(villager, online);
            ApplyPlayerAttacks(villager, online);
            if (!villager.IsActive) continue;
            TryHandleInteraction(villager, online, clock);
            Wander(villager, clock);
            ApplyGravity(villager);
            ReconcileViewers(villager, online);
        }

        _replicated.RemoveWhere(pair => !_villagers.Active.Any(v => v.EntityId == pair.VillagerId) ||
                                        !online.Any(p => p.RuntimeId == pair.PlayerId));
        foreach (var key in _lastProjected.Keys.Where(key => !_replicated.Contains(key)).ToArray())
            _lastProjected.Remove(key);
        ReplicateMoves(online);
    }

    private void EnsureBootstrapVillager(IReadOnlyList<Player.Player> online)
    {
        if (_bootstrapSpawned || _villagers.Active.Count != 0) return;
        var player = online.FirstOrDefault(p => p.IsInGame && !p.IsDead);
        if (player is null) return;
        var x = player.PositionX;
        var z = player.PositionZ - SpawnDistance;
        var y = _world.SampleSpawnFeetY((int)MathF.Floor(x), (int)MathF.Floor(z));
        var entityId = _players.AllocateRuntimeId();
        var villager = new Villager(entityId, (ulong)entityId, x, y, z)
        {
            RequestedWare = _requestedWare
        };
        villager.Wares.Add(_wareItem);
        if (_villagers.TryAdd(villager))
            _bootstrapSpawned = true;
    }

    /// <summary>
    /// The first real consumer of <c>InventoryTransactionPacket.ActorInteract</c> — previously
    /// decoded (<see cref="Packets.InventoryTransactionPacket.TargetActorRuntimeId"/>/
    /// <c>ActorActionType</c>) but never dispatched anywhere (Phase XVI/XVII deferred this
    /// exactly, twice). A real trade: consumes one <see cref="Villager.RequestedWare"/> from the
    /// player's held stack, gives one <see cref="Villager.Wares"/> item back, all-or-nothing.
    /// Reach-checked the same way <see cref="GroundMobCombat.ApplyPlayerMeleeAttacks{TMob}"/>
    /// reach-checks an attack — deliberately not reusing that helper, since it consumes an attack
    /// intent, not an interact one, and the two are different wire actions with different payloads.
    /// </summary>
    private void TryHandleInteraction(Villager villager, IReadOnlyList<Player.Player> online, GameClock clock)
    {
        if (clock.CurrentTick < villager.NextTradeTick) return;
        if (villager.Wares.Count == 0) return;

        var reachSquared = InteractDistance * InteractDistance;
        foreach (var player in online)
        {
            if (!player.IsInGame || player.IsDead) continue;
            var dx = villager.PositionX - player.PositionX;
            var dz = villager.PositionZ - player.PositionZ;
            if (dx * dx + dz * dz > reachSquared) continue;
            if (!player.TryConsumeInteractIntent(villager.EntityId)) continue;

            if (TryTrade(villager, player))
                villager.NextTradeTick = clock.CurrentTick + TradeCooldownTicks;
            return; // At most one interact resolved per tick — the intent itself is already one-shot.
        }
    }

    /// <summary>All-or-nothing exchange: the player's payment is refunded if the villager's ware can't fit their inventory.</summary>
    private bool TryTrade(Villager villager, Player.Player player)
    {
        if (!player.Inventory.TryConsume(villager.RequestedWare, 1)) return false;

        var ware = villager.Wares[0];
        if (!player.Inventory.TryAdd(ware, 1))
        {
            player.Inventory.TryAdd(villager.RequestedWare, 1); // Refund — the slot this just vacated can always take it back.
            return false;
        }

        player.Session.Protocol.Inventory.SendInventoryContent(player.Inventory);
        player.Session.Context.World.PersistInventory(player);
        TradeCount++;
        return true;
    }

    /// <summary>No loot, no XP, no HealthState involved — a pure lifecycle removal, not a death.</summary>
    private bool TryDespawn(Villager villager, GameClock clock, IReadOnlyList<Player.Player> online)
    {
        var (shouldDespawn, lastSeen) = DespawnLifecycle.EvaluateDespawn(
            villager.PositionX, villager.PositionZ, online, DespawnRadius, clock.CurrentTick, villager.LastSeenNearPlayerTick);
        villager.LastSeenNearPlayerTick = lastSeen;
        if (!shouldDespawn) return false;

        foreach (var peer in online)
            if (_replicated.Remove((villager.EntityId, peer.RuntimeId)))
            {
                _lastProjected.Remove((villager.EntityId, peer.RuntimeId));
                peer.Session.Protocol.Entity.SendRemoveActor(villager.EntityId);
                ReplicatedRemovalCount++;
            }
        villager.Remove();
        _villagers.Remove(villager);
        DespawnCount++;
        return true;
    }

    private void ReconcileViewers(Villager villager, IReadOnlyList<Player.Player> online) =>
        ViewerReconciliation.Sync(
            villager.EntityId, online, _replicated,
            peer => ActorInterest.Includes(peer, villager.PositionX, villager.PositionZ),
            onEnter: peer =>
            {
                peer.Session.Protocol.Entity.SendAddVillager(
                    villager.EntityId, villager.RuntimeId, villager.PositionX, villager.PositionY, villager.PositionZ, villager.Yaw);
                peer.Session.Protocol.Entity.SendHealth(villager.RuntimeId, villager.Health.Current, villager.Health.Maximum);
                _lastProjected[(villager.EntityId, peer.RuntimeId)] = new ProjectedPose(villager.PositionX, villager.PositionY, villager.PositionZ, villager.Yaw);
                ReplicatedSpawnCount++;
            },
            onExit: peer =>
            {
                _lastProjected.Remove((villager.EntityId, peer.RuntimeId));
                peer.Session.Protocol.Entity.SendRemoveActor(villager.EntityId);
                ReplicatedRemovalCount++;
            });

    private void ApplyPlayerAttacks(Villager villager, IReadOnlyList<Player.Player> online)
    {
        const float attackDistance = 2.25f;
        GroundMobCombat.ApplyPlayerMeleeAttacks(villager, online, attackDistance, AttackDamage, _currentTick, TryApplyDamage);
    }

    /// <summary>Concrete Villager health/removal operation — same shape as every other ground mob's, no retaliation.</summary>
    public bool TryApplyDamage(Villager villager, DamageSource source, float amount, IReadOnlyList<Player.Player> online, ulong currentTick)
    {
        // Phase XXIII-B — vanilla-parity Golem aggro trigger (see Player.LastVillagerAttack's doc
        // comment): recorded regardless of whether the hit is ultimately accepted below, since a
        // player swinging at a villager is provocative even if e.g. loot capacity later refuses it.
        if (source.OwnerRuntimeId is { } attackerId)
        {
            var attacker = _players.GetByRuntimeId(attackerId);
            if (attacker is not null)
                attacker.LastVillagerAttack = (_currentTick, villager.PositionX, villager.PositionZ);
        }

        return GroundMobCombat.TryApplyDamage(
            villager, source, amount, online, _world, _players, _replicated, _lootItem, KillExperience, "Villager", currentTick,
            removeFromStore: _villagers.Remove,
            onDeathReplicatedToPeer: peer =>
            {
                _lastProjected.Remove((villager.EntityId, peer.RuntimeId));
                ReplicatedRemovalCount++;
            });
    }

    /// <summary>Peaceful AI: pick a random heading periodically, walk it, retry sooner if blocked. Same shape as Cow's wander — see phase findings.</summary>
    private void Wander(Villager villager, GameClock clock)
    {
        if (clock.CurrentTick >= villager.WanderChangeAtTick)
            PickNewHeading(villager, clock);

        var desiredX = villager.PositionX + villager.WanderDirectionX * MovePerTick;
        var desiredZ = villager.PositionZ + villager.WanderDirectionZ * MovePerTick;
        if (!TryMove(villager, desiredX, desiredZ))
        {
            villager.WanderChangeAtTick = clock.CurrentTick; // blocked — choose a fresh heading next tick
            return;
        }
        villager.Yaw = LookMath.MoveYawTowards(villager.Yaw, LookMath.YawTowards(villager.WanderDirectionX, villager.WanderDirectionZ), LookMath.DefaultMaxTurnDegreesPerTick);
    }

    private void PickNewHeading(Villager villager, GameClock clock)
    {
        var angle = _random.NextSingle() * MathF.PI * 2f;
        villager.WanderDirectionX = MathF.Cos(angle);
        villager.WanderDirectionZ = MathF.Sin(angle);
        villager.WanderChangeAtTick = clock.CurrentTick + (ulong)_random.Next(MinWanderTicks, MaxWanderTicks);
    }

    /// <summary>Concrete Villager rule: where to step. Validity itself is shared (<see cref="GroundMobMovement"/>).</summary>
    private bool TryMove(Villager villager, float x, float z)
    {
        if (!GroundMobMovement.TryMoveHorizontal(_world, villager.PositionX, villager.PositionY, villager.PositionZ, x, z, out var resolvedY)) return false;
        villager.PositionX = x;
        villager.PositionY = resolvedY;
        villager.PositionZ = z;
        return true;
    }

    /// <summary>
    /// Phase XXIX: one tick of gravity/falling/landing. Villager has no ECS store to take a real
    /// <c>ref</c> into, so <paramref name="villager"/>'s fields round-trip through locals — always
    /// written back regardless of <see cref="GroundMobMovement.ResolveVertical"/>'s return value,
    /// since "already grounded" still resets the fall-speed field to zero.
    /// </summary>
    private void ApplyGravity(Villager villager)
    {
        var y = villager.PositionY;
        var fallSpeed = villager.VerticalFallSpeed;
        GroundMobMovement.ResolveVertical(_world, villager.PositionX, villager.PositionZ, ref y, ref fallSpeed, out _);
        villager.PositionY = y;
        villager.VerticalFallSpeed = fallSpeed;
    }

    private void ReplicateMoves(IReadOnlyList<Player.Player> online)
    {
        foreach (var peer in online)
        {
            _moveBatch.Clear();
            foreach (var villager in _villagers.Active)
            {
                var key = (villager.EntityId, peer.RuntimeId);
                if (!_replicated.Contains(key)) continue;
                var current = new ProjectedPose(villager.PositionX, villager.PositionY, villager.PositionZ, villager.Yaw);
                if (_lastProjected.TryGetValue(key, out var previous) && !current.MeaningfullyChanged(previous))
                {
                    ReplicatedMoveSkippedCount++;
                    continue;
                }
                _moveBatch.Add(new RawActorPose
                {
                    ActorRuntimeId = villager.RuntimeId,
                    X = villager.PositionX,
                    Y = villager.PositionY,
                    Z = villager.PositionZ,
                    Yaw = villager.Yaw,
                    HeadYaw = villager.Yaw
                });
                _lastProjected[key] = current;
                ReplicatedMoveCount++;
            }
            peer.Session.Protocol.Entity.SendMoveActorAbsoluteRaws(_moveBatch);
        }
    }
}
