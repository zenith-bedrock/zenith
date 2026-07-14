using Zenith.Nbt;

namespace Zenith.World;

/// <summary>
/// Runtime IDs de bloco carregados de <c>block_palette.nbt</c> (campo <c>network_id</c>).
/// Chamar <see cref="Load"/> / <see cref="EnsureLoaded"/> antes de usar (ex. boot, antes de World).
/// </summary>
static class Blocks
{
    private static int _air;
    private static int _stone;
    private static int _grassBlock;
    private static bool _loaded;

    public static int Air
    {
        get { EnsureLoaded(); return _air; }
    }

    public static int Stone
    {
        get { EnsureLoaded(); return _stone; }
    }

    public static int GrassBlock
    {
        get { EnsureLoaded(); return _grassBlock; }
    }

    public const int FlatMinY = -64;
    public const int FlatStoneTopY = -62;
    public const int FlatGrassY = -61;
    public const int FlatSpawnY = -60;

    public static void Load(BlockPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _air = palette.Require("minecraft:air");
        _stone = palette.Require("minecraft:stone");
        _grassBlock = palette.Require("minecraft:grass_block");
        _loaded = true;
    }

    public static void EnsureLoaded()
    {
        if (_loaded) return;
        Load(BlockPaletteLoader.FromEmbeddedResource());
    }

    /// <summary>Test helper: reset so the next access reloads (or <see cref="Load"/> again).</summary>
    internal static void ResetForTests()
    {
        _loaded = false;
        _air = _stone = _grassBlock = 0;
    }
}
