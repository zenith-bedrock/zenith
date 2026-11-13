using System.Text;
using Zenith.Gameplay.Entities;
using Xunit;

namespace Zenith.Tests;

public sealed class NaturalSpawnTests
{
    private const string Json = """
        {
          "categories": [ { "name": "monster", "capPerPlayer": 3, "intervalTicks": 40 } ],
          "spawns": [ { "mob": "zombie", "category": "monster", "weight": 10, "group": [1, 1], "time": "night", "biomes": ["Plains"] } ]
        }
        """;

    private static SpawnTable Table(string json = Json) => SpawnTable.Parse(new MemoryStream(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void Finalization_rejects_unknown_species_and_allows_fixing_the_registration()
    {
        var spawner = new MobSpawner();
        var error = Assert.Throws<InvalidDataException>(() => spawner.Freeze(Table()));
        Assert.Contains("spawns.json", error.Message);
        Assert.Contains("zombie", error.Message);
        Assert.Contains("monster", error.Message);
        spawner.Register("zombie", (_, _, _) => { }, () => 0);
        spawner.Freeze(Table());
        Assert.Throws<InvalidOperationException>(() => spawner.Register("cow", (_, _, _) => { }));
    }

    [Fact]
    public void Finalization_requires_a_population_counter_for_natural_spawns()
    {
        var spawner = new MobSpawner();
        spawner.Register("zombie", (_, _, _) => { });
        var error = Assert.Throws<InvalidDataException>(() => spawner.Freeze(Table()));
        Assert.Contains("population counter", error.Message);
        Assert.Contains("zombie", error.Message);
    }

    [Fact]
    public void Finalization_does_not_spawn_and_requests_still_execute_only_on_tick()
    {
        var spawned = 0;
        var spawner = new MobSpawner();
        spawner.Register("zombie", (_, _, _) => spawned++, () => spawned);
        spawner.Freeze(Table());
        spawner.Freeze(Table());
        Assert.Equal(0, spawned);
        Assert.True(spawner.Request("ZOMBIE", 1, 2, 3));
        Assert.Equal(0, spawned);
        spawner.Tick(new Zenith.Gameplay.Runtime.GameClock(), []);
        Assert.Equal(1, spawned);
    }

    private static (NaturalSpawnSystem System, List<(float X, float Y, float Z)> Spawned, MobSpawner Spawner, IntentTestFixture Fx)
        Setup(Func<int>? liveCount = null)
    {
        var fx = new IntentTestFixture();
        fx.AddInGamePlayer("hunted"); // at the fixture's default position
        var spawned = new List<(float, float, float)>();
        var spawner = new MobSpawner();
        spawner.Register("zombie", (x, y, z) => spawned.Add((x, y, z)), liveCount ?? (() => spawned.Count));
        return (new NaturalSpawnSystem(fx.World, spawner, Table(), new Random(7)), spawned, spawner, fx);
    }

    private static void Round(NaturalSpawnSystem system, MobSpawner spawner, IntentTestFixture fx)
    {
        system.Tick(fx.Clock, fx.Players.Online);
        spawner.Tick(fx.Clock, fx.Players.Online);
    }

    [Fact]
    public void The_shipped_table_loads_and_every_entry_is_a_registered_species_name()
    {
        var table = SpawnTable.Default;
        Assert.NotEmpty(table.Entries);
        var known = new[] { "zombie", "cow", "creeper", "spider", "skeleton", "iron_golem", "enderman", "fish", "minecart", "bat", "villager" };
        Assert.All(table.Entries, e => Assert.Contains(e.Mob, known));
    }

    [Theory]
    [InlineData("""{"categories":[{"name":"m","capPerPlayer":1,"intervalTicks":1}],"spawns":[{"mob":"z","category":"nope","weight":1,"group":[1,1],"time":"any","biomes":["Plains"]}]}""", "unknown category")]
    [InlineData("""{"categories":[{"name":"m","capPerPlayer":1,"intervalTicks":1}],"spawns":[{"mob":"z","category":"m","weight":0,"group":[1,1],"time":"any","biomes":["Plains"]}]}""", "weight")]
    [InlineData("""{"categories":[{"name":"m","capPerPlayer":1,"intervalTicks":1}],"spawns":[{"mob":"z","category":"m","weight":1,"group":[3,1],"time":"any","biomes":["Plains"]}]}""", "group")]
    [InlineData("""{"categories":[{"name":"m","capPerPlayer":1,"intervalTicks":1}],"spawns":[{"mob":"z","category":"m","weight":1,"group":[1,1],"time":"dusk","biomes":["Plains"]}]}""", "time")]
    [InlineData("""{"categories":[{"name":"m","capPerPlayer":1,"intervalTicks":1}],"spawns":[{"mob":"z","category":"m","weight":1,"group":[1,1],"time":"any","biomes":["Moon"]}]}""", "biome")]
    [InlineData("""{"categories":[{"name":"m","capPerPlayer":0,"intervalTicks":1}],"spawns":[]}""", "capPerPlayer")]
    public void A_bad_table_fails_loudly(string json, string expected)
    {
        var ex = Assert.Throws<InvalidDataException>(() => Table(json));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void At_night_a_zombie_spawns_between_the_min_and_max_distance_from_the_player()
    {
        var (system, spawned, spawner, fx) = Setup();
        var player = fx.Players.Online[0];
        fx.Clock.AdvanceBy(14000);

        Round(system, spawner, fx);

        var (x, _, z) = Assert.Single(spawned);
        var distance = MathF.Sqrt((x - player.PositionX) * (x - player.PositionX) + (z - player.PositionZ) * (z - player.PositionZ));
        Assert.InRange(distance, NaturalSpawnSystem.MinPlayerDistance - 0.01f, NaturalSpawnSystem.MaxPlayerDistance + 0.01f);
    }

    [Fact]
    public void By_day_a_night_only_entry_does_not_spawn()
    {
        var (system, spawned, spawner, fx) = Setup();
        fx.Clock.AdvanceBy(1000);

        Round(system, spawner, fx);

        Assert.Empty(spawned);
    }

    [Fact]
    public void The_do_mob_spawning_rule_switches_it_off()
    {
        var (system, spawned, spawner, fx) = Setup();
        Assert.True(fx.World.Rules.TrySet("doMobSpawning", "false", out _));
        fx.Clock.AdvanceBy(14000);

        Round(system, spawner, fx);

        Assert.Empty(spawned);
    }

    [Fact]
    public void The_category_cap_scales_with_players_and_is_never_exceeded()
    {
        var (system, spawned, spawner, fx) = Setup();
        fx.Clock.AdvanceBy(14000 - 40 * 10);
        for (var round = 0; round < 10; round++)
        {
            fx.Clock.AdvanceBy(40);
            Round(system, spawner, fx);
        }

        Assert.Equal(3, spawned.Count); // capPerPlayer 3 x one player
    }

    [Fact]
    public void Nothing_spawns_when_no_player_is_in_the_game()
    {
        var fx = new IntentTestFixture();
        var spawned = new List<(float, float, float)>();
        var spawner = new MobSpawner();
        spawner.Register("zombie", (x, y, z) => spawned.Add((x, y, z)));
        var system = new NaturalSpawnSystem(fx.World, spawner, Table(), new Random(1));
        fx.Clock.AdvanceBy(14000);

        Round(system, spawner, fx);

        Assert.Empty(spawned);
    }
}
