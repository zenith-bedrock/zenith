namespace Zenith.World;

/// <summary>
/// Every block the server's own code names (world generation, placement sets, gravity, passability). The member name is the
/// block's identity: its wire name is the same words in snake_case in the <c>minecraft:</c> namespace, so a new entry is one
/// line here plus an accessor on <see cref="Blocks"/>. All of them are resolved against the palette in one pass at load, and
/// one that is missing is reported with every other that is missing.
/// </summary>
enum KnownBlock
{
    Air,
    Stone,
    GrassBlock,
    Dirt,
    OakPlanks,
    OakLog,
    OakLeaves,
    OakSapling,
    Sand,
    Gravel,
    Bedrock,
    Water,
    Lava,
    Obsidian,
    Fire,
    Cobblestone,
    Deepslate,
    CoalOre,
    IronOre,
    CopperOre,
    GoldOre,
    DiamondOre,
    LapisOre,
    RedstoneOre,
    DeepslateCoalOre,
    DeepslateIronOre,
    DeepslateCopperOre,
    DeepslateGoldOre,
    DeepslateDiamondOre,
    DeepslateLapisOre,
    DeepslateRedstoneOre,
    ShortGrass,
    Dandelion,
    Poppy,
    Torch,
    OakFence,
    StoneBricks,
    CobblestoneWall,
    CraftingTable,
    Chest,
}

static class KnownBlocks
{
    private const string Namespace = "minecraft:";

    public static readonly int Count = Enum.GetValues<KnownBlock>().Length;

    public static string WireName(KnownBlock block)
    {
        var name = block.ToString();
        var builder = new System.Text.StringBuilder(Namespace, Namespace.Length + name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i])) builder.Append('_');
            builder.Append(char.ToLowerInvariant(name[i]));
        }

        return builder.ToString();
    }

    /// <summary>The runtime id of every known block, indexed by member; throws once, listing each one the palette lacks.</summary>
    public static int[] Resolve(BlockPalette palette)
    {
        var ids = new int[Count];
        var missing = new List<string>();
        foreach (var block in Enum.GetValues<KnownBlock>())
        {
            var name = WireName(block);
            if (palette.TryGet(name, out var id)) ids[(int)block] = id;
            else missing.Add(name);
        }

        if (missing.Count > 0)
            throw new InvalidOperationException("The block palette lacks: " + string.Join(", ", missing));
        return ids;
    }

    /// <summary>Runtime id to wire name, for the known blocks.</summary>
    public static Dictionary<int, string> NamesByRuntimeId(int[] ids)
    {
        var names = new Dictionary<int, string>(ids.Length);
        foreach (var block in Enum.GetValues<KnownBlock>())
            names[ids[(int)block]] = WireName(block);
        return names;
    }
}
