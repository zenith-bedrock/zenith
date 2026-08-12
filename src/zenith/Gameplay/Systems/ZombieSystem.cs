using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.Systems;

/// <summary>First living-actor vertical slice; deliberately one concrete behavior.</summary>
sealed class ZombieSystem : IGameSystem
{
    private const float SpawnDistance = 4f;
    private const float DetectionDistance = 24f;
    private const float AttackDistance = 2.25f;
    private const float MovePerTick = 0.08f;
    private const float AttackDamage = 4f;
    private const int AttackCooldownTicks = 20;
    private const string LootItemName = "minecraft:rotten_flesh";

    private readonly World.World _world;
    private readonly PlayerManager _players;
    private readonly ZombieStore _zombies;
    private readonly StackId _lootItem;
    private readonly HashSet<(long ZombieId, long PlayerId)> _replicated = new();
    private bool _bootstrapSpawned;
    private readonly Dictionary<long, ulong> _nextAttackTick = [];

    public ZombieSystem(World.World world, PlayerManager players, ZombieStore zombies, ItemPalette itemPalette)
    {
        _world = world;
        _players = players;
        _zombies = zombies;
        _lootItem = StackId.FromItem(itemPalette.Require(LootItemName));
    }

    public ZombieStore Zombies => _zombies;
    internal long ReplicatedSpawnCount { get; private set; }
    internal long ReplicatedMoveCount { get; private set; }
    internal long ReplicatedRemovalCount { get; private set; }

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        _ = clock;
        if (online.Count == 0) return;
        if (_zombies.Active.Count != 0)
            _bootstrapSpawned = true;
        EnsureBootstrapZombie(online);
        foreach (var zombie in _zombies.Active.ToArray())
        {
            if (!zombie.IsActive) continue;
            ReconcileViewers(zombie, online);
            ApplyPlayerAttacks(zombie, online);
            if (!zombie.IsActive) continue;
            var target = FindOrAcquireTarget(zombie, online);
            if (target is not null)
                AdvanceTowardTarget(zombie, target);
            TryAttackPlayer(zombie, target, clock, online);
            ReconcileViewers(zombie, online);
            ReplicateMove(zombie, online);
        }
        _replicated.RemoveWhere(pair => !_zombies.Active.Any(z => z.EntityId == pair.ZombieId) ||
                                        !online.Any(p => p.RuntimeId == pair.PlayerId));
    }

    private void EnsureBootstrapZombie(IReadOnlyList<Player.Player> online)
    {
        if (_bootstrapSpawned || _zombies.Active.Count != 0) return;
        var player = online.FirstOrDefault(p => p.IsInGame && !p.IsDead);
        if (player is null) return;
        var x = player.PositionX + SpawnDistance;
        var z = player.PositionZ;
        var y = _world.SampleSpawnFeetY((int)MathF.Floor(x), (int)MathF.Floor(z));
        var entityId = _players.AllocateRuntimeId();
        if (_zombies.TryAdd(new Zombie(entityId, (ulong)entityId, x, y, z)))
            _bootstrapSpawned = true;
    }

    private void ReconcileViewers(Zombie zombie, IReadOnlyList<Player.Player> online)
    {
        foreach (var peer in online)
        {
            var key = (zombie.EntityId, peer.RuntimeId);
            if (!ActorInterest.Includes(peer, zombie.PositionX, zombie.PositionZ))
            {
                if (_replicated.Remove(key))
                {
                    peer.Session.Protocol.Entity.SendRemoveActor(zombie.EntityId);
                    ReplicatedRemovalCount++;
                }
                continue;
            }
            if (!_replicated.Add(key)) continue;
            peer.Session.Protocol.Entity.SendAddZombie(
                zombie.EntityId, zombie.RuntimeId, zombie.PositionX, zombie.PositionY, zombie.PositionZ, zombie.Yaw);
            peer.Session.Protocol.Entity.SendHealth(zombie.RuntimeId, zombie.Health.Current, zombie.Health.Maximum);
            ReplicatedSpawnCount++;
        }
    }

    private void ApplyPlayerAttacks(Zombie zombie, IReadOnlyList<Player.Player> online)
    {
        foreach (var player in online)
        {
            if (!player.IsInGame || player.IsDead) continue;
            var dx = zombie.PositionX - player.PositionX;
            var dz = zombie.PositionZ - player.PositionZ;
            if (dx * dx + dz * dz > AttackDistance * AttackDistance) continue;
            if (!player.TryConsumeAttackIntent()) continue;
            if (TryApplyDamage(zombie, DamageSource.Melee, AttackDamage, online)) break;
        }
    }

    private void TryAttackPlayer(
        Zombie zombie,
        Player.Player? target,
        GameClock clock,
        IReadOnlyList<Player.Player> online)
    {
        if (_nextAttackTick.GetValueOrDefault(zombie.EntityId) > clock.CurrentTick) return;
        if (target is null) return;
        if (!IsTargetValid(zombie, target, out var distanceSquared) || distanceSquared > AttackDistance * AttackDistance)
            return;
        if (PlayerDamage.Apply(target, _players, online, DamageSource.MeleeFrom(zombie.EntityId), AttackDamage))
            _nextAttackTick[zombie.EntityId] = clock.CurrentTick + AttackCooldownTicks;
    }

    /// <summary>Concrete Zombie health/removal operation shared by the Projectile vertical slice.</summary>
    public bool TryApplyDamage(Zombie zombie, DamageSource source, float amount, IReadOnlyList<Player.Player> online)
    {
        if (!zombie.IsActive) return false;
        if (zombie.Health.Current <= amount && !CanDropLoot(zombie)) return false;
        var result = zombie.ApplyDamage(source, amount);
        if (!result.WasApplied) return false;
        foreach (var peer in online)
        {
            if (!_replicated.Contains((zombie.EntityId, peer.RuntimeId))) continue;
            peer.Session.Protocol.Entity.SendHealth(zombie.RuntimeId, zombie.Health.Current, zombie.Health.Maximum);
        }
        if (!result.CausedDeath) return true;
        if (!FloorDropFanout.TryDeposit(
                _world, _players, online,
                (int)MathF.Floor(zombie.PositionX), (int)MathF.Floor(zombie.PositionY), (int)MathF.Floor(zombie.PositionZ),
                _lootItem, 1))
            throw new InvalidOperationException("A prevalidated Zombie loot drop could not commit.");
        foreach (var peer in online)
            if (_replicated.Remove((zombie.EntityId, peer.RuntimeId)))
            {
                peer.Session.Protocol.Entity.SendRemoveActor(zombie.EntityId);
                ReplicatedRemovalCount++;
            }
        zombie.Remove();
        _zombies.Remove(zombie);
        return true;
    }

    private bool CanDropLoot(Zombie zombie) =>
        FloorDropFanout.CanDeposit(
            _world,
            (int)MathF.Floor(zombie.PositionX), (int)MathF.Floor(zombie.PositionY), (int)MathF.Floor(zombie.PositionZ),
            _lootItem, 1);

    private Player.Player? FindOrAcquireTarget(Zombie zombie, IReadOnlyList<Player.Player> online)
    {
        if (zombie.TargetPlayerRuntimeId is { } retainedId)
        {
            var retained = online.FirstOrDefault(player => player.RuntimeId == retainedId);
            if (retained is not null && IsTargetValid(zombie, retained, out _))
                return retained;
            zombie.TargetPlayerRuntimeId = null;
        }

        Player.Player? target = null;
        var best = DetectionDistance * DetectionDistance;
        foreach (var player in online)
        {
            if (!player.IsInGame || player.IsDead) continue;
            var dx = player.PositionX - zombie.PositionX;
            var dz = player.PositionZ - zombie.PositionZ;
            var distance = dx * dx + dz * dz;
            if (distance >= best) continue;
            best = distance;
            target = player;
        }

        zombie.TargetPlayerRuntimeId = target?.RuntimeId;
        return target;
    }

    private static bool IsTargetValid(Zombie zombie, Player.Player target, out float distanceSquared)
    {
        var dx = target.PositionX - zombie.PositionX;
        var dz = target.PositionZ - zombie.PositionZ;
        distanceSquared = dx * dx + dz * dz;
        return target.IsInGame && !target.IsDead && distanceSquared <= DetectionDistance * DetectionDistance;
    }

    private void AdvanceTowardTarget(Zombie zombie, Player.Player target)
    {
        if (!IsTargetValid(zombie, target, out var best) ||
            best <= AttackDistance * AttackDistance || best <= 0.0001f)
            return;

        var length = MathF.Sqrt(best);
        var dxn = (target.PositionX - zombie.PositionX) / length;
        var dzn = (target.PositionZ - zombie.PositionZ) / length;
        var distance = MathF.Min(MovePerTick, length - AttackDistance);
        var desiredX = zombie.PositionX + dxn * distance;
        var desiredZ = zombie.PositionZ + dzn * distance;
        var sideX = -dzn * distance;
        var sideZ = dxn * distance;

        if (TryMove(zombie, desiredX, desiredZ) ||
            TryMove(zombie, desiredX + sideX, desiredZ + sideZ) ||
            TryMove(zombie, desiredX - sideX, desiredZ - sideZ) ||
            TryMove(zombie, zombie.PositionX + sideX, zombie.PositionZ + sideZ) ||
            TryMove(zombie, zombie.PositionX - sideX, zombie.PositionZ - sideZ))
        {
            zombie.Yaw = MathF.Atan2(-dxn, dzn) * (180f / MathF.PI);
        }
    }

    /// <summary>
    /// Bounded local movement probe: two empty body cells and one supporting cell. This is a
    /// concrete Zombie rule, not a reusable navigation or collision framework.
    /// </summary>
    private bool TryMove(Zombie zombie, float x, float z)
    {
        var blockX = (int)MathF.Floor(x);
        var blockY = (int)MathF.Floor(zombie.PositionY);
        var blockZ = (int)MathF.Floor(z);
        if (_world.GetBlock(blockX, blockY, blockZ) != World.World.AirRuntimeId ||
            _world.GetBlock(blockX, blockY + 1, blockZ) != World.World.AirRuntimeId ||
            _world.GetBlock(blockX, blockY - 1, blockZ) == World.World.AirRuntimeId)
            return false;

        zombie.PositionX = x;
        zombie.PositionZ = z;
        return true;
    }

    private void ReplicateMove(Zombie zombie, IReadOnlyList<Player.Player> online)
    {
        foreach (var peer in online)
        {
            if (!_replicated.Contains((zombie.EntityId, peer.RuntimeId))) continue;
            peer.Session.Protocol.Entity.SendMoveActorAbsoluteRaw(
                zombie.RuntimeId, zombie.PositionX, zombie.PositionY, zombie.PositionZ);
            ReplicatedMoveCount++;
        }
    }
}
