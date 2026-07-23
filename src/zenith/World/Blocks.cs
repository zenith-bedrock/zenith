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
    private static int _oakLeaves;
    private static int _sand;
    private static int _gravel;
    private static int _bedrock;
    private static int _water;
    private static int _cobblestone;
    private static int _deepslate;
    private static int _coalOre;
    private static int _ironOre;
    private static int _copperOre;
    private static int _goldOre;
    private static int _diamondOre;
    private static int _lapisOre;
    private static int _redstoneOre;
    private static int _deepslateCoalOre;
    private static int _deepslateIronOre;
    private static int _deepslateCopperOre;
    private static int _deepslateGoldOre;
    private static int _deepslateDiamondOre;
    private static int _deepslateLapisOre;
    private static int _deepslateRedstoneOre;
    private static int _chest;
    private static int _chestNorth;
    private static int _chestSouth;
    private static int _chestEast;
    private static int _chestWest;
    private static HashSet<int>? _chestIds;
    private static HashSet<int>? _placeableIds;
    private static HashSet<int>? _gravityIds;
    private static Dictionary<int, string>? _nameByRuntime;
    private static BlockPalette? _palette;
    private static bool _loaded;

    public static int Air { get { EnsureLoaded(); return _air; } }
    public static int Stone { get { EnsureLoaded(); return _stone; } }
    public static int GrassBlock { get { EnsureLoaded(); return _grassBlock; } }
    public static int Dirt { get { EnsureLoaded(); return _dirt; } }
    public static int OakPlanks { get { EnsureLoaded(); return _oakPlanks; } }
    public static int OakLog { get { EnsureLoaded(); return _oakLog; } }
    public static int OakLeaves { get { EnsureLoaded(); return _oakLeaves; } }
    public static int Sand { get { EnsureLoaded(); return _sand; } }
    public static int Gravel { get { EnsureLoaded(); return _gravel; } }
    public static int Bedrock { get { EnsureLoaded(); return _bedrock; } }
    public static int Water { get { EnsureLoaded(); return _water; } }
    public static int Cobblestone { get { EnsureLoaded(); return _cobblestone; } }
    public static int Deepslate { get { EnsureLoaded(); return _deepslate; } }
    public static int CoalOre { get { EnsureLoaded(); return _coalOre; } }
    public static int IronOre { get { EnsureLoaded(); return _ironOre; } }
    public static int CopperOre { get { EnsureLoaded(); return _copperOre; } }
    public static int GoldOre { get { EnsureLoaded(); return _goldOre; } }
    public static int DiamondOre { get { EnsureLoaded(); return _diamondOre; } }
    public static int LapisOre { get { EnsureLoaded(); return _lapisOre; } }
    public static int RedstoneOre { get { EnsureLoaded(); return _redstoneOre; } }
    public static int DeepslateCoalOre { get { EnsureLoaded(); return _deepslateCoalOre; } }
    public static int DeepslateIronOre { get { EnsureLoaded(); return _deepslateIronOre; } }
    public static int DeepslateCopperOre { get { EnsureLoaded(); return _deepslateCopperOre; } }
    public static int DeepslateGoldOre { get { EnsureLoaded(); return _deepslateGoldOre; } }
    public static int DeepslateDiamondOre { get { EnsureLoaded(); return _deepslateDiamondOre; } }
    public static int DeepslateLapisOre { get { EnsureLoaded(); return _deepslateLapisOre; } }
    public static int DeepslateRedstoneOre { get { EnsureLoaded(); return _deepslateRedstoneOre; } }

    /// <summary>Item / recipe / default place form — south palette entry.</summary>
    public static int Chest { get { EnsureLoaded(); return _chest; } }

    /// <summary>Overworld min Y (Bedrock). Flat + noise share this floor.</summary>
    public const int FlatMinY = -64;
    public const int FlatStoneTopY = -62;
    public const int FlatGrassY = -61;
    public const int FlatSpawnY = -60;
    public const float PlayerEyeHeight = 1.62f;

    public static void Load(BlockPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        lock (TestLoadGate)
        {
            LoadCore(palette);
        }
    }

    private static void LoadCore(BlockPalette palette)
    {
        _palette = palette;
        _air = palette.Require("minecraft:air");
        _stone = palette.Require("minecraft:stone");
        _grassBlock = palette.Require("minecraft:grass_block");
        _dirt = palette.Require("minecraft:dirt");
        _oakPlanks = palette.Require("minecraft:oak_planks");
        _oakLog = palette.Require("minecraft:oak_log");
        _oakLeaves = palette.Require("minecraft:oak_leaves");
        _sand = palette.Require("minecraft:sand");
        _gravel = palette.Require("minecraft:gravel");
        _bedrock = palette.Require("minecraft:bedrock");
        _water = palette.Require("minecraft:water");
        _cobblestone = palette.Require("minecraft:cobblestone");
        _deepslate = palette.Require("minecraft:deepslate");
        _coalOre = palette.Require("minecraft:coal_ore");
        _ironOre = palette.Require("minecraft:iron_ore");
        _copperOre = palette.Require("minecraft:copper_ore");
        _goldOre = palette.Require("minecraft:gold_ore");
        _diamondOre = palette.Require("minecraft:diamond_ore");
        _lapisOre = palette.Require("minecraft:lapis_ore");
        _redstoneOre = palette.Require("minecraft:redstone_ore");
        _deepslateCoalOre = palette.Require("minecraft:deepslate_coal_ore");
        _deepslateIronOre = palette.Require("minecraft:deepslate_iron_ore");
        _deepslateCopperOre = palette.Require("minecraft:deepslate_copper_ore");
        _deepslateGoldOre = palette.Require("minecraft:deepslate_gold_ore");
        _deepslateDiamondOre = palette.Require("minecraft:deepslate_diamond_ore");
        _deepslateLapisOre = palette.Require("minecraft:deepslate_lapis_ore");
        _deepslateRedstoneOre = palette.Require("minecraft:deepslate_redstone_ore");
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
            _oakLeaves,
            _sand,
            _gravel,
            _cobblestone,
            _deepslate,
            _chest,
            _chestSouth,
            _chestWest,
            _chestNorth,
            _chestEast
        ];
        _gravityIds = [_sand, _gravel];
        _nameByRuntime = new Dictionary<int, string>
        {
            [_air] = "minecraft:air",
            [_stone] = "minecraft:stone",
            [_grassBlock] = "minecraft:grass_block",
            [_dirt] = "minecraft:dirt",
            [_oakPlanks] = "minecraft:oak_planks",
            [_oakLog] = "minecraft:oak_log",
            [_oakLeaves] = "minecraft:oak_leaves",
            [_sand] = "minecraft:sand",
            [_gravel] = "minecraft:gravel",
            [_bedrock] = "minecraft:bedrock",
            [_water] = "minecraft:water",
            [_cobblestone] = "minecraft:cobblestone",
            [_deepslate] = "minecraft:deepslate",
            [_coalOre] = "minecraft:coal_ore",
            [_ironOre] = "minecraft:iron_ore",
            [_copperOre] = "minecraft:copper_ore",
            [_goldOre] = "minecraft:gold_ore",
            [_diamondOre] = "minecraft:diamond_ore",
            [_lapisOre] = "minecraft:lapis_ore",
            [_redstoneOre] = "minecraft:redstone_ore",
            [_deepslateCoalOre] = "minecraft:deepslate_coal_ore",
            [_deepslateIronOre] = "minecraft:deepslate_iron_ore",
            [_deepslateCopperOre] = "minecraft:deepslate_copper_ore",
            [_deepslateGoldOre] = "minecraft:deepslate_gold_ore",
            [_deepslateDiamondOre] = "minecraft:deepslate_diamond_ore",
            [_deepslateLapisOre] = "minecraft:deepslate_lapis_ore",
            [_deepslateRedstoneOre] = "minecraft:deepslate_redstone_ore",
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

    /// <summary>Sand / gravel — cell-tick gravity set (ADR §57). Not on DigProfiles.</summary>
    public static bool IsGravity(int runtimeId)
    {
        EnsureLoaded();
        return _gravityIds is not null && _gravityIds.Contains(runtimeId);
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

    /// <summary>
    /// LevelEvent BLOCK_START_BREAK / UPDATE data — progress added per game tick toward
    /// <see cref="CrackProgressMax"/>. PocketMine uses <c>(int)(65535 * (1/breakTicks))</c>;
    /// Dragonfly uses <c>65535 / ticks</c> (trunc). Both equal truncating division for integer ticks.
    /// We further guarantee <c>floor(65535/data) &gt;= breakTicks</c> so a div-model client never
    /// ends the crack before the Survival dig gate (Round overshoots and finishes early).
    /// </summary>
    public const int CrackProgressMax = 65535;

    public static int CrackEventData(int breakTicks)
    {
        if (breakTicks <= 0) return CrackProgressMax;
        var data = Math.Max(1, CrackProgressMax / breakTicks);
        // Defensive: if data were ever rounded up, shrink until client duration >= breakTicks.
        while (data > 1 && CrackProgressMax / data < breakTicks)
            data--;
        return data;
    }

    public static void EnsureLoaded()
    {
        if (_loaded) return;
        Load(BlockPaletteLoader.FromEmbeddedResource());
    }

    private static readonly object TestLoadGate = new();

    internal static void ResetForTests()
    {
        lock (TestLoadGate)
        {
            _loaded = false;
            _air = _stone = _grassBlock = _dirt = _oakPlanks = _oakLog = _oakLeaves = _sand = _gravel = 0;
            _bedrock = _water = _cobblestone = _deepslate = _chest = 0;
            _coalOre = _ironOre = _copperOre = _goldOre = _diamondOre = _lapisOre = _redstoneOre = 0;
            _deepslateCoalOre = _deepslateIronOre = _deepslateCopperOre = _deepslateGoldOre = 0;
            _deepslateDiamondOre = _deepslateLapisOre = _deepslateRedstoneOre = 0;
            _chestNorth = _chestSouth = _chestEast = _chestWest = 0;
            _chestIds = null;
            _placeableIds = null;
            _gravityIds = null;
            _nameByRuntime = null;
            _palette = null;
        }
    }
}
