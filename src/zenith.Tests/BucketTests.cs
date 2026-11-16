using Zenith.Gameplay.Inventory;
using Xunit;
using Zenith.Gameplay.Runtime;
using Zenith.Gameplay.WorldInteraction;
using Zenith.Gameplay.WorldInteraction.Fluids;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Tests;

public sealed class BucketTests
{
    private const int Floor = 199;
    private const int Level = 200;

    public BucketTests() => Blocks.EnsureLoaded();

    private sealed record Scene(IntentTestFixture Fx, Player.Player Player, BucketItems Buckets, BucketSystem System);

    private static async Task<Scene> Build(GameMode mode = GameMode.Survival)
    {
        var fx = new IntentTestFixture();
        await fx.World.GetOrCreateColumnAsync(0, 0);
        for (var x = 0; x < 16; x++)
            for (var z = 0; z < 16; z++)
            {
                fx.World.SetBlock(x, Floor, z, Blocks.Stone);
                for (var y = Level; y < Level + 4; y++) fx.World.SetBlock(x, y, z, Blocks.Air);
            }

        var player = fx.AddInGamePlayer("bucketeer", mode);
        player.PositionX = 8.5f;
        player.PositionY = Level;
        player.PositionZ = 6.5f;
        var buckets = new BucketItems(fx.Context.ItemPalette);
        return new Scene(fx, player, buckets, new BucketSystem(fx.World, fx.Players, buckets));
    }

    private static void Use(Scene s, int x, int y, int z, byte face, int slot = 0)
    {
        var held = s.Player.Inventory.Get(slot);
        Assert.True(s.Player.Intents.SubmitBucket(new BucketIntent(x, y, z, face, slot, held.Id)));
        s.System.Tick(s.Fx.Clock, s.Fx.Players.Online);
    }

    private static int Depth(Scene s, int x, int y, int z)
    {
        var id = s.Fx.World.GetBlock(x, y, z);
        return Blocks.IsFluid(id) && Blocks.Catalog.States.TryGet(id, StateKeys.LiquidDepth, out var depth) ? depth : -1;
    }

    [Fact]
    public async Task A_water_bucket_pours_a_source_on_the_face_that_was_clicked_and_comes_back_empty()
    {
        var s = await Build();
        Assert.True(s.Player.Inventory.TrySet(0, s.Buckets.Water.Item, 1));

        Use(s, 8, Floor, 8, face: 1);

        Assert.Equal(0, Depth(s, 8, Level, 8));
        Assert.Equal(s.Buckets.Empty, s.Player.Inventory.Get(0).Id);
    }

    [Fact]
    public async Task An_empty_bucket_scoops_a_source_up_and_leaves_the_cell_empty()
    {
        var s = await Build();
        s.Fx.World.SetBlock(8, Level, 8, Blocks.Water);
        Assert.True(s.Player.Inventory.TrySet(0, s.Buckets.Empty, 1));

        Use(s, 8, Level, 8, face: 1);

        Assert.True(Blocks.IsAir(s.Fx.World.GetBlock(8, Level, 8)));
        Assert.Equal(s.Buckets.Water.Item, s.Player.Inventory.Get(0).Id);
    }

    [Fact]
    public async Task An_empty_bucket_does_not_take_flowing_water()
    {
        var s = await Build();
        var flowing = Blocks.Water;
        Assert.True(Blocks.Catalog.States.TrySet(ref flowing, StateKeys.LiquidDepth, 3));
        s.Fx.World.SetBlock(8, Level, 8, flowing);
        Assert.True(s.Player.Inventory.TrySet(0, s.Buckets.Empty, 1));

        Use(s, 8, Level, 8, face: 1);

        Assert.Equal(3, Depth(s, 8, Level, 8));
        Assert.Equal(s.Buckets.Empty, s.Player.Inventory.Get(0).Id);
    }

    [Fact]
    public async Task One_bucket_of_a_stack_is_filled_and_the_full_one_goes_to_the_inventory()
    {
        var s = await Build();
        s.Fx.World.SetBlock(8, Level, 8, Blocks.Water);
        Assert.True(s.Player.Inventory.TrySet(0, s.Buckets.Empty, 3));

        Use(s, 8, Level, 8, face: 1);

        Assert.Equal(2, s.Player.Inventory.Get(0).Count);
        Assert.Equal(s.Buckets.Empty, s.Player.Inventory.Get(0).Id);
        Assert.True(s.Player.Inventory.TryConsume(s.Buckets.Water.Item, 1)); // it is somewhere in the inventory
    }

    [Fact]
    public async Task Creative_players_keep_their_bucket()
    {
        var s = await Build(GameMode.Creative);
        Assert.True(s.Player.Inventory.TrySet(0, s.Buckets.Water.Item, 1));

        Use(s, 8, Floor, 8, face: 1);

        Assert.Equal(0, Depth(s, 8, Level, 8));
        Assert.Equal(s.Buckets.Water.Item, s.Player.Inventory.Get(0).Id);
    }

    [Fact]
    public async Task A_bucket_out_of_reach_or_not_in_the_slot_the_client_named_changes_nothing()
    {
        var s = await Build();
        Assert.True(s.Player.Inventory.TrySet(0, s.Buckets.Water.Item, 1));

        Use(s, 8, Floor, 8 + 40, face: 1); // far away
        Assert.Equal(s.Buckets.Water.Item, s.Player.Inventory.Get(0).Id);

        Assert.True(s.Player.Intents.SubmitBucket(new BucketIntent(8, Floor, 8, 1, 0, s.Buckets.Empty)));
        s.System.Tick(s.Fx.Clock, s.Fx.Players.Online);
        Assert.Equal(-1, Depth(s, 8, Level, 8));
        Assert.Equal(s.Buckets.Water.Item, s.Player.Inventory.Get(0).Id);
    }

    [Fact]
    public async Task Poured_water_flows_on_through_the_block_update_system()
    {
        var s = await Build();
        Assert.True(s.Player.Inventory.TrySet(0, s.Buckets.Water.Item, 1));
        Use(s, 8, Floor, 8, face: 1);

        var updates = new BlockUpdateSystem(s.Fx.World, s.Fx.Players);
        for (var i = 0; i < 20; i++)
        {
            s.Fx.Clock.AdvanceBy(1);
            updates.Tick(s.Fx.Clock, s.Fx.Players.Online);
        }

        Assert.Equal(1, Depth(s, 9, Level, 8));
    }

    private static StackId ItemOf(Scene s, string name) => StackId.FromItem(s.Fx.Context.ItemPalette.Require(name));

    [Fact]
    public async Task A_lava_bucket_pours_lava_and_an_empty_one_takes_it_back()
    {
        var s = await Build();
        Assert.True(s.Player.Inventory.TrySet(0, ItemOf(s, ItemNames.LavaBucket), 1));

        Use(s, 8, Floor, 8, face: 1);
        Assert.Equal(Blocks.Lava, s.Fx.World.GetBlock(8, Level, 8));
        Assert.Equal(s.Buckets.Empty, s.Player.Inventory.Get(0).Id);

        Use(s, 8, Level, 8, face: 1);
        Assert.True(Blocks.IsAir(s.Fx.World.GetBlock(8, Level, 8)));
        Assert.Equal(ItemOf(s, ItemNames.LavaBucket), s.Player.Inventory.Get(0).Id);
    }

    [Fact]
    public async Task Powder_snow_goes_into_open_space_only_and_can_be_scooped_again()
    {
        var s = await Build();
        Assert.True(Blocks.TryGetDefaultRuntimeId("minecraft:powder_snow", out var snow));
        var full = ItemOf(s, ItemNames.PowderSnowBucket);
        Assert.True(s.Player.Inventory.TrySet(0, full, 1));

        Use(s, 8, Floor, 8, face: 1);
        Assert.Equal(snow, s.Fx.World.GetBlock(8, Level, 8));

        Assert.True(s.Player.Inventory.TrySet(1, full, 1));
        Use(s, 8, Level, 8, face: 1, slot: 1); // aimed at the snow itself: it is no open space, and the cell it faces is
        Assert.Equal(snow, s.Fx.World.GetBlock(8, Level + 1, 8));

        Assert.True(s.Player.Inventory.TrySet(2, s.Buckets.Empty, 1));
        Use(s, 8, Level + 1, 8, face: 1, slot: 2);
        Assert.Equal(full, s.Player.Inventory.Get(2).Id);
    }

    [Fact]
    public async Task Lava_poured_beside_water_sets_into_stone_at_once()
    {
        var s = await Build();
        s.Fx.World.SetBlock(9, Level, 8, Blocks.Water);
        Assert.True(s.Player.Inventory.TrySet(0, ItemOf(s, ItemNames.LavaBucket), 1));
        Use(s, 8, Floor, 8, face: 1);

        var updates = new BlockUpdateSystem(s.Fx.World, s.Fx.Players);
        for (var i = 0; i < 3; i++)
        {
            s.Fx.Clock.AdvanceBy(1);
            updates.Tick(s.Fx.Clock, s.Fx.Players.Online);
        }

        Assert.Equal(Blocks.Obsidian, s.Fx.World.GetBlock(8, Level, 8));
    }
}
