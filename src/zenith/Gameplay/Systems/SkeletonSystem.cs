using Zenith.Gameplay.Runtime;
using Zenith.Player;

namespace Zenith.Gameplay.Systems;

/// <summary>Concrete ranged mob: holds range and asks ProjectileSystem to own each shot.</summary>
sealed class SkeletonSystem : IGameSystem
{
    private const float SpawnDistance = 10f;
    private const float DetectionDistance = 20f;
    private const float MinimumRange = 5f;
    private const float ShotSpeed = 0.45f;
    private const int ShotCooldownTicks = 30;
    private readonly PlayerManager _players;
    private readonly SkeletonStore _skeletons;
    private readonly ProjectileSystem _projectiles;
    private readonly HashSet<(long SkeletonId, long PlayerId)> _replicated = [];
    private readonly Dictionary<long, ulong> _nextShot = [];
    private bool _bootstrapSpawned;

    public SkeletonSystem(PlayerManager players, SkeletonStore skeletons, ProjectileSystem projectiles)
    { _players = players; _skeletons = skeletons; _projectiles = projectiles; }

    public SkeletonStore Skeletons => _skeletons;

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        if (!_bootstrapSpawned)
        {
            var player = online.FirstOrDefault(p => p.IsInGame && !p.IsDead);
            if (player is not null)
            {
                var id = _players.AllocateRuntimeId();
                _bootstrapSpawned = _skeletons.TryAdd(new Skeleton(id, (ulong)id, player.PositionX + SpawnDistance, player.PositionY, player.PositionZ));
            }
        }
        foreach (var skeleton in _skeletons.Active)
        {
            var target = FindTarget(skeleton, online);
            Reconcile(skeleton, online);
            if (target is null || _nextShot.GetValueOrDefault(skeleton.EntityId) > clock.CurrentTick) continue;
            var dx = target.PositionX - skeleton.PositionX; var dz = target.PositionZ - skeleton.PositionZ;
            var distance = MathF.Sqrt(dx * dx + dz * dz);
            if (distance < MinimumRange || distance < 0.001f) continue;
            skeleton.Yaw = MathF.Atan2(-dx, dz) * (180f / MathF.PI);
            if (_projectiles.TrySpawnFromActor(skeleton.EntityId, skeleton.PositionX, skeleton.PositionY + 1.2f, skeleton.PositionZ,
                dx / distance * ShotSpeed, 0.08f, dz / distance * ShotSpeed, online))
                _nextShot[skeleton.EntityId] = clock.CurrentTick + ShotCooldownTicks;
        }
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
            if (!ActorInterest.Includes(peer, skeleton.PositionX, skeleton.PositionZ)) { if (_replicated.Remove(key)) peer.Session.Protocol.Entity.SendRemoveActor(skeleton.EntityId); continue; }
            if (_replicated.Add(key)) peer.Session.Protocol.Entity.SendAddSkeleton(skeleton.EntityId, skeleton.RuntimeId, skeleton.PositionX, skeleton.PositionY, skeleton.PositionZ, skeleton.Yaw);
        }
    }
}
