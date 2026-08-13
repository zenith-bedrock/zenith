using Zenith.Gameplay;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

/// <summary>Phase XVI — leaf tests for the pure despawn decision function. No mob type involved.</summary>
public sealed class DespawnLifecycleTests
{
    public DespawnLifecycleTests() => Blocks.EnsureLoaded();

    [Fact]
    public void A_player_within_radius_refreshes_last_seen_and_never_despawns()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("nearby");
        player.PositionX = 5;
        player.PositionZ = 0;

        var (shouldDespawn, lastSeen) = DespawnLifecycle.EvaluateDespawn(
            positionX: 0, positionZ: 0, fx.Players.Online, despawnRadius: 64f,
            currentTick: 10_000, lastSeenNearPlayerTick: 0);

        Assert.False(shouldDespawn);
        Assert.Equal(10_000UL, lastSeen);
    }

    [Fact]
    public void No_player_within_radius_for_long_enough_reports_despawn()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("far-away");
        player.PositionX = 1000;

        var (shouldDespawn, lastSeen) = DespawnLifecycle.EvaluateDespawn(
            positionX: 0, positionZ: 0, fx.Players.Online, despawnRadius: 64f,
            currentTick: DespawnLifecycle.DefaultDespawnTicks, lastSeenNearPlayerTick: 0);

        Assert.True(shouldDespawn);
        Assert.Equal(0UL, lastSeen); // unchanged — nobody was in range to refresh it
    }

    [Fact]
    public void No_player_within_radius_but_not_long_enough_does_not_despawn()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("far-away");
        player.PositionX = 1000;

        var (shouldDespawn, _) = DespawnLifecycle.EvaluateDespawn(
            positionX: 0, positionZ: 0, fx.Players.Online, despawnRadius: 64f,
            currentTick: DespawnLifecycle.DefaultDespawnTicks - 1, lastSeenNearPlayerTick: 0);

        Assert.False(shouldDespawn);
    }

    [Fact]
    public void A_dead_or_offline_player_does_not_count_as_nearby()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("corpse");
        _ = player.ApplyDamage(DamageSource.Void, player.MaxHealth);
        Assert.True(player.IsDead);

        var (shouldDespawn, _) = DespawnLifecycle.EvaluateDespawn(
            positionX: player.PositionX, positionZ: player.PositionZ, fx.Players.Online, despawnRadius: 64f,
            currentTick: DespawnLifecycle.DefaultDespawnTicks, lastSeenNearPlayerTick: 0);

        Assert.True(shouldDespawn);
    }
}
