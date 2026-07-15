using Xunit;
using Zenith.Nbt;
using Zenith.World;

namespace Zenith.Tests;

public class BlockPaletteTests
{
    // Hashes historically hardcoded in Blocks.cs — must match palette network_id.
    private const int ExpectedAir = unchecked((int)0xDBF44120); // -604749536
    private const int ExpectedStone = unchecked((int)0x80310E21); // -2144268767
    private const int ExpectedGrass = unchecked((int)0xDE3128B4); // -567203660

    [Fact]
    public void Block_palette_file_resolves_air_stone_grass_network_ids()
    {
        var path = Path.Combine(FindRepoRoot(), "src", "zenith", "data", "block_palette.nbt");
        Assert.True(File.Exists(path), $"Missing palette at {path}");
        var palette = BlockPaletteLoader.FromGzipFile(path);
        Assert.Equal(ExpectedAir, palette.Require("minecraft:air"));
        Assert.Equal(ExpectedStone, palette.Require("minecraft:stone"));
        Assert.Equal(ExpectedGrass, palette.Require("minecraft:grass_block"));
    }

    [Fact]
    public void Blocks_Load_from_embedded_matches_expected_hashes()
    {
        Blocks.ResetForTests();
        Blocks.Load(BlockPaletteLoader.FromEmbeddedResource());
        Assert.Equal(ExpectedAir, Blocks.Air);
        Assert.Equal(ExpectedStone, Blocks.Stone);
        Assert.Equal(ExpectedGrass, Blocks.GrassBlock);
    }

    [Fact]
    public void Chest_Require_name_is_south_and_cardinals_resolve()
    {
        Blocks.ResetForTests();
        var palette = BlockPaletteLoader.FromEmbeddedResource();
        Blocks.Load(palette);

        const int south = 741882976;
        const int west = 1429214429;
        const int north = -1132117234;
        const int east = 2001328343;

        Assert.Equal(south, palette.Require("minecraft:chest"));
        Assert.Equal(south, Blocks.Chest);
        Assert.Equal(south, palette.Require("minecraft:chest", Blocks.CardinalDirectionKey, Blocks.CardinalSouth));
        Assert.Equal(west, palette.Require("minecraft:chest", Blocks.CardinalDirectionKey, Blocks.CardinalWest));
        Assert.Equal(north, palette.Require("minecraft:chest", Blocks.CardinalDirectionKey, Blocks.CardinalNorth));
        Assert.Equal(east, palette.Require("minecraft:chest", Blocks.CardinalDirectionKey, Blocks.CardinalEast));

        Assert.True(Blocks.IsChest(south));
        Assert.True(Blocks.IsChest(west));
        Assert.True(Blocks.IsChest(north));
        Assert.True(Blocks.IsChest(east));
        Assert.False(Blocks.IsChest(Blocks.Stone));
        Assert.Equal(75, Blocks.BreakTicks(north));
        Assert.Equal(north, Blocks.ChestForFacing(Blocks.CardinalNorth));
    }

    [Fact]
    public void PropertyData_empty_compound_is_valid_network_nbt()
    {
        var bytes = NbtCodec.Encode(
            new NbtNamedTag("", NbtTag.Compound(new NbtCompound())),
            NbtEncoding.Network);
        Assert.Equal(new byte[] { 0x0A, 0x00, 0x00 }, bytes);
        var decoded = NbtCodec.Decode(bytes, NbtEncoding.Network);
        Assert.Equal("", decoded.Root.Name);
        Assert.Equal(0, decoded.Root.Tag.AsCompound().Count);
    }

    /// <summary>Walk up from the test output dir until zenith.sln is found.</summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "zenith.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate zenith.sln from test BaseDirectory.");
    }
}
