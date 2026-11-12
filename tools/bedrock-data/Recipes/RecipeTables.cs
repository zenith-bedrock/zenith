namespace Zenith.BedrockData.Recipes;

/// <summary>Explicit tables turning legacy pack spellings and legacy <c>data</c> values into current item names.</summary>
internal static class RecipeTables
{
    public const string Station = "crafting_table";

    /// <summary>Legacy item spellings still used by pack JSON -> the name in the current item registry.</summary>
    public static readonly IReadOnlyDictionary<string, string> Aliases = new Dictionary<string, string>
    {
        ["minecraft:appleenchanted"] = "minecraft:enchanted_golden_apple",
        ["minecraft:carrotonastick"] = "minecraft:carrot_on_a_stick",
        ["minecraft:chain"] = "minecraft:iron_chain",
        ["chain"] = "minecraft:iron_chain",
        ["minecraft:chorus_fruit_popped"] = "minecraft:popped_chorus_fruit",
        ["minecraft:darkoak_sign"] = "minecraft:dark_oak_sign",
        ["minecraft:emptymap"] = "minecraft:empty_map",
        ["minecraft:horsearmorleather"] = "minecraft:leather_horse_armor",
        ["minecraft:melon"] = "minecraft:melon_slice",
        ["minecraft:netherstar"] = "minecraft:nether_star",
        ["minecraft:quartz_block:0"] = "minecraft:quartz_block",
        ["minecraft:record_5"] = "minecraft:music_disc_5",
        ["minecraft:reeds"] = "minecraft:sugar_cane",
        ["minecraft:sealantern"] = "minecraft:sea_lantern",
        ["minecraft:sign"] = "minecraft:oak_sign",
        ["minecraft:speckled_melon"] = "minecraft:glistering_melon_slice",
        ["minecraft:turtle_shell_piece"] = "minecraft:turtle_scute",
    };

    /// <summary>Dye colours in the order Bedrock numbers them (wool/bed/banner colour index).</summary>
    public static readonly string[] Colours =
    [
        "white", "orange", "magenta", "light_blue", "yellow", "lime", "pink", "gray", "light_gray", "cyan",
        "purple", "blue", "brown", "green", "red", "black",
    ];

    public static readonly string[] Dyes =
    [
        "ink_sac", "red_dye", "green_dye", "cocoa_beans", "lapis_lazuli", "purple_dye", "cyan_dye", "light_gray_dye",
        "gray_dye", "pink_dye", "lime_dye", "yellow_dye", "light_blue_dye", "magenta_dye", "orange_dye", "bone_meal",
        "black_dye", "brown_dye", "blue_dye", "white_dye",
    ];

    public static readonly string[] Woods = ["oak", "spruce", "birch", "jungle", "acacia", "dark_oak", "mangrove", "bamboo"];

    /// <summary>Items whose legacy <c>data</c> value selects a flattened item. Other items with data != 0 are unresolved.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> DataNames = new Dictionary<string, string[]>
    {
        ["minecraft:dye"] = Dyes.Select(n => $"minecraft:{n}").ToArray(),
        ["minecraft:boat"] = Woods.Select(w => w == "bamboo" ? "minecraft:bamboo_raft" : $"minecraft:{w}_boat").ToArray(),
        ["minecraft:chest_boat"] = Woods.Select(w => w == "bamboo" ? "minecraft:bamboo_chest_raft" : $"minecraft:{w}_chest_boat").ToArray(),
        ["minecraft:bucket"] = ["minecraft:bucket", "minecraft:milk_bucket"],
        ["minecraft:banner_pattern"] = new[] { "creeper", "skull", "flower", "mojang", "field_masoned", "bordure_indented" }
            .Select(n => $"minecraft:{n}_banner_pattern").ToArray(),
        ["minecraft:carpet"] = Colours.Select(c => $"minecraft:{c}_carpet").ToArray(),
        ["minecraft:skull"] =
        [
            "minecraft:skeleton_skull", "minecraft:wither_skeleton_skull", "minecraft:zombie_head", "minecraft:player_head",
            "minecraft:creeper_head", "minecraft:dragon_head", "minecraft:piglin_head",
        ],
        ["minecraft:sandstone"] =
            ["minecraft:sandstone", "minecraft:chiseled_sandstone", "minecraft:cut_sandstone", "minecraft:smooth_sandstone"],
        ["minecraft:quartz_block"] = ["minecraft:quartz_block", "minecraft:chiseled_quartz_block", "minecraft:quartz_pillar"],
        ["minecraft:purpur_block"] = ["minecraft:purpur_block", "minecraft:purpur_block", "minecraft:purpur_pillar"],
    };

    /// <summary>Items whose pack <c>data</c> is a legacy facing/variant hint BDS itself discards (the wire says plain <c>name:0</c>).</summary>
    public static readonly IReadOnlySet<string> IgnoredData = new HashSet<string>
    {
        "minecraft:dispenser", "minecraft:dropper", "minecraft:piston", "minecraft:sticky_piston",
        "minecraft:bamboo_planks", "minecraft:mangrove_planks",
    };

    /// <summary>Every dye item that tints a colour: the plain dye plus the raw material that doubles as one.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> ColourDyes = new Dictionary<string, string[]>
    {
        ["white"] = ["white_dye", "bone_meal"], ["orange"] = ["orange_dye"], ["magenta"] = ["magenta_dye"],
        ["light_blue"] = ["light_blue_dye"], ["yellow"] = ["yellow_dye"], ["lime"] = ["lime_dye"], ["pink"] = ["pink_dye"],
        ["gray"] = ["gray_dye"], ["light_gray"] = ["light_gray_dye"], ["cyan"] = ["cyan_dye"], ["purple"] = ["purple_dye"],
        ["blue"] = ["blue_dye", "lapis_lazuli"], ["brown"] = ["brown_dye", "cocoa_beans"], ["green"] = ["green_dye"],
        ["red"] = ["red_dye"], ["black"] = ["black_dye", "ink_sac"],
    };

    /// <summary>Slab material -> slab: three in a row make six.</summary>
    public static readonly IReadOnlyDictionary<string, string> Slabs = new Dictionary<string, string>
    {
        ["cobblestone"] = "cobblestone_slab", ["nether_brick"] = "nether_brick_slab", ["sandstone"] = "sandstone_slab",
        ["stone"] = "normal_stone_slab", ["mossy_stone_bricks"] = "mossy_stone_brick_slab", ["brick_block"] = "brick_slab",
        ["stone_bricks"] = "stone_brick_slab", ["smooth_stone"] = "smooth_stone_slab", ["red_sandstone"] = "red_sandstone_slab",
        ["prismarine_bricks"] = "prismarine_brick_slab", ["dark_prismarine"] = "dark_prismarine_slab",
        ["mossy_cobblestone"] = "mossy_cobblestone_slab", ["red_nether_brick"] = "red_nether_brick_slab",
        ["smooth_sandstone"] = "smooth_sandstone_slab", ["andesite"] = "andesite_slab", ["diorite"] = "diorite_slab",
        ["granite"] = "granite_slab", ["polished_granite"] = "polished_granite_slab",
        ["polished_andesite"] = "polished_andesite_slab", ["polished_diorite"] = "polished_diorite_slab",
        ["smooth_red_sandstone"] = "smooth_red_sandstone_slab", ["cut_red_sandstone"] = "cut_red_sandstone_slab",
        ["cut_sandstone"] = "cut_sandstone_slab", ["smooth_quartz"] = "smooth_quartz_slab", ["quartz_block"] = "quartz_slab",
        ["prismarine"] = "prismarine_slab", ["end_bricks"] = "end_stone_brick_slab", ["purpur_block"] = "purpur_slab",
    };

    /// <summary>Two slabs stacked make the chiseled block.</summary>
    public static readonly IReadOnlyDictionary<string, string> Chiseled = new Dictionary<string, string>
    {
        ["quartz_slab"] = "chiseled_quartz_block", ["stone_brick_slab"] = "chiseled_stone_bricks",
        ["red_sandstone_slab"] = "chiseled_red_sandstone", ["sandstone_slab"] = "chiseled_sandstone",
        ["purpur_slab"] = "purpur_pillar",
    };

    /// <summary>Wood types whose button/plate/trapdoor BDS registers in code. Oak uses the legacy "wooden_*" names.</summary>
    public static readonly IReadOnlyList<(string Button, string Plate, string Trapdoor, string Planks)> WoodParts =
    [
        ("wooden_button", "wooden_pressure_plate", "trapdoor", "oak_planks"),
        .. new[] { "spruce", "birch", "jungle", "acacia", "dark_oak" }
            .Select(w => ($"{w}_button", $"{w}_pressure_plate", $"{w}_trapdoor", $"{w}_planks")),
    ];

    /// <summary>Items whose colour/variant lives in an aux value Zenith's item model does not carry (even when it is 0).</summary>
    public static readonly IReadOnlySet<string> AuxItems = new HashSet<string>
    {
        "minecraft:bed", "minecraft:banner", "minecraft:firework_star", "minecraft:arrow", "minecraft:planks",
        "minecraft:empty_map", "minecraft:suspicious_stew",
    };
}
