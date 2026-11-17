using Xunit;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>Plan 004 item 26: the on-demand light model (block light, sky light, the sun's share).</summary>
public sealed class LightModelTests
{
    private const int Floor = 199;
    private const int Level = 200;

    public LightModelTests() => Blocks.EnsureLoaded();

    private static async Task<World.World> Stage()
    {
        var fx = new IntentTestFixture();
        await fx.World.GetOrCreateColumnAsync(0, 0);
        await fx.World.GetOrCreateColumnAsync(1, 0);
        for (var x = 0; x < 32; x++)
            for (var z = 0; z < 16; z++)
            {
                fx.World.SetBlock(x, Floor, z, Blocks.Stone);
                for (var y = Level; y < Level + 12; y++) fx.World.SetBlock(x, y, z, Blocks.Air);
            }

        return fx.World;
    }

    private static int Torch() => Blocks.TryGetDefaultRuntimeId("minecraft:torch", out var id) ? id : throw new InvalidOperationException("no torch");

    [Fact]
    public void The_sun_takes_nothing_by_day_and_eleven_levels_at_midnight()
    {
        Assert.Equal(0, LightModel.SkyDarkness(1000));
        Assert.Equal(0, LightModel.SkyDarkness(6000));
        Assert.Equal(11, LightModel.SkyDarkness(18000));
        Assert.InRange(LightModel.SkyDarkness(13000), 1, 10);
        Assert.InRange(LightModel.SkyDarkness(23500), 1, 10);
    }

    [Fact]
    public async Task A_cell_open_to_the_sky_has_full_sky_light_and_follows_the_time_of_day()
    {
        var world = await Stage();
        Assert.Equal(15, LightModel.SkyLight(world, 8, Level, 8));
        Assert.True(LightModel.SeesSky(world, 8, Level, 8));
        Assert.Equal(15, LightModel.Brightness(world, 8, Level, 8, worldTime: 6000));
        Assert.Equal(4, LightModel.Brightness(world, 8, Level, 8, worldTime: 18000));
    }

    [Fact]
    public async Task Under_a_roof_sky_light_leaks_in_from_the_open_edge_and_fades_with_distance()
    {
        var world = await Stage();
        for (var x = 0; x < 16; x++)
            for (var z = 0; z < 16; z++) world.SetBlock(x, Level + 2, z, Blocks.Stone); // a roof over the first chunk, open past x = 15

        Assert.False(LightModel.SeesSky(world, 8, Level, 8));
        var near = LightModel.SkyLight(world, 15, Level, 8);
        var far = LightModel.SkyLight(world, 8, Level, 8);
        Assert.InRange(near, 13, 14); // one cell in from the open edge
        Assert.True(far < near && far > 0, $"near {near} far {far}");
    }

    [Fact]
    public async Task A_sealed_room_is_dark_and_a_torch_lights_it_by_distance()
    {
        var world = await Stage();
        for (var x = 4; x <= 12; x++)
            for (var y = Level - 1; y <= Level + 4; y++)
                for (var z = 4; z <= 12; z++)
                    world.SetBlock(x, y, z, x is 4 or 12 || z is 4 or 12 || y is Level - 1 or Level + 4 ? Blocks.Stone : Blocks.Air);

        Assert.Equal(0, LightModel.Brightness(world, 8, Level, 8, worldTime: 6000));

        world.SetBlock(8, Level, 8, Torch());
        Assert.Equal(14, LightModel.BlockLight(world, 8, Level, 8));
        Assert.Equal(13, LightModel.BlockLight(world, 9, Level, 8));
        Assert.Equal(11, LightModel.BlockLight(world, 11, Level, 8));
    }

    [Fact]
    public async Task Light_does_not_pass_through_a_wall()
    {
        var world = await Stage();
        world.SetBlock(8, Level, 8, Torch());
        for (var y = Level - 1; y < Level + 12; y++)
            for (var z = 0; z < 16; z++) world.SetBlock(10, y, z, Blocks.Stone);

        Assert.Equal(13, LightModel.BlockLight(world, 9, Level, 8));
        Assert.True(LightModel.BlockLight(world, 12, Level, 8) < 8, "behind a full wall only what goes around it arrives");
    }

    [Fact]
    public async Task An_opaque_emitter_lights_its_neighbours()
    {
        var world = await Stage();
        Assert.True(Blocks.TryGetDefaultRuntimeId("minecraft:glowstone", out var glowstone));
        world.SetBlock(8, Level, 8, glowstone);

        Assert.Equal(14, LightModel.BlockLight(world, 9, Level, 8));
        Assert.Equal(12, LightModel.BlockLight(world, 11, Level, 8));
    }
}
