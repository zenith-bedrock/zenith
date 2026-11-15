using System.Text;
using Xunit;
using Zenith.Gameplay.Entities;
using Zenith.Gameplay.Loot;
using Zenith.Gameplay.Survival;

namespace Zenith.Tests;

/// <summary>Loot generation is data: the format, its validation, its randomness, and its independence from where items end up.</summary>
public sealed class LootTableTests
{
    private static IReadOnlyDictionary<string, LootTable> Parse(string json) =>
        LootTables.Parse(new MemoryStream(Encoding.UTF8.GetBytes(json)));

    private static string Table(string pools) => "{\"tables\":{\"t\":{\"pools\":[" + pools + "]}}}";

    [Fact]
    public void The_shipped_tables_load_and_every_species_has_one()
    {
        foreach (var definition in MobDefinitions.All)
            Assert.True(LootTables.Contains(definition.LootTable), definition.Name);
        LootTables.ValidateItems(Zenith.World.ItemPaletteLoader.FromEmbeddedResource());
    }

    [Fact]
    public void A_count_range_is_drawn_inclusively_and_zero_awards_nothing()
    {
        var table = Parse(Table("""{"rolls":1,"entries":[{"type":"item","name":"minecraft:bone","functions":[{"function":"set_count","count":{"min":0,"max":2}}]}]}"""))["t"];
        var counts = new HashSet<int>();
        var random = new Random(1);
        for (var i = 0; i < 300; i++)
        {
            var awards = table.Roll(new LootContext(true), random);
            counts.Add(awards.Sum(a => a.Count));
        }

        Assert.Equal(new[] { 0, 1, 2 }, counts.Order());
    }

    [Fact]
    public void Weights_pick_proportionally()
    {
        var table = Parse(Table("""{"rolls":1,"entries":[{"type":"item","name":"minecraft:bone","weight":9},{"type":"item","name":"minecraft:arrow","weight":1}]}"""))["t"];
        var random = new Random(5);
        var bones = 0;
        for (var i = 0; i < 2000; i++)
            bones += table.Roll(new LootContext(true), random).Count(a => a.Item == "minecraft:bone");

        Assert.InRange(bones, 1650, 1950);
    }

    [Fact]
    public void Killed_by_player_gates_a_pool()
    {
        var table = Parse(Table("""{"conditions":[{"condition":"killed_by_player"}],"rolls":1,"entries":[{"type":"item","name":"minecraft:bone"}]}"""))["t"];

        Assert.Empty(table.Roll(new LootContext(false), new Random(1)));
        Assert.Single(table.Roll(new LootContext(true), new Random(1)));
    }

    [Fact]
    public void A_random_chance_pool_follows_its_probability()
    {
        var table = Parse(Table("""{"conditions":[{"condition":"random_chance","chance":0.25}],"rolls":1,"entries":[{"type":"item","name":"minecraft:bone"}]}"""))["t"];
        var random = new Random(9);
        var hits = Enumerable.Range(0, 4000).Count(_ => table.Roll(new LootContext(true), random).Count == 1);

        Assert.InRange(hits, 850, 1150);
    }

    [Fact]
    public void The_same_seed_rolls_the_same_loot()
    {
        var table = LootTables.Get("zombie");
        Assert.Equal(table.Roll(new LootContext(true), new Random(42)), table.Roll(new LootContext(true), new Random(42)));
    }

    [Theory]
    [InlineData("""{"rolls":1,"entries":[{"type":"item","name":"minecraft:bone","functions":[{"function":"looting_enchant","count":{"min":0,"max":1}}]}]}""")]
    [InlineData("""{"conditions":[{"condition":"killed_by_entity"}],"rolls":1,"entries":[{"type":"item","name":"minecraft:bone"}]}""")]
    [InlineData("""{"rolls":1,"tiers":{},"entries":[{"type":"item","name":"minecraft:bone"}]}""")]
    [InlineData("""{"rolls":1,"entries":[{"type":"loot_table","name":"minecraft:bone"}]}""")]
    [InlineData("""{"rolls":1,"entries":[]}""")]
    [InlineData("""{"rolls":1,"entries":[{"type":"item","name":"minecraft:bone","weight":0}]}""")]
    [InlineData("""{"rolls":1,"entries":[{"type":"item","name":"minecraft:bone","functions":[{"function":"set_count","count":{"min":3,"max":1}}]}]}""")]
    [InlineData("""{"conditions":[{"condition":"random_chance","chance":0}],"rolls":1,"entries":[{"type":"item","name":"minecraft:bone"}]}""")]
    public void Anything_the_server_cannot_honour_fails_to_load(string pool) =>
        Assert.Throws<InvalidDataException>(() => Parse(Table(pool)));

    [Fact]
    public void A_table_lists_every_item_it_can_award()
    {
        var table = Parse(Table("""{"rolls":1,"entries":[{"type":"item","name":"minecraft:not_an_item"}]}"""))["t"];
        Assert.Contains("minecraft:not_an_item", table.Items);
    }

    [Fact]
    public void A_cow_kill_pops_each_award_as_its_own_moving_floor_item()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("hunter");
        var palette = fx.Context.ItemPalette;
        // leather 0..2 and beef 1..3: seed until a roll gives both so the assertion is about placement, not luck.
        var seed = Enumerable.Range(0, 200).First(s =>
            LootTables.Get("cow").Roll(new LootContext(true), new Random(s)).Count == 2);

        var dropped = MobLoot.Drop(LootTables.Get("cow"), new LootContext(true), new Random(seed), fx.World, fx.Players,
            fx.Players.Online, 5.5f, -60f, 5.5f, palette);

        Assert.Equal(2, dropped);
        var floor = fx.World.FloorDrops.Snapshot();
        Assert.Equal(2, floor.Count);
        Assert.Equal(2, floor.Select(d => d.Id).Distinct().Count());
        _ = player;
    }

    [Fact]
    public void Context_is_a_player_kill_only_for_melee_from_an_online_player()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("hunter");

        Assert.True(MobLoot.ContextOf(DamageSource.MeleeFrom(player.RuntimeId), fx.Players.Online).KilledByPlayer);
        Assert.False(MobLoot.ContextOf(DamageSource.MeleeFrom(player.RuntimeId + 999), fx.Players.Online).KilledByPlayer);
    }
}
