using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.Protocol;
using Zenith.World;

namespace Zenith.Gameplay.Systems;

/// <summary>
/// Phase XX, Priority 2 — second non-ground-navigation mob, and a third distinct movement-validity
/// rule (after <see cref="GroundMobMovement.CanStandAt"/> and Bat's bespoke flight clearance):
/// <see cref="TrySwim"/> requires the destination cell to BE water, not merely have air/support
/// nearby. Combat/loot/XP/despawn/replication all reuse <see cref="GroundMobCombat"/>/
/// <see cref="DespawnLifecycle"/>/<see cref="ActorInterest"/> unmodified. See the three-way
/// movement-model comparison in docs/history/phases/phase-xx-runtime-relationships-findings.md.
/// </summary>
sealed class FishSystem : IGameSystem
{
    private const float SpawnOffsetX = 4f;
    private const float MovePerTick = 0.06f;
    private const int MinWanderTicks = 30;
    private const int MaxWanderTicks = 80;
    private const string LootItemName = "minecraft:cod";
    private const int KillExperience = 1;
    private const float DespawnRadius = 64f;

    private readonly World.World _world;
    private readonly PlayerManager _players;
    private readonly FishStore _fish;
    private readonly StackId _lootItem;
    private readonly Random _random;
    private readonly HashSet<(long FishId, long PlayerId)> _replicated = new();
    private bool _bootstrapSpawned;

    public FishSystem(World.World world, PlayerManager players, FishStore fish, ItemPalette itemPalette, Random? random = null)
    {
        _world = world;
        _players = players;
        _fish = fish;
        _lootItem = StackId.FromItem(itemPalette.Require(LootItemName));
        _random = random ?? new Random();
    }

    public FishStore Fish => _fish;
    internal long ReplicatedSpawnCount { get; private set; }
    internal long ReplicatedRemovalCount { get; private set; }
    internal long DespawnCount { get; private set; }

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        if (online.Count == 0) return;
        if (_fish.Active.Count != 0)
            _bootstrapSpawned = true;
        EnsureBootstrapFish(online);

        foreach (var fish in _fish.Active.ToArray())
        {
            if (!fish.IsActive) continue;
            if (TryDespawn(fish, clock, online)) continue;
            ReconcileViewers(fish, online);
            ApplyPlayerAttacks(fish, online);
            if (!fish.IsActive) continue;
            Wander(fish, clock, online);
            ReconcileViewers(fish, online);
        }

        _replicated.RemoveWhere(pair => !_fish.Active.Any(f => f.EntityId == pair.FishId) ||
                                        !online.Any(p => p.RuntimeId == pair.PlayerId));
    }

    /// <summary>
    /// Only spawns where water already exists — a fixed offset from the first online player,
    /// checked directly rather than sampled (unlike every ground mob's <c>SampleSpawnFeetY</c>,
    /// there is no "find the nearest suitable surface" logic here; this is deliberately the
    /// smallest useful vertical slice, not a spawn-placement system). If that cell isn't water,
    /// no fish spawns this tick — acceptable for a vertical slice, not a bug.
    /// </summary>
    private void EnsureBootstrapFish(IReadOnlyList<Player.Player> online)
    {
        if (_bootstrapSpawned || _fish.Active.Count != 0) return;
        var player = online.FirstOrDefault(p => p.IsInGame && !p.IsDead);
        if (player is null) return;
        var x = player.PositionX + SpawnOffsetX;
        var y = player.PositionY;
        var z = player.PositionZ;
        var blockX = (int)MathF.Floor(x);
        var blockY = (int)MathF.Floor(y);
        var blockZ = (int)MathF.Floor(z);
        if (_world.GetBlock(blockX, blockY, blockZ) != Blocks.Water) return;

        var entityId = _players.AllocateRuntimeId();
        if (_fish.TryAdd(new Fish(entityId, (ulong)entityId, x, y, z)))
            _bootstrapSpawned = true;
    }

    /// <summary>No loot, no XP, no HealthState involved — a pure lifecycle removal, not a death. Same shape as every other ground mob's despawn — position/visibility-based, not water-based.</summary>
    private bool TryDespawn(Fish fish, GameClock clock, IReadOnlyList<Player.Player> online)
    {
        var (shouldDespawn, lastSeen) = DespawnLifecycle.EvaluateDespawn(
            fish.PositionX, fish.PositionZ, online, DespawnRadius, clock.CurrentTick, fish.LastSeenNearPlayerTick);
        fish.LastSeenNearPlayerTick = lastSeen;
        if (!shouldDespawn) return false;

        foreach (var peer in online)
            if (_replicated.Remove((fish.EntityId, peer.RuntimeId)))
                peer.Session.Protocol.Entity.SendRemoveActor(fish.EntityId);
        fish.Remove();
        _fish.Remove(fish);
        DespawnCount++;
        return true;
    }

    private void ReconcileViewers(Fish fish, IReadOnlyList<Player.Player> online) =>
        ViewerReconciliation.Sync(
            fish.EntityId, online, _replicated,
            peer => ActorInterest.Includes(peer, fish.PositionX, fish.PositionZ),
            onEnter: peer =>
            {
                peer.Session.Protocol.Entity.SendAddFish(
                    fish.EntityId, fish.RuntimeId, fish.PositionX, fish.PositionY, fish.PositionZ, fish.Yaw);
                peer.Session.Protocol.Entity.SendHealth(fish.RuntimeId, fish.Health.Current, fish.Health.Maximum);
                ReplicatedSpawnCount++;
            },
            onExit: peer =>
            {
                peer.Session.Protocol.Entity.SendRemoveActor(fish.EntityId);
                ReplicatedRemovalCount++;
            });

    private void ApplyPlayerAttacks(Fish fish, IReadOnlyList<Player.Player> online)
    {
        const float attackDistance = 2.25f;
        GroundMobCombat.ApplyPlayerMeleeAttacks(fish, online, attackDistance, 4f, TryApplyDamage);
    }

    /// <summary>Concrete Fish health/removal operation — same shape as every other ground mob's, one loot item, no retaliation.</summary>
    public bool TryApplyDamage(Fish fish, DamageSource source, float amount, IReadOnlyList<Player.Player> online) =>
        GroundMobCombat.TryApplyDamage(
            fish, source, amount, online, _world, _players, _replicated, _lootItem, KillExperience, "Fish",
            removeFromStore: _fish.Remove,
            onDeathReplicatedToPeer: _ => ReplicatedRemovalCount++);

    /// <summary>3D wander, structurally identical to Bat's — the difference is entirely in <see cref="TrySwim"/>'s validity rule, not in how a heading is picked or walked.</summary>
    private void Wander(Fish fish, GameClock clock, IReadOnlyList<Player.Player> online)
    {
        if (clock.CurrentTick >= fish.WanderChangeAtTick)
            PickNewHeading(fish, clock);

        var desiredX = fish.PositionX + fish.WanderDirectionX * MovePerTick;
        var desiredY = fish.PositionY + fish.WanderDirectionY * MovePerTick;
        var desiredZ = fish.PositionZ + fish.WanderDirectionZ * MovePerTick;
        if (TrySwim(fish, desiredX, desiredY, desiredZ))
            BroadcastMove(fish, online);
        else
            fish.WanderChangeAtTick = clock.CurrentTick; // out of water — choose a fresh heading next tick
    }

    private void BroadcastMove(Fish fish, IReadOnlyList<Player.Player> online)
    {
        foreach (var peer in online)
        {
            if (!_replicated.Contains((fish.EntityId, peer.RuntimeId))) continue;
            peer.Session.Protocol.Entity.SendMoveActorAbsoluteRaw(fish.RuntimeId, fish.PositionX, fish.PositionY, fish.PositionZ);
        }
    }

    private void PickNewHeading(Fish fish, GameClock clock)
    {
        var yaw = _random.NextSingle() * MathF.PI * 2f;
        var horizontal = MathF.Cos(_random.NextSingle() * MathF.PI * 0.5f);
        fish.WanderDirectionX = MathF.Cos(yaw) * horizontal;
        fish.WanderDirectionZ = MathF.Sin(yaw) * horizontal;
        fish.WanderDirectionY = (_random.NextSingle() * 2f - 1f) * (1f - horizontal);
        fish.WanderChangeAtTick = clock.CurrentTick + (ulong)_random.Next(MinWanderTicks, MaxWanderTicks);
        fish.Yaw = MathF.Atan2(-fish.WanderDirectionX, fish.WanderDirectionZ) * (180f / MathF.PI);
    }

    /// <summary>
    /// The third movement-validity rule (see this system's doc comment): the destination cell
    /// itself must BE water. No support requirement (unlike ground), no mere-air requirement
    /// (unlike flight) — occupancy of a specific block type is the entire rule.
    /// </summary>
    private bool TrySwim(Fish fish, float x, float y, float z)
    {
        var blockX = (int)MathF.Floor(x);
        var blockY = (int)MathF.Floor(y);
        var blockZ = (int)MathF.Floor(z);
        if (_world.GetBlock(blockX, blockY, blockZ) != Blocks.Water) return false;

        fish.PositionX = x;
        fish.PositionY = y;
        fish.PositionZ = z;
        return true;
    }
}
