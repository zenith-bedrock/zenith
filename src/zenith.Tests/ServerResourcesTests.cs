using Zenith.Logging;
using Zenith.Server.Composition;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

public sealed class ServerResourcesTests
{
    [Fact]
    public void Prepared_catalogs_resolve_real_gameplay_data_without_a_world_or_tick()
    {
        var resources = ServerResources.Load(new NullLogger());
        Assert.Equal(resources.Blocks.Require("minecraft:stone"), Blocks.Stone);
        Assert.Equal(StackId.FromItem(resources.Items.Require("minecraft:diamond")), BlockLoot.DropFor(Blocks.DiamondOre));
        Assert.True(resources.Creative.TryGet(Zenith.Gameplay.Inventory.CreativeCatalog.Stone, out var stone, out _));
        Assert.Equal(StackId.FromBlock(Blocks.Stone), stone);
        Assert.True(resources.Recipes.Count > 0);
    }

    private sealed class NullLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}
