using Xunit;
using Zenith.Ecs;
using Zenith.Gameplay.Entities;
using Zenith.Gameplay.Survival;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>Plan 004 item 8: fire and lava light a body, flames hurt once a second, water puts them out.</summary>
public sealed class BurningTests
{
    private const int Floor = 199;
    private const int Level = 200;

    public BurningTests() => Blocks.EnsureLoaded();

    private static async Task<(IntentTestFixture Fx, Player.Player Player, BurnSystem System, RecordingClientChannel Client)> Stage(GameMode mode = GameMode.Survival)
    {
        var fx = new IntentTestFixture();
        await fx.World.GetOrCreateColumnAsync(0, 0);
        for (var x = 0; x < 16; x++)
            for (var z = 0; z < 16; z++)
            {
                fx.World.SetBlock(x, Floor, z, Blocks.Stone);
                for (var y = Level; y < Level + 4; y++) fx.World.SetBlock(x, y, z, Blocks.Air);
            }

        var player = fx.AddInGamePlayer("walker", mode);
        var client = new RecordingClientChannel();
        player.AttachClient(client);
        (player.PositionX, player.PositionY, player.PositionZ) = (8.5f, Level, 8.5f);
        return (fx, player, new BurnSystem(fx.Players, fx.World), client);
    }

    private static void Run(IntentTestFixture fx, BurnSystem system, int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            fx.Clock.AdvanceBy(1);
            system.Tick(fx.Clock, fx.Players.Online);
        }
    }

    [Fact]
    public void Contact_renews_the_time_but_never_shortens_it_and_water_ends_it()
    {
        Assert.Equal(159, Burning.Step(0, Burning.FireIgniteTicks, 1, out _));
        Assert.Equal(299, Burning.Step(100, Burning.LavaIgniteTicks, 1, out _));
        Assert.Equal(199, Burning.Step(200, Burning.FireIgniteTicks, 1, out _));
        Assert.Equal(0, Burning.Step(100, Burning.Extinguish, 1, out _));
        Burning.Step(50, 0, Burning.DamageIntervalTicks, out var hurt);
        Assert.True(hurt);
        Burning.Step(50, 0, Burning.DamageIntervalTicks + 1, out hurt);
        Assert.False(hurt);
    }

    [Fact]
    public async Task Stepping_into_fire_sets_a_survival_player_alight_and_the_flames_hurt_once_a_second()
    {
        var (fx, player, system, client) = await Stage();
        fx.World.SetBlock(8, Level, 8, Blocks.Fire);

        Run(fx, system, 1);
        Assert.True(player.BurnTicks > 0);
        Assert.NotEmpty(client.Of("SendActorFlags")); // the flames were announced

        fx.World.SetBlock(8, Level, 8, Blocks.Air);
        var before = player.Health;
        Run(fx, system, 60);
        Assert.Equal(before - 3f * Burning.Damage, player.Health, 0.01f);
    }

    [Fact]
    public async Task The_flames_burn_out_after_their_time()
    {
        var (fx, player, system, _) = await Stage();
        fx.World.SetBlock(8, Level, 8, Blocks.Fire);
        Run(fx, system, 1);
        fx.World.SetBlock(8, Level, 8, Blocks.Air);

        Run(fx, system, Burning.FireIgniteTicks + 5);

        Assert.Equal(0, player.BurnTicks);
    }

    [Fact]
    public async Task Lava_burns_longer_than_fire_and_water_puts_the_player_out()
    {
        var (fx, player, system, _) = await Stage();
        fx.World.SetBlock(8, Level, 8, Blocks.Lava);
        Run(fx, system, 1);
        Assert.True(player.BurnTicks > Burning.FireIgniteTicks);

        fx.World.SetBlock(8, Level, 8, Blocks.Water);
        Run(fx, system, 1);
        Assert.Equal(0, player.BurnTicks);
    }

    [Fact]
    public async Task A_creative_player_never_burns()
    {
        var (fx, player, system, _) = await Stage(GameMode.Creative);
        fx.World.SetBlock(8, Level, 8, Blocks.Fire);

        Run(fx, system, 30);

        Assert.Equal(0, player.BurnTicks);
        Assert.Equal(20f, player.Health);
    }

    [Fact]
    public async Task A_mob_in_fire_burns_takes_damage_and_its_viewers_are_told()
    {
        var (fx, _, _, client) = await Stage();
        var stores = new EntityRuntime();
        var cow = new CowSystem(fx.World, fx.Players, stores, fx.Context.ItemPalette, new Random(1));
        var damage = new DamageDispatch();
        damage.Register(cow.Owns, cow.TryApplyDamage);
        var id = cow.SpawnCow(3.5f, Level, 3.5f);
        fx.World.SetBlock(3, Level, 3, Blocks.Fire);
        var hazard = new MobHazardSystem(fx.World, stores, damage, _ => false);
        cow.Tick(fx.Clock, fx.Players.Online); // the species system gives the mob its identity when it first replicates
        stores.Health.TryGet(id, out var start);
        var startHealth = start.State.Current; // the state is shared, not copied

        for (var i = 0; i < 45; i++)
        {
            fx.Clock.AdvanceBy(1);
            hazard.Tick(fx.Clock, fx.Players.Online);
        }

        Assert.True(hazard.BurnTicksOf(id) > 0);
        Assert.True(stores.Health.TryGet(id, out var now) && now.State.Current < startHealth);
        Assert.Contains(client.Of("SendActorBurning"), args => (bool)args[1]!);
    }
}
