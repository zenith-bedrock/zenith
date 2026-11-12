namespace Zenith.BedrockData.Recipes;

/// <summary>
/// Recipes BDS registers in code rather than shipping as pack JSON: colour dyeing families and a handful of vanilla
/// recipes (slabs, small wood parts, ...). Built from rules over real flattened item names, never from the wire's
/// junk <c>name:aux</c> descriptors. Every item used must exist in the registry.
/// </summary>
internal static class CodeRecipes
{
    public static List<Recipe> Build(IReadOnlySet<string> registry)
    {
        var all = new List<Recipe>();
        Dyeing(all);
        Harnesses(all);
        Beds(all);
        BedDyeing(all);
        all.AddRange(Vanilla());
        foreach (var r in all)
        {
            foreach (var cell in r.Inputs.OfType<Ingredient>().Where(c => c.Name is not null))
                if (!registry.Contains(cell.Name!))
                    throw new InvalidDataException($"generated recipe {r.Id} uses unknown item {cell.Name}");
            if (!registry.Contains(r.Outputs[0].Name))
                throw new InvalidDataException($"generated recipe {r.Id} makes unknown item {r.Outputs[0].Name}");
        }
        return all;
    }

    private static void Dyeing(List<Recipe> output)
    {
        Family("dye_wool", c => $"minecraft:{c}_wool");
        Family("dye_wool_slab", c => $"minecraft:{c}_wool_slab");
        Family("dye_wool_stairs", c => $"minecraft:{c}_wool_stairs");
        Family("dye_shulker_box", c => $"minecraft:{c}_shulker_box", "minecraft:undyed_shulker_box");
        Family("dye_bundle", c => $"minecraft:{c}_bundle", "minecraft:bundle");
        Family("dye_cushion", c => $"minecraft:{c}_cushion");
        Family("dye_harness", c => $"minecraft:{c}_harness");

        // A coloured block + any dye -> that colour.
        void Family(string prefix, Func<string, string> member, string? undyed = null)
        {
            var sources = RecipeTables.Colours.Select(member).Concat(undyed is null ? [] : [undyed]).ToList();
            foreach (var target in RecipeTables.Colours)
            {
                var result = member(target);
                foreach (var source in sources.Where(s => s != result))
                    foreach (var dye in RecipeTables.ColourDyes[target])
                        output.Add(new Recipe
                        {
                            Id = $"{prefix}_{target}_from_{source.Split(':')[1]}_with_{dye}",
                            Inputs = [Ingredient.Item($"minecraft:{dye}"), Ingredient.Item(source)],
                            Outputs = [new Product(result, 1)],
                        });
            }
        }
    }

    /// <summary>A bed is the item <c>bed</c> whose aux is the wool's colour index; bamboo planks have their own recipe.</summary>
    private static void Beds(List<Recipe> output)
    {
        for (var colour = 0; colour < RecipeTables.Colours.Length; colour++)
        {
            var name = RecipeTables.Colours[colour];
            foreach (var (suffix, planks) in new[] { ("", Ingredient.OfTag("minecraft:planks")), ("_bamboo", Item("bamboo_planks")) })
                output.Add(new Recipe
                {
                    Id = $"bed_{name}{suffix}", Width = 3, Height = 2, Symmetric = true,
                    Inputs = [Item($"{name}_wool"), Item($"{name}_wool"), Item($"{name}_wool"), planks, planks, planks],
                    Outputs = [new Product("minecraft:bed", 1, colour)],
                });
        }
    }

    /// <summary>A bed of another colour + any dye of the target colour recolours it (20 dyes x 15 source colours).</summary>
    private static void BedDyeing(List<Recipe> output)
    {
        for (var target = 0; target < RecipeTables.Colours.Length; target++)
            foreach (var dye in RecipeTables.ColourDyes[RecipeTables.Colours[target]])
                for (var source = 0; source < RecipeTables.Colours.Length; source++)
                {
                    if (source == target) continue;
                    output.Add(new Recipe
                    {
                        Id = $"bed_dye_{dye}_from_{RecipeTables.Colours[source]}",
                        Inputs = [Ingredient.Item("minecraft:bed", aux: source), Item(dye)],
                        Outputs = [new Product("minecraft:bed", 1, target)],
                    });
                }
    }

    private static void Harnesses(List<Recipe> output)
    {
        foreach (var c in RecipeTables.Colours) // a harness is made from a wool of its colour
            output.Add(new Recipe
            {
                Id = $"harness_{c}", Width = 3, Height = 2, Symmetric = false,
                Inputs =
                [
                    Item("leather"), Item("leather"), Item("leather"),
                    Item("glass"), Item($"{c}_wool"), Item("glass"),
                ],
                Outputs = [new Product($"minecraft:{c}_harness", 1)],
            });
    }

    private static List<Recipe> Vanilla()
    {
        var out_ = new List<Recipe>();

        void Shaped(string id, int w, int h, string?[] cells, string result, int count = 1) =>
            out_.Add(new Recipe
            {
                Id = id, Width = w, Height = h, Symmetric = true,
                Inputs = cells.Select(c => c is null ? null : Item(c)).ToList(),
                Outputs = [new Product($"minecraft:{result}", count)],
            });

        foreach (var (material, slab) in RecipeTables.Slabs)
            Shaped($"{slab}_from_{material}", 3, 1, [material, material, material], slab, 6);
        foreach (var (slab, block) in RecipeTables.Chiseled)
            Shaped($"{block}_from_{slab}", 1, 2, [slab, slab], block);
        foreach (var (button, plate, trapdoor, planks) in RecipeTables.WoodParts)
        {
            Shaped($"{button}_from_{planks}", 1, 1, [planks], button);
            Shaped($"{plate}_from_{planks}", 2, 1, [planks, planks], plate);
            Shaped($"{trapdoor}_from_{planks}", 3, 2, [planks, planks, planks, planks, planks, planks], trapdoor, 2);
        }
        foreach (var (chiseled, slab) in new[] { ("chiseled_sandstone", "sandstone_slab"), ("chiseled_red_sandstone", "red_sandstone_slab") })
            Shaped($"{slab}_from_{chiseled}", 3, 1, [chiseled, chiseled, chiseled], slab, 6);
        Shaped("stick_from_bamboo", 1, 2, ["bamboo", "bamboo"], "stick");
        Shaped("decorated_pot_from_bricks", 3, 3, [null, "brick", null, "brick", null, "brick", null, "brick", null], "decorated_pot");
        return out_;
    }

    private static Ingredient Item(string name) => Ingredient.Item($"minecraft:{name}");
}
