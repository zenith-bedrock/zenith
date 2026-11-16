using Xunit;
using Zenith.Gameplay.WorldInteraction;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Tests;

public class BlockShapesTests
{
    private static int Id(string name)
    {
        Assert.True(Blocks.TryGetDefaultRuntimeId(name, out var id), name);
        return id;
    }

    private static string? State(int id, string key) =>
        Blocks.Palette.TryGetState(id, out var state) ? state.Get(key) : null;

    [Theory]
    [InlineData("minecraft:wooden_door", "Door")]
    [InlineData("minecraft:iron_door", "Door")]
    [InlineData("minecraft:trapdoor", "Trapdoor")]
    [InlineData("minecraft:oak_fence_gate", "FenceGate")]
    [InlineData("minecraft:cobblestone_wall", "Wall")]
    public void Interactive_families_are_classified(string name, string kind)
    {
        Blocks.TryGetDefaultRuntimeId(name, out var id);
        if (id == 0) id = Id(name.Replace("oak_fence_gate", "fence_gate"));
        Assert.Equal(Enum.Parse<BlockKind>(kind), Blocks.Catalog.KindOf(id));
    }

    [Fact]
    public void Placing_a_door_writes_both_halves_and_toggling_opens_both()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("carpenter");
        IntentTestHelpers.StandNear(player, 3, 65, 0);
        var door = Id("minecraft:wooden_door");
        Assert.True(player.Inventory.TrySetBlock(0, door, 3));
        fx.World.SetBlock(1, 63, 0, Blocks.Stone);

        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.Set(1, 64, 0, door, hotbarSlot: 0)));
        var system = new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability);
        system.Tick(fx.Clock, fx.Players.Online);

        var lower = fx.World.GetBlock(1, 64, 0);
        var upper = fx.World.GetBlock(1, 65, 0);
        Assert.Equal("0", State(lower, "upper_block_bit"));
        Assert.Equal("1", State(upper, "upper_block_bit"));
        Assert.Equal(2, player.Inventory.Get(0).Count);

        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.Toggle(1, 65, 0))); // click the upper half
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal("1", State(fx.World.GetBlock(1, 64, 0), "open_bit"));
        Assert.Equal("1", State(fx.World.GetBlock(1, 65, 0), "open_bit"));
        Assert.True(Blocks.IsPassableDecoration(fx.World.GetBlock(1, 64, 0)) || Blocks.Catalog.IsPassable(fx.World.GetBlock(1, 64, 0)));

        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.Toggle(1, 64, 0)));
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal("0", State(fx.World.GetBlock(1, 64, 0), "open_bit"));
    }

    [Fact]
    public void A_door_without_headroom_is_refused_and_keeps_the_item()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("carpenter");
        IntentTestHelpers.StandNear(player, 3, 65, 0);
        var door = Id("minecraft:wooden_door");
        Assert.True(player.Inventory.TrySetBlock(0, door, 1));
        fx.World.SetBlock(1, 65, 0, Blocks.Stone);

        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.Set(1, 64, 0, door, hotbarSlot: 0)));
        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.Air, fx.World.GetBlock(1, 64, 0));
        Assert.Equal(1, player.Inventory.Get(0).Count);
    }

    [Fact]
    public void Walls_connect_to_each_other_and_disconnect_when_a_neighbour_goes()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("mason");
        IntentTestHelpers.StandNear(player, 3, 66, 3);
        var wall = Id("minecraft:cobblestone_wall");
        Assert.True(player.Inventory.TrySetBlock(0, wall, 5));
        var system = new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability);

        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.Set(1, 64, 1, wall, hotbarSlot: 0)));
        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.Set(2, 64, 1, wall, hotbarSlot: 0)));
        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal("short", State(fx.World.GetBlock(1, 64, 1), "wall_connection_type_east"));
        Assert.Equal("short", State(fx.World.GetBlock(2, 64, 1), "wall_connection_type_west"));
        Assert.Equal("none", State(fx.World.GetBlock(2, 64, 1), "wall_connection_type_east"));

        var writes = new List<BlockShapes.Write>();
        fx.World.SetBlock(2, 64, 1, Blocks.Air);
        BlockShapes.AfterRemoval(fx.World, 2, 64, 1, wall, writes);
        Assert.Contains(writes, w => w is { X: 1, Y: 64, Z: 1 } && State(w.BlockRuntimeId, "wall_connection_type_east") == "none");
    }

    [Fact]
    public void A_second_slab_merges_into_a_double_slab_and_drops_two_when_broken()
    {
        var slabBottom = Id("minecraft:oak_slab");
        var slabTop = BlockShapes.WithState(slabBottom, ("minecraft:vertical_half", "top"))!.Value;

        Assert.True(BlockShapes.TryMergeInto(slabBottom, slabBottom, requireOppositeHalf: false, 1, out var viaTopFace));
        Assert.False(BlockShapes.TryMergeInto(slabBottom, slabBottom, requireOppositeHalf: false, 0, out _), "bottom face of a bottom slab opens the other half");
        Assert.True(BlockShapes.TryMergeInto(slabBottom, slabTop, requireOppositeHalf: true, null, out var viaNeighbour));
        Assert.False(BlockShapes.TryMergeInto(slabBottom, slabBottom, requireOppositeHalf: true, null, out _), "same half cannot merge");
        Assert.Equal(viaTopFace, viaNeighbour);
        Assert.True(BlockShapes.IsSlabMerge(slabBottom, viaTopFace));
        Assert.False(BlockShapes.TryMergeInto(slabBottom, Id("minecraft:spruce_slab"), false, 1, out _), "different slab types never merge");
        Assert.Equal(2, BlockLoot.DropOf(viaTopFace).Count);
    }

    [Fact]
    public void Door_drops_the_door_item_and_breaking_one_half_removes_the_other()
    {
        var door = Id("minecraft:wooden_door");
        var drop = BlockLoot.DropOf(door);
        Assert.True(drop.Id.IsItem);
        Assert.Equal(1, drop.Count);

        var fx = new IntentTestFixture();
        var lower = door;
        var upper = BlockShapes.WithState(door, ("upper_block_bit", "1"))!.Value;
        fx.World.SetBlock(1, 64, 0, lower);
        fx.World.SetBlock(1, 65, 0, upper);
        var writes = new List<BlockShapes.Write>();
        BlockShapes.AfterRemoval(fx.World, 1, 64, 0, lower, writes);
        Assert.Contains(writes, w => w is { X: 1, Y: 65, Z: 0 } && w.BlockRuntimeId == Blocks.Air);
    }
}
