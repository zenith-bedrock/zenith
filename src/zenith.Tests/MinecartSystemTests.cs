using Zenith.Ecs;
using Zenith.Gameplay;
using Zenith.Gameplay.Systems;
using Zenith.Player;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

/// <summary>
/// Phase XXI — Minecart is the second ECS-authoritative actor: no AI, one feature-specific
/// component (<see cref="VehicleOccupancy"/>, the rider relationship). Tests read state through
/// <see cref="MinecartSystem.Stores"/>/<see cref="MinecartSystem.Occupancy"/> instead of a
/// concrete <c>Minecart</c> object's properties — see
/// docs/history/phases/phase-xxi-ecs-foundation-findings.md for the DX comparison this made visible.
/// </summary>
public sealed class MinecartSystemTests
{
    private static Position Pos(MinecartSystem system, EntityId id)
    {
        Assert.True(system.Stores.Positions.TryGet(id, out var pos));
        return pos;
    }

    private static long ActorUniqueId(MinecartSystem system, EntityId id)
    {
        Assert.True(system.Stores.Identities.TryGet(id, out var identity));
        return identity.ActorUniqueId;
    }

    private static long? Occupant(MinecartSystem system, EntityId id)
    {
        Assert.True(system.Occupancy.TryGet(id, out var occupancy));
        return occupancy.OccupantPlayerRuntimeId;
    }

    [Fact]
    public void Bootstrap_spawns_one_minecart_near_the_first_online_player()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("engineer");
        player.PositionX = 0;
        player.PositionZ = 0;
        var system = new MinecartSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);

        system.Tick(fx.Clock, fx.Players.Online);

        var id = Assert.Single(system.Minecarts);
        Assert.True(system.Stores.Entities.IsAlive(id));
    }

    [Fact]
    public void A_minecart_never_moves_on_its_own_without_being_ridden()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("distant-engineer");
        player.PositionX = 1000;
        player.PositionZ = 1000;
        var system = new MinecartSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnMinecart(0, Blocks.FlatSpawnY, 0);

        for (var i = 0; i < 100; i++)
            system.Tick(fx.Clock, fx.Players.Online);

        var pos = Pos(system, id);
        Assert.Equal(0f, pos.X);
        Assert.Equal(0f, pos.Z);
    }

    [Fact]
    public void Interacting_mounts_the_rider_and_the_cart_creeps_forward()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("rider");
        player.Yaw = 0f;
        var system = new MinecartSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnMinecart(player.PositionX + 1, player.PositionY, player.PositionZ);

        player.SubmitInteractIntent(ActorUniqueId(system, id));
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(1, system.MountCount);
        Assert.Equal(ActorUniqueId(system, id), player.RidingEntityId);
        Assert.Equal(player.RuntimeId, Occupant(system, id));

        var start = Pos(system, id);
        for (var i = 0; i < 10; i++)
            system.Tick(fx.Clock, fx.Players.Online);

        var moved = Pos(system, id);
        Assert.True(moved.X != start.X || moved.Z != start.Z); // steered forward by the rider's Yaw
        Assert.Equal(moved.X, player.PositionX); // rider's authoritative position follows the cart
        Assert.Equal(moved.Z, player.PositionZ);
    }

    [Fact]
    public void Interacting_again_while_riding_dismounts()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("rider");
        var system = new MinecartSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnMinecart(player.PositionX + 1, player.PositionY, player.PositionZ);

        player.SubmitInteractIntent(ActorUniqueId(system, id));
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.NotNull(player.RidingEntityId);

        player.SubmitInteractIntent(ActorUniqueId(system, id));
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(1, system.DismountCount);
        Assert.Null(player.RidingEntityId);
        Assert.Null(Occupant(system, id));
    }

    [Fact]
    public void Duplicate_interact_submissions_in_the_same_tick_mount_only_once()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("eager-rider");
        var system = new MinecartSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnMinecart(player.PositionX + 1, player.PositionY, player.PositionZ);

        var uniqueId = ActorUniqueId(system, id);
        player.SubmitInteractIntent(uniqueId);
        player.SubmitInteractIntent(uniqueId); // overwrite-latest mailbox — still just one pending interact
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(1, system.MountCount);
        Assert.Equal(0, system.DismountCount);
    }

    [Fact]
    public void A_second_player_cannot_mount_an_already_occupied_cart()
    {
        var fx = new IntentTestFixture();
        var first = fx.AddInGamePlayer("first-rider");
        var second = fx.AddInGamePlayer("second-rider");
        second.PositionX = first.PositionX;
        second.PositionZ = first.PositionZ;
        var system = new MinecartSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnMinecart(first.PositionX + 1, first.PositionY, first.PositionZ);
        var uniqueId = ActorUniqueId(system, id);

        first.SubmitInteractIntent(uniqueId);
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(first.RuntimeId, Occupant(system, id));

        second.SubmitInteractIntent(uniqueId);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(first.RuntimeId, Occupant(system, id));
        Assert.Null(second.RidingEntityId);
        Assert.Equal(1, system.MountCount);
    }

    [Fact]
    public void Disconnecting_while_mounted_releases_the_relationship()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("vanishing-rider");
        var system = new MinecartSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnMinecart(player.PositionX + 1, player.PositionY, player.PositionZ);

        player.SubmitInteractIntent(ActorUniqueId(system, id));
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.NotNull(Occupant(system, id));

        player.IsInGame = false; // same signal NetworkSession sets on disconnect
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(1, system.DismountCount);
        Assert.Null(Occupant(system, id));
        Assert.Null(player.RidingEntityId);
    }

    [Fact]
    public void Destroying_an_occupied_cart_dismounts_the_rider_before_removal()
    {
        var fx = new IntentTestFixture();
        var rider = fx.AddInGamePlayer("rider");
        var attacker = fx.AddInGamePlayer("attacker");
        attacker.PositionX = rider.PositionX;
        attacker.PositionZ = rider.PositionZ;
        var system = new MinecartSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnMinecart(rider.PositionX + 1, rider.PositionY, rider.PositionZ);

        rider.SubmitInteractIntent(ActorUniqueId(system, id));
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.NotNull(rider.RidingEntityId);

        for (var i = 0; i < 2; i++) // 6 health / 4 damage-per-hit
        {
            attacker.SubmitAttackIntent();
            system.Tick(fx.Clock, fx.Players.Online);
        }

        Assert.False(system.Stores.Entities.IsAlive(id));
        Assert.Equal(1, system.DismountCount);
        Assert.Null(rider.RidingEntityId);
    }

    [Fact]
    public void Reconnecting_does_not_restore_a_previous_riding_relationship()
    {
        // No persistence exists for the riding relationship (Minecart's occupancy and Player's
        // RidingEntityId are both RAM-only) — a "reconnect" is, deterministically, just a fresh
        // join with no memory of the prior session's mount state.
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("rider");
        var system = new MinecartSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnMinecart(player.PositionX + 1, player.PositionY, player.PositionZ);
        player.SubmitInteractIntent(ActorUniqueId(system, id));
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.NotNull(Occupant(system, id));

        player.IsInGame = false;
        system.Tick(fx.Clock, fx.Players.Online); // disconnect cleanup runs
        var reconnected = fx.AddInGamePlayer("rider-reconnected");

        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Null(Occupant(system, id));
        Assert.Null(reconnected.RidingEntityId);
    }

    [Fact]
    public void Melee_destroy_drops_a_minecart_item_and_awards_experience()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("attacker");
        var system = new MinecartSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnMinecart(player.PositionX + 1, player.PositionY, player.PositionZ);

        for (var i = 0; i < 2; i++) // 6 health / 4 damage-per-hit
        {
            player.SubmitAttackIntent();
            system.Tick(fx.Clock, fx.Players.Online);
        }

        Assert.Empty(system.Minecarts);
        Assert.False(system.Stores.Entities.IsAlive(id));
        var loot = Assert.Single(fx.World.FloorDrops.Snapshot());
        Assert.Equal(fx.Context.ItemPalette.Require("minecraft:minecart"), loot.Id.Value);
        Assert.Equal(1, player.ExperiencePoints);
    }

    [Fact]
    public void A_minecart_with_no_player_ever_nearby_despawns_after_the_window_elapses()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("distant-engineer");
        player.PositionX = 1000;
        player.PositionZ = 1000;
        var system = new MinecartSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnMinecart(0, Blocks.FlatSpawnY, 0);

        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(0, system.DespawnCount);

        fx.Clock.AdvanceBy((int)DespawnLifecycle.DefaultDespawnTicks);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(1, system.DespawnCount);
        Assert.False(system.Stores.Entities.IsAlive(id));
        Assert.Empty(system.Minecarts);
    }

    [Fact]
    public void NewInGamePlayerReceivesExistingMinecartReplication()
    {
        var fx = new IntentTestFixture();
        var first = fx.AddInGamePlayer("first");
        first.Chunks.Radius = 1;
        first.Chunks.RememberMany([(0, 0), (0, -1)]);
        var system = new MinecartSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        system.Tick(fx.Clock, fx.Players.Online);
        var before = fx.Transport.Captured.Count;

        var second = fx.AddInGamePlayer("second");
        second.Chunks.Radius = 1;
        second.Chunks.RememberMany([(0, 0), (0, -1)]);
        system.Tick(fx.Clock, fx.Players.Online);
        foreach (var p in fx.Players.Online)
            p.Session.RakSession.Tick();

        Assert.True(fx.Transport.Captured.Count > before);
        Assert.True(first.IsInGame && second.IsInGame);
    }

    [Fact]
    public void A_late_joining_player_observes_an_existing_mount_via_replication()
    {
        var fx = new IntentTestFixture();
        var rider = fx.AddInGamePlayer("rider");
        rider.Chunks.Radius = 1;
        rider.Chunks.RememberMany([(0, 0), (0, -1)]);
        var system = new MinecartSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnMinecart(rider.PositionX + 1, rider.PositionY, rider.PositionZ);
        rider.SubmitInteractIntent(ActorUniqueId(system, id));
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.NotNull(Occupant(system, id));
        var before = fx.Transport.Captured.Count;

        var late = fx.AddInGamePlayer("late-joiner");
        late.Chunks.Radius = 1;
        late.Chunks.RememberMany([(0, 0), (0, -1)]);
        system.Tick(fx.Clock, fx.Players.Online);
        foreach (var p in fx.Players.Online)
            p.Session.RakSession.Tick();

        // The late joiner's viewer-reconciliation enter path sends the mount link alongside the
        // spawn/health packets — a strictly larger capture delta than a plain unoccupied spawn.
        Assert.True(fx.Transport.Captured.Count > before);
    }
}
