using Xunit;
using Zenith.Gameplay.WorldInteraction;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Tests;

public class BlockShapesTests
{
    private static BlockStateTable States => Blocks.Catalog.States;

    private static int Id(string name)
    {
        Assert.True(Blocks.TryGetDefaultRuntimeId(name, out var id), name);
        return id;
    }

    private static int With(int id, BitKey key, bool value)
    {
        Assert.True(States.TrySet(ref id, key, value));
        return id;
    }

    private static int With<T>(int id, EnumKey<T> key, T value) where T : unmanaged, Enum
    {
        Assert.True(States.TrySet(ref id, key, value));
        return id;
    }

    [Theory]
    [InlineData("minecraft:wooden_door", "Door")]
    [InlineData("minecraft:iron_door", "Door")]
    [InlineData("minecraft:trapdoor", "Trapdoor")]
    [InlineData("minecraft:fence_gate", "FenceGate")]
    [InlineData("minecraft:cobblestone_wall", "Wall")]
    public void Interactive_families_are_classified(string name, string kind) =>
        Assert.Equal(Enum.Parse<BlockKind>(kind), Blocks.Catalog.KindOf(Id(name)));

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

        Assert.False(States.Bit(fx.World.GetBlock(1, 64, 0), StateKeys.UpperBlock));
        Assert.True(States.Bit(fx.World.GetBlock(1, 65, 0), StateKeys.UpperBlock));
        Assert.Equal(2, player.Inventory.Get(0).Count);

        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.Toggle(1, 65, 0))); // click the upper half
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.True(States.Bit(fx.World.GetBlock(1, 64, 0), StateKeys.Open));
        Assert.True(States.Bit(fx.World.GetBlock(1, 65, 0), StateKeys.Open));
        Assert.True(Blocks.IsPassableDecoration(fx.World.GetBlock(1, 64, 0)));

        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.Toggle(1, 64, 0)));
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.False(States.Bit(fx.World.GetBlock(1, 64, 0), StateKeys.Open));
        Assert.False(States.Bit(fx.World.GetBlock(1, 65, 0), StateKeys.Open));
    }

    [Fact]
    public void Iron_doors_ignore_the_hand()
    {
        var fx = new IntentTestFixture();
        var door = Id("minecraft:iron_door");
        fx.World.SetBlock(1, 64, 0, door);
        var writes = new List<BlockWrite>();
        Assert.False(BlockShapes.TryInteract(fx.World, 1, 64, 0, writes));
        Assert.Empty(writes);
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
        var wall = Id("minecraft:cobblestone_wall");
        var update = new BlockUpdateSystem(fx.World, fx.Players);
        var east = StateKeys.WallConnections[(int)Cardinal.East];
        var west = StateKeys.WallConnections[(int)Cardinal.West];

        fx.World.SetBlock(1, 64, 1, wall);
        fx.World.SetBlock(2, 64, 1, wall);
        update.Tick(fx.Clock, fx.Players.Online);

        Assert.True(States.TryGet(fx.World.GetBlock(1, 64, 1), east, out WallConnection a) && a == WallConnection.Short);
        Assert.True(States.TryGet(fx.World.GetBlock(2, 64, 1), west, out WallConnection b) && b == WallConnection.Short);
        Assert.True(States.TryGet(fx.World.GetBlock(2, 64, 1), east, out WallConnection c) && c == WallConnection.None);

        fx.World.SetBlock(2, 64, 1, Blocks.Air);
        update.Tick(fx.Clock, fx.Players.Online);
        Assert.True(States.TryGet(fx.World.GetBlock(1, 64, 1), east, out WallConnection d) && d == WallConnection.None);
    }

    [Fact]
    public void A_wall_running_straight_has_no_post_until_something_sits_on_it()
    {
        var fx = new IntentTestFixture();
        var wall = Id("minecraft:cobblestone_wall");
        var update = new BlockUpdateSystem(fx.World, fx.Players);
        for (var x = 0; x < 3; x++) fx.World.SetBlock(x, 64, 4, wall);
        update.Tick(fx.Clock, fx.Players.Online);
        Assert.False(States.Bit(fx.World.GetBlock(1, 64, 4), StateKeys.WallPost));

        fx.World.SetBlock(1, 65, 4, Blocks.Stone);
        update.Tick(fx.Clock, fx.Players.Online);
        Assert.True(States.Bit(fx.World.GetBlock(1, 64, 4), StateKeys.WallPost));
        Assert.True(States.TryGet(fx.World.GetBlock(1, 64, 4), StateKeys.WallConnections[(int)Cardinal.East], out WallConnection tall) &&
                    tall == WallConnection.Tall);
    }

    [Fact]
    public void A_second_slab_merges_into_a_double_slab_and_drops_two_when_broken()
    {
        var slabBottom = Id("minecraft:oak_slab");
        var slabTop = With(slabBottom, StateKeys.VerticalHalf, VerticalHalf.Top);

        Assert.True(BlockShapes.TryMergeSlab(slabBottom, slabBottom, requireOppositeHalf: false, (byte)Face.Up, out var viaTopFace));
        Assert.False(BlockShapes.TryMergeSlab(slabBottom, slabBottom, requireOppositeHalf: false, (byte)Face.Down, out _), "bottom face of a bottom slab opens the other half");
        Assert.True(BlockShapes.TryMergeSlab(slabBottom, slabTop, requireOppositeHalf: true, null, out var viaNeighbour));
        Assert.False(BlockShapes.TryMergeSlab(slabBottom, slabBottom, requireOppositeHalf: true, null, out _), "same half cannot merge");
        Assert.Equal(viaTopFace, viaNeighbour);
        Assert.True(BlockShapes.IsSlabMerge(slabBottom, viaTopFace));
        Assert.False(BlockShapes.TryMergeSlab(slabBottom, Id("minecraft:spruce_slab"), false, (byte)Face.Up, out _), "different slab types never merge");
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
        var upper = With(door, StateKeys.UpperBlock, true);
        fx.World.SetBlock(1, 64, 0, door);
        fx.World.SetBlock(1, 65, 0, upper);
        var writes = new List<BlockWrite>();
        BlockShapes.OnRemoved(fx.World, 1, 64, 0, door, writes);
        Assert.Contains(writes, w => w is { X: 1, Y: 65, Z: 0 } && w.BlockRuntimeId == Blocks.Air);
    }
}
