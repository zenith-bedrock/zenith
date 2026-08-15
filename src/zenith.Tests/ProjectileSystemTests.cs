using Zenith.Ecs;
using Zenith.Player;
using Xunit;
using Zenith.Gameplay.Entities;
using Zenith.Gameplay.Survival;

namespace Zenith.Tests;

/// <summary>
/// Phase XXI — Projectile is the third ECS-authoritative actor, deliberately chosen to prove the
/// ECS isn't built around <see cref="IDamageableActor"/>: it has Position/Velocity/ActorIdentity
/// but no HealthComponent at all. Hit resolution against Zombie now goes through the generalized
/// cross-species query (<see cref="ProjectileSystem.Projectiles"/> vs. any entity with
/// Health+Position) rather than a direct <c>ZombieStore</c> coupling — see
/// docs/history/phases/phase-xxi-ecs-foundation-findings.md.
/// </summary>
public sealed class ProjectileSystemTests
{
    [Fact]
    public void ProjectileSpawnIsTickOwnedAndCarriesItsOwnerIdentity()
    {
        var fx = new IntentTestFixture();
        var owner = fx.AddInGamePlayer("owner");
        owner.Yaw = -90f; // +X
        var system = CreateSystem(fx, out _, out _);

        owner.SubmitProjectileIntent();
        system.Tick(fx.Clock, fx.Players.Online);

        var id = Assert.Single(system.Projectiles);
        Assert.True(system.ProjectileStates.TryGet(id, out var state));
        Assert.Equal(owner.RuntimeId, state.OwnerRuntimeId);
        Assert.True(system.Stores.Positions.TryGet(id, out var pos));
        Assert.True(pos.X > owner.PositionX);
    }

    [Fact]
    public void ProjectileImpactDamagesZombieWithProjectileAttributionAndRemovesOnce()
    {
        var fx = new IntentTestFixture();
        var owner = fx.AddInGamePlayer("owner");
        owner.Yaw = -90f;
        var system = CreateSystem(fx, out var zombies, out _);
        var zombieId = zombies.SpawnZombie(1f, owner.PositionY, owner.PositionZ);
        zombies.TryApplyDamage(zombieId, DamageSource.Generic, 14f, fx.Players.Online, fx.Clock.CurrentTick);
        fx.Clock.AdvanceBy(11); // past the hit-invulnerability window — the arrow is a distinct, later hit.

        owner.SubmitProjectileIntent();
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Empty(system.Projectiles);
        Assert.False(zombies.Stores.Entities.IsAlive(zombieId));

        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Empty(system.Projectiles);
    }

    [Fact]
    public void ActorProjectileImpactDamagesAnotherPlayerAndRemovesOnce()
    {
        var fx = new IntentTestFixture();
        var target = fx.AddInGamePlayer("target");
        var system = CreateSystem(fx, out _, out _);
        system.TrySpawnFromActor(999, 0.4f, target.PositionY + 1f, target.PositionZ, -0.1f, 0f, 0f, fx.Players.Online);

        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Empty(system.Projectiles);
        Assert.Equal(14f, target.Health);
    }

    [Fact]
    public void WorldImpactRemovesProjectileAndNeverMutatesZombieState()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("observer");
        var system = CreateSystem(fx, out var zombies, out _);
        var zombieId = zombies.SpawnZombie(20f, player.PositionY, 0f);
        system.TrySpawnFromActor(player.RuntimeId, 0f, -60.5f, 0f, 0f, -0.1f, 0f, fx.Players.Online);

        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Empty(system.Projectiles);
        Assert.True(zombies.Stores.Health.TryGet(zombieId, out var health));
        Assert.Equal(health.State.Maximum, health.State.Current);
    }

    [Fact]
    public void Projectile_skips_projection_when_authoritative_position_is_unchanged()
    {
        var fx = new IntentTestFixture();
        var observer = fx.AddInGamePlayer("observer");
        observer.Chunks.Radius = -1;
        var system = CreateSystem(fx, out _, out _);
        var id = SpawnRaw(system, fx, 999, 0.4f, -57f, 0f, 0f, 0f, 0f);

        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(0, system.ReplicatedMoveCount);
        Assert.True(system.ReplicatedMoveSkippedCount > 0);
        Assert.True(system.Stores.Entities.IsAlive(id));
    }

    [Fact]
    public void LateJoinReceivesExistingProjectileState()
    {
        var fx = new IntentTestFixture();
        var first = fx.AddInGamePlayer("first");
        first.PositionY = 100f;
        first.Chunks.Radius = 1;
        first.Chunks.RememberMany([(0, 0)]);
        first.Yaw = -90f;
        var system = CreateSystem(fx, out _, out _);
        first.SubmitProjectileIntent();
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Single(system.Projectiles);
        var before = fx.Transport.Captured.Count;

        var late = fx.AddInGamePlayer("late");
        late.Chunks.Radius = 1;
        late.Chunks.RememberMany([(0, 0)]);
        system.Tick(fx.Clock, fx.Players.Online);
        foreach (var player in fx.Players.Online)
            player.Session.RakSession.Tick();

        Assert.True(fx.Transport.Captured.Count > before);
    }

    [Fact]
    public void Interest_reconciles_late_join_and_reconnect_without_stale_actor_state()
    {
        var fx = new IntentTestFixture();
        var first = fx.AddInGamePlayer("first");
        first.Chunks.Radius = -1;
        var system = CreateSystem(fx, out _, out _);
        var id = SpawnRaw(system, fx, first.RuntimeId, 0.5f, -57f, 0.5f, 0f, 0f, 0f);

        system.Tick(fx.Clock, fx.Players.Online);
        var late = fx.AddInGamePlayer("late");
        late.Chunks.Radius = -1;
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(2, system.ReplicatedSpawnCount);

        late.IsInGame = false;
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(1, system.ReplicatedRemovalCount);

        late.IsInGame = true;
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(3, system.ReplicatedSpawnCount);
        Assert.True(system.Stores.Entities.IsAlive(id));
    }

    [Fact]
    public void ChunkInterest_reconcilesSpawnAndRemovalForRelevantObserversOnly()
    {
        var fx = new IntentTestFixture();
        var owner = fx.AddInGamePlayer("owner");
        var distant = fx.AddInGamePlayer("distant");
        owner.PositionY = distant.PositionY = 100f;
        owner.PositionZ = 0.5f;
        distant.PositionX = distant.PositionZ = 128f;
        owner.Chunks.Radius = distant.Chunks.Radius = 1;
        owner.Chunks.RememberMany([(0, 0)]);
        distant.Chunks.RememberMany([(8, 8)]);
        owner.Yaw = -90f;
        var system = CreateSystem(fx, out _, out _);

        owner.SubmitProjectileIntent();
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(1, system.ReplicatedSpawnCount);

        distant.Chunks.RememberMany([(0, 0)]);
        Assert.Single(system.Projectiles);
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Single(system.Projectiles);
        Assert.Equal(2, system.ReplicatedSpawnCount);

        distant.Chunks.Forget(0, 0);
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(1, system.ReplicatedRemovalCount);
    }

    [Fact]
    public void ChunkInterest_reconcilesWhenProjectileCrossesIntoAnotherKnownColumn()
    {
        var fx = new IntentTestFixture();
        var west = fx.AddInGamePlayer("west");
        var east = fx.AddInGamePlayer("east");
        west.Chunks.Radius = east.Chunks.Radius = 1;
        west.Chunks.RememberMany([(0, 0)]);
        east.Chunks.RememberMany([(1, 0)]);
        var system = CreateSystem(fx, out _, out _);
        SpawnRaw(system, fx, west.RuntimeId, 15.8f, 100f, 0.5f, 0.5f, 0f, 0f);

        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(2, system.ReplicatedSpawnCount);
        Assert.Equal(1, system.ReplicatedRemovalCount);
        Assert.Equal(0, system.ReplicatedMoveCount);
    }

    /// <summary>
    /// Spawns a projectile with an explicit position/velocity without going through the tick-owned
    /// `SubmitProjectileIntent` path — the old tests did this by constructing a <c>Projectile</c>
    /// object directly and adding it to a store; the ECS equivalent is calling the same
    /// <c>TrySpawnFromActor</c> the real spawn path uses, since there is no other way to attach a
    /// fully-formed entity outside gameplay's own composition step (by design — see
    /// docs/history/phases/phase-xxi-ecs-foundation-findings.md, "no dual authoritative state").
    /// </summary>
    private static EntityId SpawnRaw(ProjectileSystem system, IntentTestFixture fx, long ownerRuntimeId,
        float x, float y, float z, float vx, float vy, float vz)
    {
        system.TrySpawnFromActor(ownerRuntimeId, x, y, z, vx, vy, vz, fx.Players.Online);
        return system.Projectiles[^1];
    }

    private static ProjectileSystem CreateSystem(IntentTestFixture fx, out ZombieSystem zombies, out MinecartSystem minecarts)
    {
        var stores = new EntityRuntime();
        zombies = new ZombieSystem(fx.World, fx.Players, stores, fx.Context.ItemPalette);
        minecarts = new MinecartSystem(fx.World, fx.Players, stores, fx.Context.ItemPalette);
        var damage = new DamageDispatch();
        damage.Register(zombies.Owns, zombies.TryApplyDamage);
        damage.Register(minecarts.Owns, minecarts.TryApplyDamage);
        return new ProjectileSystem(fx.World, fx.Players, stores, damage);
    }

    /// <summary>
    /// Phase XXII — builds every ECS-damageable species (all five: Zombie/Minecart/Cow/Skeleton/
    /// Spider) registered into one shared <see cref="DamageDispatch"/>, for the damage-seam tests
    /// below proving Projectile no longer needs a per-species type-switch to hit any of them.
    /// </summary>
    private static ProjectileSystem CreateSystemWithFullRoster(
        IntentTestFixture fx, out ZombieSystem zombies, out MinecartSystem minecarts,
        out CowSystem cows, out SpiderSystem spiders)
    {
        var stores = new EntityRuntime();
        zombies = new ZombieSystem(fx.World, fx.Players, stores, fx.Context.ItemPalette);
        minecarts = new MinecartSystem(fx.World, fx.Players, stores, fx.Context.ItemPalette);
        cows = new CowSystem(fx.World, fx.Players, stores, fx.Context.ItemPalette);
        spiders = new SpiderSystem(fx.World, fx.Players, stores, fx.Context.ItemPalette);
        var damage = new DamageDispatch();
        damage.Register(zombies.Owns, zombies.TryApplyDamage);
        damage.Register(minecarts.Owns, minecarts.TryApplyDamage);
        damage.Register(cows.Owns, cows.TryApplyDamage);
        damage.Register(spiders.Owns, spiders.TryApplyDamage);
        return new ProjectileSystem(fx.World, fx.Players, stores, damage);
    }

    [Fact]
    public void Projectile_can_damage_a_cow_through_the_shared_dispatch_not_a_species_switch()
    {
        var fx = new IntentTestFixture();
        var owner = fx.AddInGamePlayer("hunter");
        var system = CreateSystemWithFullRoster(fx, out _, out _, out var cows, out _);
        var cowId = cows.SpawnCow(10f, 100f, 0f); // 10 HP; a 6-damage hit must leave it alive but wounded, not untouched.
        Assert.True(cows.Stores.Health.TryGet(cowId, out var before));
        var maxHealth = before.State.Maximum;

        SpawnRaw(system, fx, owner.RuntimeId, 9.7f, 100f, 0f, 0.05f, 0f, 0f);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.True(cows.Stores.Health.TryGet(cowId, out var after));
        Assert.True(after.State.Current < maxHealth); // dispatch actually applied damage, not a silent no-op
        Assert.Empty(system.Projectiles);
    }

    [Fact]
    public void Projectile_can_damage_a_spider_through_the_shared_dispatch_not_a_species_switch()
    {
        var fx = new IntentTestFixture();
        var owner = fx.AddInGamePlayer("hunter");
        var system = CreateSystemWithFullRoster(fx, out _, out _, out _, out var spiders);
        var spiderId = spiders.SpawnSpider(10f, 100f, 0f); // 16 HP; a 6-damage hit must leave it alive but wounded, not untouched.
        Assert.True(spiders.Stores.Health.TryGet(spiderId, out var before));
        var maxHealth = before.State.Maximum;

        SpawnRaw(system, fx, owner.RuntimeId, 9.7f, 100f, 0f, 0.05f, 0f, 0f);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.True(spiders.Stores.Health.TryGet(spiderId, out var after));
        Assert.True(after.State.Current < maxHealth); // dispatch actually applied damage, not a silent no-op
        Assert.Empty(system.Projectiles);
    }

    [Fact]
    public void Projectile_can_damage_a_minecart_through_the_shared_dispatch_not_a_species_switch()
    {
        var fx = new IntentTestFixture();
        var owner = fx.AddInGamePlayer("hunter");
        var system = CreateSystemWithFullRoster(fx, out _, out var minecarts, out _, out _);
        var minecartId = minecarts.SpawnMinecart(10f, 100f, 0f); // 6 HP; a projectile hit outright kills it, unlike the tougher cow/spider.
        Assert.True(minecarts.Stores.Entities.IsAlive(minecartId));

        SpawnRaw(system, fx, owner.RuntimeId, 9.7f, 100f, 0f, 0.05f, 0f, 0f);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.False(minecarts.Stores.Entities.IsAlive(minecartId)); // dispatch actually applied damage, not a silent no-op
        Assert.Empty(system.Projectiles);
    }

    [Fact]
    public void Projectile_can_damage_a_skeleton_through_the_shared_dispatch_not_a_species_switch()
    {
        var fx = new IntentTestFixture();
        var owner = fx.AddInGamePlayer("hunter");
        var stores = new EntityRuntime();
        var zombies = new ZombieSystem(fx.World, fx.Players, stores, fx.Context.ItemPalette);
        var minecarts = new MinecartSystem(fx.World, fx.Players, stores, fx.Context.ItemPalette);
        var cows = new CowSystem(fx.World, fx.Players, stores, fx.Context.ItemPalette);
        var spiders = new SpiderSystem(fx.World, fx.Players, stores, fx.Context.ItemPalette);
        var damage = new DamageDispatch();
        damage.Register(zombies.Owns, zombies.TryApplyDamage);
        damage.Register(minecarts.Owns, minecarts.TryApplyDamage);
        damage.Register(cows.Owns, cows.TryApplyDamage);
        damage.Register(spiders.Owns, spiders.TryApplyDamage);
        var system = new ProjectileSystem(fx.World, fx.Players, stores, damage);
        var skeletons = new SkeletonSystem(fx.World, fx.Players, stores, system, fx.Context.ItemPalette);
        damage.Register(skeletons.Owns, skeletons.TryApplyDamage);

        var skeletonId = skeletons.SpawnSkeleton(10f, 100f, 0f); // 20 HP; a hit must leave it alive but wounded, not untouched.
        Assert.True(skeletons.Stores.Health.TryGet(skeletonId, out var before));
        var maxHealth = before.State.Maximum;

        SpawnRaw(system, fx, owner.RuntimeId, 9.7f, 100f, 0f, 0.05f, 0f, 0f);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.True(skeletons.Stores.Health.TryGet(skeletonId, out var after));
        Assert.True(after.State.Current < maxHealth); // dispatch actually applied damage, not a silent no-op
        Assert.Empty(system.Projectiles);
    }
}
