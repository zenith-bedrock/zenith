using System.Collections.Generic;
using Zenith.Gameplay.Runtime;
using Zenith.Protocol;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.Systems;

/// <summary>
/// Aplica <see cref="InventoryStackIntent"/> no tick e responde ItemStackResponse (same-session).
/// Slot references are resolved against the authoritative open-container session and player UI.
/// </summary>
sealed class InventorySystem : IGameSystem
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

        if (online.Count == 0) return;

        foreach (var player in online)
        {
            // A disconnect can race with the GameLoop's once-per-tick online snapshot. Do not
            // let a window request accepted before transport teardown create a new authoritative
            // container session after that player has left gameplay.
            if (!player.IsInGame)
            {
                while (player.TryConsumeWindowIntent(out _)) { }
                continue;
            }

            while (player.TryConsumeWindowIntent(out var window))
                ApplyWindow(player, window, online);
        }

        foreach (var player in online)
        {
            while (player.TryConsumeInventoryStack(out var intent))
            {
                // See the window pass above. A player can have been in the tick snapshot when
                // the network lifecycle removes it; queued client work is cancelled, never
                // committed after that boundary.
                if (!player.IsInGame || player.IsDead) continue;
                Apply(player, intent, online);
            }
        }
    }

    private void ApplyWindow(
        global::Zenith.Player.Player player,
        in InventoryWindowIntent intent,
        IReadOnlyList<global::Zenith.Player.Player> online)
    {
        var inv = player.Session.Protocol.Inventory;
        switch (intent.Action)
        {
            case InventoryWindowIntent.Kind.OpenInventory:
                // Bedrock may retransmit Interact(OpenInventory) while awaiting the first
                // ContainerOpen. Re-emitting ContainerOpen for an already-open player view can
                // crash the client (Dragonfly's handler_interact documents this behavior).
                // The open-container state is gameplay-owned, so coalesce it here rather than
                // making the network handler a second writer.
                if (player.InventoryWindowOpen)
                    return;

                if (player.OpenChest.HasValue)
                    ChestLidFanout.ReleaseOpener(online, _world, player);
                var inventorySession = player.OpenPlayerContainer(
                    (byte)InventoryContainerMap.WindowInventory,
                    InventoryContainerMap.WindowTypeInventory);
                inv.BeginOpenContainerSession(inventorySession.Generation);
                inv.SendContainerOpen(
                    (int)MathF.Floor(player.PositionX),
                    (int)MathF.Floor(player.PositionY),
                    (int)MathF.Floor(player.PositionZ));
                inv.SendUiInventoryContent(player);
                break;

            case InventoryWindowIntent.Kind.OpenChest:
                if (player.OpenChest is { } previous &&
                    !previous.Contains(intent.X, intent.Y, intent.Z))
                    ChestLidFanout.ReleaseOpener(online, _world, player);

                var view = ChestPairing.ViewFor(_world, intent.X, intent.Y, intent.Z);
                _world.Chests.Ensure(view.PrimaryX, view.PrimaryY, view.PrimaryZ);
                if (view.TryGetPartner(out var partnerX, out var partnerY, out var partnerZ))
                    _world.Chests.Ensure(partnerX, partnerY, partnerZ);

                var chestSession = player.OpenChestContainer(
                    (byte)InventoryContainerMap.WindowChest,
                    InventoryContainerMap.WindowTypeChest,
                    view);
                inv.BeginOpenContainerSession(chestSession.Generation);

                var primaryFirst = _world.Chests.TryAddOpener(
                    view.PrimaryX, view.PrimaryY, view.PrimaryZ, player.RuntimeId);
                var partnerFirst = false;
                if (view.TryGetPartner(out partnerX, out partnerY, out partnerZ))
                    partnerFirst = _world.Chests.TryAddOpener(partnerX, partnerY, partnerZ, player.RuntimeId);

                if (primaryFirst)
                    ChestLidFanout.Open(online, player.Session, view.PrimaryX, view.PrimaryY, view.PrimaryZ);
                if (partnerFirst)
                    ChestLidFanout.Open(online, player.Session, partnerX, partnerY, partnerZ);

                inv.SendChestOpen(intent.X, intent.Y, intent.Z);
                inv.SendChestContent(_world.Chests, view);
                inv.SendInventoryContent(player.Inventory);
                player.Session.Context.Logger.Debug(
                    $"Chest open for {player.Username} @ {intent.X},{intent.Y},{intent.Z} slots={view.SlotCount}");
                break;

            case InventoryWindowIntent.Kind.Close:
                // Phase XXIII-B fix: real Bedrock clients (documented in PocketMine's
                // InventoryManager::onClientRemoveWindow, "since 1.21.100 and probably earlier")
                // sometimes send WindowId 0xFF ("none", signed -1) instead of echoing the actual
                // window id — e.g. if another UI was already open when the close fired. Zenith's
                // exact-match check silently rejected that as "stale" and never cleared
                // player.OpenContainer, permanently locking the player out of every future
                // container open (found via real-client testing). Treat 0xFF as "close whatever is
                // currently open", the same workaround PocketMine uses.
                if (player.OpenContainer is not { } active)
                {
                    player.Session.Context.Logger.Debug(
                        $"Ignored stale container close from {player.Username}: {intent.WindowId}/{intent.WindowType}.");
                    return;
                }
                if (intent.WindowId != InventoryWindowIntent.UnknownWindowId &&
                    (active.WindowId != intent.WindowId || active.WindowType != intent.WindowType))
                {
                    player.Session.Context.Logger.Debug(
                        $"Ignored mismatched container close from {player.Username}: {intent.WindowId}/{intent.WindowType} (active {active.WindowId}/{active.WindowType}).");
                    return;
                }

                if (active.Target == OpenContainerSession.TargetKind.Chest)
                    ChestLidFanout.ReleaseOpener(online, _world, player);
                else
                    _ = player.TryCloseContainer(active.WindowId, active.WindowType, out _);
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
        var protocol = player.Session.Protocol.Inventory;
        var inventory = player.Inventory;

        if (!player.TryClaimInventoryRequest(intent.RequestId))
        {
            protocol.SendItemStackResponseError(intent.RequestId);
            protocol.SendInventoryContent(inventory);
            protocol.SendUiInventoryContent(player);
            if (player.OpenChest is { } openReplay)
                protocol.SendChestContent(_world.Chests, openReplay);
            player.Session.Context.Logger.Debug(
                $"ISR rejected for {player.Username}: duplicate request {intent.RequestId}.");
            return;
        }

        if ((intent.RequiresOpenContainer || intent.ExpectedOpenContainerGeneration != 0) &&
            (player.OpenContainer is not { } openContainer ||
             (intent.RequiredOpenContainerTarget is { } requiredTarget && openContainer.Target != requiredTarget) ||
             (intent.ExpectedOpenContainerGeneration != 0 &&
              openContainer.Generation != intent.ExpectedOpenContainerGeneration)))
        {
            protocol.SendItemStackResponseError(intent.RequestId);
            protocol.SendInventoryContent(inventory);
            protocol.SendUiInventoryContent(player);
            if (player.OpenChest is { } openStale)
                protocol.SendChestContent(_world.Chests, openStale);
            player.Session.Context.Logger.Debug(
                $"ISR rejected for {player.Username}: stale open-container session (request {intent.RequestId}).");
            return;
        }

        if (!ValidateClientStackNetIds(protocol, intent.Actions))
        {
            protocol.SendItemStackResponseError(intent.RequestId);
            protocol.SendInventoryContent(inventory);
            protocol.SendUiInventoryContent(player);
            if (player.OpenChest is { } openMismatch)
                protocol.SendChestContent(_world.Chests, openMismatch);
            player.Session.Context.Logger.Debug(
                $"ISR rejected for {player.Username}: stack net id mismatch (request {intent.RequestId}).");
            return;
        }

        var invSnap = inventory.CaptureSnapshot();
        var armorSnap = inventory.SnapshotArmor();
        var craftSnap = player.CraftUi.CaptureSnapshot();
        InventorySlot[]? chestSnap = null;
        OpenChestView? chestView = player.OpenChest;
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
                    // Materialize CreatedOutput so same-request Take/Place can move it;
                    // refuse if a prior result is still sitting untaken.
                    if (!GetSlot(player, InventorySlotReference.CraftResult).IsEmpty ||
                        !_recipes.TryCraftFromGrid(player.CraftUi, action.RecipeNetId, out var crafted, action.CraftTimes) ||
                        !TrySetSlot(player, InventorySlotReference.CraftResult, crafted))
                    {
                        ok = false;
                        break;
                    }

                    for (var g = 0; g < PlayerCraftUi.GridSize; g++)
                    {
                        AddWireTouch(wireTouches, InventorySlotReference.CraftGrid(g),
                            InventoryContainerMap.CraftingInput,
                            InventoryContainerMap.CraftGridWireSlot(g));
                    }

                    AddWireTouch(wireTouches, InventorySlotReference.CraftResult,
                        InventoryContainerMap.CreatedOutput,
                        InventoryContainerMap.CraftingResultWireSlot);
                    break;

                case InventoryStackActionKind.CraftCreative:
                    // Creative pick: full MaxStack into CreatedOutput; same-request Place/Take/Drop moves it.
                    // CreatedOutput is authoritative pending state: never overwrite a prior result.
                    if (!GetSlot(player, InventorySlotReference.CraftResult).IsEmpty ||
                        player.GameMode != GameMode.Creative ||
                        !_creative.TryGet(action.CreativeNetId, out var creativeId, out _) ||
                        !TrySetSlot(player, InventorySlotReference.CraftResult,
                            new InventorySlot(creativeId, PlayerInventory.MaxStack)))
                    {
                        ok = false;
                        break;
                    }

                    AddWireTouch(wireTouches, InventorySlotReference.CraftResult,
                        InventoryContainerMap.CreatedOutput,
                        InventoryContainerMap.CraftingResultWireSlot);
                    break;

                case InventoryStackActionKind.Create:
                    // Idempotent ack after CraftRecipe/CraftCreative wrote the craft result.
                    if (GetSlot(player, InventorySlotReference.CraftResult).IsEmpty)
                    {
                        ok = false;
                        break;
                    }

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
                FloorDropFanout.PlayerThrowPickupDelay);
        }

        if (!ok)
        {
            inventory.RestoreSnapshot(invSnap);
            inventory.RestoreArmorSnapshot(armorSnap);
            player.CraftUi.RestoreSnapshot(craftSnap);
            if (chestView is { } cvFail && chestSnap is not null)
                _world.Chests.RestoreOpenSnapshot(cvFail, chestSnap);
            protocol.SendItemStackResponseError(intent.RequestId);
            protocol.SendInventoryContent(inventory);
            protocol.SendUiInventoryContent(player);
            if (chestView is { } open)
                protocol.SendChestContent(_world.Chests, open);
            return;
        }

        protocol.SendItemStackResponseOk(intent.RequestId, player, wireTouches);
        if (player.InventoryWindowOpen)
            protocol.SendUiInventoryContent(player);
        if (chestView is { } openAfter)
            protocol.SendChestContent(_world.Chests, openAfter);
        _world.PersistInventory(player);

        var armorTouched = false;
        foreach (var touch in wireTouches)
        {
            if (touch.Reference.Area != InventorySlotArea.Armor) continue;
            armorTouched = true;
            break;
        }

        if (armorTouched)
        {
            protocol.SendArmorContent(inventory);
            _world.PersistArmor(player);
            ArmorFanout.Broadcast(player, online);
        }

        if (chestView is { } openChest)
        {
            _world.PersistChest(openChest.PrimaryX, openChest.PrimaryY, openChest.PrimaryZ);
            if (openChest.TryGetPartner(out var ppx, out var ppy, out var ppz))
                _world.PersistChest(ppx, ppy, ppz);
        }
    }

    /// <summary>
    /// Soft match before mutate (§54). Positive client ids must equal last DescribeForWire;
    /// ≤0 skipped (air / deferred prediction).
    /// </summary>
    private static bool ValidateClientStackNetIds(InventoryProtocol protocol, InventoryStackAction[] actions)
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

    private InventorySlot GetSlot(global::Zenith.Player.Player player, in InventorySlotReference reference) =>
        InventorySlotResolver.GetSlot(player, _world, reference);

    private bool TrySetSlot(global::Zenith.Player.Player player, in InventorySlotReference reference, InventorySlot value) =>
        InventorySlotResolver.TrySetSlot(player, _world, reference, value);

    private bool TryTransfer(global::Zenith.Player.Player player, in InventorySlotReference from, in InventorySlotReference to, int count)
    {
        if (from == to) return false;
        if (!IsValidReference(player, from) || !IsValidReference(player, to)) return false;
        if (count <= 0 || !PlayerInventory.IsValidStackCount(count)) return false;

        var src = GetSlot(player, from);
        if (src.IsEmpty || count > src.Count) return false;
        if (!CanPlaceInArmorSlot(player, to, src)) return false;

        var dst = GetSlot(player, to);
        if (!dst.IsEmpty && dst.Id != src.Id) return false;

        var space = dst.IsEmpty ? PlayerInventory.MaxStack : PlayerInventory.MaxStack - dst.Count;
        if (count > space) return false;

        var newDstCount = (dst.IsEmpty ? 0 : dst.Count) + count;
        var newSrcCount = src.Count - count;

        if (!TrySetSlot(player, to, new InventorySlot(src.Id, newDstCount))) return false;
        if (!TrySetSlot(player, from, newSrcCount == 0 ? InventorySlot.Empty : src with { Count = newSrcCount }))
            return false;
        return true;
    }

    private bool TryRemoveForDrop(
        global::Zenith.Player.Player player,
        in InventorySlotReference from,
        int count,
        List<FloorDropFanout.DepositRequest> pendingDrops)
    {
        if (!IsValidReference(player, from)) return false;
        if (count <= 0 || !PlayerInventory.IsValidStackCount(count)) return false;

        var src = GetSlot(player, from);
        if (src.IsEmpty || count > src.Count) return false;

        var left = src.Count - count;
        if (!TrySetSlot(player, from, left == 0 ? InventorySlot.Empty : src with { Count = left }))
            return false;

        pendingDrops.Add(new FloorDropFanout.DepositRequest(src.Id, count));
        return true;
    }

    private bool TryDestroy(
        global::Zenith.Player.Player player,
        in InventorySlotReference from,
        int count)
    {
        if (!IsValidReference(player, from)) return false;
        if (count <= 0 || !PlayerInventory.IsValidStackCount(count)) return false;

        var src = GetSlot(player, from);
        if (src.IsEmpty || count > src.Count) return false;

        var left = src.Count - count;
        return TrySetSlot(player, from, left == 0 ? InventorySlot.Empty : src with { Count = left });
    }

    private bool TrySwap(global::Zenith.Player.Player player, in InventorySlotReference a, in InventorySlotReference b)
    {
        if (a == b) return false;
        if (!IsValidReference(player, a) || !IsValidReference(player, b)) return false;

        var sa = GetSlot(player, a);
        var sb = GetSlot(player, b);
        if (!CanPlaceInArmorSlot(player, a, sb) || !CanPlaceInArmorSlot(player, b, sa)) return false;
        if (!TrySetSlot(player, a, sb)) return false;
        if (!TrySetSlot(player, b, sa)) return false;
        return true;
    }

    /// <summary>
    /// Server-authoritative armor slot-type guard (Phase XI.2): a piece can only land in the armor
    /// slot matching its equipment kind. Every other area accepts any item, unchanged.
    /// </summary>
    private static bool CanPlaceInArmorSlot(global::Zenith.Player.Player player, in InventorySlotReference reference, InventorySlot value)
    {
        if (reference.Area != InventorySlotArea.Armor) return true;
        if (value.IsEmpty) return true;
        return ArmorItems.TryGet(player.Session.Context.ItemPalette, value.Id, out var slot, out _) &&
               slot == reference.Index;
    }

    private bool IsValidReference(global::Zenith.Player.Player player, in InventorySlotReference reference)
    {
        return reference.Area switch
        {
            InventorySlotArea.OpenContainer => player.OpenContainer is
                { Target: OpenContainerSession.TargetKind.Chest, Chest: { } view } &&
                reference.Index >= 0 && reference.Index < view.SlotCount && IsOpenChestAccessible(player, view),
            InventorySlotArea.CraftGrid => reference.Index >= 0 && reference.Index < PlayerCraftUi.GridSize,
            InventorySlotArea.CraftResult => reference.Index == 0,
            InventorySlotArea.Cursor => reference.Index == 0,
            InventorySlotArea.PlayerInventory => PlayerInventory.IsValidInventorySlot(reference.Index),
            InventorySlotArea.Armor => PlayerInventory.IsValidArmorSlot(reference.Index),
            _ => false
        };
    }

    private bool IsOpenChestAccessible(global::Zenith.Player.Player player, in OpenChestView view)
    {
        if (!Blocks.IsChest(_world.GetBlock(view.PrimaryX, view.PrimaryY, view.PrimaryZ)))
            return false;

        var primaryInReach = BlockEditSystem.IsWithinReach(player, view.PrimaryX, view.PrimaryY, view.PrimaryZ);
        if (!view.TryGetPartner(out var partnerX, out var partnerY, out var partnerZ))
            return primaryInReach;

        return Blocks.IsChest(_world.GetBlock(partnerX, partnerY, partnerZ)) &&
               (primaryInReach || BlockEditSystem.IsWithinReach(player, partnerX, partnerY, partnerZ));
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
