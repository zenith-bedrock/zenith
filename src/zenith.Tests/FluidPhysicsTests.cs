using Xunit;
using Zenith.Ecs;
using Zenith.Gameplay.Entities;
using Zenith.Gameplay.Runtime;
using Zenith.Gameplay.Survival;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Tests;

public sealed class FluidPhysicsTests
{
    private const int Floor = 199;
    private const int Level = 200;

    public FluidPhysicsTests() => Blocks.EnsureLoaded();

    private static async Task<(IntentTestFixture Fx, Player.Player Player, AirSupplySystem System)> Pool(GameMode mode = GameMode.Survival)
    {
        var fx = new IntentTestFixture();
        await fx.World.GetOrCreateColumnAsync(0, 0);
        for (var x = 0; x < 16; x++)
            for (var z = 0; z < 16; z++)
            {
                fx.World.SetBlock(x, Floor, z, Blocks.Stone);
                for (var y = Level; y < Level + 6; y++) fx.World.SetBlock(x, y, z, Blocks.Air);
            }

        var player = fx.AddInGamePlayer("diver", mode);
        player.PositionX = 8.5f;
        player.PositionY = Level;
        player.PositionZ = 8.5f;
        return (fx, player, new AirSupplySystem(fx.Players));
    }

    private static void Fill(IntentTestFixture fx, int block, int fromY, int toY)
    {
        for (var y = fromY; y <= toY; y++) fx.World.SetBlock(8, y, 8, block);
    }

    private static void Run(IntentTestFixture fx, AirSupplySystem system, int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            fx.Clock.AdvanceBy(1);
            system.Tick(fx.Clock, fx.Players.Online);
        }
    }

    [Fact]
    public async Task Breath_runs_down_with_the_head_under_water_and_refills_at_the_surface()
    {
        var (fx, player, system) = await Pool();
        Fill(fx, Blocks.Water, Level, Level + 2);

        Run(fx, system, 100);
        Assert.Equal(AirSupplySystem.MaxAirSupply - 100, player.AirSupply);

        Fill(fx, Blocks.Air, Level, Level + 2);
        Run(fx, system, 30);
        Assert.Equal(AirSupplySystem.MaxAirSupply, player.AirSupply);
    }

    [Fact]
    public async Task A_player_that_runs_out_of_breath_takes_drowning_damage_about_once_a_second()
    {
        var (fx, player, system) = await Pool();
        Fill(fx, Blocks.Water, Level, Level + 2);
        var before = player.Health;

        Run(fx, system, AirSupplySystem.MaxAirSupply + 25);

        Assert.True(player.Health < before);
        Assert.Equal(before - AirSupplySystem.DrownDamage, player.Health);
    }

    [Fact]
    public async Task Creative_players_do_not_drown()
    {
        var (fx, player, system) = await Pool(GameMode.Creative);
        Fill(fx, Blocks.Water, Level, Level + 2);
        var before = player.Health;

        Run(fx, system, AirSupplySystem.MaxAirSupply + 60);

        Assert.Equal(before, player.Health);
        Assert.Equal(AirSupplySystem.MaxAirSupply, player.AirSupply);
    }

    [Fact]
    public async Task Standing_in_lava_burns()
    {
        var (fx, player, system) = await Pool();
        Fill(fx, Blocks.Lava, Level, Level + 1);
        var before = player.Health;

        Run(fx, system, 5);

        Assert.True(player.Health <= before - AirSupplySystem.LavaDamage);
    }

    [Fact]
    public async Task The_owner_is_told_the_air_supply_only_as_it_changes_enough()
    {
        var (fx, player, system) = await Pool();
        Fill(fx, Blocks.Water, Level, Level + 2);

        Run(fx, system, 25);

        Assert.True(player.Replicated.AirSupply <= AirSupplySystem.MaxAirSupply - 20);
    }

    [Fact]
    public async Task Swimming_needs_a_sprint_and_a_body_under_water()
    {
        var (fx, player, _) = await Pool();
        Fill(fx, Blocks.Water, Level, Level + 2);

        player.IsSprinting = false;
        Assert.False(SubmersionProbe.IsSwimming(player));
        player.IsSprinting = true;
        Assert.True(SubmersionProbe.IsSwimming(player));

        Fill(fx, Blocks.Air, Level + 1, Level + 2);
        Assert.False(SubmersionProbe.IsSwimming(player)); // head above water: a wade, not a swim
    }

    [Fact]
    public async Task A_ground_mob_in_water_floats_up_instead_of_sinking()
    {
        var (fx, _, _) = await Pool();
        Fill(fx, Blocks.Water, Level, Level + 3);
        var y = Level + 0.2f;
        var fall = 0f;

        for (var i = 0; i < 200; i++)
            GroundMobMovement.ResolveVertical(fx.World, 8.5f, 8.5f, ref y, ref fall, out _);

        Assert.True(y >= Level + 3 - 0.01f, $"mob stopped at {y}");
        Assert.True(y < Level + 4);
    }

    [Fact]
    public async Task A_ground_mob_over_land_still_falls_and_lands()
    {
        var (fx, _, _) = await Pool();
        var y = Level + 4f;
        var fall = 0f;

        for (var i = 0; i < 100; i++)
            GroundMobMovement.ResolveVertical(fx.World, 3.5f, 3.5f, ref y, ref fall, out _);

        Assert.Equal(Level, y);
    }

    private static (CowSystem Cow, MobHazardSystem Hazard, EntityId Id) Mob(IntentTestFixture fx, float y)
    {
        var stores = new EntityRuntime();
        var cow = new CowSystem(fx.World, fx.Players, stores, fx.Context.ItemPalette, new Random(1));
        var damage = new DamageDispatch();
        damage.Register(cow.Owns, cow.TryApplyDamage);
        var id = cow.SpawnCow(8.5f, y, 8.5f);
        return (cow, new MobHazardSystem(fx.World, stores, damage, _ => false), id);
    }

    private static float CowHealth(CowSystem cow, EntityId id) =>
        cow.Stores.Health.TryGet(id, out var health) ? health.State.Current : 0f;

    [Fact]
    public async Task A_mob_standing_in_lava_burns_on_the_damage_cadence()
    {
        var (fx, _, _) = await Pool();
        Fill(fx, Blocks.Lava, Level, Level + 1);
        var (cow, hazard, id) = Mob(fx, Level);
        var before = CowHealth(cow, id);

        for (var i = 0; i < 30; i++)
        {
            fx.Clock.AdvanceBy(1);
            hazard.Tick(fx.Clock, fx.Players.Online);
        }

        Assert.True(CowHealth(cow, id) < before, "lava should hurt the mob");
    }

    [Fact]
    public async Task A_mob_with_its_head_under_water_drowns_once_the_breath_is_spent()
    {
        var (fx, _, _) = await Pool();
        Fill(fx, Blocks.Water, Level, Level + 3);
        var (cow, hazard, id) = Mob(fx, Level);
        var before = CowHealth(cow, id);

        for (var i = 0; i < AirSupplySystem.MaxAirSupply - 5; i++)
        {
            fx.Clock.AdvanceBy(1);
            hazard.Tick(fx.Clock, fx.Players.Online);
        }
        Assert.Equal(before, CowHealth(cow, id)); // still holding its breath

        for (var i = 0; i < 40; i++)
        {
            fx.Clock.AdvanceBy(1);
            hazard.Tick(fx.Clock, fx.Players.Online);
        }
        Assert.True(CowHealth(cow, id) < before, "the mob should have drowned a little");
    }

    [Fact]
    public async Task A_mob_on_dry_land_takes_no_liquid_damage()
    {
        var (fx, _, _) = await Pool();
        var (cow, hazard, id) = Mob(fx, Level);
        var before = CowHealth(cow, id);

        for (var i = 0; i < 400; i++)
        {
            fx.Clock.AdvanceBy(1);
            hazard.Tick(fx.Clock, fx.Players.Online);
        }

        Assert.Equal(before, CowHealth(cow, id));
    }
}
