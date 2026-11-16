using Xunit;
using Zenith.World;

namespace Zenith.Tests;

public sealed class KnownBlockTests
{
    [Theory]
    [InlineData(KnownBlock.Air, "minecraft:air")]
    [InlineData(KnownBlock.GrassBlock, "minecraft:grass_block")]
    [InlineData(KnownBlock.DeepslateRedstoneOre, "minecraft:deepslate_redstone_ore")]
    public void A_wire_name_is_the_members_words_in_snake_case(KnownBlock block, string expected) =>
        Assert.Equal(expected, KnownBlocks.WireName(block));

    [Fact]
    public void Every_known_block_resolves_and_has_its_own_id()
    {
        Blocks.EnsureLoaded();
        var ids = KnownBlocks.Resolve(Blocks.Palette);
        Assert.Equal(KnownBlocks.Count, ids.Length);
        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.Equal(Blocks.Stone, ids[(int)KnownBlock.Stone]);
    }
}
