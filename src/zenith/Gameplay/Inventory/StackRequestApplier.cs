using Zenith.Gameplay.Ports;
using System.Collections.Generic;
using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.World;
using Zenith.Gameplay.Replication;
using Zenith.Gameplay.Survival;
using Zenith.Gameplay.WorldInteraction;

namespace Zenith.Gameplay.Inventory;

/// <summary>
/// One ItemStackRequest, atomically: validates it against the open container and the advertised stack ids, snapshots every store it
/// may touch, applies each action, deposits drops, and either commits (response, persistence, fan-out) or rolls everything back.
/// </summary>
sealed class StackRequestApplier
{
    private readonly PlayerManager _players;
    private readonly World.World _world;
    private readonly RecipeRegistry _recipes;
    private readonly CreativeCatalog _creative;
    private readonly InventorySlots _slots;

    public StackRequestApplier(
        PlayerManager players, World.World world, RecipeRegistry recipes, CreativeCatalog creative, InventorySlots slots)
    {
        _players = players;
        _world = world;
        _recipes = recipes;
        _creative = creative;
        _slots = slots;
    }

    public void Apply(
        global::Zenith.Player.Player player,
        in InventoryStackIntent intent,
        IReadOnlyList<global::Zenith.Player.Player> online)
    {
        var protocol = player.Client.Inventory;
        var inventory = player.Inventory;

        if (!intent.UncorrelatedLegacyDrop && !player.Windows.TryClaimInventoryRequest(intent.RequestId))
        {
            Reject(player, intent, $"duplicate request {intent.RequestId}");
            return;
        }

        if ((intent.RequiresOpenContainer || intent.ExpectedOpenContainerGeneration != 0) &&
            (player.Windows.OpenContainer is not { } openContainer ||
             (intent.RequiredOpenContainerTarget is { } requiredTarget && openContainer.Target != requiredTarget) ||
             (intent.ExpectedOpenContainerGeneration != 0 &&
              openContainer.Generation != intent.ExpectedOpenContainerGeneration)))
        {
            Reject(player, intent, $"stale open-container session (request {intent.RequestId})");
            return;
        }

        if (!ValidateClientStackNetIds(protocol, intent.Actions))
        {
            Reject(player, intent, $"stack net id mismatch (request {intent.RequestId})");
            return;
        }

        var before = RequestSnapshot.Capture(player, _world);

        var wireTouches = new List<WireTouch>();
        var pendingDrops = new List<FloorDropFanout.DepositRequest>();
        var ok = true;

        foreach (var action in intent.Actions)
        {
            ok = TryApplyAction(player, action, wireTouches, pendingDrops);
            if (!ok) break;
        }

        if (ok && pendingDrops.Count != 0)
        {
            var x = (int)MathF.Floor(player.PositionX);
            var y = (int)MathF.Floor(player.PositionY);
            var z = (int)MathF.Floor(player.PositionZ);
            ok = FloorDropFanout.TryDepositBatch(
                _world, _players, online, x, y, z, pendingDrops,
                FloorDropFanout.PlayerThrowPickupDelay, searchRadius: 3,
                launch: FloorDropPhysics.Launch(_world, player));
        }

        if (!ok)
        {
            before.Restore(player, _world);
            Reject(player, intent, reason: null);
            return;
        }

        if (!intent.UncorrelatedLegacyDrop) protocol.SendItemStackResponseOk(intent.RequestId, player, wireTouches);
        if (player.Windows.InventoryWindowOpen)
            protocol.SendUiInventoryContent(player);
        if (before.Chest is { } openAfter)
            protocol.SendChestContent(_world.Chests, openAfter);
        _world.Persistence.PersistInventory(player);

        var armorTouched = false;
        var offhandTouched = false;
        foreach (var touch in wireTouches)
        {
            armorTouched |= touch.Reference.Area == InventorySlotArea.Armor;
            offhandTouched |= touch.Reference.Area == InventorySlotArea.Offhand;
        }

        if (armorTouched)
        {
            protocol.SendArmorContent(inventory);
            _world.Persistence.PersistArmor(player);
            ArmorFanout.Broadcast(player, online);
        }

        if (offhandTouched)
        {
            protocol.SendOffhandContent(inventory);
            _world.Persistence.PersistOffhand(player);
            ArmorFanout.BroadcastOffhand(player, online);
        }

        if (before.Chest is { } openChest)
        {
            _world.MarkBlockEntityDirty(openChest.PrimaryX, openChest.PrimaryZ);
            if (openChest.TryGetPartner(out var ppx, out var ppy, out var ppz))
                _world.MarkBlockEntityDirty(ppx, ppz);
        }
    }

    /// <summary>One action of a request. False means the whole request must be rolled back.</summary>
    private bool TryApplyAction(
        global::Zenith.Player.Player player, InventoryStackAction action, List<WireTouch> wireTouches, List<FloorDropFanout.DepositRequest> pendingDrops)
    {
        switch (action.Kind)
        {
            case InventoryStackActionKind.CraftRecipe:
            {
                // ADR §139: which physical grid has the ingredients follows which container is
                // actually open — a table-only recipe is refused inside TryCraftFromGrid itself
                // (isAtTable), not by which grid happened to be passed in.
                var isAtTable = player.Windows.IsAtCraftingTable;
                ICraftGrid grid = isAtTable ? player.Windows.TableCraftUi : player.Windows.CraftUi;

                // Materialize CreatedOutput so same-request Take/Place can move it;
                // refuse if a prior result is still sitting untaken.
                if (player.Windows.CraftUi.HasPendingOutputs ||
                    !_recipes.TryCraftOutputsFromGrid(grid, action.RecipeNetId, out var crafted, action.CraftTimes, isAtTable) ||
                    !player.Windows.CraftUi.TryStageOutputs(crafted))
                {
                    return false;
                }

                for (var g = 0; g < grid.GridSize; g++)
                {
                    AddWireTouch(wireTouches,
                        isAtTable ? InventorySlotReference.TableCraftGrid(g) : InventorySlotReference.CraftGrid(g),
                        InventoryContainerMap.CraftingInput,
                        isAtTable ? InventoryContainerMap.TableCraftGridWireSlot(g) : InventoryContainerMap.CraftGridWireSlot(g));
                }

                AddWireTouch(wireTouches, InventorySlotReference.CraftResult,
                    InventoryContainerMap.CreatedOutput,
                    InventoryContainerMap.CraftingResultWireSlot);
                return true;
            }

            case InventoryStackActionKind.CraftAuto:
                return TryAutoCraft(player, action, wireTouches);

            case InventoryStackActionKind.CraftCreative:
                // Creative pick: one legal stack into CreatedOutput; same-request Place/Take/Drop moves it.
                // CreatedOutput is authoritative pending state: never overwrite a prior result.
                if (player.Windows.CraftUi.HasPendingOutputs ||
                    player.GameMode != GameMode.Creative ||
                    !_creative.TryGet(action.CreativeNetId, out var creativeId, out _) ||
                    !player.Windows.CraftUi.TryStageOutputs([new InventorySlot(creativeId, ItemStackLimits.For(creativeId))]))
                {
                    player.Level.Logger?.Debug(
                        $"CraftCreative refused for {player.Username}: net={action.CreativeNetId} mode={player.GameMode} pending={player.Windows.CraftUi.HasPendingOutputs}");
                    return false;
                }

                AddWireTouch(wireTouches, InventorySlotReference.CraftResult,
                    InventoryContainerMap.CreatedOutput,
                    InventoryContainerMap.CraftingResultWireSlot);
                return true;

            case InventoryStackActionKind.Create:
                // Select one indexed result into the shared CreatedOutput slot.
                if (!player.Windows.CraftUi.TrySelectOutput(action.ResultIndex))
                {
                    return false;
                }

                AddWireTouch(wireTouches, InventorySlotReference.CraftResult,
                    InventoryContainerMap.CreatedOutput, InventoryContainerMap.CraftingResultWireSlot);
                return true;

            case InventoryStackActionKind.NoOp:
                return true;

            case InventoryStackActionKind.Swap:
                if (!_slots.TrySwap(player, action.From, action.To))
                {
                    return false;
                }

                AddWireTouch(wireTouches, action.From, action.FromWire);
                AddWireTouch(wireTouches, action.To, action.ToWire);
                return true;

            case InventoryStackActionKind.Drop:
            {
                if (action.ExpectedSourceStack is { } original && _slots.GetSlot(player, action.From) != original)
                {
                    return false;
                }
                var count = action.Count;
                if (count == 0)
                    count = _slots.GetSlot(player, action.From).Count;
                if (!_slots.TryRemoveForDrop(player, action.From, count, pendingDrops))
                {
                    return false;
                }

                AddWireTouch(wireTouches, action.From, action.FromWire);
                return true;
            }

            case InventoryStackActionKind.Destroy:
                // The Bedrock Destroy request is the Creative UI's delete gesture. It is not
                // a drop and cannot be used to delete survival state.
                if (player.GameMode != GameMode.Creative ||
                    !_slots.TryDestroy(player, action.From, action.Count))
                {
                    return false;
                }

                AddWireTouch(wireTouches, action.From, action.FromWire);
                return true;

            case InventoryStackActionKind.Mine:
                // Bedrock's MineBlock ISR is its durability reconciliation hook. Zenith has
                // no durability component yet, so validate the advertised held stack and
                // echo it through ItemStackResponse without inventing a mutation here.
                if (!_slots.IsValidReference(player, action.From))
                {
                    return false;
                }

                AddWireTouch(wireTouches, action.From, action.FromWire);
                return true;

            default:
            {
                var count = action.Count;
                if (count == 0)
                    count = _slots.GetSlot(player, action.From).Count;
                if (!_slots.TryTransfer(player, action.From, action.To, count))
                {
                    return false;
                }

                AddWireTouch(wireTouches, action.From, action.FromWire);
                AddWireTouch(wireTouches, action.To, action.ToWire);
                return true;
            }
        }
    }

    /// <summary>Tells the client the request failed and resends every store it may have shown stale, optionally logging why.</summary>
    private void Reject(global::Zenith.Player.Player player, in InventoryStackIntent intent, string? reason)
    {
        var protocol = player.Client.Inventory;
        if (!intent.UncorrelatedLegacyDrop) protocol.SendItemStackResponseError(intent.RequestId);
        protocol.SendInventoryContent(player.Inventory);
        protocol.SendUiInventoryContent(player);
        if (player.Windows.OpenChest is { } open)
            protocol.SendChestContent(_world.Chests, open);
        if (reason is not null)
            player.Level.Logger?.Debug($"ISR rejected for {player.Username}: {reason}.");
    }

    /// <summary>Every store a request may touch, captured before it runs so a failure leaves nothing half-applied.</summary>
    private readonly struct RequestSnapshot
    {
        private readonly InventorySnapshot _inventory;
        private readonly InventorySlot[] _armor;
        private readonly PlayerCraftUi.FullSnapshot _craft;
        private readonly InventorySlot[] _tableCraft;
        private readonly InventorySlot[]? _chestSlots;

        public OpenChestView? Chest { get; }

        private RequestSnapshot(InventorySnapshot inventory, InventorySlot[] armor, PlayerCraftUi.FullSnapshot craft, InventorySlot[] tableCraft, OpenChestView? chest, InventorySlot[]? chestSlots)
        {
            _inventory = inventory;
            _armor = armor;
            _craft = craft;
            _tableCraft = tableCraft;
            Chest = chest;
            _chestSlots = chestSlots;
        }

        public static RequestSnapshot Capture(global::Zenith.Player.Player player, World.World world)
        {
            var chest = player.Windows.OpenChest;
            return new RequestSnapshot(
                player.Inventory.CaptureSnapshot(), player.Inventory.SnapshotArmor(),
                player.Windows.CraftUi.CaptureFullSnapshot(), player.Windows.TableCraftUi.CaptureSnapshot(),
                chest, chest is { } view ? world.Chests.CaptureOpenSnapshot(view) : null);
        }

        public void Restore(global::Zenith.Player.Player player, World.World world)
        {
            player.Inventory.RestoreSnapshot(_inventory);
            player.Inventory.RestoreArmorSnapshot(_armor);
            player.Windows.CraftUi.RestoreFullSnapshot(_craft);
            player.Windows.TableCraftUi.RestoreSnapshot(_tableCraft);
            if (Chest is { } view && _chestSlots is not null)
                world.Chests.RestoreOpenSnapshot(view, _chestSlots);
        }
    }

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

        var sources = references.Select(r => _slots.GetSlot(player, r)).ToArray();
        if (!_recipes.TryPlanAutoCraft(action.RecipeNetId, action.CraftTimes, sources, isAtTable, out var recipe, out var takes))
            return false;

        for (var i = 0; i < references.Count; i++)
        {
            if (takes[i] == 0) continue;
            var left = sources[i].Count - takes[i];
            if (!_slots.TrySetSlot(player, references[i], left == 0 ? InventorySlot.Empty : sources[i] with { Count = left }))
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

    private static void AddWireTouch(List<WireTouch> touches, in InventorySlotReference reference, WireSlot wire)
    {
        if (wire.ContainerId == 0 && InventoryContainerMap.TryToWire(reference, out var c, out var s))
            AddWireTouch(touches, reference, c, s);
        else
            AddWireTouch(touches, reference, wire.ContainerId, wire.Slot);
    }

    private static void AddWireTouch(List<WireTouch> touches, in InventorySlotReference reference, byte containerId, byte slot)
    {
        foreach (var t in touches)
        {
            if (t.Reference == reference && t.ContainerId == containerId && t.Slot == slot)
                return;
        }

        touches.Add(new WireTouch(reference, containerId, slot));
    }
}
