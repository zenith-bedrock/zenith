using Zenith.Gameplay.Systems;
using Zenith.Player;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

/// <summary>
/// Phase XXVI — end-to-end mining/tool-tier progression: tool → break duration → harvest
/// eligibility → drop → inventory, through the same BlockEditSystem path a real dig uses.
/// </summary>
public class MiningProgressionTests
{
    public MiningProgressionTests()
    {
        Blocks.EnsureLoaded();
        Tools.EnsureLoaded();
    }

    private static Zenith.Player.Player MineAt(IntentTestFixture fx, int x, int y, int z, int blockRuntimeId, int toolNetworkId)
    {
        var player = fx.AddInGamePlayer("miner");
        player.PositionX = x + 0.5f;
        player.PositionY = y;
        player.PositionZ = z + 0.5f;
        fx.World.SetBlock(x, y, z, blockRuntimeId);

        // Survival players seed a starter hotbar (slots 0-5) — clear it so slot 1 is a reliable,
        // predictable drop destination for these tests.
        player.Inventory.Clear();
        Assert.True(player.Inventory.TrySetItem(0, toolNetworkId, 1));
        player.SelectedHotbarSlot = 0;

        var need = Blocks.BreakTicks(blockRuntimeId, StackId.FromItem(toolNetworkId));
        Assert.True(need >= 0);
        player.BeginBreak(x, y, z, tick: fx.Clock.CurrentTick, requiredTicks: need, heldStackId: StackId.FromItem(toolNetworkId));
        if (need > 0)
            fx.Clock.AdvanceBy(need);
        Assert.True(player.SubmitBlockEdit(BlockEditIntent.BreakWithDig(x, y, z, player.BreakStartedTick, player.BreakRequiredTicks)));
        new BlockEditSystem(fx.Players, fx.World).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.Air, fx.World.GetBlock(x, y, z));
        return player;
    }

    [Fact]
    public void Wood_pickaxe_breaks_diamond_ore_but_yields_no_drop()
    {
        var fx = new IntentTestFixture();
        var wood = Tools.Require("minecraft:wooden_pickaxe");
        var player = MineAt(fx, 5, 64, 5, Blocks.DiamondOre, wood);

        // Under MinHarvestTier — block breaks, nothing lands in inventory or on the floor.
        // Slot 0 legitimately holds the wooden pickaxe itself; every other slot must stay empty.
        Assert.False(player.Inventory.Get(0).IsEmpty);
        for (var i = 1; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(player.Inventory.Get(i).IsEmpty);
    }

    [Fact]
    public void Iron_pickaxe_breaks_diamond_ore_and_the_diamond_item_lands_in_inventory()
    {
        var fx = new IntentTestFixture();
        var iron = Tools.Require("minecraft:iron_pickaxe");
        var player = MineAt(fx, 6, 64, 6, Blocks.DiamondOre, iron);

        var diamond = fx.Context.ItemPalette.Require("minecraft:diamond");
        var slot = player.Inventory.Get(1); // slot 0 holds the pickaxe
        Assert.Equal(StackId.FromItem(diamond), slot.Id);
        Assert.Equal(1, slot.Count);
    }

    [Fact]
    public void Stone_pickaxe_harvests_iron_ore_which_still_drops_the_ore_block_pending_a_furnace()
    {
        var fx = new IntentTestFixture();
        var stone = Tools.Require("minecraft:stone_pickaxe");
        var player = MineAt(fx, 7, 64, 7, Blocks.IronOre, stone);

        var slot = player.Inventory.Get(1);
        Assert.Equal(StackId.FromBlock(Blocks.IronOre), slot.Id);
        Assert.Equal(1, slot.Count);
    }

    [Fact]
    public void Wood_pickaxe_harvests_coal_ore_and_gets_the_coal_item()
    {
        var fx = new IntentTestFixture();
        var wood = Tools.Require("minecraft:wooden_pickaxe");
        var player = MineAt(fx, 8, 64, 8, Blocks.CoalOre, wood);

        var coal = fx.Context.ItemPalette.Require("minecraft:coal");
        var slot = player.Inventory.Get(1);
        Assert.Equal(StackId.FromItem(coal), slot.Id);
        Assert.Equal(1, slot.Count);
    }
}
