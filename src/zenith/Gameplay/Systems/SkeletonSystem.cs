using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.Systems;

/// <summary>Concrete ranged mob: owns range and loot decisions, while ProjectileSystem owns each shot.</summary>
sealed class SkeletonSystem : IGameSystem
{
    private const float SpawnDistance = 10f;
    private const float DetectionDistance = 20f;
    private const float MinimumRange = 5f;
    private const float AttackDistance = 2.25f;
    private const float AttackDamage = 4f;
    private const float ShotSpeed = 0.45f;
    private const int ShotCooldownTicks = 30;
    private const string LootItemName = "minecraft:bone";

    private readonly World.World _world;
    private readonly PlayerManager _players;
    private readonly SkeletonStore _skeletons;
    private readonly ProjectileSystem _projectiles;
    private readonly StackId _lootItem;
    private readonly HashSet<(long SkeletonId, long PlayerId)> _replicated = [];
    private readonly Dictionary<long, ulong> _nextShot = [];
    private bool _bootstrapSpawned;

    public SkeletonSystem(World.World world, PlayerManager players, SkeletonStore skeletons, ProjectileSystem projectiles, ItemPalette itemPalette)
    {
        _world = world;
        _players = players;
        _skeletons = skeletons;
        _projectiles = projectiles;
        _lootItem = StackId.FromItem(itemPalette.Require(LootItemName));
    }

    public SkeletonStore Skeletons => _skeletons;

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        if (_skeletons.Active.Count != 0)
            _bootstrapSpawned = true;
        if (!_bootstrapSpawned)
        {
            var player = online.FirstOrDefault(p => p.IsInGame && !p.IsDead);
            if (player is not null)
            {
                var id = _players.AllocateRuntimeId();
                _bootstrapSpawned = _skeletons.TryAdd(new Skeleton(id, (ulong)id, player.PositionX + SpawnDistance, player.PositionY, player.PositionZ));
            }
        }

        foreach (var skeleton in _skeletons.Active.ToArray())
        {
            ApplyPlayerAttacks(skeleton, online);
            if (!skeleton.IsActive) continue;

            var target = FindTarget(skeleton, online);
            Reconcile(skeleton, online);
            if (target is null || _nextShot.GetValueOrDefault(skeleton.EntityId) > clock.CurrentTick) continue;
            var dx = target.PositionX - skeleton.PositionX;
            var dz = target.PositionZ - skeleton.PositionZ;
            var distance = MathF.Sqrt(dx * dx + dz * dz);
            if (distance < MinimumRange || distance < 0.001f) continue;
            skeleton.Yaw = MathF.Atan2(-dx, dz) * (180f / MathF.PI);
            if (_projectiles.TrySpawnFromActor(skeleton.EntityId, skeleton.PositionX, skeleton.PositionY + 1.2f, skeleton.PositionZ,
                dx / distance * ShotSpeed, 0.08f, dz / distance * ShotSpeed, online))
                _nextShot[skeleton.EntityId] = clock.CurrentTick + ShotCooldownTicks;
        }

        _replicated.RemoveWhere(pair => !_skeletons.Active.Any(s => s.EntityId == pair.SkeletonId) ||
                                        !online.Any(p => p.RuntimeId == pair.PlayerId));
    }

    private static Player.Player? FindTarget(Skeleton skeleton, IReadOnlyList<Player.Player> online) =>
        online.Where(p => p.IsInGame && !p.IsDead)
            .OrderBy(p => (p.PositionX - skeleton.PositionX) * (p.PositionX - skeleton.PositionX) + (p.PositionZ - skeleton.PositionZ) * (p.PositionZ - skeleton.PositionZ))
            .FirstOrDefault(p => (p.PositionX - skeleton.PositionX) * (p.PositionX - skeleton.PositionX) + (p.PositionZ - skeleton.PositionZ) * (p.PositionZ - skeleton.PositionZ) <= DetectionDistance * DetectionDistance);

    private void Reconcile(Skeleton skeleton, IReadOnlyList<Player.Player> online)
    {
        foreach (var peer in online)
        {
            var key = (skeleton.EntityId, peer.RuntimeId);
            if (!ActorInterest.Includes(peer, skeleton.PositionX, skeleton.PositionZ))
            {
                if (_replicated.Remove(key)) peer.Session.Protocol.Entity.SendRemoveActor(skeleton.EntityId);
                continue;
            }
            if (!_replicated.Add(key)) continue;
            peer.Session.Protocol.Entity.SendAddSkeleton(skeleton.EntityId, skeleton.RuntimeId, skeleton.PositionX, skeleton.PositionY, skeleton.PositionZ, skeleton.Yaw);
            peer.Session.Protocol.Entity.SendHealth(skeleton.RuntimeId, skeleton.Health.Current, skeleton.Health.Maximum);
        }
    }

    private void ApplyPlayerAttacks(Skeleton skeleton, IReadOnlyList<Player.Player> online)
    {
        foreach (var player in online)
        {
            if (!player.IsInGame || player.IsDead) continue;
            var dx = skeleton.PositionX - player.PositionX;
            var dz = skeleton.PositionZ - player.PositionZ;
            if (dx * dx + dz * dz > AttackDistance * AttackDistance) continue;
            if (!player.TryConsumeAttackIntent()) continue;
            if (TryApplyDamage(skeleton, DamageSource.Melee, AttackDamage, online)) break;
        }
    }

    internal bool TryApplyDamage(Skeleton skeleton, DamageSource source, float amount, IReadOnlyList<Player.Player> online)
    {
        if (!skeleton.IsActive) return false;
        if (skeleton.Health.Current <= amount && !CanDropLoot(skeleton)) return false;
        var result = skeleton.ApplyDamage(source, amount);
        if (!result.WasApplied) return false;

        foreach (var peer in online)
            if (_replicated.Contains((skeleton.EntityId, peer.RuntimeId)))
                peer.Session.Protocol.Entity.SendHealth(skeleton.RuntimeId, skeleton.Health.Current, skeleton.Health.Maximum);
        if (!result.CausedDeath) return true;

        if (!FloorDropFanout.TryDeposit(
                _world, _players, online,
                (int)MathF.Floor(skeleton.PositionX), (int)MathF.Floor(skeleton.PositionY), (int)MathF.Floor(skeleton.PositionZ),
                _lootItem, 1))
            throw new InvalidOperationException("A prevalidated Skeleton loot drop could not commit.");

        foreach (var peer in online)
            if (_replicated.Remove((skeleton.EntityId, peer.RuntimeId))) peer.Session.Protocol.Entity.SendRemoveActor(skeleton.EntityId);
        skeleton.Remove();
        _skeletons.Remove(skeleton);
        return true;
    }

    private bool CanDropLoot(Skeleton skeleton) =>
        FloorDropFanout.CanDeposit(
            _world,
            (int)MathF.Floor(skeleton.PositionX), (int)MathF.Floor(skeleton.PositionY), (int)MathF.Floor(skeleton.PositionZ),
            _lootItem, 1);
}
