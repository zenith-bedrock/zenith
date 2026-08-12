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
            AdvanceTowardNearestPlayer(zombie, online);
            TryAttackPlayer(zombie, clock, online);
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
                if (_replicated.Remove(key)) peer.Session.Protocol.Entity.SendRemoveActor(zombie.EntityId);
                continue;
            }
            if (!_replicated.Add(key)) continue;
            peer.Session.Protocol.Entity.SendAddZombie(
                zombie.EntityId, zombie.RuntimeId, zombie.PositionX, zombie.PositionY, zombie.PositionZ, zombie.Yaw);
            peer.Session.Protocol.Entity.SendHealth(zombie.RuntimeId, zombie.Health.Current, zombie.Health.Maximum);
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

    private void TryAttackPlayer(Zombie zombie, GameClock clock, IReadOnlyList<Player.Player> online)
    {
        if (_nextAttackTick.GetValueOrDefault(zombie.EntityId) > clock.CurrentTick) return;
        var target = online.FirstOrDefault(player => player.IsInGame && !player.IsDead &&
            (player.PositionX - zombie.PositionX) * (player.PositionX - zombie.PositionX) +
            (player.PositionZ - zombie.PositionZ) * (player.PositionZ - zombie.PositionZ) <= AttackDistance * AttackDistance);
        if (target is null) return;
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
            if (_replicated.Remove((zombie.EntityId, peer.RuntimeId))) peer.Session.Protocol.Entity.SendRemoveActor(zombie.EntityId);
        zombie.Remove();
        _zombies.Remove(zombie);
        return true;
    }

    private bool CanDropLoot(Zombie zombie) =>
        FloorDropFanout.CanDeposit(
            _world,
            (int)MathF.Floor(zombie.PositionX), (int)MathF.Floor(zombie.PositionY), (int)MathF.Floor(zombie.PositionZ),
            _lootItem, 1);

    private static void AdvanceTowardNearestPlayer(Zombie zombie, IReadOnlyList<Player.Player> online)
    {
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
        if (target is null || best <= AttackDistance * AttackDistance || best <= 0.0001f) return;
        var length = MathF.Sqrt(best);
        var dxn = (target.PositionX - zombie.PositionX) / length;
        var dzn = (target.PositionZ - zombie.PositionZ) / length;
        zombie.PositionX += dxn * MathF.Min(MovePerTick, length - AttackDistance);
        zombie.PositionZ += dzn * MathF.Min(MovePerTick, length - AttackDistance);
        zombie.Yaw = MathF.Atan2(-dxn, dzn) * (180f / MathF.PI);
    }

    private void ReplicateMove(Zombie zombie, IReadOnlyList<Player.Player> online)
    {
        foreach (var peer in online)
        {
            if (!_replicated.Contains((zombie.EntityId, peer.RuntimeId))) continue;
            peer.Session.Protocol.Entity.SendMoveActorAbsoluteRaw(
                zombie.RuntimeId, zombie.PositionX, zombie.PositionY, zombie.PositionZ);
        }
    }
}
