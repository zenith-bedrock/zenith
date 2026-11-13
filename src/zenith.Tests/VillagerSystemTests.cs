using System.Linq;
using Zenith.Ecs;
using Zenith.Player;
using Zenith.World;
using Xunit;
using Zenith.Gameplay.Entities;
using Zenith.Gameplay.Survival;

namespace Zenith.Tests;

/// <summary>
/// The villager: spawn/lifecycle/replication/damage checkpoints every ground mob has, plus the one real trade (wheat in, emerald
/// out), which is the first consumer of <c>ActorInteract</c>. An ECS species like every other.
/// </summary>
public sealed class VillagerSystemTests
{
    private static Position Pos(VillagerSystem system, EntityId id)
    {
        Assert.True(system.Stores.Positions.TryGet(id, out var pos));
        return pos;
    }

    private static long UniqueId(VillagerSystem system, EntityId id)
    {
        Assert.True(system.Stores.Identities.TryGet(id, out var identity));
        return identity.ActorUniqueId;
    }

    [Fact]
    public void Villager_wanders_without_any_player_nearby()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("distant-townsfolk");
        player.PositionX = 1000;
        player.PositionZ = 1000;
        var system = new VillagerSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette, new Random(1));
        var id = system.SpawnVillager(0, Blocks.FlatSpawnY, 0);

        var start = Pos(system, id);
        for (var i = 0; i < 200; i++)
            system.Tick(fx.Clock, fx.Players.Online);

        var end = Pos(system, id);
        Assert.True(end.X != start.X || end.Z != start.Z);
    }

    [Fact]
    public void Villager_never_attacks_or_damages_a_nearby_player()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("bystander");
        var system = new VillagerSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette, new Random(2));
        system.SpawnVillager(player.PositionX + 0.5f, player.PositionY, player.PositionZ);

        for (var i = 0; i < 100; i++)
            system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(20f, player.Health);
        Assert.False(player.IsDead);
    }

    [Fact]
    public void Melee_kill_drops_emerald_and_awards_experience()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("attacker");
        var system = new VillagerSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnVillager(player.PositionX + 1, player.PositionY, player.PositionZ);

        for (var i = 0; i < 5; i++) // 20 health / 4 damage-per-hit
        {
            player.Intents.SubmitAttackIntent();
            system.Tick(fx.Clock, fx.Players.Online);
            fx.Clock.AdvanceBy(11);
        }

        Assert.Empty(system.Villagers);
        Assert.False(system.Stores.Entities.IsAlive(id));
        var loot = Assert.Single(fx.World.FloorDrops.Snapshot());
        Assert.Equal(fx.Context.ItemPalette.Require("minecraft:emerald"), loot.Id.Value);
        Assert.Equal(1, player.ExperiencePoints);
    }

    [Fact]
    public void A_hit_is_remembered_as_the_players_last_villager_attack_for_the_golem_aggro_rule()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("attacker");
        var system = new VillagerSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnVillager(player.PositionX + 1, player.PositionY, player.PositionZ);

        system.TryApplyDamage(id, DamageSource.MeleeFrom(player.RuntimeId), 1f, fx.Players.Online, 77);

        Assert.NotNull(player.LastVillagerAttack);
        Assert.Equal(77UL, player.LastVillagerAttack!.Value.Item1);
        Assert.Equal(player.PositionX + 1, player.LastVillagerAttack.Value.Item2);
    }

    [Fact]
    public void A_villager_with_no_player_ever_nearby_despawns_after_the_window_elapses()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("distant-townsfolk");
        player.PositionX = 1000;
        player.PositionZ = 1000;
        var system = new VillagerSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette, new Random(1));
        var id = system.SpawnVillager(0, Blocks.FlatSpawnY, 0);

        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(0, system.DespawnCount);

        fx.Clock.AdvanceBy((int)DespawnLifecycle.DefaultDespawnTicks);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(1, system.DespawnCount);
        Assert.False(system.Stores.Entities.IsAlive(id));
        Assert.Empty(system.Villagers);
    }

    [Fact]
    public void Interacting_with_wheat_held_trades_for_an_emerald()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("trader");
        var system = new VillagerSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnVillager(player.PositionX + 1, player.PositionY, player.PositionZ);
        player.Inventory.TrySetItem(0, fx.Context.ItemPalette.Require("minecraft:wheat"), 1);
        player.SelectedHotbarSlot = 0;

        player.Intents.SubmitInteractIntent(UniqueId(system, id));
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(1, system.TradeCount);
        var wheatId = fx.Context.ItemPalette.Require("minecraft:wheat");
        var emeraldId = fx.Context.ItemPalette.Require("minecraft:emerald");
        var slots = Enumerable.Range(0, PlayerInventory.FullInventorySize).Select(player.Inventory.Get).ToArray();
        Assert.DoesNotContain(slots, slot => slot.Id.IsItem && slot.Id.Value == wheatId);
        Assert.Contains(slots, slot => slot.Id.IsItem && slot.Id.Value == emeraldId && slot.Count == 1);
    }

    [Fact]
    public void A_second_trade_inside_the_cooldown_is_refused()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("trader");
        var system = new VillagerSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnVillager(player.PositionX + 1, player.PositionY, player.PositionZ);
        player.Inventory.TrySetItem(0, fx.Context.ItemPalette.Require("minecraft:wheat"), 2);
        player.SelectedHotbarSlot = 0;

        player.Intents.SubmitInteractIntent(UniqueId(system, id));
        system.Tick(fx.Clock, fx.Players.Online);
        fx.Clock.AdvanceBy(1);
        player.Intents.SubmitInteractIntent(UniqueId(system, id));
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(1, system.TradeCount);
    }

    [Fact]
    public void Interacting_without_the_requested_ware_does_not_trade()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("empty-handed");
        var system = new VillagerSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnVillager(player.PositionX + 1, player.PositionY, player.PositionZ);

        player.Intents.SubmitInteractIntent(UniqueId(system, id));
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(0, system.TradeCount);
    }

    [Fact]
    public void Interacting_outside_reach_does_not_trade()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("distant-trader");
        var system = new VillagerSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        var id = system.SpawnVillager(player.PositionX + 10, player.PositionY, player.PositionZ);
        player.Inventory.TrySetItem(0, fx.Context.ItemPalette.Require("minecraft:wheat"), 1);
        player.SelectedHotbarSlot = 0;

        player.Intents.SubmitInteractIntent(UniqueId(system, id));
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(0, system.TradeCount);
        Assert.Equal(1, player.Inventory.Get(0).Count);
    }

    [Fact]
    public void NewInGamePlayerReceivesExistingVillagerReplication()
    {
        var fx = new IntentTestFixture();
        var first = fx.AddInGamePlayer("first");
        first.Chunks.Radius = 1;
        first.Chunks.RememberMany([(0, 0)]);
        var system = new VillagerSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        system.SpawnVillager(8, Blocks.FlatSpawnY, 8); // mid-column, so a wander step cannot leave the known column
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(1, system.ReplicatedSpawnCount);

        var second = fx.AddInGamePlayer("second");
        second.Chunks.Radius = 1;
        second.Chunks.RememberMany([(0, 0)]);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(2, system.ReplicatedSpawnCount);
        Assert.True(first.IsInGame && second.IsInGame);
    }
}
