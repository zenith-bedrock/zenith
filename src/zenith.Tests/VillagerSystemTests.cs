using System.Linq;
using Zenith.Player;
using Zenith.World;
using Xunit;
using Zenith.Gameplay.Entities;

namespace Zenith.Tests;

/// <summary>
/// Phase XVII, Priority 3 — first non-player entity with inventory-shaped data. Phase XVIII wired
/// a real trade interaction on top (wheat in, emerald out) — see the trade tests below. These
/// tests cover the same spawn/lifecycle/replication/damage-death checkpoints every other ground
/// mob got, plus Wares data and the trade path.
/// </summary>
public sealed class VillagerSystemTests
{
    [Fact]
    public void Bootstrap_spawns_one_villager_with_seeded_wares_near_the_first_online_player()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("townsfolk");
        player.PositionX = 0;
        player.PositionZ = 0;
        var system = new VillagerSystem(fx.World, fx.Players, new VillagerStore(), fx.Context.ItemPalette);

        system.Tick(fx.Clock, fx.Players.Online);

        var villager = Assert.Single(system.Villagers.Active);
        Assert.True(villager.IsActive);
        Assert.NotEqual(0, villager.EntityId);
        Assert.Equal((ulong)villager.EntityId, villager.RuntimeId);
        Assert.Single(villager.Wares);
    }

    [Fact]
    public void Villager_wanders_without_any_player_nearby()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("distant-townsfolk");
        player.PositionX = 1000;
        player.PositionZ = 1000;
        var store = new VillagerStore();
        var villager = new Villager(fx.Players.AllocateRuntimeId(), 99, 0, Blocks.FlatSpawnY, 0);
        Assert.True(store.TryAdd(villager));
        var system = new VillagerSystem(fx.World, fx.Players, store, fx.Context.ItemPalette, new Random(1));

        var startX = villager.PositionX;
        var startZ = villager.PositionZ;
        for (var i = 0; i < 200; i++)
            system.Tick(fx.Clock, fx.Players.Online);

        Assert.True(villager.PositionX != startX || villager.PositionZ != startZ);
    }

    [Fact]
    public void Villager_never_attacks_or_damages_a_nearby_player()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("bystander");
        var store = new VillagerStore();
        var villager = new Villager(fx.Players.AllocateRuntimeId(), 99, player.PositionX + 0.5f, player.PositionY, player.PositionZ);
        Assert.True(store.TryAdd(villager));
        var system = new VillagerSystem(fx.World, fx.Players, store, fx.Context.ItemPalette, new Random(2));

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
        var store = new VillagerStore();
        var villager = new Villager(fx.Players.AllocateRuntimeId(), 99, player.PositionX + 1, player.PositionY, player.PositionZ);
        Assert.True(store.TryAdd(villager));
        var system = new VillagerSystem(fx.World, fx.Players, store, fx.Context.ItemPalette);

        for (var i = 0; i < 5; i++) // 20 health / 4 damage-per-hit
        {
            player.SubmitAttackIntent();
            system.Tick(fx.Clock, fx.Players.Online);
            fx.Clock.AdvanceBy(11);
        }

        Assert.Empty(store.Active);
        Assert.False(villager.IsActive);
        Assert.True(villager.Health.IsDead);
        var loot = Assert.Single(fx.World.FloorDrops.Snapshot());
        Assert.Equal(fx.Context.ItemPalette.Require("minecraft:emerald"), loot.Id.Value);
        Assert.Equal(1, player.ExperiencePoints);
    }

    [Fact]
    public void A_villager_with_no_player_ever_nearby_despawns_after_the_window_elapses()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("distant-townsfolk");
        player.PositionX = 1000;
        player.PositionZ = 1000;
        var store = new VillagerStore();
        var villager = new Villager(fx.Players.AllocateRuntimeId(), 99, 0, Blocks.FlatSpawnY, 0);
        Assert.True(store.TryAdd(villager));
        var system = new VillagerSystem(fx.World, fx.Players, store, fx.Context.ItemPalette, new Random(1));

        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(0, system.DespawnCount);

        fx.Clock.AdvanceBy((int)DespawnLifecycle.DefaultDespawnTicks);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(1, system.DespawnCount);
        Assert.False(villager.IsActive);
        Assert.Empty(system.Villagers.Active);
    }

    [Fact]
    public void Interacting_with_wheat_held_trades_for_an_emerald()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("trader");
        var store = new VillagerStore();
        var villager = new Villager(fx.Players.AllocateRuntimeId(), 99, player.PositionX + 1, player.PositionY, player.PositionZ)
        {
            RequestedWare = StackId.FromItem(fx.Context.ItemPalette.Require("minecraft:wheat"))
        };
        villager.Wares.Add(StackId.FromItem(fx.Context.ItemPalette.Require("minecraft:emerald")));
        Assert.True(store.TryAdd(villager));
        var system = new VillagerSystem(fx.World, fx.Players, store, fx.Context.ItemPalette);
        player.Inventory.TrySetItem(0, fx.Context.ItemPalette.Require("minecraft:wheat"), 1);
        player.SelectedHotbarSlot = 0;

        player.SubmitInteractIntent(villager.EntityId);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(1, system.TradeCount);
        var wheatId = fx.Context.ItemPalette.Require("minecraft:wheat");
        var emeraldId = fx.Context.ItemPalette.Require("minecraft:emerald");
        var slots = Enumerable.Range(0, PlayerInventory.FullInventorySize).Select(player.Inventory.Get).ToArray();
        Assert.DoesNotContain(slots, slot => slot.Id.IsItem && slot.Id.Value == wheatId);
        Assert.Contains(slots, slot => slot.Id.IsItem && slot.Id.Value == emeraldId && slot.Count == 1);
    }

    [Fact]
    public void Interacting_without_the_requested_ware_does_not_trade()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("empty-handed");
        var store = new VillagerStore();
        var villager = new Villager(fx.Players.AllocateRuntimeId(), 99, player.PositionX + 1, player.PositionY, player.PositionZ)
        {
            RequestedWare = StackId.FromItem(fx.Context.ItemPalette.Require("minecraft:wheat"))
        };
        villager.Wares.Add(StackId.FromItem(fx.Context.ItemPalette.Require("minecraft:emerald")));
        Assert.True(store.TryAdd(villager));
        var system = new VillagerSystem(fx.World, fx.Players, store, fx.Context.ItemPalette);

        player.SubmitInteractIntent(villager.EntityId);
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(0, system.TradeCount);
    }

    [Fact]
    public void Interacting_outside_reach_does_not_trade()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("distant-trader");
        var store = new VillagerStore();
        var villager = new Villager(fx.Players.AllocateRuntimeId(), 99, player.PositionX + 10, player.PositionY, player.PositionZ)
        {
            RequestedWare = StackId.FromItem(fx.Context.ItemPalette.Require("minecraft:wheat"))
        };
        villager.Wares.Add(StackId.FromItem(fx.Context.ItemPalette.Require("minecraft:emerald")));
        Assert.True(store.TryAdd(villager));
        var system = new VillagerSystem(fx.World, fx.Players, store, fx.Context.ItemPalette);
        player.Inventory.TrySetItem(0, fx.Context.ItemPalette.Require("minecraft:wheat"), 1);
        player.SelectedHotbarSlot = 0;

        player.SubmitInteractIntent(villager.EntityId);
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
        // Villager's bootstrap spawn is negative-Z of the player, unlike Zombie/Cow's positive
        // offset — remember every chunk the spawn could land in.
        first.Chunks.RememberMany([(0, 0), (0, -1)]);
        var system = new VillagerSystem(fx.World, fx.Players, new VillagerStore(), fx.Context.ItemPalette);
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
}
