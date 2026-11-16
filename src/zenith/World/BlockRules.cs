namespace Zenith.World;

/// <summary>
/// Name-based knowledge the palette cannot express: which blocks need behaviour Zenith does not simulate yet
/// (so they stay unsupported) and which are ground decoration. Everything is matched on the flattened Bedrock block
/// name, so a new vanilla variant (another wood, another flower) is covered without an edit.
/// </summary>
static class BlockSupportRules
{
    // Block entities, liquids, portals and technical blocks: placing them without their logic would be a silent lie.
    private static readonly HashSet<string> ExcludedNames = new(StringComparer.Ordinal)
    {
        "minecraft:air", "minecraft:water", "minecraft:flowing_water", "minecraft:lava", "minecraft:flowing_lava",
        "minecraft:fire", "minecraft:soul_fire", "minecraft:portal", "minecraft:end_portal", "minecraft:end_gateway",
        "minecraft:mob_spawner", "minecraft:trial_spawner", "minecraft:vault", "minecraft:structure_block",
        "minecraft:structure_void", "minecraft:jigsaw", "minecraft:barrier", "minecraft:border_block", "minecraft:allow",
        "minecraft:deny", "minecraft:light_block", "minecraft:beacon", "minecraft:enchanting_table", "minecraft:jukebox",
        "minecraft:conduit", "minecraft:bell", "minecraft:brewing_stand", "minecraft:hopper", "minecraft:lectern",
        "minecraft:unknown", "minecraft:moving_block", "minecraft:info_update", "minecraft:info_update2",
        "minecraft:reserved6", "minecraft:chest", "minecraft:trapped_chest", "minecraft:ender_chest",
        "minecraft:crafting_table", "minecraft:bed", "minecraft:sea_pickle", "minecraft:cake", "minecraft:sponge"
    };

    public static bool IsExcluded(string name)
    {
        if (ExcludedNames.Contains(name)) return true;
        return name.Contains("command_block", StringComparison.Ordinal) ||
               name.Contains("shulker_box", StringComparison.Ordinal) ||
               name.Contains("_sign", StringComparison.Ordinal) ||
               name.Contains("_head", StringComparison.Ordinal) ||
               name.Contains("_skull", StringComparison.Ordinal) ||
               name.EndsWith("_bed", StringComparison.Ordinal) ||
               name.EndsWith("banner", StringComparison.Ordinal) ||
               name.StartsWith("minecraft:item.", StringComparison.Ordinal);
    }

    private static readonly string[] PlantTokens =
    [
        "dandelion", "poppy", "blue_orchid", "allium", "azure_bluet", "_tulip", "oxeye_daisy", "cornflower",
        "lily_of_the_valley", "wither_rose", "torchflower", "closed_eyeblossom", "open_eyeblossom", "short_grass",
        "short_dry_grass", "tall_dry_grass", "fern", "dead_bush", "brown_mushroom", "red_mushroom", "_sapling",
        "crimson_fungus", "warped_fungus", "crimson_roots", "warped_roots", "nether_sprouts", "firefly_bush", "bush"
    ];

    public static bool IsPlant(string name)
    {
        // Block-form mushrooms, leaves and large ferns are not ground decoration.
        if (name.Contains("mushroom_block", StringComparison.Ordinal) || name.Contains("leaves", StringComparison.Ordinal) ||
            name.Contains("large_fern", StringComparison.Ordinal) || name.Contains("sweet_berry", StringComparison.Ordinal))
            return false;
        foreach (var token in PlantTokens)
            if (name.Contains(token, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>Plants that only survive on soil; mushrooms, fungi and roots accept any solid ground.</summary>
    public static bool NeedsSoil(string name) =>
        !(name.Contains("mushroom", StringComparison.Ordinal) || name.Contains("fungus", StringComparison.Ordinal) ||
          name.Contains("roots", StringComparison.Ordinal) || name.Contains("sprouts", StringComparison.Ordinal));

    public static bool IsSoil(string name) =>
        name is "minecraft:grass_block" or "minecraft:dirt" or "minecraft:podzol" or "minecraft:coarse_dirt" or
            "minecraft:rooted_dirt" or "minecraft:mycelium" or "minecraft:moss_block" or "minecraft:mud" or
            "minecraft:farmland" or "minecraft:muddy_mangrove_roots" or "minecraft:dirt_with_roots" or "minecraft:grass_path" or
            "minecraft:dirt_path";

    public static bool IsTorch(string name) =>
        name is "minecraft:torch" or "minecraft:soul_torch" or "minecraft:copper_torch";

    /// <summary>
    /// States that only change appearance (pane connections, leaf bookkeeping bits): the default permutation is placed.
    /// </summary>
    public static bool IsCosmeticState(BlockFamily family)
    {
        foreach (var property in family.Properties)
        {
            if (!(property.Name.StartsWith("minecraft:connection_", StringComparison.Ordinal) ||
                  property.Name is "persistent_bit" or "update_bit" or "deprecated"))
                return false;
        }
        return true;
    }

    /// <summary>State-independent facts about a block type, decided once when the catalog is built (needs the kind already set).</summary>
    public static BlockTraits TraitsOf(BlockFamily family)
    {
        var name = family.Name;
        var traits = BlockTraits.None;
        if (IsSoil(name)) traits |= BlockTraits.Soil;
        if (family.Kind == BlockKind.Plant && NeedsSoil(name)) traits |= BlockTraits.NeedsSoil;
        if (name.StartsWith("minecraft:iron_", StringComparison.Ordinal)) traits |= BlockTraits.RedstoneOnly;
        // Walls, fences and panes join full opaque blocks, not glass or foliage.
        if (family.Kind is BlockKind.Cube or BlockKind.Pillar &&
            !name.Contains("glass", StringComparison.Ordinal) && !name.Contains("leaves", StringComparison.Ordinal))
            traits |= BlockTraits.WallConnectable;
        return traits;
    }
}

/// <summary>Name suffixes that tell block families of the same state signature apart.</summary>
static class BlockNames
{
    public static bool IsDoor(string name) => name.EndsWith("_door", StringComparison.Ordinal);

    public static bool IsTrapdoor(string name) => name.EndsWith("trapdoor", StringComparison.Ordinal);

    public static bool IsFenceGate(string name) => name.EndsWith("fence_gate", StringComparison.Ordinal);

    public static bool IsSlab(string name) => name.EndsWith("_slab", StringComparison.Ordinal);

    public static bool IsDoubleSlab(string name) => name.EndsWith("_double_slab", StringComparison.Ordinal);
}

/// <summary>
/// Derives the harvesting tool, tier and drop gating from a block name (the table the original curated profiles were
/// written from). A heuristic over flattened Bedrock names — exact vanilla values for the families that matter, with
/// curated <see cref="DigProfiles"/> rows always winning where a block needs different treatment.
/// </summary>
static class BlockToolRules
{
    private static readonly string[] NoToolTokens =
    [
        "wool", "carpet", "glass", "_bed", "cake", "glowstone", "sea_lantern", "slime", "honey", "cactus", "web",
        "leaves", "hay_block", "sponge", "moss_block", "moss_carpet", "wart_block", "shroomlight", "sculk", "target",
        "dried_kelp", "tnt", "scaffolding", "torch", "vine", "lily", "sapling"
    ];

    private static readonly string[] WoodTypes =
        ["oak_", "spruce_", "birch_", "jungle_", "acacia_", "dark_oak_", "mangrove_", "cherry_", "bamboo_", "crimson_", "warped_", "pale_oak_"];

    // Stairs/slabs/buttons of a wood type carry no wood word of their own ("oak_stairs"), only the type prefix.
    private static bool IsWoodFamily(string n)
    {
        foreach (var type in WoodTypes)
            if (n.StartsWith(type, StringComparison.Ordinal)) return true;
        return false;
    }

    // Order matters: the first matching rule wins.
    public static (ToolKind Tool, bool RequiresTool, ToolTier Tier) Classify(string name)
    {
        var n = name[(name.IndexOf(':') + 1)..];
        bool Has(string token) => n.Contains(token, StringComparison.Ordinal);

        // Pickaxe-tier blocks that also contain wood/dirt-like words must be decided before the generic families.
        if (Has("netherite") || Has("ancient_debris") || Has("obsidian") || Has("respawn_anchor"))
            return (ToolKind.Pickaxe, true, ToolTier.Diamond);
        if (Has("nether_gold"))
            return (ToolKind.Pickaxe, true, ToolTier.Wood);
        if (Has("diamond") || Has("emerald") || Has("gold") || Has("redstone_ore") || Has("redstone_block"))
            return (ToolKind.Pickaxe, true, ToolTier.Iron);
        if (Has("iron") || Has("copper") && !Has("copper_torch") || Has("lapis") || Has("raw_iron") || Has("raw_copper"))
            return (ToolKind.Pickaxe, true, ToolTier.Stone);

        if (Has("concrete_powder") || n is "sand" or "red_sand" or "soul_sand" or "gravel" or "clay" or "snow" or "snow_layer" or
            "dirt" or "grass_block" or "podzol" or "mycelium" or "coarse_dirt" or "rooted_dirt" or "mud" or "soul_soil" or
            "farmland" or "grass_path" or "dirt_path" or "muddy_mangrove_roots" or "dirt_with_roots" or "suspicious_sand" or
            "suspicious_gravel" or "powder_snow")
            return (ToolKind.Shovel, false, ToolTier.None);

        foreach (var token in NoToolTokens)
            if (n.Contains(token, StringComparison.Ordinal)) return (ToolKind.None, false, ToolTier.None);

        if (Has("stone") || Has("brick") || Has("deepslate") || Has("tuff") || Has("basalt") || Has("blackstone") ||
            Has("quartz") || Has("prismarine") || Has("purpur") || Has("terracotta") || Has("concrete") ||
            Has("netherrack") || Has("cobble") || Has("andesite") || Has("granite") || Has("diorite") || Has("anvil") ||
            Has("nylium") || Has("amethyst") || Has("coral_block") || Has("dripstone") || Has("calcite") ||
            Has("sandstone") || Has("magma") || Has("ice") || Has("rail") || Has("chain") || Has("bars") ||
            Has("furnace") || Has("smoker") || Has("coal") || Has("_ore") || Has("bone_block") || Has("lodestone") ||
            Has("cauldron") || Has("lantern") || Has("end_rod") || Has("mud_brick") || Has("packed_mud") || Has("polished") || Has("smooth") ||
            Has("resin") || Has("lightning_rod") || Has("grindstone") || Has("copper"))
            return (ToolKind.Pickaxe, !Has("ice"), ToolTier.Wood);

        if (IsWoodFamily(n) || Has("log") || Has("wood") || Has("planks") || Has("stem") || Has("hyphae") || Has("fence") || Has("door") ||
            Has("trapdoor") || Has("barrel") || Has("bookshelf") || Has("sign") || Has("ladder") || Has("shelf") ||
            Has("note_block") || Has("loom") || Has("table") || Has("composter") || Has("bee") || Has("button") ||
            Has("pressure_plate") || Has("mosaic") || Has("mushroom_block") || Has("pumpkin") || Has("melon"))
            return (ToolKind.Axe, false, ToolTier.None);

        return (ToolKind.None, false, ToolTier.None);
    }
}
