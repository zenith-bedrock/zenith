using Zenith.Gameplay;
using Zenith.Packets;
using Zenith.World;

namespace Zenith.Protocol;

/// <summary>Builds CraftingDataPacket from <see cref="RecipeRegistry"/> SSOT (ADR §35 / §54 / §55).</summary>
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
                var (id, count) = snap.Inputs[j];
                if (!id.IsBlock)
                    throw new InvalidOperationException(
                        $"CraftingData: recipe {snap.NetId} input must be StackKind.Block (got {id}).");
                if (!Blocks.TryGetName(id.Value, out var inName))
                    throw new InvalidOperationException($"CraftingData: unknown input BlockRuntimeId {id.Value}.");
                inputs[j] = new DefaultDescriptorInput(inName, 0, count);
            }

            if (!snap.Output.IsBlock)
                throw new InvalidOperationException(
                    $"CraftingData: recipe {snap.NetId} output must be StackKind.Block (got {snap.Output}).");
            if (!Blocks.TryGetName(snap.Output.Value, out var outName))
                throw new InvalidOperationException($"CraftingData: unknown output BlockRuntimeId {snap.Output.Value}.");

            recipes[i] = new ShapelessCraftingRecipe
            {
                RecipeId = $"zenith:recipe_{snap.NetId}",
                Inputs = inputs,
                Outputs =
                [
                    new NetworkItemStack(palette.Require(outName), (ushort)snap.OutCount, snap.Output.Value)
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
