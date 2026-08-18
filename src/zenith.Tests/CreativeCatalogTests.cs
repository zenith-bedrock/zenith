using Zenith.World;
using Xunit;
using Zenith.Gameplay.Inventory;

namespace Zenith.Tests;

public class CreativeCatalogTests
{
    public CreativeCatalogTests() => Blocks.EnsureLoaded();

    [Fact]
    public void CreateDefault_registers_curated_blocks_and_tools()
    {
        var catalog = CreativeCatalog.CreateDefault();

        Assert.True(catalog.TryGet(CreativeCatalog.Stone, out var stone, out var stoneCount));
        Assert.Equal(Blocks.Stone, stone.Value);
        Assert.Equal(1, stoneCount);

        Assert.True(catalog.TryGet(CreativeCatalog.WoodenPickaxe, out var pickaxe, out var pickaxeCount));
        Assert.False(pickaxe.IsEmpty);
        Assert.Equal(1, pickaxeCount);
    }

    [Fact]
    public void TryGet_returns_false_for_unregistered_net_id()
    {
        var catalog = CreativeCatalog.CreateDefault();
        Assert.False(catalog.TryGet(9999, out _, out _));
    }

    [Fact]
    public void SnapshotEntries_is_stable_ordered_by_net_id()
    {
        var catalog = CreativeCatalog.CreateDefault();
        var snapshot = catalog.SnapshotEntries();

        for (var i = 1; i < snapshot.Count; i++)
            Assert.True(snapshot[i - 1].NetId < snapshot[i].NetId);
    }
}
