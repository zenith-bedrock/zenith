using Xunit;
using Zenith.Gameplay.Runtime;
using Zenith.Gameplay.WorldInteraction.Fluids;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>Plan 004 item 8: flint and steel lights a fire in the open cell its face looks at and wears one point.</summary>
public sealed class FlintAndSteelTests
{
    private const int Floor = 199;
    private const int Level = 200;

    public FlintAndSteelTests() => Blocks.EnsureLoaded();

    private static async Task<(IntentTestFixture Fx, Player.Player Player, BucketSystem System, StackId Item)> Stage(GameMode mode = GameMode.Survival)
    {
        var fx = new IntentTestFixture();
        await fx.World.GetOrCreateColumnAsync(0, 0);
        for (var x = 0; x < 16; x++)
            for (var z = 0; z < 16; z++)
            {
                fx.World.SetBlock(x, Floor, z, Blocks.Stone);
                for (var y = Level; y < Level + 4; y++) fx.World.SetBlock(x, y, z, Blocks.Air);
            }

        var player = fx.AddInGamePlayer("lighter", mode);
        (player.PositionX, player.PositionY, player.PositionZ) = (8.5f, Level, 6.5f);
        var item = StackId.FromItem(fx.Context.ItemPalette.Require("minecraft:flint_and_steel"));
        Assert.True(player.Inventory.TrySet(0, item, 1));
        var system = new BucketSystem(fx.World, fx.Players, new BucketItems(fx.Context.ItemPalette), durability: fx.Context.Durability);
        return (fx, player, system, item);
    }

    private static void Use(IntentTestFixture fx, Player.Player player, BucketSystem system, int x, int y, int z, byte face)
    {
        Assert.True(player.Intents.SubmitBucket(new BucketIntent(x, y, z, face, 0, player.Inventory.Get(0).Id)));
        system.Tick(fx.Clock, fx.Players.Online);
    }

    [Fact]
    public async Task It_lights_fire_on_the_face_that_was_clicked_and_wears_the_item()
    {
        var (fx, player, system, item) = await Stage();

        Use(fx, player, system, 8, Floor, 8, face: 1);

        Assert.True(Blocks.IsFire(fx.World.GetBlock(8, Level, 8)));
        var held = player.Inventory.Get(0);
        Assert.Equal(item.Value, held.Id.Value);
        Assert.Equal(1, held.Id.Damage);
    }

    [Fact]
    public async Task Creative_keeps_the_item_whole()
    {
        var (fx, player, system, item) = await Stage(GameMode.Creative);

        Use(fx, player, system, 8, Floor, 8, face: 1);

        Assert.True(Blocks.IsFire(fx.World.GetBlock(8, Level, 8)));
        Assert.Equal(item, player.Inventory.Get(0).Id);
    }

    [Fact]
    public async Task A_cell_that_is_not_open_is_left_alone_and_the_item_is_not_worn()
    {
        var (fx, player, system, item) = await Stage();
        fx.World.SetBlock(8, Level, 8, Blocks.Stone);

        Use(fx, player, system, 8, Floor, 8, face: 1);

        Assert.Equal(Blocks.Stone, fx.World.GetBlock(8, Level, 8));
        Assert.Equal(item, player.Inventory.Get(0).Id);
    }

    [Fact]
    public async Task Out_of_reach_lights_nothing()
    {
        var (fx, player, system, _) = await Stage();
        player.PositionZ = 0.5f;

        Use(fx, player, system, 8, Floor, 14, face: 1);

        Assert.True(Blocks.IsAir(fx.World.GetBlock(8, Level, 14)));
    }
}
