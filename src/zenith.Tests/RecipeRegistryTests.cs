using Zenith.Gameplay;
using Zenith.Player;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

public class RecipeRegistryTests
{
    public RecipeRegistryTests() => Blocks.EnsureLoaded();

    [Fact]
    public void TryMatch_log_to_planks()
    {
        var reg = RecipeRegistry.CreateDefault();
        Assert.True(reg.TryMatch([(Blocks.OakLog, 1)], out var output));
        Assert.Equal(Blocks.OakPlanks, output.RuntimeId);
        Assert.Equal(4, output.Count);
    }

    [Fact]
    public void TryMatch_planks_to_chest()
    {
        var reg = RecipeRegistry.CreateDefault();
        Assert.True(reg.TryMatch([(Blocks.OakPlanks, 8)], out var output));
        Assert.Equal(Blocks.Chest, output.RuntimeId);
        Assert.Equal(1, output.Count);
    }

    [Fact]
    public void TryMatch_rejects_wrong_counts()
    {
        var reg = RecipeRegistry.CreateDefault();
        Assert.False(reg.TryMatch([(Blocks.OakLog, 2)], out _));
        Assert.False(reg.TryMatch([(Blocks.OakPlanks, 7)], out _));
    }

    [Fact]
    public void TryCraft_consumes_and_outputs()
    {
        var reg = RecipeRegistry.CreateDefault();
        var inv = new PlayerInventory();
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(inv.TrySet(i, Blocks.Air, 0));
        Assert.True(inv.TrySet(3, Blocks.OakLog, 1));

        Assert.True(reg.TryCraft(inv, RecipeRegistry.OakLogToPlanks));
        Assert.True(inv.Get(3).IsEmpty);
        Assert.Equal(Blocks.OakPlanks, inv.Get(0).RuntimeId);
        Assert.Equal(4, inv.Get(0).Count);
    }

    [Fact]
    public void TryCraft_fails_when_inputs_missing()
    {
        var reg = RecipeRegistry.CreateDefault();
        var inv = new PlayerInventory();
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            Assert.True(inv.TrySet(i, Blocks.Air, 0));
        Assert.True(inv.TrySet(0, Blocks.OakPlanks, 7));

        Assert.False(reg.TryCraft(inv, RecipeRegistry.OakPlanksToChest));
        Assert.Equal(7, inv.Get(0).Count);
        Assert.Equal(Blocks.OakPlanks, inv.Get(0).RuntimeId);
    }
}
