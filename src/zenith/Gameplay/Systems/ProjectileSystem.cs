using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.Protocol;
using Zenith.World;

namespace Zenith.Gameplay.Systems;

/// <summary>
/// First short-lived, velocity-heavy actor. It is deliberately concrete: gameplay owns the
/// simulation and collision; Protocol only projects spawn/move/remove outcomes.
/// </summary>
sealed class ProjectileSystem : IGameSystem
{
    private const float LaunchSpeed = 0.5f;
    private const float LaunchUpwardVelocity = 0.1f;
    private const float GravityPerTick = 0.03f;
    private const float HitRadius = 0.55f;
    private const float Damage = 6f;
    private const int MaximumLifetimeTicks = 80;

    private readonly World.World _world;
    private readonly PlayerManager _players;
    private readonly ProjectileStore _projectiles;
    private readonly ZombieSystem _zombies;
    private readonly HashSet<(long ProjectileId, long PlayerId)> _replicated = [];
    private readonly Dictionary<(long ProjectileId, long PlayerId), ProjectedPosition> _lastProjected = [];
    private readonly List<RawActorPose> _moveBatch = [];

    public ProjectileSystem(World.World world, PlayerManager players, ProjectileStore projectiles, ZombieSystem zombies)
    {
        _world = world;
        _players = players;
        _projectiles = projectiles;
        _zombies = zombies;
    }

    public ProjectileStore Projectiles => _projectiles;
    internal long ReplicatedSpawnCount { get; private set; }
    internal long ReplicatedMoveCount { get; private set; }
    internal long ReplicatedMoveSkippedCount { get; private set; }
    internal long ReplicatedRemovalCount { get; private set; }
    internal long RemovalCount { get; private set; }

    /// <summary>Concrete gameplay spawn used by a ranged actor; this system remains the projectile lifecycle owner.</summary>
    public bool TrySpawnFromActor(long ownerRuntimeId, float x, float y, float z, float velocityX, float velocityY, float velocityZ,
        IReadOnlyList<Player.Player> online)
    {
        var entityId = _players.AllocateRuntimeId();
        var projectile = new Projectile(entityId, (ulong)entityId, ownerRuntimeId, x, y, z, velocityX, velocityY, velocityZ);
        if (!_projectiles.TryAdd(projectile)) return false;
        ReconcileViewers(projectile, online);
        return true;
    }

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        _ = clock;
        SpawnFromPlayerInputs(online);
        foreach (var projectile in _projectiles.Active.ToArray())
        {
            if (!projectile.IsActive) continue;
            ReconcileViewers(projectile, online);
            Advance(projectile, online);
        }

        _replicated.RemoveWhere(pair => !_projectiles.Active.Any(p => p.EntityId == pair.ProjectileId) ||
                                        !online.Any(p => p.RuntimeId == pair.PlayerId));
        foreach (var key in _lastProjected.Keys.Where(key => !_replicated.Contains(key)).ToArray())
            _lastProjected.Remove(key);
        ReplicateMoves(online);
    }

    private void SpawnFromPlayerInputs(IReadOnlyList<Player.Player> online)
    {
        foreach (var player in online)
        {
            if (!player.IsInGame || player.IsDead || !player.TryConsumeProjectileIntent()) continue;
            var radians = player.Yaw * (MathF.PI / 180f);
            var velocityX = -MathF.Sin(radians) * LaunchSpeed;
            var velocityZ = MathF.Cos(radians) * LaunchSpeed;
            _ = TrySpawnFromActor(player.RuntimeId, player.PositionX, player.PositionY + 1.2f, player.PositionZ,
                velocityX, LaunchUpwardVelocity, velocityZ, online);
        }
    }

    private void Advance(Projectile projectile, IReadOnlyList<Player.Player> online)
    {
        var nextX = projectile.PositionX + projectile.VelocityX;
        var nextY = projectile.PositionY + projectile.VelocityY;
        var nextZ = projectile.PositionZ + projectile.VelocityZ;

        var zombie = FindHitZombie(projectile, nextX, nextY, nextZ);
        if (zombie is not null)
        {
            _zombies.TryApplyDamage(zombie, DamageSource.Projectile(projectile.OwnerRuntimeId), Damage, online);
            Remove(projectile, online);
            return;
        }

        var player = FindHitPlayer(projectile, nextX, nextY, nextZ, online);
        if (player is not null)
        {
            PlayerDamage.Apply(player, _players, online, DamageSource.Projectile(projectile.OwnerRuntimeId), Damage);
            Remove(projectile, online);
            return;
        }

        if (HitsWorld(nextX, nextY, nextZ) || ++projectile.AgeTicks >= MaximumLifetimeTicks)
        {
            Remove(projectile, online);
            return;
        }

        projectile.PositionX = nextX;
        projectile.PositionY = nextY;
        projectile.PositionZ = nextZ;
        projectile.VelocityY -= GravityPerTick;
        ReconcileViewers(projectile, online);
    }

    private void ReplicateMoves(IReadOnlyList<Player.Player> online)
    {
        foreach (var peer in online)
        {
            _moveBatch.Clear();
            foreach (var projectile in _projectiles.Active)
            {
                var key = (projectile.EntityId, peer.RuntimeId);
                if (!_replicated.Contains(key)) continue;
                var current = new ProjectedPosition(projectile.PositionX, projectile.PositionY, projectile.PositionZ);
                if (_lastProjected.TryGetValue(key, out var previous) && !current.MeaningfullyChanged(previous))
                {
                    ReplicatedMoveSkippedCount++;
                    continue;
                }
                _moveBatch.Add(new RawActorPose
                {
                    ActorRuntimeId = projectile.RuntimeId,
                    X = projectile.PositionX,
                    Y = projectile.PositionY,
                    Z = projectile.PositionZ
                });
                _lastProjected[key] = current;
                ReplicatedMoveCount++;
            }
            peer.Session.Protocol.Entity.SendMoveActorAbsoluteRaws(_moveBatch);
        }
    }

    private Zombie? FindHitZombie(Projectile projectile, float x, float y, float z)
    {
        foreach (var zombie in _zombies.Zombies.Active)
        {
            if (!zombie.IsActive) continue;
            if (MathF.Abs(zombie.PositionX - x) > HitRadius || MathF.Abs(zombie.PositionZ - z) > HitRadius) continue;
            if (y < zombie.PositionY || y > zombie.PositionY + 2f) continue;
            return zombie;
        }
        return null;
    }

    private static Player.Player? FindHitPlayer(Projectile projectile, float x, float y, float z,
        IReadOnlyList<Player.Player> online)
    {
        foreach (var player in online)
        {
            // The existing player snowball slice only targets Zombies. Concrete ranged actors use
            // an id not owned by an online player, which lets their projectile affect players
            // without silently introducing player-vs-player combat in this phase.
            if (player.RuntimeId == projectile.OwnerRuntimeId) return null;
        }

        foreach (var player in online)
        {
            if (!player.IsInGame || player.IsDead || player.RuntimeId == projectile.OwnerRuntimeId) continue;
            if (MathF.Abs(player.PositionX - x) > HitRadius || MathF.Abs(player.PositionZ - z) > HitRadius) continue;
            if (y < player.PositionY || y > player.PositionY + 2f) continue;
            return player;
        }
        return null;
    }

    private bool HitsWorld(float x, float y, float z) =>
        _world.GetBlock((int)MathF.Floor(x), (int)MathF.Floor(y), (int)MathF.Floor(z)) != World.World.AirRuntimeId;

    private void ReconcileViewers(Projectile projectile, IReadOnlyList<Player.Player> online)
    {
        foreach (var peer in online)
        {
            var key = (projectile.EntityId, peer.RuntimeId);
            if (!ActorInterest.Includes(peer, projectile.PositionX, projectile.PositionZ))
            {
                if (_replicated.Remove(key))
                {
                    _lastProjected.Remove(key);
                    peer.Session.Protocol.Entity.SendRemoveActor(projectile.EntityId);
                    ReplicatedRemovalCount++;
                }
                continue;
            }
            if (!_replicated.Add(key)) continue;
            peer.Session.Protocol.Entity.SendAddProjectile(
                projectile.EntityId, projectile.RuntimeId, projectile.PositionX, projectile.PositionY, projectile.PositionZ,
                projectile.VelocityX, projectile.VelocityY, projectile.VelocityZ);
            _lastProjected[key] = new ProjectedPosition(projectile.PositionX, projectile.PositionY, projectile.PositionZ);
            ReplicatedSpawnCount++;
        }
    }

    private void Remove(Projectile projectile, IReadOnlyList<Player.Player> online)
    {
        if (!projectile.IsActive) return;
        projectile.Remove();
        _projectiles.Remove(projectile);
        RemovalCount++;
        foreach (var peer in online)
        {
            if (_replicated.Remove((projectile.EntityId, peer.RuntimeId)))
            {
                _lastProjected.Remove((projectile.EntityId, peer.RuntimeId));
                peer.Session.Protocol.Entity.SendRemoveActor(projectile.EntityId);
                ReplicatedRemovalCount++;
            }
        }
    }

    private readonly record struct ProjectedPosition(float X, float Y, float Z)
    {
        private const float PositionEpsilonSquared = 0.0001f;

        public bool MeaningfullyChanged(ProjectedPosition previous)
        {
            var dx = X - previous.X;
            var dy = Y - previous.Y;
            var dz = Z - previous.Z;
            return dx * dx + dy * dy + dz * dz > PositionEpsilonSquared;
        }
    }
}
