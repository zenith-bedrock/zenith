using Zenith.Gameplay.Runtime;
using Zenith.Player;
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
    internal long RemovalCount { get; private set; }

    public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
    {
        _ = clock;
        SpawnFromPlayerInputs(online);
        foreach (var projectile in _projectiles.Active.ToArray())
        {
            if (!projectile.IsActive) continue;
            ReplicateNewViewers(projectile, online);
            Advance(projectile, online);
        }

        _replicated.RemoveWhere(pair => !_projectiles.Active.Any(p => p.EntityId == pair.ProjectileId) ||
                                        !online.Any(p => p.RuntimeId == pair.PlayerId));
    }

    private void SpawnFromPlayerInputs(IReadOnlyList<Player.Player> online)
    {
        foreach (var player in online)
        {
            if (!player.IsInGame || player.IsDead || !player.TryConsumeProjectileIntent()) continue;
            var radians = player.Yaw * (MathF.PI / 180f);
            var velocityX = -MathF.Sin(radians) * LaunchSpeed;
            var velocityZ = MathF.Cos(radians) * LaunchSpeed;
            var entityId = _players.AllocateRuntimeId();
            var projectile = new Projectile(
                entityId, (ulong)entityId, player.RuntimeId,
                player.PositionX, player.PositionY + 1.2f, player.PositionZ,
                velocityX, LaunchUpwardVelocity, velocityZ);
            if (!_projectiles.TryAdd(projectile)) continue;
            ReplicateNewViewers(projectile, online);
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

        if (HitsWorld(nextX, nextY, nextZ) || ++projectile.AgeTicks >= MaximumLifetimeTicks)
        {
            Remove(projectile, online);
            return;
        }

        projectile.PositionX = nextX;
        projectile.PositionY = nextY;
        projectile.PositionZ = nextZ;
        projectile.VelocityY -= GravityPerTick;
        foreach (var peer in online)
        {
            if (!peer.IsInGame || peer.IsDead || !_replicated.Contains((projectile.EntityId, peer.RuntimeId))) continue;
            peer.Session.Protocol.Entity.SendMoveActorAbsoluteRaw(
                projectile.RuntimeId, projectile.PositionX, projectile.PositionY, projectile.PositionZ);
            ReplicatedMoveCount++;
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

    private bool HitsWorld(float x, float y, float z) =>
        _world.GetBlock((int)MathF.Floor(x), (int)MathF.Floor(y), (int)MathF.Floor(z)) != World.World.AirRuntimeId;

    private void ReplicateNewViewers(Projectile projectile, IReadOnlyList<Player.Player> online)
    {
        foreach (var peer in online)
        {
            if (!peer.IsInGame || peer.IsDead || !_replicated.Add((projectile.EntityId, peer.RuntimeId))) continue;
            peer.Session.Protocol.Entity.SendAddProjectile(
                projectile.EntityId, projectile.RuntimeId, projectile.PositionX, projectile.PositionY, projectile.PositionZ,
                projectile.VelocityX, projectile.VelocityY, projectile.VelocityZ);
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
            if (peer.IsInGame && _replicated.Contains((projectile.EntityId, peer.RuntimeId)))
                peer.Session.Protocol.Entity.SendRemoveActor(projectile.EntityId);
        }
    }
}
