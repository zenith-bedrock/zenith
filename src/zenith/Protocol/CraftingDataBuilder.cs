using Zenith.Gameplay;
using Zenith.Packets;
using Zenith.World;

namespace Zenith.Protocol;

/// <summary>Builds CraftingDataPacket from <see cref="RecipeRegistry"/> SSOT (ADR §35 / §54).</summary>
static class CraftingDataBuilder
{
    /// <summary>Wire DTOs from <see cref="RecipeRegistry"/> SSOT (ADR §35) — no second recipe table.</summary>
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
                var (runtimeId, count) = snap.Inputs[j];
                if (!Blocks.TryGetName(runtimeId, out var inName))
                    throw new InvalidOperationException($"CraftingData: unknown input runtime {runtimeId}.");
                inputs[j] = new DefaultDescriptorInput(palette.Require(inName), 0, count);
            }

            if (!Blocks.TryGetName(snap.OutRuntimeId, out var outName))
                throw new InvalidOperationException($"CraftingData: unknown output runtime {snap.OutRuntimeId}.");

            recipes[i] = new ShapelessCraftingRecipe
            {
                RecipeId = $"zenith:recipe_{snap.NetId}",
                Inputs = inputs,
                Outputs =
                [
                    new NetworkItemStack(palette.Require(outName), (ushort)snap.OutCount, snap.OutRuntimeId)
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

}
