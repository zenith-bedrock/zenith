using System.Text;
using Xunit;
using Zenith.Ecs;
using Zenith.Gameplay.Entities;
using Zenith.Gameplay.Runtime;
using Zenith.Gameplay.WorldInteraction;
using Zenith.Gameplay.WorldInteraction.RandomTicks;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>Plan 004 item 26: what the light model changes in the game (spawning, grass, saplings, the sun on the undead).</summary>
public sealed class LightConsumerTests
{
    public LightConsumerTests() => Blocks.EnsureLoaded();

    private const string DarkMonster = """
        {
          "categories": [ { "name": "monster", "capPerPlayer": 3, "intervalTicks": 40 } ],
          "spawns": [ { "mob": "zombie", "category": "monster", "weight": 10, "herd": { "min_size": 1, "max_size": 1 }, "filters": [ { "test": "light_level", "operator": "<=", "value": 7 } ] } ]
        }
        """;

    private static int SpawnsAt(int worldTime, string json)
    {
        var fx = new IntentTestFixture();
        fx.AddInGamePlayer("hunted");
        var spawned = 0;
        var spawner = new MobSpawner();
        spawner.Register("zombie", (_, _, _) => spawned++, () => spawned);
        var system = new NaturalSpawnSystem(fx.World, spawner, SpawnTable.Parse(new MemoryStream(Encoding.UTF8.GetBytes(json))), new Random(7));
        fx.Clock.AdvanceBy(worldTime - worldTime % 40);
        for (var round = 0; round < 20; round++)
        {
            fx.Clock.AdvanceBy(40);
            system.Tick(fx.Clock, fx.Players.Online);
            spawner.Tick(fx.Clock, fx.Players.Online);
        }

        return spawned;
    }

    [Fact]
    public void A_dark_entry_spawns_on_the_surface_at_night_and_not_in_daylight()
    {
        Assert.Equal(0, SpawnsAt(2000, DarkMonster));
        Assert.True(SpawnsAt(17000, DarkMonster) > 0);
    }

    [Fact]
    public void An_entry_with_no_light_rule_is_not_held_back_by_the_sun()
    {
        const string any = """
            {
              "categories": [ { "name": "monster", "capPerPlayer": 3, "intervalTicks": 40 } ],
              "spawns": [ { "mob": "zombie", "category": "monster", "weight": 10, "herd": { "min_size": 1, "max_size": 1 } } ]
            }
            """;
        Assert.True(SpawnsAt(2000, any) > 0);
    }

    [Fact]
    public void The_brightness_filters_are_the_packs_zero_to_seven_for_monsters_and_seven_up_for_animals()
    {
        // The shipped entries on a lit and an unlit cell of a plains column (on grass, Normal difficulty).
        var table = SpawnTable.Default;
        var zombie = table.Entries.Single(e => e.Mob == "zombie");
        var cow = table.Entries.Single(e => e.Mob == "cow");
        var fx = new IntentTestFixture();
        fx.World.SetBlock(5, 63, 5, Blocks.GrassBlock);
        for (var y = 64; y < 80; y++) fx.World.SetBlock(5, y, 5, Blocks.Air);
        Assert.True(zombie.Allows(new Zenith.Gameplay.Filters.FilterContext(fx.World, 5, 64, 5, 18000)));
        Assert.False(zombie.Allows(new Zenith.Gameplay.Filters.FilterContext(fx.World, 5, 64, 5, 6000)));
        Assert.False(cow.Allows(new Zenith.Gameplay.Filters.FilterContext(fx.World, 5, 64, 5, 18000)));
        Assert.True(cow.Allows(new Zenith.Gameplay.Filters.FilterContext(fx.World, 5, 64, 5, 6000)));
    }

    // ---- grass and saplings --------------------------------------------------------------------------------------

    private static async Task<IntentTestFixture> ViewedColumn()
    {
        var fx = new IntentTestFixture();
        Assert.True(fx.World.Rules.TrySet(Rulebook.GameRules.RandomTickSpeedName, "4096", out _));
        fx.AddInGamePlayer("ticker");
        await fx.World.GetOrCreateColumnAsync(0, 0);
        fx.World.ChunkResidency.Acquire(0, 0);
        return fx;
    }

    private static void Run(IntentTestFixture fx, int ticks, int startTime = 0)
    {
        var system = new RandomTickSystem(fx.World, RandomTickBehaviors.Default(), fx.Context.EnvironmentPolicies, new Random(1));
        var clock = new GameClock();
        clock.AdvanceBy(startTime);
        for (var i = 0; i < ticks; i++)
        {
            clock.AdvanceBy(1);
            system.Tick(clock, fx.Players.Online);
        }
    }

    [Fact]
    public async Task Grass_under_a_solid_block_turns_back_into_dirt()
    {
        var fx = await ViewedColumn();
        fx.World.SetBlock(5, 64, 5, Blocks.GrassBlock);
        fx.World.SetBlock(5, 65, 5, Blocks.Stone);

        Run(fx, 100);

        Assert.Equal(Blocks.Dirt, fx.World.GetBlock(5, 64, 5));
    }

    [Fact]
    public async Task Grass_in_the_open_stays_grass()
    {
        var fx = await ViewedColumn();
        fx.World.SetBlock(5, 64, 5, Blocks.GrassBlock);

        Run(fx, 100);

        Assert.Equal(Blocks.GrassBlock, fx.World.GetBlock(5, 64, 5));
    }

    [Fact]
    public async Task Grass_does_not_spread_at_night()
    {
        var fx = await ViewedColumn();
        fx.World.SetBlock(5, 64, 5, Blocks.Stone);
        fx.World.SetBlock(6, 64, 5, Blocks.Dirt);
        fx.World.SetBlock(5, 64, 5, Blocks.GrassBlock);

        Run(fx, 100, startTime: 17000);

        Assert.Equal(Blocks.Dirt, fx.World.GetBlock(6, 64, 5));
    }

    [Fact]
    public async Task A_sapling_in_the_dark_does_not_grow_and_one_in_daylight_does()
    {
        var fx = await ViewedColumn();
        fx.World.SetBlock(8, 63, 8, Blocks.Dirt);
        fx.World.SetBlock(8, 64, 8, Blocks.OakSapling);
        Run(fx, 600, startTime: 17000);
        Assert.False(Blocks.IsLog(fx.World.GetBlock(8, 64, 8)));

        Run(fx, 600, startTime: 2000);
        Assert.True(Blocks.IsLog(fx.World.GetBlock(8, 64, 8)));
    }

    // ---- the sun and the undead ----------------------------------------------------------------------------------

    private static async Task<(IntentTestFixture Fx, MobHazardSystem Hazard, EntityId Id)> UndeadStage()
    {
        var fx = new IntentTestFixture();
        await fx.World.GetOrCreateColumnAsync(0, 0);
        for (var x = 0; x < 16; x++)
            for (var z = 0; z < 16; z++)
            {
                fx.World.SetBlock(x, 199, z, Blocks.Stone);
                for (var y = 200; y < 212; y++) fx.World.SetBlock(x, y, z, Blocks.Air);
            }

        var stores = new EntityRuntime();
        var zombies = new ZombieSystem(fx.World, fx.Players, stores, fx.Context.ItemPalette);
        var damage = new DamageDispatch();
        damage.Register(zombies);
        var id = zombies.SpawnZombie(8.5f, 200f, 8.5f);
        return (fx, new MobHazardSystem(fx.World, stores, damage, _ => false, zombies.Owns), id);
    }

    private static void Tick(IntentTestFixture fx, MobHazardSystem hazard, int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            fx.Clock.AdvanceBy(1);
            hazard.Tick(fx.Clock, fx.Players.Online);
        }
    }

    [Fact]
    public async Task A_zombie_in_daylight_under_the_open_sky_catches_fire()
    {
        var (fx, hazard, id) = await UndeadStage();
        fx.Clock.AdvanceBy(2000);

        Tick(fx, hazard, 20);

        Assert.True(hazard.BurnTicksOf(id) > 0);
    }

    [Fact]
    public async Task A_zombie_at_night_or_under_a_roof_does_not_burn()
    {
        var (fx, hazard, id) = await UndeadStage();
        fx.Clock.AdvanceBy(17000);
        Tick(fx, hazard, 20);
        Assert.Equal(0, hazard.BurnTicksOf(id));

        var (fx2, hazard2, id2) = await UndeadStage();
        for (var x = 0; x < 16; x++)
            for (var z = 0; z < 16; z++) fx2.World.SetBlock(x, 205, z, Blocks.Stone);
        fx2.Clock.AdvanceBy(2000);
        Tick(fx2, hazard2, 20);
        Assert.Equal(0, hazard2.BurnTicksOf(id2));
    }
}
