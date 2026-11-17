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
        /// <summary>The runtime id of every <see cref="KnownBlock"/>, by member index.</summary>
        public int[] Ids { get; set; } = [];

        public int this[KnownBlock block] => Ids[(int)block];

        public BlockFamily FireFamily { get; set; } = null!;
        public int ChestNorth { get; set; }
        public int ChestSouth { get; set; }
        public int ChestEast { get; set; }
        public int ChestWest { get; set; }
        public HashSet<int> ChestIds { get; set; } = [];
        public HashSet<int> PlaceableIds { get; set; } = [];
        public HashSet<int> GravityIds { get; set; } = [];
        public HashSet<int> PassableIds { get; set; } = [];
        public Dictionary<int, string> NameByRuntime { get; set; } = [];
        public BlockPalette Palette { get; set; } = null!;
        public BlockCatalog Catalog { get; set; } = null!;
    }

    private static volatile Loaded? _loaded;

    public static int Air => Current[KnownBlock.Air];
    public static int Stone => Current[KnownBlock.Stone];
    public static int GrassBlock => Current[KnownBlock.GrassBlock];
    public static int Dirt => Current[KnownBlock.Dirt];
    public static int OakPlanks => Current[KnownBlock.OakPlanks];
    public static int OakLog => Current[KnownBlock.OakLog];
    public static int OakLeaves => Current[KnownBlock.OakLeaves];
    public static int OakSapling => Current[KnownBlock.OakSapling];
    public static int Sand => Current[KnownBlock.Sand];
    public static int Gravel => Current[KnownBlock.Gravel];
    public static int Bedrock => Current[KnownBlock.Bedrock];
    public static int Water => Current[KnownBlock.Water];
    public static int Lava => Current[KnownBlock.Lava];
    public static int Obsidian => Current[KnownBlock.Obsidian];
    public static int Fire => Current[KnownBlock.Fire];
    public static int Cobblestone => Current[KnownBlock.Cobblestone];
    public static int Deepslate => Current[KnownBlock.Deepslate];
    public static int CoalOre => Current[KnownBlock.CoalOre];
    public static int IronOre => Current[KnownBlock.IronOre];
    public static int CopperOre => Current[KnownBlock.CopperOre];
    public static int GoldOre => Current[KnownBlock.GoldOre];
    public static int DiamondOre => Current[KnownBlock.DiamondOre];
    public static int LapisOre => Current[KnownBlock.LapisOre];
    public static int RedstoneOre => Current[KnownBlock.RedstoneOre];
    public static int DeepslateCoalOre => Current[KnownBlock.DeepslateCoalOre];
    public static int DeepslateIronOre => Current[KnownBlock.DeepslateIronOre];
    public static int DeepslateCopperOre => Current[KnownBlock.DeepslateCopperOre];
    public static int DeepslateGoldOre => Current[KnownBlock.DeepslateGoldOre];
    public static int DeepslateDiamondOre => Current[KnownBlock.DeepslateDiamondOre];
    public static int DeepslateLapisOre => Current[KnownBlock.DeepslateLapisOre];
    public static int DeepslateRedstoneOre => Current[KnownBlock.DeepslateRedstoneOre];

    /// <summary>Item / recipe / default place form — south palette entry.</summary>
    public static int Chest => Current[KnownBlock.Chest];

    /// <summary>Ground-cover decoration (world-gen vegetation, ADR §137) — see <see cref="IsPassableDecoration"/>.</summary>
    public static int ShortGrass => Current[KnownBlock.ShortGrass];
    public static int Dandelion => Current[KnownBlock.Dandelion];
    public static int Poppy => Current[KnownBlock.Poppy];

    /// <summary>Craftable decoration/building blocks (ADR §138). Torch is passable; the rest are full solid.</summary>
    public static int Torch => Current[KnownBlock.Torch];
    public static int OakFence => Current[KnownBlock.OakFence];
    public static int StoneBricks => Current[KnownBlock.StoneBricks];
    public static int CobblestoneWall => Current[KnownBlock.CobblestoneWall];
    public static int CraftingTable => Current[KnownBlock.CraftingTable];

    public static bool IsCraftingTable(int runtimeId)
    {
        return runtimeId == Current[KnownBlock.CraftingTable];
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
        b.Catalog = new BlockCatalog(palette, DefaultProperties.Value, DefaultDefinitions.Value);
        b.Ids = KnownBlocks.Resolve(palette);
        b.FireFamily = b.Catalog.States.FamilyOf(b[KnownBlock.Fire])!;
        b.ChestSouth = palette.Require("minecraft:chest", CardinalDirectionKey, CardinalSouth);
        b.ChestWest = palette.Require("minecraft:chest", CardinalDirectionKey, CardinalWest);
        b.ChestNorth = palette.Require("minecraft:chest", CardinalDirectionKey, CardinalNorth);
        b.ChestEast = palette.Require("minecraft:chest", CardinalDirectionKey, CardinalEast);
        b.ChestIds = [b.ChestSouth, b.ChestWest, b.ChestNorth, b.ChestEast];
        // Require(name) may equal south — keep one set entry.
        b.ChestIds.Add(b[KnownBlock.Chest]);
        b.PlaceableIds =
        [
            b[KnownBlock.Stone],
            b[KnownBlock.GrassBlock],
            b[KnownBlock.Dirt],
            b[KnownBlock.OakPlanks],
            b[KnownBlock.OakLog],
            b[KnownBlock.OakLeaves],
            b[KnownBlock.Sand],
            b[KnownBlock.Gravel],
            b[KnownBlock.Cobblestone],
            b[KnownBlock.Deepslate],
            b[KnownBlock.Chest],
            b.ChestSouth,
            b.ChestWest,
            b.ChestNorth,
            b.ChestEast,
            b[KnownBlock.Torch],
            b[KnownBlock.OakFence],
            b[KnownBlock.StoneBricks],
            b[KnownBlock.CobblestoneWall],
            b[KnownBlock.CraftingTable]
        ];
        b.GravityIds = [b[KnownBlock.Sand], b[KnownBlock.Gravel]];
        b.PassableIds = [b[KnownBlock.ShortGrass], b[KnownBlock.Dandelion], b[KnownBlock.Poppy], b[KnownBlock.Torch]];
        b.NameByRuntime = KnownBlocks.NamesByRuntimeId(b.Ids);
        foreach (var chest in new[] { b.ChestSouth, b.ChestWest, b.ChestNorth, b.ChestEast })
            b.NameByRuntime[chest] = KnownBlocks.WireName(KnownBlock.Chest);
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
        if (runtimeId == Current[KnownBlock.Chest]) { cardinal = CardinalSouth; return true; }
        cardinal = CardinalSouth;
        return false;
    }

    /// <summary>
    /// Supported placeables and full-cube building families — not “any palette rid” (§12/§161).
    /// Air is never placeable.
    /// </summary>
    public static bool IsPlaceable(int runtimeId)
    {
        return Current.PlaceableIds.Contains(runtimeId) || SolidBuildingBlocks.TryGet(runtimeId, out _) ||
               Current.Catalog.IsPlaceable(runtimeId);
    }

    /// <summary>Sand / gravel — cell-tick gravity set (ADR §57). Not on DigProfiles.</summary>
    public static bool IsGravity(int runtimeId)
    {
        return Current.GravityIds.Contains(runtimeId);
    }

    /// <summary>True for blocks that take random ticks (the <c>RandomTick</c> trait in block_definitions.json).</summary>
    public static bool IsRandomTicking(int runtimeId) =>
        Current.Catalog.States.FamilyOf(runtimeId) is { } family && (family.Traits & BlockTraits.RandomTick) != 0;

    /// <summary>True for trunks (the <c>Log</c> trait): what leaves hold on to.</summary>
    public static bool IsLog(int runtimeId) =>
        Current.Catalog.States.FamilyOf(runtimeId) is { } family && (family.Traits & BlockTraits.Log) != 0;

    /// <summary>The fire block, at any age (ADR §172).</summary>
    public static bool IsFire(int runtimeId) => Current.Catalog.States.FamilyOf(runtimeId) == Current.FireFamily;

    /// <summary>How much of an explosion the block absorbs (block_properties.json); water and lava 100, obsidian 1200, bedrock 3.6 million.</summary>
    public static float BlastResistanceOf(int runtimeId) => (float)(Current.Catalog.States.FamilyOf(runtimeId)?.BlastResistance ?? 0.0);

    /// <summary>How readily the block burns away (block_properties.json), 0 when it does not.</summary>
    public static int FlammabilityOf(int runtimeId) => Current.Catalog.States.FamilyOf(runtimeId)?.Flammability ?? 0;

    /// <summary>How strongly the block invites fire to spread next to it (block_properties.json), 0 when it does not.</summary>
    public static int FlameEncouragementOf(int runtimeId) => Current.Catalog.States.FamilyOf(runtimeId)?.FlameEncouragement ?? 0;

    public static bool IsLeaves(int runtimeId) => Current.Catalog.KindOf(runtimeId) == BlockKind.Leaves;

    /// <summary>Literal empty air. Never occupies, never supports (Phase XXIX).</summary>
    public static bool IsAir(int runtimeId)
    {
        return runtimeId == Current[KnownBlock.Air];
    }

    /// <summary>Which liquid the block is (any depth), or <see cref="FluidType.None"/>.</summary>
    public static FluidType FluidOf(int runtimeId)
    {
        if (Current.Catalog.States.FamilyOf(runtimeId) is not { } family) return FluidType.None;
        if ((family.Traits & BlockTraits.Water) != 0) return FluidType.Water;
        return (family.Traits & BlockTraits.Lava) != 0 ? FluidType.Lava : FluidType.None;
    }

    /// <summary>
    /// Water or lava, at any depth. Has no collision box of its own:
    /// never blocks occupancy and never supports a ground actor, a falling block, or a projectile
    /// (Phase XXIX — replaces the old "any non-air block is solid" assumption that let ground mobs
    /// stand on water). Cross-referenced against PocketMine-MP's <c>Liquid::recalculateCollisionBoxes</c>
    /// (always empty) and Basalt/Dragonfly's block-permutation flags — see
    /// docs/history/phases/phase-xxix-ground-actor-physics-findings.md.
    /// </summary>
    public static bool IsFluid(int runtimeId)
    {
        return FluidOf(runtimeId) != FluidType.None;
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
        return Current.PassableIds.Contains(runtimeId) || Current.Catalog.IsPassable(runtimeId);
    }

    /// <summary>
    /// Real solid collision — not air, not a fluid, not passable decoration. The one physical fact
    /// <see cref="BlocksMovement"/> and <see cref="CanSupportGroundActor"/> both reduce to today;
    /// kept as two named call shapes since a future consumer (e.g. a partial-height block) may
    /// legitimately need to diverge, but there is exactly one classification underneath until that
    /// happens.
    /// </summary>
    private static bool IsSolid(int runtimeId) => !IsAir(runtimeId) && !IsFluid(runtimeId) && !IsFire(runtimeId) && !IsPassableDecoration(runtimeId);

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
        if (IsChest(runtimeId)) return Current[KnownBlock.Chest];
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

    private static readonly Lazy<System.Collections.Frozen.FrozenDictionary<string, BlockProperty>> DefaultProperties =
        new(() => BlockPropertiesLoader.FromEmbeddedResource());

    private static readonly Lazy<System.Collections.Frozen.FrozenDictionary<string, BlockDefinition>> DefaultDefinitions =
        new(() => BlockDefinitionLoader.FromEmbeddedResource());

    /// <summary>State-driven classification, placement kinds and derived dig profiles for the loaded palette.</summary>
    internal static BlockCatalog Catalog => Current.Catalog;

    internal static BlockPalette Palette => Current.Palette;

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

    private static readonly Lock LoadGate = new();
}
