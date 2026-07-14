namespace Zenith.World;

/// <summary>
/// Runtime IDs de bloco carregados de <c>block_palette.nbt</c> (campo <c>network_id</c>).
/// Chamar <see cref="Load"/> / <see cref="EnsureLoaded"/> antes de usar (ex. boot, antes de World).
/// </summary>
/// <remarks>
/// Dívida conhecida: acesso estático tipo service-locator. Novos registries (item, biome, …)
/// entram via <c>ServerContext</c>; migrar Blocks quando o domínio de inventário/bloco for tocado de novo.
/// </remarks>
static class Blocks
{
    private static int _air;
    private static int _stone;
    private static int _grassBlock;
    private static Dictionary<int, string>? _nameByRuntime;
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
        _nameByRuntime = new Dictionary<int, string>
        {
            [_air] = "minecraft:air",
            [_stone] = "minecraft:stone",
            [_grassBlock] = "minecraft:grass_block"
        };
        _loaded = true;
    }

    /// <summary>Nome conhecido para um block runtime id carregado (air/stone/grass).</summary>
    public static bool TryGetName(int runtimeId, out string name)
    {
        EnsureLoaded();
        if (_nameByRuntime is not null && _nameByRuntime.TryGetValue(runtimeId, out name!))
            return true;
        name = "";
        return false;
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
        _nameByRuntime = null;
    }
}
