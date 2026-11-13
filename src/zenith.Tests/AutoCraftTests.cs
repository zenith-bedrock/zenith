using Xunit;
using Zenith.Binary;
using Zenith.Gameplay.Inventory;
using Zenith.Packets;
using Zenith.Player;
using Zenith.Protocol;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>
/// Recipe-book crafting (ISR <c>CraftRecipeAuto</c>): the server draws one craft's ingredients from the open
/// crafting grid and then the player inventory (hotbar first), and puts the result in CreatedOutput for the
/// request's own Take/Place to move. Behaviour reference: what a real client's request builder expects.
/// </summary>
public class AutoCraftTests
{
    private static readonly RecipeRegistry Reg = TestRecipes.Default;

    private static InventorySlot Block(int rid, int n = 1) => InventorySlot.OfBlock(rid, n);

    // ---- allocation (pure) ---------------------------------------------------------------------------------

    [Fact]
    public void Plan_takes_from_sources_in_order_and_only_what_the_crafts_need()
    {
        InventorySlot[] sources = [Block(Blocks.OakLog, 1), Block(Blocks.OakLog, 5)];

        Assert.True(Reg.TryPlanAutoCraft(TestRecipes.OakLogToPlanks, 2, sources, isAtCraftingTable: false, out _, out var takes));

        Assert.Equal([1, 1], takes); // the first source is used up before the second is touched
    }

    [Fact]
    public void Plan_refuses_when_the_sources_cannot_afford_every_craft()
    {
        InventorySlot[] sources = [Block(Blocks.OakLog, 1)];

        Assert.False(Reg.TryPlanAutoCraft(TestRecipes.OakLogToPlanks, 2, sources, isAtCraftingTable: false, out _, out _));
    }

    [Fact]
    public void Plan_refuses_a_table_recipe_away_from_a_table()
    {
        var sources = Enumerable.Repeat(Block(Blocks.OakPlanks, 64), 4).ToArray();

        Assert.False(Reg.TryPlanAutoCraft(TestRecipes.Chest, 1, sources, isAtCraftingTable: false, out _, out _));
        Assert.True(Reg.TryPlanAutoCraft(TestRecipes.Chest, 1, sources, isAtCraftingTable: true, out _, out var takes));
        Assert.Equal(8, takes.Sum());
    }

    [Fact]
    public void Plan_refuses_an_unknown_recipe_and_an_over_large_craft_count()
    {
        InventorySlot[] sources = [Block(Blocks.OakLog, 64)];

        Assert.False(Reg.TryPlanAutoCraft(0xFFFFFF, 1, sources, isAtCraftingTable: true, out _, out _));
        Assert.False(Reg.TryPlanAutoCraft(TestRecipes.OakLogToPlanks, 17, sources, isAtCraftingTable: false, out _, out _)); // 68 planks > a stack
    }

    // ---- InventorySystem integration -----------------------------------------------------------------------

    [Fact]
    public void Auto_craft_consumes_inventory_ingredients_and_the_request_places_the_result()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("book");
        player.Windows.OpenPlayerContainer(0, 0xff);
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySetBlock(i, Blocks.Air, 0));
        Assert.True(player.Inventory.TrySetBlock(20, Blocks.OakLog, 3));

        Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(40, [
            InventoryStackAction.CraftAuto(TestRecipes.OakLogToPlanks, 2),
            InventoryStackAction.CreateOutput(),
            InventoryStackAction.Transfer(
                InventoryContainerMap.CraftResultFlat, 5, 8,
                new WireSlot(InventoryContainerMap.CreatedOutput, InventoryContainerMap.CraftingResultWireSlot),
                new WireSlot(InventoryContainerMap.Inventory, 5)),
        ], requiresOpenContainer: true)));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(1, player.Inventory.Get(20).Count); // 3 logs - 2 crafts
        Assert.Equal(Blocks.OakPlanks, player.Inventory.Get(5).Id.Value);
        Assert.Equal(8, player.Inventory.Get(5).Count);
        Assert.True(player.Windows.CraftUi.Result.IsEmpty);
    }

    [Fact]
    public void Auto_craft_draws_from_the_grid_before_the_inventory()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("gridfirst");
        player.Windows.OpenPlayerContainer(0, 0xff);
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySetBlock(i, Blocks.Air, 0));
        Assert.True(player.Windows.CraftUi.TrySetGrid(2, Block(Blocks.OakLog, 1)));
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.OakLog, 1));

        Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(41, [
            InventoryStackAction.CraftAuto(TestRecipes.OakLogToPlanks, 1),
            InventoryStackAction.CreateOutput(),
        ], requiresOpenContainer: true)));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.Windows.CraftUi.GetGrid(2).IsEmpty);
        Assert.Equal(1, player.Inventory.Get(0).Count);
        Assert.Equal(4, player.Windows.CraftUi.Result.Count);
    }

    [Fact]
    public void Auto_craft_that_cannot_be_afforded_changes_nothing()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("short");
        player.Windows.OpenPlayerContainer(0, 0xff);
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySetBlock(i, Blocks.Air, 0));
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.OakLog, 1));

        Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(42, [
            InventoryStackAction.CraftAuto(TestRecipes.OakLogToPlanks, 2),
            InventoryStackAction.CreateOutput(),
        ], requiresOpenContainer: true)));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(1, player.Inventory.Get(0).Count);
        Assert.True(player.Windows.CraftUi.Result.IsEmpty);
    }

    [Fact]
    public void Auto_craft_refuses_while_a_previous_result_is_still_untaken()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("stuck");
        player.Windows.OpenPlayerContainer(0, 0xff);
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySetBlock(i, Blocks.Air, 0));
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.OakLog, 4));
        var sys = fx.CreateInventorySystem();

        Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(43, [
            InventoryStackAction.CraftAuto(TestRecipes.OakLogToPlanks, 1), InventoryStackAction.CreateOutput(),
        ], requiresOpenContainer: true)));
        sys.Tick(fx.Clock, fx.Players.Online);
        Assert.Equal(4, player.Windows.CraftUi.Result.Count);

        Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(44, [
            InventoryStackAction.CraftAuto(TestRecipes.OakLogToPlanks, 1), InventoryStackAction.CreateOutput(),
        ], requiresOpenContainer: true)));
        sys.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(3, player.Inventory.Get(0).Count); // only the first request consumed
        Assert.Equal(4, player.Windows.CraftUi.Result.Count);
    }

    [Fact]
    public void Auto_craft_of_a_table_recipe_is_refused_in_the_personal_grid()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("notable");
        player.Windows.OpenPlayerContainer(0, 0xff);
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.TrySetBlock(i, Blocks.Air, 0));
        Assert.True(player.Inventory.TrySetBlock(0, Blocks.OakPlanks, 64));

        Assert.True(player.Intents.SubmitInventoryStack(InventoryStackIntent.Create(45, [
            InventoryStackAction.CraftAuto(TestRecipes.Chest, 1), InventoryStackAction.CreateOutput(),
        ], requiresOpenContainer: true)));
        fx.CreateInventorySystem().Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(64, player.Inventory.Get(0).Count);
        Assert.True(player.Windows.CraftUi.Result.IsEmpty);
    }

    // ---- wire decode -----------------------------------------------------------------------------------------

    [Fact]
    public void Decoded_craft_recipe_auto_carries_its_recipe_and_craft_count()
    {
        var writer = new BinaryStream();
        writer.WriteUnsignedVarInt(1);
        writer.WriteVarInt(9);
        writer.WriteUnsignedVarInt(1);
        writer.WriteUnsignedVarInt(ItemStackRequestPacket.ActionCraftRecipeAuto);
        writer.WriteByte(ItemStackRequestPacket.ActionCraftRecipeAuto);
        writer.WriteUnsignedVarInt(77); // recipe network id
        writer.WriteByte(3); // times crafted
        writer.WriteUnsignedVarInt(0); // no ingredient descriptors
        writer.WriteUnsignedVarInt(0);
        writer.WriteInt(0, BinaryStream.Endianess.Little);

        var stream = new BinaryStream(writer.GetBufferDisposing().ToArray());
        var packet = new ItemStackRequestPacket();
        packet.Decode(ref stream);

        Assert.True(packet.Requests[0].AllSupported);
        Assert.Equal(77u, packet.Requests[0].Actions[0].RecipeNetId);
        Assert.Equal(3, packet.Requests[0].Actions[0].CraftTimes);
    }
}
