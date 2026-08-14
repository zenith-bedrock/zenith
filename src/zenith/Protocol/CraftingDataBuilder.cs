using Zenith.Gameplay;
using Zenith.Packets;
using Zenith.World;

namespace Zenith.Protocol;

/// <summary>Builds CraftingDataPacket from <see cref="RecipeRegistry"/> SSOT (ADR §35 / §54 / §55).</summary>
static class CraftingDataBuilder
{
    /// <summary>
    /// Wire DTOs from <see cref="RecipeRegistry"/> SSOT (ADR §35) — no second recipe table.
    /// Both StackId kinds are valid here: the wire "name" descriptor (<see cref="DefaultDescriptorInput"/>)
    /// and <see cref="NetworkItemStack"/> both identify by palette name/network id regardless of
    /// whether the underlying stack is a block or a plain item (Phase XXV — tool/stick recipes
    /// output items, not blocks; the earlier Block-only assumption only held because the first two
    /// recipes happened to both output blocks).
    /// </summary>
    public static CraftingDataPacket Build(RecipeRegistry registry, ItemPalette palette)
    {
        var snapshots = registry.SnapshotRecipes();
        var recipes = new ShapelessCraftingRecipe[snapshots.Count];
        for (var i = 0; i < snapshots.Count; i++)
        {
            var snap = snapshots[i];
            var inputs = new DefaultDescriptorInput[snap.Inputs.Length];
            for (var j = 0; j < snap.Inputs.Length; j++)
            {
                var (id, count) = snap.Inputs[j];
                inputs[j] = new DefaultDescriptorInput(ResolveName(id, palette, snap.NetId, "input"), 0, count);
            }

            var outName = ResolveName(snap.Output, palette, snap.NetId, "output");
            var outBlockRid = snap.Output.IsBlock ? snap.Output.Value : 0;

            recipes[i] = new ShapelessCraftingRecipe
            {
                RecipeId = $"zenith:recipe_{snap.NetId}",
                Inputs = inputs,
                Outputs =
                [
                    new NetworkItemStack(palette.Require(outName), (ushort)snap.OutCount, outBlockRid)
                ],
                RecipeNetworkId = snap.NetId
            };
        }

        return new CraftingDataPacket
        {
            Recipes = recipes,
            ClearRecipes = true
        };
    }

    private static string ResolveName(StackId id, ItemPalette palette, uint recipeNetId, string role)
    {
        if (id.IsBlock)
        {
            if (!Blocks.TryGetName(id.Value, out var name))
                throw new InvalidOperationException(
                    $"CraftingData: unknown {role} BlockRuntimeId {id.Value} (recipe {recipeNetId}).");
            return name;
        }

        if (!palette.TryGetName(id.Value, out var itemName))
            throw new InvalidOperationException(
                $"CraftingData: unknown {role} ItemNetworkId {id.Value} (recipe {recipeNetId}).");
        return itemName;
    }
}
