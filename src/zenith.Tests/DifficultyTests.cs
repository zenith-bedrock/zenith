using Xunit;
using Zenith.Gameplay.Commands;
using Zenith.Gameplay.Entities;
using Zenith.Gameplay.Replication;
using Zenith.Gameplay.Survival;
using Zenith.Rulebook;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>Plan 004 item 20: the world difficulty, and what it changes (starvation, mob damage, monsters on Peaceful).</summary>
public sealed class DifficultyTests
{
    [Fact]
    public void A_fresh_world_is_Normal_and_the_wire_values_are_bedrocks()
    {
        var fx = new IntentTestFixture();
        Assert.Equal(Difficulty.Normal, fx.World.Difficulty.Value);
        Assert.Equal(2, Zenith.Packets.StartGamePacket.DefaultDifficulty);
        Assert.Equal([0, 1, 2, 3], new[] { Difficulty.Peaceful, Difficulty.Easy, Difficulty.Normal, Difficulty.Hard }.Select(d => (int)d));
    }

    [Fact]
    public void Mob_damage_follows_vanillas_scale()
    {
        Assert.Equal(0f, DifficultyRules.ScaleMobDamage(Difficulty.Peaceful, 6f));
        Assert.Equal(4f, DifficultyRules.ScaleMobDamage(Difficulty.Easy, 6f));
        Assert.Equal(1f, DifficultyRules.ScaleMobDamage(Difficulty.Easy, 1f)); // never more than the hit itself
        Assert.Equal(6f, DifficultyRules.ScaleMobDamage(Difficulty.Normal, 6f));
        Assert.Equal(9f, DifficultyRules.ScaleMobDamage(Difficulty.Hard, 6f));
    }

    private static (IntentTestFixture Fx, Player.Player Player, HungerSystem System) Starving(Difficulty difficulty, float health)
    {
        var fx = new IntentTestFixture();
        fx.World.Difficulty.Set(difficulty);
        var player = fx.AddInGamePlayer("starving");
        player.Hunger = 0f;
        _ = player.ApplyDamage(DamageSource.Generic, 20f - health, 0);
        return (fx, player, new HungerSystem(fx.Context.ItemPalette, fx.Players));
    }

    private static void Run(IntentTestFixture fx, HungerSystem system, int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            fx.Clock.AdvanceBy(1);
            system.Tick(fx.Clock, fx.Players.Online);
        }
    }

    [Fact]
    public void Starvation_stops_at_ten_on_Easy()
    {
        var (fx, player, system) = Starving(Difficulty.Easy, 12f);
        Run(fx, system, 400);
        Assert.Equal(10f, player.Health);
        Assert.False(player.IsDead);
    }

    [Fact]
    public void Starvation_stops_at_one_on_Normal()
    {
        var (fx, player, system) = Starving(Difficulty.Normal, 3f);
        Run(fx, system, 400);
        Assert.Equal(1f, player.Health);
        Assert.False(player.IsDead);
    }

    [Fact]
    public void Starvation_kills_on_Hard()
    {
        var (fx, player, system) = Starving(Difficulty.Hard, 3f);
        Run(fx, system, 400);
        Assert.True(player.IsDead);
    }

    [Fact]
    public void Peaceful_refills_hunger_and_heals()
    {
        var (fx, player, system) = Starving(Difficulty.Peaceful, 10f);
        Run(fx, system, 100);
        Assert.Equal(20f, player.Hunger);
        Assert.True(player.Health >= 14f);
    }

    [Fact]
    public void A_mobs_hit_is_scaled_by_the_difficulty_and_a_players_is_not()
    {
        float Hit(Difficulty difficulty, bool fromPlayer)
        {
            var fx = new IntentTestFixture();
            fx.World.Difficulty.Set(difficulty);
            var victim = fx.AddInGamePlayer("victim");
            var attacker = fx.AddInGamePlayer("attacker");
            var owner = fromPlayer ? attacker.RuntimeId : 424242L;
            PlayerDamage.ApplyCore(victim, fx.Players, DamageSource.MeleeFrom(owner), 6f, 100);
            return 20f - victim.Health;
        }

        Assert.Equal(4f, Hit(Difficulty.Easy, fromPlayer: false));
        Assert.Equal(9f, Hit(Difficulty.Hard, fromPlayer: false));
        Assert.Equal(0f, Hit(Difficulty.Peaceful, fromPlayer: false));
        Assert.Equal(6f, Hit(Difficulty.Easy, fromPlayer: true));
    }

    [Fact]
    public void An_arrow_is_not_scaled_by_difficulty_its_own_damage_range_is_one_to_five_on_Hard_and_one_to_four_otherwise()
    {
        var random = new Random(5);
        var normal = Enumerable.Range(0, 400).Select(_ => Zenith.Gameplay.Entities.ProjectileSystem.RollDamage(Difficulty.Normal, random)).ToArray();
        var hard = Enumerable.Range(0, 400).Select(_ => Zenith.Gameplay.Entities.ProjectileSystem.RollDamage(Difficulty.Hard, random)).ToArray();
        Assert.InRange(normal.Min(), 1, 1);
        Assert.Equal(4, normal.Max());
        Assert.Equal(5, hard.Max());

        float Arrow(Difficulty difficulty)
        {
            var fx = new IntentTestFixture();
            fx.World.Difficulty.Set(difficulty);
            var victim = fx.AddInGamePlayer("victim");
            PlayerDamage.ApplyCore(victim, fx.Players, DamageSource.Projectile(424242L), 3f, 100);
            return 20f - victim.Health;
        }

        Assert.Equal(3f, Arrow(Difficulty.Easy));
        Assert.Equal(3f, Arrow(Difficulty.Hard));
        Assert.Equal(0f, Arrow(Difficulty.Peaceful));
    }

    [Fact]
    public void The_difficulty_command_reads_and_changes_it_and_the_sync_tells_the_clients()
    {
        var fx = new IntentTestFixture();
        var op = fx.AddOperator("op");
        var client = new RecordingClientChannel();
        op.AttachClient(client);
        var commands = new CommandRuntime(fx.Players, fx.Context.Permissions, difficulty: fx.World.Difficulty);
        var sync = new WorldStateSyncSystem(fx.World);

        Assert.Contains("Normal", commands.Execute(op, "/difficulty").Message);
        Assert.Contains("Hard", commands.Execute(op, "/difficulty hard").Message);
        Assert.Equal(Difficulty.Hard, fx.World.Difficulty.Value);

        sync.Tick(fx.Clock, fx.Players.Online);
        sync.Tick(fx.Clock, fx.Players.Online);
        Assert.Single(client.Of("SendDifficulty"));
        Assert.Equal(3, client.Of("SendDifficulty").Single()[0]);
    }

    [Fact]
    public void A_player_without_permission_cannot_change_it()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("pleb");
        var commands = new CommandRuntime(fx.Players, fx.Context.Permissions, difficulty: fx.World.Difficulty);

        commands.Execute(player, "/difficulty easy");

        Assert.Equal(Difficulty.Normal, fx.World.Difficulty.Value);
    }

    [Fact]
    public void Monsters_do_not_spawn_on_Peaceful_but_animals_do()
    {
        const string json = """
            {
              "categories": [ { "name": "monster", "capPerPlayer": 3, "intervalTicks": 40 }, { "name": "creature", "capPerPlayer": 3, "intervalTicks": 40 } ],
              "spawns": [
                { "mob": "zombie", "category": "monster", "weight": 10, "herd": { "min_size": 1, "max_size": 1 }, "filters": [ { "test": "is_difficulty", "operator": "!=", "value": "peaceful" } ] },
                { "mob": "cow", "category": "creature", "weight": 10, "herd": { "min_size": 1, "max_size": 1 } }
              ]
            }
            """;
        (int Zombies, int Cows) Count(Difficulty difficulty)
        {
            var fx = new IntentTestFixture();
            fx.World.Difficulty.Set(difficulty);
            fx.AddInGamePlayer("hunted");
            int zombies = 0, cows = 0;
            var spawner = new MobSpawner();
            spawner.Register("zombie", (_, _, _) => zombies++, () => zombies);
            spawner.Register("cow", (_, _, _) => cows++, () => cows);
            var system = new NaturalSpawnSystem(fx.World, spawner, SpawnTable.Parse(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json))), new Random(7));
            for (var round = 0; round < 20; round++)
            {
                fx.Clock.AdvanceBy(40);
                system.Tick(fx.Clock, fx.Players.Online);
                spawner.Tick(fx.Clock, fx.Players.Online);
            }

            return (zombies, cows);
        }

        Assert.True(Count(Difficulty.Normal).Zombies > 0);
        var peaceful = Count(Difficulty.Peaceful);
        Assert.Equal(0, peaceful.Zombies);
        Assert.True(peaceful.Cows > 0);
    }

    [Fact]
    public void The_difficulty_survives_a_restart()
    {
        var storage = new InMemoryChunkStorage();
        var first = new World.World(storage);
        first.Difficulty.Set(Difficulty.Hard);
        WorldStates.Save(first, "difficulty");

        var second = new World.World(storage);
        WorldStates.LoadAll(second);

        Assert.Equal(Difficulty.Hard, second.Difficulty.Value);
    }
}
