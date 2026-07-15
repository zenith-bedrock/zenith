namespace Zenith.World;

/// <summary>
/// Runtime IDs de bloco carregados de <c>block_palette.nbt</c> (campo <c>network_id</c>).
/// Chamar <see cref="Load"/> / <see cref="EnsureLoaded"/> antes de usar (ex. boot, antes de World).
/// </summary>
/// <remarks>
/// Known debt (ADR §25): ainda estático. Cresceu a set lista mínima de variedade sem migrar
/// pra <c>ServerContext</c> nesta leva — chest/crafting usam os mesmos ids; migrar o façade
/// quando recipes/chest store já estabilizarem.
/// </remarks>
static class Blocks
{
    private static int _air;
    private static int _stone;
    private static int _grassBlock;
    private static int _dirt;
    private static int _oakPlanks;
    private static int _oakLog;
    private static int _sand;
    private static int _chest;
    private static Dictionary<int, string>? _nameByRuntime;
    private static bool _loaded;

    public static int Air { get { EnsureLoaded(); return _air; } }
    public static int Stone { get { EnsureLoaded(); return _stone; } }
    public static int GrassBlock { get { EnsureLoaded(); return _grassBlock; } }
    public static int Dirt { get { EnsureLoaded(); return _dirt; } }
    public static int OakPlanks { get { EnsureLoaded(); return _oakPlanks; } }
    public static int OakLog { get { EnsureLoaded(); return _oakLog; } }
    public static int Sand { get { EnsureLoaded(); return _sand; } }
    public static int Chest { get { EnsureLoaded(); return _chest; } }

    public const int FlatMinY = -64;
    public const int FlatStoneTopY = -62;
    public const int FlatGrassY = -61;
    public const int FlatSpawnY = -60;
    public const float PlayerEyeHeight = 1.62f;

    public static void Load(BlockPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _air = palette.Require("minecraft:air");
        _stone = palette.Require("minecraft:stone");
        _grassBlock = palette.Require("minecraft:grass_block");
        _dirt = palette.Require("minecraft:dirt");
        _oakPlanks = palette.Require("minecraft:oak_planks");
        _oakLog = palette.Require("minecraft:oak_log");
        _sand = palette.Require("minecraft:sand");
        _chest = palette.Require("minecraft:chest");
        _nameByRuntime = new Dictionary<int, string>
        {
            [_air] = "minecraft:air",
            [_stone] = "minecraft:stone",
            [_grassBlock] = "minecraft:grass_block",
            [_dirt] = "minecraft:dirt",
            [_oakPlanks] = "minecraft:oak_planks",
            [_oakLog] = "minecraft:oak_log",
            [_sand] = "minecraft:sand",
            [_chest] = "minecraft:chest",
        };
        _loaded = true;
    }

    public static bool TryGetName(int runtimeId, out string name)
    {
        EnsureLoaded();
        if (_nameByRuntime is not null && _nameByRuntime.TryGetValue(runtimeId, out name!))
            return true;
        name = "";
        return false;
    }

    /// <summary>
    /// Empty-hand dig duration in GameLoop ticks (20 TPS). Approximates Bedrock/Java hand break
    /// times (hardness×5 seconds) for the starter palette — tools/enchants deferred (ADR §27).
    /// Creative uses InstantBuild and skips this gate.
    /// </summary>
    public static int BreakTicks(int runtimeId)
    {
        EnsureLoaded();
        if (runtimeId == _air) return 0;
        // dirt/sand hardness 0.5 → 0.75s → 15 ticks
        if (runtimeId == _dirt || runtimeId == _sand) return 15;
        // grass_block hardness 0.6 → 0.9s → 18 ticks
        if (runtimeId == _grassBlock) return 18;
        // planks/log hardness 2 → 3s → 60 ticks
        if (runtimeId == _oakPlanks || runtimeId == _oakLog) return 60;
        // chest hardness 2.5 → 3.75s → 75 ticks
        if (runtimeId == _chest) return 75;
        // stone hardness 1.5, hand unsuitable → 7.5s → 150 ticks
        if (runtimeId == _stone) return 150;
        return 60;
    }

    /// <summary>LevelEvent BLOCK_START_BREAK data — progress per tick scaled to 65535 (PM/Geyser).</summary>
    public static int CrackEventData(int breakTicks)
    {
        if (breakTicks <= 0) return 65535;
        return Math.Max(1, (int)Math.Round(65535.0 / breakTicks));
    }

    public static void EnsureLoaded()
    {
        if (_loaded) return;
        Load(BlockPaletteLoader.FromEmbeddedResource());
    }

    internal static void ResetForTests()
    {
        _loaded = false;
        _air = _stone = _grassBlock = _dirt = _oakPlanks = _oakLog = _sand = _chest = 0;
        _nameByRuntime = null;
    }
}
