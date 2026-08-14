using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.Protocol;
using Zenith.World;

namespace Zenith.Gameplay.Systems;

/// <summary>
/// Phase XVII, Priority 1 — the first non-ground-navigation mob. Combat/loot/XP bookkeeping is
/// shared via <see cref="GroundMobCombat"/>, same as every other ground mob: that contract turned
/// out to be movement-agnostic despite its name (identity + position + health + damage + removal,
/// nothing about walking). Movement itself is bespoke 3D flight, deliberately NOT
/// <see cref="GroundMobMovement"/> — a bat has no supporting-block requirement and wanders on a
/// vertical axis no ground mob uses. Kept entirely local since there is only one flying mob so
/// far; see docs/history/phases/phase-xvii-runtime-pressure-findings.md for the resulting evidence.
/// </summary>
sealed class BatSystem : IGameSystem
{
    private const float SpawnDistance = 5f;
    private const float SpawnHeightOffset = 3f;
    private const float MovePerTick = 0.1f;
    private const int MinWanderTicks = 30;
    private const int MaxWanderTicks = 80;
    private const float WanderVerticalBias = 0.5f; // Keeps the random heading from drifting mostly sideways.
    private const string LootItemName = "minecraft:feather";
    private const int KillExperience = 1;
    private const float DespawnRadius = 64f;

    private readonly World.World _world;
    private readonly PlayerManager _players;
    private readonly BatStore _bats;
    private readonly StackId _lootItem;
    private readonly Random _random;
    private readonly HashSet<(long BatId, long PlayerId)> _replicated = new();
    private bool _bootstrapSpawned;

    public BatSystem(World.World world, PlayerManager players, BatStore bats, ItemPalette itemPalette, Random? random = null)
    {
        _world = world;
        _players = players;
        _bats = bats;
        _lootItem = StackId.FromItem(itemPalette.Require(LootItemName));
        _random = random ?? new Random();
    }

    public BatStore Bats => _bats;
    internal long ReplicatedSpawnCount { get; private set; }
    internal long ReplicatedRemovalCount { get; private set; }
    internal long DespawnCount { get; private set; }

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        if (online.Count == 0) return;
        if (_bats.Active.Count != 0)
            _bootstrapSpawned = true;
        EnsureBootstrapBat(online);

        foreach (var bat in _bats.Active.ToArray())
        {
            if (!bat.IsActive) continue;
            if (TryDespawn(bat, clock, online)) continue;
            ReconcileViewers(bat, online);
            ApplyPlayerAttacks(bat, online, clock.CurrentTick);
            if (!bat.IsActive) continue;
            Wander(bat, clock, online);
            ReconcileViewers(bat, online);
        }

        _replicated.RemoveWhere(pair => !_bats.Active.Any(b => b.EntityId == pair.BatId) ||
                                        !online.Any(p => p.RuntimeId == pair.PlayerId));
    }

    private void EnsureBootstrapBat(IReadOnlyList<Player.Player> online)
    {
        if (_bootstrapSpawned || _bats.Active.Count != 0) return;
        var player = online.FirstOrDefault(p => p.IsInGame && !p.IsDead);
        if (player is null) return;
        var x = player.PositionX;
        var z = player.PositionZ + SpawnDistance;
        var y = _world.SampleSpawnFeetY((int)MathF.Floor(x), (int)MathF.Floor(z)) + SpawnHeightOffset;
        var entityId = _players.AllocateRuntimeId();
        if (_bats.TryAdd(new Bat(entityId, (ulong)entityId, x, y, z)))
            _bootstrapSpawned = true;
    }

    /// <summary>No loot, no XP, no HealthState involved — a pure lifecycle removal, not a death. Same shape as every other ground mob's despawn — the despawn rule turned out position-mode-agnostic too.</summary>
    private bool TryDespawn(Bat bat, GameClock clock, IReadOnlyList<Player.Player> online)
    {
        var (shouldDespawn, lastSeen) = DespawnLifecycle.EvaluateDespawn(
            bat.PositionX, bat.PositionZ, online, DespawnRadius, clock.CurrentTick, bat.LastSeenNearPlayerTick);
        bat.LastSeenNearPlayerTick = lastSeen;
        if (!shouldDespawn) return false;

        foreach (var peer in online)
            if (_replicated.Remove((bat.EntityId, peer.RuntimeId)))
                peer.Session.Protocol.Entity.SendRemoveActor(bat.EntityId);
        bat.Remove();
        _bats.Remove(bat);
        DespawnCount++;
        return true;
    }

    private void ReconcileViewers(Bat bat, IReadOnlyList<Player.Player> online) =>
        ViewerReconciliation.Sync(
            bat.EntityId, online, _replicated,
            peer => ActorInterest.Includes(peer, bat.PositionX, bat.PositionZ),
            onEnter: peer =>
            {
                peer.Session.Protocol.Entity.SendAddBat(
                    bat.EntityId, bat.RuntimeId, bat.PositionX, bat.PositionY, bat.PositionZ, bat.Yaw);
                peer.Session.Protocol.Entity.SendHealth(bat.RuntimeId, bat.Health.Current, bat.Health.Maximum);
                ReplicatedSpawnCount++;
            },
            onExit: peer =>
            {
                peer.Session.Protocol.Entity.SendRemoveActor(bat.EntityId);
                ReplicatedRemovalCount++;
            });

    private void ApplyPlayerAttacks(Bat bat, IReadOnlyList<Player.Player> online, ulong currentTick)
    {
        const float attackDistance = 2.25f;
        GroundMobCombat.ApplyPlayerMeleeAttacks(bat, online, attackDistance, 4f, currentTick, TryApplyDamage);
    }

    /// <summary>Concrete Bat health/removal operation — same shape as every other ground mob's, one loot item, no retaliation.</summary>
    public bool TryApplyDamage(Bat bat, DamageSource source, float amount, IReadOnlyList<Player.Player> online, ulong currentTick) =>
        GroundMobCombat.TryApplyDamage(
            bat, source, amount, online, _world, _players, _replicated, _lootItem, KillExperience, "Bat", currentTick,
            removeFromStore: _bats.Remove,
            onDeathReplicatedToPeer: _ => ReplicatedRemovalCount++);

    /// <summary>
    /// Bespoke 3D wander: picks a random heading including a vertical component, walks it, retries
    /// sooner if blocked. The one real behavioral difference from every ground mob's wander is the
    /// Y axis being live, not fixed to a sampled ground height.
    /// </summary>
    private void Wander(Bat bat, GameClock clock, IReadOnlyList<Player.Player> online)
    {
        if (clock.CurrentTick >= bat.WanderChangeAtTick)
            PickNewHeading(bat, clock);

        var desiredX = bat.PositionX + bat.WanderDirectionX * MovePerTick;
        var desiredY = bat.PositionY + bat.WanderDirectionY * MovePerTick;
        var desiredZ = bat.PositionZ + bat.WanderDirectionZ * MovePerTick;
        if (TryFly(bat, desiredX, desiredY, desiredZ))
        {
            bat.Yaw = LookMath.MoveYawTowards(bat.Yaw, LookMath.YawTowards(bat.WanderDirectionX, bat.WanderDirectionZ), LookMath.DefaultMaxTurnDegreesPerTick);
            BroadcastMove(bat, online);
        }
        else
            bat.WanderChangeAtTick = clock.CurrentTick; // blocked — choose a fresh heading next tick
    }

    /// <summary>
    /// No batching/move-skip optimization here (unlike Cow/Zombie's <c>ReplicateMoves</c>) — one
    /// flying mob doesn't yet justify that machinery; see the phase findings for why this was left
    /// as the simplest thing that replicates correctly rather than copying the batched shape.
    /// </summary>
    private void BroadcastMove(Bat bat, IReadOnlyList<Player.Player> online)
    {
        foreach (var peer in online)
        {
            if (!_replicated.Contains((bat.EntityId, peer.RuntimeId))) continue;
            peer.Session.Protocol.Entity.SendMoveActorAbsoluteRaw(bat.RuntimeId, bat.PositionX, bat.PositionY, bat.PositionZ, yaw: bat.Yaw, headYaw: bat.Yaw);
        }
    }

    private void PickNewHeading(Bat bat, GameClock clock)
    {
        var yaw = _random.NextSingle() * MathF.PI * 2f;
        var horizontal = MathF.Cos(_random.NextSingle() * MathF.PI * WanderVerticalBias);
        bat.WanderDirectionX = MathF.Cos(yaw) * horizontal;
        bat.WanderDirectionZ = MathF.Sin(yaw) * horizontal;
        bat.WanderDirectionY = (_random.NextSingle() * 2f - 1f) * (1f - horizontal);
        bat.WanderChangeAtTick = clock.CurrentTick + (ulong)_random.Next(MinWanderTicks, MaxWanderTicks);
    }

    /// <summary>
    /// Flight-validity probe, kept local and bespoke rather than reused from
    /// <see cref="GroundMobMovement"/>: a flying mob needs body clearance but, unlike every ground
    /// mob, no solid block underneath. This is the concrete difference the phase brief asked to be
    /// tested for.
    /// </summary>
    private bool TryFly(Bat bat, float x, float y, float z)
    {
        var blockX = (int)MathF.Floor(x);
        var blockY = (int)MathF.Floor(y);
        var blockZ = (int)MathF.Floor(z);
        if (_world.GetBlock(blockX, blockY, blockZ) != World.World.AirRuntimeId ||
            _world.GetBlock(blockX, blockY + 1, blockZ) != World.World.AirRuntimeId)
            return false;

        bat.PositionX = x;
        bat.PositionY = y;
        bat.PositionZ = z;
        return true;
    }
}
