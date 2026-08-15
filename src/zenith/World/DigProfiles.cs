namespace Zenith.World;

/// <summary>
/// Sparse dig capability per block runtime id (ADR §55).
/// <see cref="DestroySpeed"/> = Endstone dump destroy_speed / wiki hardness.
/// <see cref="MinHarvestTier"/> (Phase XXVI) = lowest <see cref="ToolTier"/> of
/// <see cref="HarvestTool"/> kind that still drops loot; <see cref="ToolTier.None"/> (default) means
/// "any tier of the right kind" — the pre-Phase-XXVI behavior every non-ore profile still uses.
/// </summary>
readonly record struct DigProfile(
    double DestroySpeed,
    ToolKind HarvestTool,
    ToolKind EffectiveTool,
    bool RequiresCorrectToolForDrops,
    ToolTier MinHarvestTier = ToolTier.None);

/// <summary>Dig profile table — missing entry = Survival cannot dig (no silent default).</summary>
static class DigProfiles
{
    private static readonly object Gate = new();
    private static readonly Dictionary<int, DigProfile> ByBlockRuntimeId = new();
    private static bool _loaded;

    public static void EnsureLoaded()
    {
        if (_loaded) return;
        lock (Gate)
        {
            if (_loaded) return;
            LoadCuratedUnlocked();
        }
    }

    /// <summary>
    /// Contributor / boot registration. Thread-safe; ensures curated table is loaded first.
    /// Prefer <see cref="OverrideForTests"/> in unit tests so rows do not leak across cases.
    /// </summary>
    public static void Register(
        int blockRuntimeId,
        double destroySpeed,
        ToolKind harvestTool,
        ToolKind effectiveTool,
        bool requiresCorrectTool,
        ToolTier minHarvestTier = ToolTier.None)
    {
        lock (Gate)
        {
            if (!_loaded)
                LoadCuratedUnlocked();
            RegisterUnlocked(blockRuntimeId, destroySpeed, harvestTool, effectiveTool, requiresCorrectTool, minHarvestTier);
        }
    }

    public static bool TryGet(int blockRuntimeId, out DigProfile profile)
    {
        lock (Gate)
        {
            if (!_loaded)
                LoadCuratedUnlocked();
            return ByBlockRuntimeId.TryGetValue(blockRuntimeId, out profile!);
        }
    }

    /// <summary>
    /// Test-only scoped row: installs a profile for <paramref name="blockRuntimeId"/> and
    /// restores the previous map entry (or removes the key) on dispose — no full-table reset.
    /// </summary>
    internal static IDisposable OverrideForTests(
        int blockRuntimeId,
        double destroySpeed,
        ToolKind harvestTool,
        ToolKind effectiveTool,
        bool requiresCorrectTool,
        ToolTier minHarvestTier = ToolTier.None)
    {
        lock (Gate)
        {
            if (!_loaded)
                LoadCuratedUnlocked();
            var had = ByBlockRuntimeId.TryGetValue(blockRuntimeId, out var previous);
            RegisterUnlocked(blockRuntimeId, destroySpeed, harvestTool, effectiveTool, requiresCorrectTool, minHarvestTier);
            return new OverrideScope(blockRuntimeId, had, previous);
        }
    }

    private static void LoadCuratedUnlocked()
    {
        Blocks.EnsureLoaded();
        ByBlockRuntimeId.Clear();

        // Soft — always harvestable; shovel effective.
        RegisterUnlocked(Blocks.Dirt, 0.5, ToolKind.None, ToolKind.Shovel, requiresCorrectTool: false);
        RegisterUnlocked(Blocks.Sand, 0.5, ToolKind.None, ToolKind.Shovel, requiresCorrectTool: false);
        RegisterUnlocked(Blocks.Gravel, 0.6, ToolKind.None, ToolKind.Shovel, requiresCorrectTool: false);
        RegisterUnlocked(Blocks.GrassBlock, 0.6, ToolKind.None, ToolKind.Shovel, requiresCorrectTool: false);

        // Wood / chest — axe effective; always harvestable for timing.
        RegisterUnlocked(Blocks.OakPlanks, 2.0, ToolKind.None, ToolKind.Axe, requiresCorrectTool: false);
        RegisterUnlocked(Blocks.OakLog, 2.0, ToolKind.None, ToolKind.Axe, requiresCorrectTool: false);
        RegisterUnlocked(Blocks.OakLeaves, 0.2, ToolKind.None, ToolKind.None, requiresCorrectTool: false);
        RegisterUnlocked(Blocks.Chest, 2.5, ToolKind.None, ToolKind.Axe, requiresCorrectTool: false);
        RegisterUnlocked(Blocks.ChestForFacing(Blocks.CardinalNorth), 2.5, ToolKind.None, ToolKind.Axe, false);
        RegisterUnlocked(Blocks.ChestForFacing(Blocks.CardinalSouth), 2.5, ToolKind.None, ToolKind.Axe, false);
        RegisterUnlocked(Blocks.ChestForFacing(Blocks.CardinalEast), 2.5, ToolKind.None, ToolKind.Axe, false);
        RegisterUnlocked(Blocks.ChestForFacing(Blocks.CardinalWest), 2.5, ToolKind.None, ToolKind.Axe, false);

        // Stone — pickaxe harvest + effective.
        RegisterUnlocked(Blocks.Stone, 1.5, ToolKind.Pickaxe, ToolKind.Pickaxe, requiresCorrectTool: true);
        RegisterUnlocked(Blocks.Cobblestone, 2.0, ToolKind.Pickaxe, ToolKind.Pickaxe, requiresCorrectTool: true);
        RegisterUnlocked(Blocks.Deepslate, 3.0, ToolKind.Pickaxe, ToolKind.Pickaxe, requiresCorrectTool: true);

        // Phase XXVI — minimum harvest tier per vanilla-adjacent reference (coal: any pickaxe;
        // iron/copper/lapis: stone+; gold/diamond/redstone: iron+). Previously every ore only
        // gated on ToolKind==Pickaxe, so a bare wood pickaxe harvested diamond exactly as well as a
        // diamond pickaxe (only break *speed* differed) — a real, live bug once ore DigProfiles
        // existed, not the "no-op until a tier gap is registered" the earlier audit assumed.
        RegisterUnlocked(Blocks.CoalOre, 3.0, ToolKind.Pickaxe, ToolKind.Pickaxe, requiresCorrectTool: true, ToolTier.Wood);
        RegisterUnlocked(Blocks.IronOre, 3.0, ToolKind.Pickaxe, ToolKind.Pickaxe, requiresCorrectTool: true, ToolTier.Stone);
        RegisterUnlocked(Blocks.CopperOre, 3.0, ToolKind.Pickaxe, ToolKind.Pickaxe, requiresCorrectTool: true, ToolTier.Stone);
        RegisterUnlocked(Blocks.GoldOre, 3.0, ToolKind.Pickaxe, ToolKind.Pickaxe, requiresCorrectTool: true, ToolTier.Iron);
        RegisterUnlocked(Blocks.DiamondOre, 3.0, ToolKind.Pickaxe, ToolKind.Pickaxe, requiresCorrectTool: true, ToolTier.Iron);
        RegisterUnlocked(Blocks.LapisOre, 3.0, ToolKind.Pickaxe, ToolKind.Pickaxe, requiresCorrectTool: true, ToolTier.Stone);
        RegisterUnlocked(Blocks.RedstoneOre, 3.0, ToolKind.Pickaxe, ToolKind.Pickaxe, requiresCorrectTool: true, ToolTier.Iron);
        RegisterUnlocked(Blocks.DeepslateCoalOre, 4.5, ToolKind.Pickaxe, ToolKind.Pickaxe, requiresCorrectTool: true, ToolTier.Wood);
        RegisterUnlocked(Blocks.DeepslateIronOre, 4.5, ToolKind.Pickaxe, ToolKind.Pickaxe, requiresCorrectTool: true, ToolTier.Stone);
        RegisterUnlocked(Blocks.DeepslateCopperOre, 4.5, ToolKind.Pickaxe, ToolKind.Pickaxe, requiresCorrectTool: true, ToolTier.Stone);
        RegisterUnlocked(Blocks.DeepslateGoldOre, 4.5, ToolKind.Pickaxe, ToolKind.Pickaxe, requiresCorrectTool: true, ToolTier.Iron);
        RegisterUnlocked(Blocks.DeepslateDiamondOre, 4.5, ToolKind.Pickaxe, ToolKind.Pickaxe, requiresCorrectTool: true, ToolTier.Iron);
        RegisterUnlocked(Blocks.DeepslateLapisOre, 4.5, ToolKind.Pickaxe, ToolKind.Pickaxe, requiresCorrectTool: true, ToolTier.Stone);
        RegisterUnlocked(Blocks.DeepslateRedstoneOre, 4.5, ToolKind.Pickaxe, ToolKind.Pickaxe, requiresCorrectTool: true, ToolTier.Iron);

        _loaded = true;
    }

    private static void RegisterUnlocked(
        int blockRuntimeId,
        double destroySpeed,
        ToolKind harvestTool,
        ToolKind effectiveTool,
        bool requiresCorrectTool,
        ToolTier minHarvestTier = ToolTier.None)
    {
        ByBlockRuntimeId[blockRuntimeId] = new DigProfile(
            destroySpeed, harvestTool, effectiveTool, requiresCorrectTool, minHarvestTier);
    }

    private sealed class OverrideScope(
        int blockRuntimeId,
        bool hadPrevious,
        DigProfile previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            lock (Gate)
            {
                if (hadPrevious)
                    ByBlockRuntimeId[blockRuntimeId] = previous;
                else
                    ByBlockRuntimeId.Remove(blockRuntimeId);
            }
        }
    }
}
