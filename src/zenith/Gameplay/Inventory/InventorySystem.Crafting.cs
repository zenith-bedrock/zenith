using Zenith.Gameplay.Ports;
using System.Collections.Generic;
using Zenith.Gameplay.Runtime;
using Zenith.Protocol;
using Zenith.Player;
using Zenith.World;
using Zenith.Gameplay.Replication;
using Zenith.Gameplay.Survival;
using Zenith.Gameplay.WorldInteraction;

namespace Zenith.Gameplay.Inventory;

/// <summary>Pre-checks and the recipe-book craft: what a request may do before it mutates anything.</summary>
sealed partial class InventorySystem
{
    /// <summary>
    /// Soft match before mutate (§54). Positive client ids must equal last DescribeForWire;
    /// ≤0 skipped (air / deferred prediction).
    /// </summary>
    private static bool ValidateClientStackNetIds(IInventoryChannel protocol, InventoryStackAction[] actions)
    {
        foreach (var action in actions)
        {
            switch (action.Kind)
            {
                case InventoryStackActionKind.Transfer:
                case InventoryStackActionKind.Swap:
                    if (!protocol.MatchesAdvertisedStackNetId(action.From, action.FromWire.StackNetworkId) ||
                        !protocol.MatchesAdvertisedStackNetId(action.To, action.ToWire.StackNetworkId))
                        return false;
                    break;

                case InventoryStackActionKind.Drop:
                case InventoryStackActionKind.Destroy:
                case InventoryStackActionKind.Mine:
                    if (!protocol.MatchesAdvertisedStackNetId(action.From, action.FromWire.StackNetworkId))
                        return false;
                    break;
            }
        }

        return true;
    }

    /// <summary>
    /// Recipe-book craft: drains the open grid and then the inventory (hotbar first) per
    /// <see cref="RecipeRegistry.TryPlanAutoCraft"/> and leaves the result in CreatedOutput for the same request's
    /// Take/Place. The caller rolls every store back when this returns false after partially writing.
    /// </summary>
    private bool TryAutoCraft(
        global::Zenith.Player.Player player,
        in InventoryStackAction action,
        List<WireTouch> wireTouches)
    {
        if (player.Windows.CraftUi.HasPendingOutputs) return false;

        var isAtTable = player.Windows.IsAtCraftingTable;
        ICraftGrid grid = isAtTable ? player.Windows.TableCraftUi : player.Windows.CraftUi;

        var references = new List<InventorySlotReference>(grid.GridSize + PlayerInventory.FullInventorySize);
        for (var g = 0; g < grid.GridSize; g++)
            references.Add(isAtTable ? InventorySlotReference.TableCraftGrid(g) : InventorySlotReference.CraftGrid(g));
        for (var i = 0; i < PlayerInventory.FullInventorySize; i++)
            references.Add(InventorySlotReference.Player(i)); // slots 0..8 are the hotbar, so it drains first

        var sources = references.Select(r => GetSlot(player, r)).ToArray();
        if (!_recipes.TryPlanAutoCraft(action.RecipeNetId, action.CraftTimes, sources, isAtTable, out var recipe, out var takes))
            return false;

        for (var i = 0; i < references.Count; i++)
        {
            if (takes[i] == 0) continue;
            var left = sources[i].Count - takes[i];
            if (!TrySetSlot(player, references[i], left == 0 ? InventorySlot.Empty : sources[i] with { Count = left }))
                return false;
            AddWireTouch(wireTouches, references[i], default);
        }

        var times = action.CraftTimes;
        if (!player.Windows.CraftUi.TryStageOutputs(recipe.Outputs.Select(
                output => new InventorySlot(output.Stack, output.Count * times)).ToArray()))
            return false;
        AddWireTouch(wireTouches, InventorySlotReference.CraftResult,
            InventoryContainerMap.CreatedOutput, InventoryContainerMap.CraftingResultWireSlot);
        return true;
    }
}
