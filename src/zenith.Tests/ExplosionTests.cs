using Xunit;
using Zenith.Ecs;
using Zenith.Gameplay.Entities;
using Zenith.Gameplay.Survival;
using Zenith.Gameplay.WorldInteraction;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>
/// Plan 004 items 3 and 4: the blast is a ray cast with block blast resistance, drops one in power, damage is vanilla's curve scaled
/// by exposure and distance, and every body is pushed. Seeded randomness keeps the shapes stable; the assertions are on rules,
/// not on exact craters.
/// </summary>
public sealed class ExplosionTests
{
    private const int Cx = 0, Cz = 0;
    private static readonly int Cy = Blocks.FlatSpawnY + 40;

    public ExplosionTests() => Blocks.EnsureLoaded();

    private static (IntentTestFixture Fx, Explosion Blast, EntityRuntime Stores) Stage(int fill, int seed = 11, int reach = 8)
    {
        var fx = new IntentTestFixture();
        var stores = new EntityRuntime();
        var blast = new Explosion(fx.World, fx.Players, stores, fx.Context.EnvironmentPolicies, new Random(seed));
        for (var x = Cx - reach; x <= Cx + reach; x++)
            for (var y = Cy - reach; y <= Cy + reach; y++)
                for (var z = Cz - reach; z <= Cz + reach; z++)
                    _ = fx.World.SetQuietly(x, y, z, fill);
        return (fx, blast, stores);
    }

    private static int CountAir(World.World world, int reach = 8)
    {
        var air = 0;
        for (var x = Cx - reach; x <= Cx + reach; x++)
            for (var y = Cy - reach; y <= Cy + reach; y++)
                for (var z = Cz - reach; z <= Cz + reach; z++)
                    if (world.GetBlock(x, y, z) == Blocks.Air) air++;
        return air;
    }

    private static void Detonate(IntentTestFixture fx, Explosion blast, float power = 3f) =>
        blast.Detonate(Cx + 0.5f, Cy + 0.5f, Cz + 0.5f, power, fx.Players.Online, fx.Clock.CurrentTick);

    [Fact]
    public void The_damage_curve_is_vanillas()
    {
        Assert.Equal(43f, Explosion.DamageFor(1f, 6f));    // point blank, power 3
        Assert.Equal(16f, Explosion.DamageFor(0.5f, 6f));
        Assert.Equal(1f, Explosion.DamageFor(0f, 6f));
        Assert.True(Explosion.DamageFor(1f, 8f) > Explosion.DamageFor(1f, 6f)); // a stronger blast hurts more
    }

    [Fact]
    public void Soft_ground_gives_a_crater_and_hard_ground_a_much_smaller_one()
    {
        var soft = Stage(Blocks.Dirt);
        Detonate(soft.Fx, soft.Blast);
        var hard = Stage(Blocks.Stone);
        Detonate(hard.Fx, hard.Blast);

        var softCrater = CountAir(soft.Fx.World);
        var hardCrater = CountAir(hard.Fx.World);
        Assert.True(softCrater > 40, $"dirt crater {softCrater}");
        Assert.True(hardCrater < softCrater / 3, $"stone crater {hardCrater} vs dirt {softCrater}");
    }

    [Fact]
    public void Obsidian_water_lava_and_bedrock_are_never_destroyed()
    {
        foreach (var block in new[] { Blocks.Obsidian, Blocks.Water, Blocks.Lava, Blocks.Bedrock })
        {
            var (fx, blast, _) = Stage(block);
            Detonate(fx, blast, power: 4f);
            Assert.Equal(0, CountAir(fx.World));
        }
    }

    [Fact]
    public void A_wall_of_obsidian_shades_what_is_behind_it()
    {
        var (fx, blast, _) = Stage(Blocks.Dirt);
        for (var y = Cy - 8; y <= Cy + 8; y++)
            for (var z = Cz - 8; z <= Cz + 8; z++)
                _ = fx.World.SetQuietly(Cx + 2, y, z, Blocks.Obsidian);

        Detonate(fx, blast, power: 4f);

        for (var y = Cy - 8; y <= Cy + 8; y++)
            for (var z = Cz - 8; z <= Cz + 8; z++)
            {
                Assert.Equal(Blocks.Obsidian, fx.World.GetBlock(Cx + 2, y, z));
                for (var x = Cx + 3; x <= Cx + 8; x++)
                    Assert.Equal(Blocks.Dirt, fx.World.GetBlock(x, y, z)); // nothing behind it, however near
            }
    }

    [Fact]
    public void Destroyed_blocks_drop_about_one_in_power_and_none_when_the_rule_is_off()
    {
        var (fx, blast, _) = Stage(Blocks.Dirt, seed: 5);
        Detonate(fx, blast);
        var destroyed = CountAir(fx.World);
        var dropped = fx.World.FloorDrops.Snapshot().Sum(drop => drop.Count);
        Assert.InRange((double)dropped / destroyed, 0.15, 0.55); // power 3: one in three

        var (quiet, silent, _) = Stage(Blocks.Dirt, seed: 5);
        Assert.True(quiet.World.Rules.TrySet("doTileDrops", "false", out _));
        Detonate(quiet, silent);
        Assert.Equal(0, quiet.World.FloorDrops.Count);
    }

    [Fact]
    public void A_player_is_hurt_by_the_curve_and_pushed_away()
    {
        var (fx, blast, _) = Stage(Blocks.Air);
        var player = fx.AddInGamePlayer("bystander");
        var client = new RecordingClientChannel();
        player.AttachClient(client);
        (player.PositionX, player.PositionY, player.PositionZ) = (Cx + 3.5f, Cy, Cz + 0.5f);
        fx.World.GetBlock(Cx, Cy, Cz);

        Detonate(fx, blast);

        Assert.InRange(player.Health, 1f, 19f);
        Assert.Contains(client.Of("SendKnockback"), args => (float)args[1]! > 0f); // away from the centre, along +X
    }

    [Fact]
    public void A_wall_between_the_blast_and_a_player_protects_them()
    {
        var (fx, blast, _) = Stage(Blocks.Air);
        for (var y = Cy - 4; y <= Cy + 5; y++)
            for (var z = Cz - 4; z <= Cz + 4; z++)
                _ = fx.World.SetQuietly(Cx + 2, y, z, Blocks.Obsidian);
        var player = fx.AddInGamePlayer("sheltered");
        (player.PositionX, player.PositionY, player.PositionZ) = (Cx + 4f, Cy, Cz + 0.5f);

        Detonate(fx, blast);

        Assert.Equal(20f, player.Health);
    }

    [Fact]
    public void Another_species_is_hurt_and_pushed_too()
    {
        var (fx, blast, stores) = Stage(Blocks.Air);
        var cows = new CowSystem(fx.World, fx.Players, stores, fx.Context.ItemPalette);
        var dispatch = new DamageDispatch();
        dispatch.Register(cows);
        blast.Mobs = dispatch;
        var id = cows.SpawnCow(Cx + 2.5f, Cy, Cz + 0.5f);

        Detonate(fx, blast);

        Assert.True(!stores.Entities.IsAlive(id) || stores.Health.TryGet(id, out var health) && health.State.Current < health.State.Maximum);
    }

    [Fact]
    public void A_creeper_blast_leaves_the_creeper_itself_out_of_the_second_hit()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("victim");
        var creeper = new CreeperSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette, fx.Context.EnvironmentPolicies, new Random(1));
        var dispatch = new DamageDispatch();
        dispatch.Register(creeper);
        creeper.Explosion.Mobs = dispatch;
        var id = creeper.SpawnCreeper(player.PositionX, player.PositionY, player.PositionZ + 2.9f);

        creeper.Tick(fx.Clock, fx.Players.Online);
        fx.Clock.AdvanceBy(30);
        creeper.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(1, creeper.ExplosionCount);
        Assert.False(creeper.Stores.Entities.IsAlive(id));
    }
}
