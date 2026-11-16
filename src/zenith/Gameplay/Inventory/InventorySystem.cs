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

/// <summary>
/// Aplica <see cref="InventoryStackIntent"/> no tick e responde ItemStackResponse (same-session).
/// Slot references are resolved against the authoritative open-container session and player UI.
/// </summary>
sealed partial class InventorySystem : IGameSystem
{
    private readonly PlayerManager _players;

    private readonly World.World _world;

    private readonly RecipeRegistry _recipes;

    private readonly CreativeCatalog _creative;

    public InventorySystem(PlayerManager players, World.World world, RecipeRegistry recipes, CreativeCatalog creative)
    {
        _players = players;
        _world = world;
        _recipes = recipes;
        _creative = creative;
    }

    public void Tick(GameClock clock, IReadOnlyList<global::Zenith.Player.Player> online)
    {
        _ = clock;

        // Disconnect is produced by the network lifecycle, but ChestStore opener state remains
        // gameplay-owned. Drain even when the departing player has already left Online.
        while (_players.TryConsumeDisconnectedContainerCleanup(out var disconnected))
            ChestLidFanout.ReleaseOpener(online, _world, disconnected);

        // Same reasoning (ADR §104b Adendo): PlayerInventory has no lock, so building its save blob must
        // happen here, on the GameLoop thread that owns its mutation — not directly from the
        // network-close callback, which could otherwise race a concurrent Apply for this same
        // player. Persists whatever was last successfully applied; any intent still queued for a
        // now-departed player is simply never processed, not force-applied post-disconnect.
        while (_players.TryConsumeDisconnectedInventoryPersist(out var departed))
        {
            _world.PersistInventory(departed);
            _world.PersistPlayerData(departed);
        }

        if (online.Count == 0) return;

        foreach (var player in online)
        {
            if (player.IsInGame && !player.IsDead && player.Windows.RecoverUiToInventory(player.Inventory))
            {
                player.Client.Inventory.SendInventoryContent(player.Inventory);
                player.Client.Inventory.SendUiInventoryContent(player);
                _world.PersistInventory(player);
            }
            while (player.Windows.TryConsumeInventoryRequest(out var window, out var stack))
            {
                try
                {
                    // A departed player may still occur in this tick's online snapshot.
                    if (!player.IsInGame) continue;
                    if (window is { } lifecycle)
                    {
                        if (!player.IsDead || lifecycle.Action == InventoryWindowIntent.Kind.Close)
                            ApplyWindow(player, lifecycle, online);
                    }
                    else if (!player.IsDead && stack is { } transaction)
                        Apply(player, transaction, online);
                }
                finally { player.Windows.CompleteInventoryWindow(); }
            }
        }
    }

    private void ApplyWindow(
        global::Zenith.Player.Player player,
        in InventoryWindowIntent intent,
        IReadOnlyList<global::Zenith.Player.Player> online)
    {
        var inv = player.Client.Inventory;
        switch (intent.Action)
        {
            case InventoryWindowIntent.Kind.OpenInventory:
                // Bedrock may retransmit Interact(OpenInventory) while awaiting the first
                // ContainerOpen. Re-emitting ContainerOpen for an already-open player view can
                // crash the client (Dragonfly's handler_interact documents this behavior).
                // The open-container state is gameplay-owned, so coalesce it here rather than
                // making the network handler a second writer.
                var inventoryAlreadyOpen = player.Windows.InventoryWindowOpen;
                if (inventoryAlreadyOpen && player.Windows.OpenContainer?.Generation == intent.Generation)
                    return;

                if (player.Windows.OpenChest.HasValue)
                    ChestLidFanout.ReleaseOpener(online, _world, player);
                var inventorySession = player.Windows.OpenPlayerContainer(
                    (byte)InventoryContainerMap.WindowInventory,
                    InventoryContainerMap.WindowTypeInventory, intent.Generation);
                inv.BeginOpenContainerSession(inventorySession.Generation);
                if (!inventoryAlreadyOpen)
                    inv.SendContainerOpen(
                        (int)MathF.Floor(player.PositionX),
                        (int)MathF.Floor(player.PositionY),
                        (int)MathF.Floor(player.PositionZ));
                inv.SendUiInventoryContent(player);
                break;

            case InventoryWindowIntent.Kind.OpenChest:
                var view = ChestPairing.ViewFor(_world, intent.X, intent.Y, intent.Z);
                // A chest block whose storage cell does not exist yet gets one here.
                _world.Chests.Ensure(view.PrimaryX, view.PrimaryY, view.PrimaryZ);
                var partnerX = 0;
                var partnerY = 0;
                var partnerZ = 0;
                if (view.TryGetPartner(out partnerX, out partnerY, out partnerZ))
                    _world.Chests.Ensure(partnerX, partnerY, partnerZ);

                if (player.Windows.OpenChest is { } previous &&
                    !previous.Contains(intent.X, intent.Y, intent.Z))
                    ChestLidFanout.ReleaseOpener(online, _world, player);

                var chestSession = player.Windows.OpenChestContainer(
                    (byte)InventoryContainerMap.WindowChest,
                    InventoryContainerMap.WindowTypeChest,
                    view, intent.Generation);
                inv.BeginOpenContainerSession(chestSession.Generation);

                var primaryFirst = _world.Chests.TryAddOpener(
                    view.PrimaryX, view.PrimaryY, view.PrimaryZ, player.RuntimeId);
                var partnerFirst = false;
                if (view.TryGetPartner(out partnerX, out partnerY, out partnerZ))
                    partnerFirst = _world.Chests.TryAddOpener(partnerX, partnerY, partnerZ, player.RuntimeId);

                if (primaryFirst)
                    ChestLidFanout.Open(online, player.Client, view.PrimaryX, view.PrimaryY, view.PrimaryZ);
                if (partnerFirst)
                    ChestLidFanout.Open(online, player.Client, partnerX, partnerY, partnerZ);

                inv.SendChestOpen(intent.X, intent.Y, intent.Z);
                inv.SendChestContent(_world.Chests, view);
                inv.SendInventoryContent(player.Inventory);
                player.Level.Logger?.Debug(
                    $"Chest open for {player.Username} @ {intent.X},{intent.Y},{intent.Z} slots={view.SlotCount}");
                break;

            case InventoryWindowIntent.Kind.OpenCraftingTable:
                // Same re-transmit-crash risk OpenInventory's dedup guard above exists for.
                var tableAlreadyOpen = player.Windows.IsAtCraftingTable;
                if (tableAlreadyOpen && player.Windows.OpenContainer?.Generation == intent.Generation)
                    return;

                if (player.Windows.OpenChest.HasValue)
                    ChestLidFanout.ReleaseOpener(online, _world, player);

                var tableSession = player.Windows.OpenCraftingTableContainer(
                    (byte)InventoryContainerMap.WindowCraftingTable,
                    InventoryContainerMap.WindowTypeWorkbench, intent.Generation);
                inv.BeginOpenContainerSession(tableSession.Generation);
                if (!tableAlreadyOpen)
                    inv.SendCraftingTableOpen(intent.X, intent.Y, intent.Z);
                inv.SendUiInventoryContent(player);
                break;

            case InventoryWindowIntent.Kind.Close:
                // Phase XXIII-B fix: real Bedrock clients (documented in PocketMine's
                // InventoryManager::onClientRemoveWindow, "since 1.21.100 and probably earlier")
                // sometimes send WindowId 0xFF ("none", signed -1) instead of echoing the actual
                // window id — e.g. if another UI was already open when the close fired. Zenith's
                // exact-match check silently rejected that as "stale" and never cleared
                // player.Windows.OpenContainer, permanently locking the player out of every future
                // container open (found via real-client testing). Treat 0xFF as "close whatever is
                // currently open", the same workaround PocketMine uses.
                if (player.Windows.OpenContainer is not { } active)
                {
                    player.Level.Logger?.Debug(
                        $"Ignored stale container close from {player.Username}: {intent.WindowId}/{intent.WindowType}.");
                    return;
                }
                // Phase XXVI fix: WindowType ("ContainerType" on the wire) is NOT validated here —
                // confirmed against both PocketMine (onClientRemoveWindow only ever reasons about
                // windowId, the packet's ContainerType field is never even read) and Dragonfly
                // (ContainerCloseHandler switches purely on WindowID; ContainerType is only echoed
                // back in the ack, never compared). A real client was observed sending a stale/
                // unrelated ContainerType (247) on an otherwise-correct close (WindowId matched the
                // open chest exactly) — the previous WindowType comparison rejected that as
                // "mismatched", leaving the session open server-side and producing a rapid-fire
                // reopen loop client-side as it kept retrying. gophertunnel's own doc comment on this
                // field confirms it exists to validate a *server-initiated* close on the client side,
                // not something the server should validate on an incoming client-initiated close.
                if (active.Generation != intent.Generation ||
                    (intent.WindowId != InventoryWindowIntent.UnknownWindowId && active.WindowId != intent.WindowId))
                {
                    player.Level.Logger?.Debug(
                        $"Ignored mismatched container close from {player.Username}: {intent.WindowId}/{intent.WindowType} (active {active.WindowId}/{active.WindowType}).");
                    return;
                }

                if (active.Target == OpenContainerSession.TargetKind.Chest)
                    ChestLidFanout.ReleaseOpener(online, _world, player);
                else
                    _ = player.Windows.TryCloseContainer(active.WindowId, active.WindowType, out _);
                inv.EndOpenContainerSession();
                inv.SendContainerClose(active.WindowId, active.WindowType);
                break;
        }
    }

    private void Apply(
        global::Zenith.Player.Player player,
        in InventoryStackIntent intent,
        IReadOnlyList<global::Zenith.Player.Player> online)
    {
        var protocol = player.Client.Inventory;
        var inventory = player.Inventory;

        if (!intent.UncorrelatedLegacyDrop && !player.Windows.TryClaimInventoryRequest(intent.RequestId))
        {
            protocol.SendItemStackResponseError(intent.RequestId);
            protocol.SendInventoryContent(inventory);
            protocol.SendUiInventoryContent(player);
            if (player.Windows.OpenChest is { } openReplay)
                protocol.SendChestContent(_world.Chests, openReplay);
            player.Level.Logger?.Debug(
                $"ISR rejected for {player.Username}: duplicate request {intent.RequestId}.");
            return;
        }

        if ((intent.RequiresOpenContainer || intent.ExpectedOpenContainerGeneration != 0) &&
            (player.Windows.OpenContainer is not { } openContainer ||
             (intent.RequiredOpenContainerTarget is { } requiredTarget && openContainer.Target != requiredTarget) ||
             (intent.ExpectedOpenContainerGeneration != 0 &&
              openContainer.Generation != intent.ExpectedOpenContainerGeneration)))
        {
            if (!intent.UncorrelatedLegacyDrop) protocol.SendItemStackResponseError(intent.RequestId);
            protocol.SendInventoryContent(inventory);
            protocol.SendUiInventoryContent(player);
            if (player.Windows.OpenChest is { } openStale)
                protocol.SendChestContent(_world.Chests, openStale);
            player.Level.Logger?.Debug(
                $"ISR rejected for {player.Username}: stale open-container session (request {intent.RequestId}).");
            return;
        }

        if (!ValidateClientStackNetIds(protocol, intent.Actions))
        {
            if (!intent.UncorrelatedLegacyDrop) protocol.SendItemStackResponseError(intent.RequestId);
            protocol.SendInventoryContent(inventory);
            protocol.SendUiInventoryContent(player);
            if (player.Windows.OpenChest is { } openMismatch)
                protocol.SendChestContent(_world.Chests, openMismatch);
            player.Level.Logger?.Debug(
                $"ISR rejected for {player.Username}: stack net id mismatch (request {intent.RequestId}).");
            return;
        }

        var invSnap = inventory.CaptureSnapshot();
        var armorSnap = inventory.SnapshotArmor();
        var craftSnap = player.Windows.CraftUi.CaptureFullSnapshot();
        var tableCraftSnap = player.Windows.TableCraftUi.CaptureSnapshot();
        InventorySlot[]? chestSnap = null;
        OpenChestView? chestView = player.Windows.OpenChest;
        if (chestView is { } cv)
            chestSnap = _world.Chests.CaptureOpenSnapshot(cv);

        var wireTouches = new List<WireTouch>();
        var pendingDrops = new List<FloorDropFanout.DepositRequest>();
        var ok = true;

        foreach (var action in intent.Actions)
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
                        ok = false;
                        break;
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
                    break;
                }

                case InventoryStackActionKind.CraftAuto:
                    if (!TryAutoCraft(player, action, wireTouches))
                        ok = false;
                    break;

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
                        ok = false;
                        break;
                    }

                    AddWireTouch(wireTouches, InventorySlotReference.CraftResult,
                        InventoryContainerMap.CreatedOutput,
                        InventoryContainerMap.CraftingResultWireSlot);
                    break;

                case InventoryStackActionKind.Create:
                    // Select one indexed result into the shared CreatedOutput slot.
                    if (!player.Windows.CraftUi.TrySelectOutput(action.ResultIndex))
                    {
                        ok = false;
                        break;
                    }

                    AddWireTouch(wireTouches, InventorySlotReference.CraftResult,
                        InventoryContainerMap.CreatedOutput, InventoryContainerMap.CraftingResultWireSlot);
                    break;

                case InventoryStackActionKind.NoOp:
                    break;

                case InventoryStackActionKind.Swap:
                    if (!TrySwap(player, action.From, action.To))
                    {
                        ok = false;
                        break;
                    }

                    AddWireTouch(wireTouches, action.From, action.FromWire);
                    AddWireTouch(wireTouches, action.To, action.ToWire);
                    break;

                case InventoryStackActionKind.Drop:
                {
                    if (action.ExpectedSourceStack is { } original && GetSlot(player, action.From) != original)
                    {
                        ok = false;
                        break;
                    }
                    var count = action.Count;
                    if (count == 0)
                        count = GetSlot(player, action.From).Count;
                    if (!TryRemoveForDrop(player, action.From, count, pendingDrops))
                    {
                        ok = false;
                        break;
                    }

                    AddWireTouch(wireTouches, action.From, action.FromWire);
                    break;
                }

                case InventoryStackActionKind.Destroy:
                    // The Bedrock Destroy request is the Creative UI's delete gesture. It is not
                    // a drop and cannot be used to delete survival state.
                    if (player.GameMode != GameMode.Creative ||
                        !TryDestroy(player, action.From, action.Count))
                    {
                        ok = false;
                        break;
                    }

                    AddWireTouch(wireTouches, action.From, action.FromWire);
                    break;

                case InventoryStackActionKind.Mine:
                    // Bedrock's MineBlock ISR is its durability reconciliation hook. Zenith has
                    // no durability component yet, so validate the advertised held stack and
                    // echo it through ItemStackResponse without inventing a mutation here.
                    if (!IsValidReference(player, action.From))
                    {
                        ok = false;
                        break;
                    }

                    AddWireTouch(wireTouches, action.From, action.FromWire);
                    break;

                default:
                {
                    var count = action.Count;
                    if (count == 0)
                        count = GetSlot(player, action.From).Count;
                    if (!TryTransfer(player, action.From, action.To, count))
                    {
                        ok = false;
                        break;
                    }

                    AddWireTouch(wireTouches, action.From, action.FromWire);
                    AddWireTouch(wireTouches, action.To, action.ToWire);
                    break;
                }
            }

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
            inventory.RestoreSnapshot(invSnap);
            inventory.RestoreArmorSnapshot(armorSnap);
            player.Windows.CraftUi.RestoreFullSnapshot(craftSnap);
            player.Windows.TableCraftUi.RestoreSnapshot(tableCraftSnap);
            if (chestView is { } cvFail && chestSnap is not null)
                _world.Chests.RestoreOpenSnapshot(cvFail, chestSnap);
            if (!intent.UncorrelatedLegacyDrop) protocol.SendItemStackResponseError(intent.RequestId);
            protocol.SendInventoryContent(inventory);
            protocol.SendUiInventoryContent(player);
            if (chestView is { } open)
                protocol.SendChestContent(_world.Chests, open);
            return;
        }

        if (!intent.UncorrelatedLegacyDrop) protocol.SendItemStackResponseOk(intent.RequestId, player, wireTouches);
        if (player.Windows.InventoryWindowOpen)
            protocol.SendUiInventoryContent(player);
        if (chestView is { } openAfter)
            protocol.SendChestContent(_world.Chests, openAfter);
        _world.PersistInventory(player);

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
            _world.PersistArmor(player);
            ArmorFanout.Broadcast(player, online);
        }

        if (offhandTouched)
        {
            protocol.SendOffhandContent(inventory);
            _world.PersistOffhand(player);
            ArmorFanout.BroadcastOffhand(player, online);
        }

        if (chestView is { } openChest)
        {
            _world.MarkBlockEntityDirty(openChest.PrimaryX, openChest.PrimaryZ);
            if (openChest.TryGetPartner(out var ppx, out var ppy, out var ppz))
                _world.MarkBlockEntityDirty(ppx, ppz);
        }
    }
}
