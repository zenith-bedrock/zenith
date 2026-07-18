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
    public const string CardinalNorth = "north";
    public const string CardinalSouth = "south";
    public const string CardinalEast = "east";
    public const string CardinalWest = "west";
    public const string CardinalDirectionKey = "minecraft:cardinal_direction";

    private static int _air;
    private static int _stone;
    private static int _grassBlock;
    private static int _dirt;
    private static int _oakPlanks;
    private static int _oakLog;
    private static int _sand;
    private static int _chest;
    private static int _chestNorth;
    private static int _chestSouth;
    private static int _chestEast;
    private static int _chestWest;
    private static HashSet<int>? _chestIds;
    private static HashSet<int>? _placeableIds;
    private static Dictionary<int, string>? _nameByRuntime;
    private static BlockPalette? _palette;
    private static bool _loaded;

    public static int Air { get { EnsureLoaded(); return _air; } }
    public static int Stone { get { EnsureLoaded(); return _stone; } }
    public static int GrassBlock { get { EnsureLoaded(); return _grassBlock; } }
    public static int Dirt { get { EnsureLoaded(); return _dirt; } }
    public static int OakPlanks { get { EnsureLoaded(); return _oakPlanks; } }
    public static int OakLog { get { EnsureLoaded(); return _oakLog; } }
    public static int Sand { get { EnsureLoaded(); return _sand; } }

    /// <summary>Item / recipe / default place form — south palette entry.</summary>
    public static int Chest { get { EnsureLoaded(); return _chest; } }

    public const int FlatMinY = -64;
    public const int FlatStoneTopY = -62;
    public const int FlatGrassY = -61;
    public const int FlatSpawnY = -60;
    public const float PlayerEyeHeight = 1.62f;

    public static void Load(BlockPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _palette = palette;
        _air = palette.Require("minecraft:air");
        _stone = palette.Require("minecraft:stone");
        _grassBlock = palette.Require("minecraft:grass_block");
        _dirt = palette.Require("minecraft:dirt");
        _oakPlanks = palette.Require("minecraft:oak_planks");
        _oakLog = palette.Require("minecraft:oak_log");
        _sand = palette.Require("minecraft:sand");
        _chest = palette.Require("minecraft:chest");
        _chestSouth = palette.Require("minecraft:chest", CardinalDirectionKey, CardinalSouth);
        _chestWest = palette.Require("minecraft:chest", CardinalDirectionKey, CardinalWest);
        _chestNorth = palette.Require("minecraft:chest", CardinalDirectionKey, CardinalNorth);
        _chestEast = palette.Require("minecraft:chest", CardinalDirectionKey, CardinalEast);
        _chestIds = [_chestSouth, _chestWest, _chestNorth, _chestEast];
        // Require(name) may equal south — keep one set entry.
        _chestIds.Add(_chest);
        _placeableIds =
        [
            _stone,
            _grassBlock,
            _dirt,
            _oakPlanks,
            _oakLog,
            _sand,
            _chest,
            _chestSouth,
            _chestWest,
            _chestNorth,
            _chestEast
        ];
        _nameByRuntime = new Dictionary<int, string>
        {
            [_air] = "minecraft:air",
            [_stone] = "minecraft:stone",
            [_grassBlock] = "minecraft:grass_block",
            [_dirt] = "minecraft:dirt",
            [_oakPlanks] = "minecraft:oak_planks",
            [_oakLog] = "minecraft:oak_log",
            [_sand] = "minecraft:sand",
            [_chestSouth] = "minecraft:chest",
            [_chestWest] = "minecraft:chest",
            [_chestNorth] = "minecraft:chest",
            [_chestEast] = "minecraft:chest",
        };
        if (!_nameByRuntime.ContainsKey(_chest))
            _nameByRuntime[_chest] = "minecraft:chest";
        _loaded = true;
    }

    public static bool IsChest(int runtimeId)
    {
        EnsureLoaded();
        return _chestIds is not null && _chestIds.Contains(runtimeId);
    }

    /// <summary>Cardinal facing for a chest rid; false if not a chest.</summary>
    public static bool TryGetChestCardinal(int runtimeId, out string cardinal)
    {
        EnsureLoaded();
        if (runtimeId == _chestNorth) { cardinal = CardinalNorth; return true; }
        if (runtimeId == _chestSouth) { cardinal = CardinalSouth; return true; }
        if (runtimeId == _chestEast) { cardinal = CardinalEast; return true; }
        if (runtimeId == _chestWest) { cardinal = CardinalWest; return true; }
        if (runtimeId == _chest) { cardinal = CardinalSouth; return true; }
        cardinal = CardinalSouth;
        return false;
    }

    /// <summary>
    /// Curated placeables only (starter set + chest facings) — not “any palette rid” (§12).
    /// Air is never placeable.
    /// </summary>
    public static bool IsPlaceable(int runtimeId)
    {
        EnsureLoaded();
        return _placeableIds is not null && _placeableIds.Contains(runtimeId);
    }

    /// <summary>World block rid for a cardinal facing; unknown → south item form.</summary>
    public static int ChestForFacing(string cardinalDirection)
    {
        EnsureLoaded();
        return cardinalDirection switch
        {
            CardinalNorth => _chestNorth,
            CardinalEast => _chestEast,
            CardinalWest => _chestWest,
            _ => _chestSouth
        };
    }

    /// <summary>
    /// Inventory / floor-drop merge equivalence (exact rid or same palette name — chest facings, etc.).
    /// </summary>
    public static bool SameMergeItem(int a, int b)
    {
        if (a == b) return true;
        if (IsChest(a) && IsChest(b)) return true;
        return TryGetName(a, out var na) && TryGetName(b, out var nb)
               && string.Equals(na, nb, StringComparison.Ordinal);
    }

    /// <summary>Canonical rid for stacking (chest facings → item form; named blocks → preferred palette entry).</summary>
    public static int NormalizeMergeRuntimeId(int runtimeId)
    {
        EnsureLoaded();
        if (IsChest(runtimeId)) return _chest;
        if (TryGetName(runtimeId, out var name) && _palette!.TryGet(name, out var canonical))
            return canonical;
        return runtimeId;
    }

    public static bool TryGetName(int runtimeId, out string name)
    {
        EnsureLoaded();
        if (_nameByRuntime is not null && _nameByRuntime.TryGetValue(runtimeId, out name!))
            return true;
        if (_palette is not null && _palette.TryGetName(runtimeId, out name!))
            return true;
        name = "";
        return false;
    }

    /// <summary>
    /// Dig duration in GameLoop ticks (20 TPS). Delegates to <see cref="BreakDuration"/>
    /// (empty-hand / tool-aware — ADR §27). Creative uses InstantBuild and skips this gate.
    /// </summary>
    public static int BreakTicks(int runtimeId) => BreakDuration.BreakTicks(runtimeId);

    /// <summary>Dig duration with held <see cref="StackId"/> (ADR §55). Returns -1 if no DigProfile.</summary>
    public static int BreakTicks(int blockRuntimeId, StackId held) =>
        BreakDuration.BreakTicks(blockRuntimeId, held);

    /// <summary>LevelEvent BLOCK_START_BREAK data — progress per tick scaled to <see cref="CrackProgressMax"/>.</summary>
    public const int CrackProgressMax = 65535;

    public static int CrackEventData(int breakTicks)
    {
        if (breakTicks <= 0) return CrackProgressMax;
        return Math.Max(1, (int)Math.Round(CrackProgressMax / (double)breakTicks));
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
        _chestNorth = _chestSouth = _chestEast = _chestWest = 0;
        _chestIds = null;
        _placeableIds = null;
        _nameByRuntime = null;
        _palette = null;
    }
}
