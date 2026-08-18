using System.Linq;
using Zenith.Player;
using Zenith.World;
using Xunit;
using Zenith.Gameplay.Inventory;

namespace Zenith.Tests;

public class RecipeRegistryTests
{
    public RecipeRegistryTests() => Blocks.EnsureLoaded();

    [Fact]
    public void TryMatch_log_to_planks()
    {
        var reg = RecipeRegistry.CreateDefault();
        Assert.True(reg.TryMatch([(StackId.FromBlock(Blocks.OakLog), 1)], out var output));
        Assert.Equal(Blocks.OakPlanks, output.Id.Value);
        Assert.Equal(4, output.Count);
    }

    [Fact]
    public void TryMatch_planks_to_chest()
    {
        var reg = RecipeRegistry.CreateDefault();
        Assert.True(reg.TryMatch([(StackId.FromBlock(Blocks.OakPlanks), 8)], out var output));
        Assert.Equal(Blocks.Chest, output.Id.Value);
        Assert.Equal(1, output.Count);
    }

    [Fact]
    public void TryMatch_rejects_wrong_counts()
    {
        var reg = RecipeRegistry.CreateDefault();
        Assert.False(reg.TryMatch([(StackId.FromBlock(Blocks.OakLog), 2)], out _));
        Assert.False(reg.TryMatch([(StackId.FromBlock(Blocks.OakPlanks), 7)], out _));
    }

    [Fact]
    public void TryCraft_consumes_and_outputs()
    {
        var reg = RecipeRegistry.CreateDefault();
        var inv = new PlayerInventory();
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(inv.TrySetBlock(i, Blocks.Air, 0));
        Assert.True(inv.TrySetBlock(3, Blocks.OakLog, 1));

        Assert.True(reg.TryCraft(inv, RecipeRegistry.OakLogToPlanks));
        Assert.True(inv.Get(3).IsEmpty);
        Assert.Equal(Blocks.OakPlanks, inv.Get(0).Id.Value);
        Assert.Equal(4, inv.Get(0).Count);
    }

    [Fact]
    public void TryCraft_fails_when_inputs_missing()
    {
        var reg = RecipeRegistry.CreateDefault();
        var inv = new PlayerInventory();
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(inv.TrySetBlock(i, Blocks.Air, 0));
        Assert.True(inv.TrySetBlock(0, Blocks.OakPlanks, 7));

        Assert.False(reg.TryCraft(inv, RecipeRegistry.OakPlanksToChest));
        Assert.Equal(7, inv.Get(0).Count);
        Assert.Equal(Blocks.OakPlanks, inv.Get(0).Id.Value);
    }

    [Fact]
    public void TryMatch_rejects_item_kind_even_with_matching_value()
    {
        var reg = RecipeRegistry.CreateDefault();
        // Same numeric value as OakLog block rid must not craft when Kind=Item.
        var bogus = StackId.FromItem(Blocks.OakLog);
        Assert.False(reg.TryMatch([(bogus, 1)], out _));
    }

    /// <summary>
    /// A caller mutating the array TryGet handed back must not corrupt the registry's own stored
    /// recipe — TryGet returns a defensive copy, not the backing array, so "frozen" actually means
    /// existing entries can't change, not just that no new ones can be added.
    /// </summary>
    [Fact]
    public void TryGet_returned_inputs_array_is_a_copy_not_the_backing_storage()
    {
        var reg = RecipeRegistry.CreateDefault();

        Assert.True(reg.TryGet(RecipeRegistry.OakLogToPlanks, out _, out _, out var firstRead));
        firstRead[0] = (StackId.FromBlock(Blocks.Stone), 99);

        Assert.True(reg.TryGet(RecipeRegistry.OakLogToPlanks, out _, out _, out var secondRead));
        Assert.Equal(StackId.FromBlock(Blocks.OakLog), secondRead[0].Id);
        Assert.Equal(1, secondRead[0].Count);
    }

    /// <summary>Same guarantee as <see cref="TryGet_returned_inputs_array_is_a_copy_not_the_backing_storage"/>, via SnapshotRecipes' wire-facing path.</summary>
    [Fact]
    public void SnapshotRecipes_returned_inputs_array_is_a_copy_not_the_backing_storage()
    {
        var reg = RecipeRegistry.CreateDefault();

        var firstSnapshot = reg.SnapshotRecipes();
        var target = firstSnapshot.Single(r => r.NetId == RecipeRegistry.OakLogToPlanks);
        target.Inputs[0] = (StackId.FromBlock(Blocks.Stone), 99);

        var secondSnapshot = reg.SnapshotRecipes();
        var reread = secondSnapshot.Single(r => r.NetId == RecipeRegistry.OakLogToPlanks);
        Assert.Equal(StackId.FromBlock(Blocks.OakLog), reread.Inputs[0].Id);
        Assert.Equal(1, reread.Inputs[0].Count);
    }
}
