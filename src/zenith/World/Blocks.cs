namespace Zenith.World;

/// <summary>
/// Runtime IDs de bloco carregados de <c>block_palette.nbt</c> (campo <c>network_id</c>).
/// Chamar <see cref="Load"/> / <see cref="EnsureLoaded"/> antes de usar (ex. boot, antes de World).
/// </summary>
/// <remarks>
/// Known debt (ADR §25): ainda estático. Cresceu a set lista mínima de variedade sem migrar
/// pra <c>ServerContext</c> nesta leva — chest/crafting usam os mesmos ids; migrar o façade
/// quando recipes/chest store já estabilizarem. ADR §112 adicionou <see cref="IBlockRegistry"/>/
/// <see cref="BlockRegistry"/> como seam injetável atrás desta classe (delega pra cá, não move
/// call sites) — código novo que precisar de uma dependência testável deve preferir
/// <c>ServerContext.BlockRegistry</c> em vez de chamar esta façade diretamente.
/// </remarks>
static class Blocks
{
    public const string CardinalNorth = "north";
    public const string CardinalSouth = "south";
    public const string CardinalEast = "east";
    public const string CardinalWest = "west";
    public const string CardinalDirectionKey = "minecraft:cardinal_direction";

    /// <summary>
    /// One immutable-after-build generation of every id and set derived from a palette. A (re)load builds a new <see cref="Loaded"/> off to the
    /// side and publishes it with one reference write, so a reader on another thread sees either the old generation or the new one, never a
    /// half-assigned mix.
    /// </summary>
    private sealed class Loaded
    {
        public int Air { get; set; }
        public int Stone { get; set; }
        public int GrassBlock { get; set; }
        public int Dirt { get; set; }
        public int OakPlanks { get; set; }
        public int OakLog { get; set; }
        public int OakLeaves { get; set; }
        public int Sand { get; set; }
        public int Gravel { get; set; }
        public int Bedrock { get; set; }
        public int Water { get; set; }
        public int Cobblestone { get; set; }
        public int Deepslate { get; set; }
        public int CoalOre { get; set; }
        public int IronOre { get; set; }
        public int CopperOre { get; set; }
        public int GoldOre { get; set; }
        public int DiamondOre { get; set; }
        public int LapisOre { get; set; }
        public int RedstoneOre { get; set; }
        public int DeepslateCoalOre { get; set; }
        public int DeepslateIronOre { get; set; }
        public int DeepslateCopperOre { get; set; }
        public int DeepslateGoldOre { get; set; }
        public int DeepslateDiamondOre { get; set; }
        public int DeepslateLapisOre { get; set; }
        public int DeepslateRedstoneOre { get; set; }
        public int Chest { get; set; }
        public int ChestNorth { get; set; }
        public int ChestSouth { get; set; }
        public int ChestEast { get; set; }
        public int ChestWest { get; set; }
        public int ShortGrass { get; set; }
        public int Dandelion { get; set; }
        public int Poppy { get; set; }
        public int Torch { get; set; }
        public int OakFence { get; set; }
        public int StoneBricks { get; set; }
        public int CobblestoneWall { get; set; }
        public int CraftingTable { get; set; }
        public HashSet<int> ChestIds { get; set; } = [];
        public HashSet<int> PlaceableIds { get; set; } = [];
        public HashSet<int> GravityIds { get; set; } = [];
        public HashSet<int> FluidIds { get; set; } = [];
        public HashSet<int> PassableIds { get; set; } = [];
        public Dictionary<int, string> NameByRuntime { get; set; } = [];
        public BlockPalette Palette { get; set; } = null!;
    }

    private static volatile Loaded? _loaded;

    public static int Air => Current.Air;
    public static int Stone => Current.Stone;
    public static int GrassBlock => Current.GrassBlock;
    public static int Dirt => Current.Dirt;
    public static int OakPlanks => Current.OakPlanks;
    public static int OakLog => Current.OakLog;
    public static int OakLeaves => Current.OakLeaves;
    public static int Sand => Current.Sand;
    public static int Gravel => Current.Gravel;
    public static int Bedrock => Current.Bedrock;
    public static int Water => Current.Water;
    public static int Cobblestone => Current.Cobblestone;
    public static int Deepslate => Current.Deepslate;
    public static int CoalOre => Current.CoalOre;
    public static int IronOre => Current.IronOre;
    public static int CopperOre => Current.CopperOre;
    public static int GoldOre => Current.GoldOre;
    public static int DiamondOre => Current.DiamondOre;
    public static int LapisOre => Current.LapisOre;
    public static int RedstoneOre => Current.RedstoneOre;
    public static int DeepslateCoalOre => Current.DeepslateCoalOre;
    public static int DeepslateIronOre => Current.DeepslateIronOre;
    public static int DeepslateCopperOre => Current.DeepslateCopperOre;
    public static int DeepslateGoldOre => Current.DeepslateGoldOre;
    public static int DeepslateDiamondOre => Current.DeepslateDiamondOre;
    public static int DeepslateLapisOre => Current.DeepslateLapisOre;
    public static int DeepslateRedstoneOre => Current.DeepslateRedstoneOre;

    /// <summary>Item / recipe / default place form — south palette entry.</summary>
    public static int Chest => Current.Chest;

    /// <summary>Ground-cover decoration (world-gen vegetation, ADR §137) — see <see cref="IsPassableDecoration"/>.</summary>
    public static int ShortGrass => Current.ShortGrass;
    public static int Dandelion => Current.Dandelion;
    public static int Poppy => Current.Poppy;

    /// <summary>Craftable decoration/building blocks (ADR §138). Torch is passable; the rest are full solid.</summary>
    public static int Torch => Current.Torch;
    public static int OakFence => Current.OakFence;
    public static int StoneBricks => Current.StoneBricks;
    public static int CobblestoneWall => Current.CobblestoneWall;
    public static int CraftingTable => Current.CraftingTable;

    public static bool IsCraftingTable(int runtimeId)
    {
        return runtimeId == Current.CraftingTable;
    }

    /// <summary>Overworld min Y (Bedrock). Flat + noise share this floor.</summary>
    public const int FlatMinY = -64;
    public const int FlatStoneTopY = -62;
    public const int FlatGrassY = -61;
    public const int FlatSpawnY = -60;
    public const float PlayerEyeHeight = 1.62f;

    public static void Load(BlockPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        var loaded = Build(palette);
        lock (LoadGate)
            _loaded = loaded;
    }

    private static Loaded Build(BlockPalette palette)
    {
        var b = new Loaded();
        b.Palette = palette;
        b.Air = palette.Require("minecraft:air");
        b.Stone = palette.Require("minecraft:stone");
        b.GrassBlock = palette.Require("minecraft:grass_block");
        b.Dirt = palette.Require("minecraft:dirt");
        b.OakPlanks = palette.Require("minecraft:oak_planks");
        b.OakLog = palette.Require("minecraft:oak_log");
        b.OakLeaves = palette.Require("minecraft:oak_leaves");
        b.Sand = palette.Require("minecraft:sand");
        b.Gravel = palette.Require("minecraft:gravel");
        b.Bedrock = palette.Require("minecraft:bedrock");
        b.Water = palette.Require("minecraft:water");
        b.Cobblestone = palette.Require("minecraft:cobblestone");
        b.Deepslate = palette.Require("minecraft:deepslate");
        b.CoalOre = palette.Require("minecraft:coal_ore");
        b.IronOre = palette.Require("minecraft:iron_ore");
        b.CopperOre = palette.Require("minecraft:copper_ore");
        b.GoldOre = palette.Require("minecraft:gold_ore");
        b.DiamondOre = palette.Require("minecraft:diamond_ore");
        b.LapisOre = palette.Require("minecraft:lapis_ore");
        b.RedstoneOre = palette.Require("minecraft:redstone_ore");
        b.DeepslateCoalOre = palette.Require("minecraft:deepslate_coal_ore");
        b.DeepslateIronOre = palette.Require("minecraft:deepslate_iron_ore");
        b.DeepslateCopperOre = palette.Require("minecraft:deepslate_copper_ore");
        b.DeepslateGoldOre = palette.Require("minecraft:deepslate_gold_ore");
        b.DeepslateDiamondOre = palette.Require("minecraft:deepslate_diamond_ore");
        b.DeepslateLapisOre = palette.Require("minecraft:deepslate_lapis_ore");
        b.DeepslateRedstoneOre = palette.Require("minecraft:deepslate_redstone_ore");
        b.ShortGrass = palette.Require("minecraft:short_grass");
        b.Dandelion = palette.Require("minecraft:dandelion");
        b.Poppy = palette.Require("minecraft:poppy");
        b.Torch = palette.Require("minecraft:torch");
        b.OakFence = palette.Require("minecraft:oak_fence");
        b.StoneBricks = palette.Require("minecraft:stone_bricks");
        b.CobblestoneWall = palette.Require("minecraft:cobblestone_wall");
        b.CraftingTable = palette.Require("minecraft:crafting_table");
        b.Chest = palette.Require("minecraft:chest");
        b.ChestSouth = palette.Require("minecraft:chest", CardinalDirectionKey, CardinalSouth);
        b.ChestWest = palette.Require("minecraft:chest", CardinalDirectionKey, CardinalWest);
        b.ChestNorth = palette.Require("minecraft:chest", CardinalDirectionKey, CardinalNorth);
        b.ChestEast = palette.Require("minecraft:chest", CardinalDirectionKey, CardinalEast);
        b.ChestIds = [b.ChestSouth, b.ChestWest, b.ChestNorth, b.ChestEast];
        // Require(name) may equal south — keep one set entry.
        b.ChestIds.Add(b.Chest);
        b.PlaceableIds =
        [
            b.Stone,
            b.GrassBlock,
            b.Dirt,
            b.OakPlanks,
            b.OakLog,
            b.OakLeaves,
            b.Sand,
            b.Gravel,
            b.Cobblestone,
            b.Deepslate,
            b.Chest,
            b.ChestSouth,
            b.ChestWest,
            b.ChestNorth,
            b.ChestEast,
            b.Torch,
            b.OakFence,
            b.StoneBricks,
            b.CobblestoneWall,
            b.CraftingTable
        ];
        b.GravityIds = [b.Sand, b.Gravel];
        // Phase XXIX: only fluid the palette currently loads — no lava block exists in Zenith yet.
        // Extend this set, not a scattered `== Blocks.Water` check, the day a second fluid lands.
        b.FluidIds = [b.Water];
        b.PassableIds = [b.ShortGrass, b.Dandelion, b.Poppy, b.Torch];
        b.NameByRuntime = new Dictionary<int, string>
        {
            [b.ShortGrass] = "minecraft:short_grass",
            [b.Dandelion] = "minecraft:dandelion",
            [b.Poppy] = "minecraft:poppy",
            [b.Torch] = "minecraft:torch",
            [b.OakFence] = "minecraft:oak_fence",
            [b.StoneBricks] = "minecraft:stone_bricks",
            [b.CobblestoneWall] = "minecraft:cobblestone_wall",
            [b.CraftingTable] = "minecraft:crafting_table",
            [b.Air] = "minecraft:air",
            [b.Stone] = "minecraft:stone",
            [b.GrassBlock] = "minecraft:grass_block",
            [b.Dirt] = "minecraft:dirt",
            [b.OakPlanks] = "minecraft:oak_planks",
            [b.OakLog] = "minecraft:oak_log",
            [b.OakLeaves] = "minecraft:oak_leaves",
            [b.Sand] = "minecraft:sand",
            [b.Gravel] = "minecraft:gravel",
            [b.Bedrock] = "minecraft:bedrock",
            [b.Water] = "minecraft:water",
            [b.Cobblestone] = "minecraft:cobblestone",
            [b.Deepslate] = "minecraft:deepslate",
            [b.CoalOre] = "minecraft:coal_ore",
            [b.IronOre] = "minecraft:iron_ore",
            [b.CopperOre] = "minecraft:copper_ore",
            [b.GoldOre] = "minecraft:gold_ore",
            [b.DiamondOre] = "minecraft:diamond_ore",
            [b.LapisOre] = "minecraft:lapis_ore",
            [b.RedstoneOre] = "minecraft:redstone_ore",
            [b.DeepslateCoalOre] = "minecraft:deepslate_coal_ore",
            [b.DeepslateIronOre] = "minecraft:deepslate_iron_ore",
            [b.DeepslateCopperOre] = "minecraft:deepslate_copper_ore",
            [b.DeepslateGoldOre] = "minecraft:deepslate_gold_ore",
            [b.DeepslateDiamondOre] = "minecraft:deepslate_diamond_ore",
            [b.DeepslateLapisOre] = "minecraft:deepslate_lapis_ore",
            [b.DeepslateRedstoneOre] = "minecraft:deepslate_redstone_ore",
            [b.ChestSouth] = "minecraft:chest",
            [b.ChestWest] = "minecraft:chest",
            [b.ChestNorth] = "minecraft:chest",
            [b.ChestEast] = "minecraft:chest",
        };
        if (!b.NameByRuntime.ContainsKey(b.Chest))
            b.NameByRuntime[b.Chest] = "minecraft:chest";
        return b;
    }

    public static bool IsChest(int runtimeId)
    {
        return Current.ChestIds.Contains(runtimeId);
    }

    /// <summary>Cardinal facing for a chest rid; false if not a chest.</summary>
    public static bool TryGetChestCardinal(int runtimeId, out string cardinal)
    {
        if (runtimeId == Current.ChestNorth) { cardinal = CardinalNorth; return true; }
        if (runtimeId == Current.ChestSouth) { cardinal = CardinalSouth; return true; }
        if (runtimeId == Current.ChestEast) { cardinal = CardinalEast; return true; }
        if (runtimeId == Current.ChestWest) { cardinal = CardinalWest; return true; }
        if (runtimeId == Current.Chest) { cardinal = CardinalSouth; return true; }
        cardinal = CardinalSouth;
        return false;
    }

    /// <summary>
    /// Curated placeables only (starter set + chest facings) — not “any palette rid” (§12).
    /// Air is never placeable.
    /// </summary>
    public static bool IsPlaceable(int runtimeId)
    {
        return Current.PlaceableIds.Contains(runtimeId);
    }

    /// <summary>Sand / gravel — cell-tick gravity set (ADR §57). Not on DigProfiles.</summary>
    public static bool IsGravity(int runtimeId)
    {
        return Current.GravityIds.Contains(runtimeId);
    }

    /// <summary>Literal empty air. Never occupies, never supports (Phase XXIX).</summary>
    public static bool IsAir(int runtimeId)
    {
        return runtimeId == Current.Air;
    }

    /// <summary>
    /// Water (and any future fluid — see <see cref="LoadCore"/>). Has no collision box of its own:
    /// never blocks occupancy and never supports a ground actor, a falling block, or a projectile
    /// (Phase XXIX — replaces the old "any non-air block is solid" assumption that let ground mobs
    /// stand on water). Cross-referenced against PocketMine-MP's <c>Liquid::recalculateCollisionBoxes</c>
    /// (always empty) and Basalt/Dragonfly's block-permutation flags — see
    /// docs/history/phases/phase-xxix-ground-actor-physics-findings.md.
    /// </summary>
    public static bool IsFluid(int runtimeId)
    {
        return Current.FluidIds.Contains(runtimeId);
    }

    /// <summary>
    /// Ground-cover decoration placed by world-gen (short grass, flowers — ADR §137): not air, not
    /// a fluid, but thin/passable, not the "real solid collision" <see cref="IsSolid"/> means. Kept
    /// as its own set rather than folding into <see cref="IsFluid"/>'s naming — a liquid body and a
    /// thin decoration are physically unrelated, they just share the same "not real collision"
    /// conclusion here.
    /// </summary>
    public static bool IsPassableDecoration(int runtimeId)
    {
        return Current.PassableIds.Contains(runtimeId);
    }

    /// <summary>
    /// Real solid collision — not air, not a fluid, not passable decoration. The one physical fact
    /// <see cref="BlocksMovement"/> and <see cref="CanSupportGroundActor"/> both reduce to today;
    /// kept as two named call shapes since a future consumer (e.g. a partial-height block) may
    /// legitimately need to diverge, but there is exactly one classification underneath until that
    /// happens.
    /// </summary>
    private static bool IsSolid(int runtimeId) => !IsAir(runtimeId) && !IsFluid(runtimeId) && !IsPassableDecoration(runtimeId);

    /// <summary>Blocks body occupancy for any actor's feet/head cell. Air and fluid are both occupiable.</summary>
    public static bool BlocksMovement(int runtimeId) => IsSolid(runtimeId);

    /// <summary>A ground actor may rest on this block. Water is explicitly excluded — the Phase XXIX fix.</summary>
    public static bool CanSupportGroundActor(int runtimeId) => IsSolid(runtimeId);

    /// <summary>An actor's body may occupy this cell (air or fluid) — the inverse of <see cref="BlocksMovement"/>.</summary>
    public static bool CanOccupy(int runtimeId) => !BlocksMovement(runtimeId);

    /// <summary>World block rid for a cardinal facing; unknown → south item form.</summary>
    public static int ChestForFacing(string cardinalDirection)
    {
        return cardinalDirection switch
        {
            CardinalNorth => Current.ChestNorth,
            CardinalEast => Current.ChestEast,
            CardinalWest => Current.ChestWest,
            _ => Current.ChestSouth
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
        if (IsChest(runtimeId)) return Current.Chest;
        if (TryGetName(runtimeId, out var name) && Current.Palette.TryGet(name, out var canonical))
            return canonical;
        return runtimeId;
    }

    /// <summary>Preferred (default-state) runtime id of a named block; false when the name is not a block.</summary>
    public static bool TryGetDefaultRuntimeId(string name, out int runtimeId)
    {
        runtimeId = 0;
        return Current.Palette.TryGet(name, out runtimeId);
    }

    public static bool TryGetName(int runtimeId, out string name)
    {
        if (Current.NameByRuntime.TryGetValue(runtimeId, out name!))
            return true;
        if (Current.Palette.TryGetName(runtimeId, out name!))
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

    public static void EnsureLoaded() => _ = Current;

    /// <summary>The loaded generation; the first access loads the embedded palette.</summary>
    private static Loaded Current
    {
        get
        {
            if (_loaded is { } loaded) return loaded;
            lock (LoadGate)
                return _loaded ??= Build(BlockPaletteLoader.FromEmbeddedResource());
        }
    }

    private static readonly object LoadGate = new();
}
