using Xunit;
using Zenith.Gameplay.WorldInteraction;
using Zenith.Modules.Protect;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>
/// Plan 002, phase 3: a block that has lost its support, or is reshaped by a neighbour, asks the environment policies. A protected
/// block stays (even when that leaves it unsupported), nothing drops, and lifting the protection replays nothing by itself.
/// </summary>
public sealed class EnvironmentSupportLossTests
{
    private const int Floor = 199;
    private const int Level = 200;

    public EnvironmentSupportLossTests() => Blocks.EnsureLoaded();

    private static async Task<IntentTestFixture> Ground()
    {
        var fx = new IntentTestFixture();
        await fx.World.GetOrCreateColumnAsync(0, 0);
        for (var x = 0; x < 16; x++)
            for (var z = 0; z < 16; z++)
            {
                fx.World.SetBlock(x, Floor, z, Blocks.Stone);
                for (var y = Level; y < Level + 4; y++) fx.World.SetBlock(x, y, z, Blocks.Air);
            }

        fx.World.BlockUpdates.Clear();
        return fx;
    }

    private static int Torch()
    {
        Assert.True(Blocks.TryGetDefaultRuntimeId("minecraft:torch", out var id));
        Assert.True(Blocks.Catalog.States.TrySet(ref id, StateKeys.TorchFacing, TorchFacing.Top)); // standing: needs the block below
        return id;
    }

    private static void Protect(IntentTestFixture fx, int x, int y, int z) =>
        Assert.True(fx.Context.Protect.Regions.TryAdd(ProtectRegion.Between("vault", x, y, z, x, y, z), out _));

    private static void Run(IntentTestFixture fx, BlockUpdateSystem system, int ticks = 5)
    {
        for (var i = 0; i < ticks; i++)
        {
            fx.Clock.AdvanceBy(1);
            system.Tick(fx.Clock, fx.Players.Online);
        }
    }

    private static BlockUpdateSystem System(IntentTestFixture fx) => new(fx.World, fx.Players, fx.Context.EnvironmentPolicies);

    /// <summary>A torch standing on a stone block; the stone is then removed from outside any region.</summary>
    private static void TorchLosesItsSupport(IntentTestFixture fx)
    {
        fx.World.SetBlock(8, Level - 1, 8, Blocks.Stone);
        fx.World.SetBlock(8, Level, 8, Torch());
        fx.World.BlockUpdates.Clear();
        Assert.True(fx.World.TrySetBlock(8, Level - 1, 8, Blocks.Air));
    }

    [Fact]
    public async Task A_torch_in_a_region_survives_its_support_going_and_nothing_drops()
    {
        var control = await Ground();
        TorchLosesItsSupport(control);
        Run(control, System(control));
        Assert.Equal(Blocks.Air, control.World.GetBlock(8, Level, 8));
        Assert.True(control.World.FloorDrops.Count > 0, "control: the torch breaks and drops");

        var fx = await Ground();
        Protect(fx, 8, Level, 8);
        TorchLosesItsSupport(fx);
        var system = System(fx);
        Run(fx, system);

        Assert.Equal(Torch(), fx.World.GetBlock(8, Level, 8));
        Assert.Equal(0, fx.World.FloorDrops.Count);
        Assert.Equal(Blocks.Air, fx.World.GetBlock(8, Level - 1, 8)); // the support really is gone
    }

    [Fact]
    public async Task It_stays_on_later_neighbour_updates_while_the_region_holds_and_the_world_is_quiet()
    {
        var fx = await Ground();
        Protect(fx, 8, Level, 8);
        TorchLosesItsSupport(fx);
        var system = System(fx);
        Run(fx, system);

        for (var i = 0; i < 3; i++)
        {
            _ = fx.World.BlockUpdates.Enqueue(8, Level - 1, 8);
            Run(fx, system);
            Assert.Equal(Torch(), fx.World.GetBlock(8, Level, 8));
        }

        Assert.Equal(0, fx.World.FloorDrops.Count);
        Assert.Equal(0, fx.World.BlockUpdates.Count);
        Assert.Equal(0, fx.World.ScheduledTicks.Count);
    }

    [Fact]
    public async Task Once_the_region_is_removed_the_torch_breaks_on_the_next_update_and_not_before()
    {
        var fx = await Ground();
        Protect(fx, 8, Level, 8);
        TorchLosesItsSupport(fx);
        var system = System(fx);
        Run(fx, system);

        Assert.True(fx.Context.Protect.Regions.TryRemove("vault"));
        Run(fx, system, 50);
        Assert.Equal(Torch(), fx.World.GetBlock(8, Level, 8)); // lifting the protection replays nothing by itself
        Assert.Equal(0, fx.World.FloorDrops.Count);

        _ = fx.World.BlockUpdates.Enqueue(8, Level - 1, 8);
        Run(fx, system);

        Assert.Equal(Blocks.Air, fx.World.GetBlock(8, Level, 8));
        Assert.True(fx.World.FloorDrops.Count > 0);
    }

    [Fact]
    public async Task A_protected_door_half_stays_while_the_unprotected_half_goes()
    {
        Assert.True(Blocks.TryGetDefaultRuntimeId("minecraft:wooden_door", out var lower));
        var upper = lower;
        Assert.True(Blocks.Catalog.States.TrySet(ref upper, StateKeys.UpperBlock, true));

        var fx = await Ground();
        Protect(fx, 8, Level + 1, 8); // the upper half only
        fx.World.SetBlock(8, Level, 8, lower);
        fx.World.SetBlock(8, Level + 1, 8, upper);
        fx.World.BlockUpdates.Clear();
        Assert.True(fx.World.TrySetBlock(8, Floor, 8, Blocks.Air));
        Run(fx, System(fx), 10);

        // Per cell, as stated: the lower half has no floor and is not protected, so it breaks; the protected half is left.
        Assert.Equal(Blocks.Air, fx.World.GetBlock(8, Level, 8));
        Assert.Equal(upper, fx.World.GetBlock(8, Level + 1, 8));
    }

    [Fact]
    public async Task A_protected_wall_is_not_reshaped_by_a_neighbour_and_an_unprotected_one_is()
    {
        Assert.True(Blocks.TryGetDefaultRuntimeId("minecraft:cobblestone_wall", out var wall));
        var east = StateKeys.WallConnections[(int)Cardinal.East];

        async Task<int> Connected(bool protectedCell)
        {
            var fx = await Ground();
            if (protectedCell) Protect(fx, 8, Level, 8);
            fx.World.SetBlock(8, Level, 8, wall);
            fx.World.SetBlock(9, Level, 8, wall);
            var system = System(fx);
            Run(fx, system);
            Assert.True(Blocks.Catalog.States.TryGet(fx.World.GetBlock(8, Level, 8), east, out WallConnection connection));
            return (int)connection;
        }

        Assert.Equal((int)WallConnection.Short, await Connected(protectedCell: false));
        Assert.Equal((int)WallConnection.None, await Connected(protectedCell: true));
    }
}
