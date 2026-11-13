using Xunit;
using Zenith.Gameplay.Inventory;
using Zenith.Gameplay.WorldInteraction;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>ADR §148: durable items wear when used and are gone at their maximum damage.</summary>
public class ItemWearTests
{
    private static readonly ItemPalette Palette = ItemPaletteLoader.FromEmbeddedResource();
    private static readonly ItemDurability Table = ItemDurability.CreateDefault(Palette);

    public ItemWearTests() => Blocks.EnsureLoaded();

    private static int Pickaxe => Palette.Require("minecraft:wooden_pickaxe"); // max durability 59

    [Fact]
    public void A_durable_stack_takes_damage_and_keeps_its_aux()
    {
        var stack = new InventorySlot(StackId.FromItem(Pickaxe, damage: 10), 1);

        var outcome = ItemWear.Apply(Table, stack, 3, out var after);

        Assert.Equal(ItemWear.Outcome.Worn, outcome);
        Assert.Equal(13, after.Id.Damage);
        Assert.Equal(1, after.Count);
        Assert.Equal(stack.Id.Value, after.Id.Value);
    }

    [Fact]
    public void Reaching_the_maximum_breaks_the_item_one_short_does_not()
    {
        Assert.Equal(ItemWear.Outcome.Worn, ItemWear.Apply(Table, new InventorySlot(StackId.FromItem(Pickaxe, damage: 57), 1), 1, out var almost));
        Assert.Equal(58, almost.Id.Damage);

        Assert.Equal(ItemWear.Outcome.Broke, ItemWear.Apply(Table, almost, 1, out var gone));
        Assert.True(gone.IsEmpty);
    }

    [Fact]
    public void Items_that_do_not_wear_and_blocks_are_left_alone()
    {
        var stick = new InventorySlot(StackId.FromItem(Palette.Require("minecraft:stick")), 4);
        var dirt = InventorySlot.OfBlock(Blocks.Dirt, 4);

        Assert.Equal(ItemWear.Outcome.NotDurable, ItemWear.Apply(Table, stick, 1, out var s));
        Assert.Equal(stick, s);
        Assert.Equal(ItemWear.Outcome.NotDurable, ItemWear.Apply(Table, dirt, 1, out var d));
        Assert.Equal(dirt, d);
        Assert.Equal(ItemWear.Outcome.NotDurable, ItemWear.Apply(Table, InventorySlot.Empty, 1, out _));
    }

    [Fact]
    public void Breaking_a_block_in_survival_wears_the_held_tool_and_persists_it()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("miner");
        player.PositionX = 0.5f; player.PositionY = 90; player.PositionZ = 0.5f;
        fx.World.SetBlock(0, 90, 0, Blocks.Dirt);
        Assert.True(player.Inventory.TrySet(0, StackId.FromItem(Pickaxe, damage: 20), 1));
        player.SelectedHotbarSlot = 0;
        var need = Blocks.BreakTicks(Blocks.Dirt, StackId.FromItem(Pickaxe, damage: 20));
        player.Dig.BeginBreak(0, 90, 0, fx.Clock.CurrentTick, need, StackId.FromItem(Pickaxe, damage: 20));
        fx.Clock.AdvanceBy(need);
        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.BreakWithDig(0, 90, 0, player.Dig.BreakStartedTick, player.Dig.BreakRequiredTicks)));

        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.Air, fx.World.GetBlock(0, 90, 0));
        Assert.Equal(21, player.Inventory.Get(0).Id.Damage);
        // What the world stored is what a rejoin loads.
        var reloaded = new PlayerInventory();
        Assert.True(reloaded.TryLoadMainFromBlob(player.Inventory.PackMainBlob()));
        Assert.Equal(21, reloaded.Get(0).Id.Damage);
    }

    [Fact]
    public void The_last_point_of_durability_breaks_the_tool_in_the_hand()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("last");
        player.PositionX = 0.5f; player.PositionY = 90; player.PositionZ = 0.5f;
        fx.World.SetBlock(0, 90, 0, Blocks.Dirt);
        var nearlyGone = StackId.FromItem(Pickaxe, damage: 58);
        Assert.True(player.Inventory.TrySet(0, nearlyGone, 1));
        player.SelectedHotbarSlot = 0;
        var need = Blocks.BreakTicks(Blocks.Dirt, nearlyGone);
        player.Dig.BeginBreak(0, 90, 0, fx.Clock.CurrentTick, need, nearlyGone);
        fx.Clock.AdvanceBy(need);
        Assert.True(player.Intents.SubmitBlockEdit(BlockEditIntent.BreakWithDig(0, 90, 0, player.Dig.BreakStartedTick, player.Dig.BreakRequiredTicks)));

        new BlockEditSystem(fx.Players, fx.World, fx.Context.Durability).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(Blocks.Air, fx.World.GetBlock(0, 90, 0));
        // The broken tool's slot is free again, so the dirt it just dug may land there: what must be gone is the tool.
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.False(player.Inventory.Get(i).Id.IsItem && player.Inventory.Get(i).Id.Value == Pickaxe, $"slot {i} still holds the pickaxe");
    }
}
