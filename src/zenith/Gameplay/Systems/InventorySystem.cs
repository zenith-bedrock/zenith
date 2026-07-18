using System.Collections.Generic;
using Zenith.Gameplay.Runtime;
using Zenith.Packets;
using Zenith.Protocol;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.Systems;

/// <summary>
/// Aplica <see cref="InventoryStackIntent"/> no tick e responde ItemStackResponse (same-session).
/// Slots flat ≥ <see cref="InventoryContainerMap.ChestBase"/> → chest;
/// craft UI flats → <see cref="PlayerCraftUi"/>.
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

    public void Tick(GameClock clock) => Tick(clock, _players.Online);

    public void Tick(GameClock clock, IReadOnlyList<global::Zenith.Player.Player> online)
    {
        _ = clock;
        if (online.Count == 0) return;

        foreach (var player in online)
        {
            while (player.TryConsumeWindowIntent(out var window))
                ApplyWindow(player, window);
        }

        foreach (var player in online)
        {
            while (player.TryConsumeInventoryStack(out var intent))
            {
                if (player.IsDead) continue;
                Apply(player, intent);
            }
        }
    }

    private void ApplyWindow(global::Zenith.Player.Player player, in InventoryWindowIntent intent)
    {
        var inv = player.Session.Protocol.Inventory;
        switch (intent.Action)
        {
            case InventoryWindowIntent.Kind.OpenInventory:
                player.InventoryWindowOpen = true;
                inv.SendContainerOpen(
                    (int)MathF.Floor(player.PositionX),
                    (int)MathF.Floor(player.PositionY),
                    (int)MathF.Floor(player.PositionZ));
                inv.SendUiInventoryContent(player);
                break;

            case InventoryWindowIntent.Kind.OpenChest:
                _world.Chests.Ensure(intent.X, intent.Y, intent.Z);
                player.OpenChest = (intent.X, intent.Y, intent.Z);
                inv.SendChestOpen(intent.X, intent.Y, intent.Z);
                inv.SendChestContent(_world.Chests, intent.X, intent.Y, intent.Z);
                inv.SendInventoryContent(player.Inventory);
                player.Session.Context.Logger.Debug(
                    $"Chest open for {player.Username} @ {intent.X},{intent.Y},{intent.Z}");
                break;

            case InventoryWindowIntent.Kind.Close:
                if (intent.WindowId == InventoryContentPacket.WindowInventory)
                    player.InventoryWindowOpen = false;
                player.OpenChest = null;
                inv.SendContainerClose(intent.WindowId, intent.WindowType);
                break;
        }
    }

    private void Apply(global::Zenith.Player.Player player, in InventoryStackIntent intent)
    {
        var protocol = player.Session.Protocol.Inventory;
        var inventory = player.Inventory;

        if (!ValidateClientStackNetIds(protocol, intent.Actions))
        {
            protocol.SendItemStackResponseError(intent.RequestId);
            protocol.SendInventoryContent(inventory);
            protocol.SendUiInventoryContent(player);
            if (player.OpenChest is { } openMismatch)
                protocol.SendChestContent(_world.Chests, openMismatch.X, openMismatch.Y, openMismatch.Z);
            player.Session.Context.Logger.Debug(
                $"ISR rejected for {player.Username}: stack net id mismatch (request {intent.RequestId}).");
            return;
        }

        var invSnap = inventory.CaptureSnapshot();
        var craftSnap = player.CraftUi.CaptureSnapshot();
        InventorySlot[]? chestSnap = null;
        (int X, int Y, int Z)? chestPos = player.OpenChest;
        if (chestPos is { } pos)
            chestSnap = _world.Chests.CaptureSnapshot(pos.X, pos.Y, pos.Z);

        var wireTouches = new List<WireTouch>();
        var ok = true;

        foreach (var action in intent.Actions)
        {
            switch (action.Kind)
            {
                case InventoryStackActionKind.CraftRecipe:
                    // Materialize CreatedOutput so same-request Take/Place can move it;
                    // refuse if a prior result is still sitting untaken.
                    if (!GetSlot(player, InventoryContainerMap.CraftResultFlat).IsEmpty ||
                        !_recipes.TryCraftFromGrid(player.CraftUi, action.RecipeNetId, out var crafted, action.CraftTimes) ||
                        !TrySetSlot(player, InventoryContainerMap.CraftResultFlat, crafted))
                    {
                        ok = false;
                        break;
                    }

                    for (var g = 0; g < PlayerCraftUi.GridSize; g++)
                    {
                        var flat = InventoryContainerMap.CraftUiBase + g;
                        AddWireTouch(wireTouches, flat,
                            InventoryContainerMap.CraftingInput,
                            InventoryContainerMap.CraftGridWireSlot(g));
                    }

                    AddWireTouch(wireTouches, InventoryContainerMap.CraftResultFlat,
                        InventoryContainerMap.CreatedOutput,
                        InventoryContainerMap.CraftingResultWireSlot);
                    break;

                case InventoryStackActionKind.CraftCreative:
                    // Creative pick: full MaxStack into CreatedOutput; same-request Place/Take/Drop moves it.
                    if (player.GameMode != GameMode.Creative ||
                        !_creative.TryGet(action.CreativeNetId, out var creativeRid, out _) ||
                        !TrySetSlot(player, InventoryContainerMap.CraftResultFlat,
                            new InventorySlot(creativeRid, PlayerInventory.MaxStack)))
                    {
                        ok = false;
                        break;
                    }

                    AddWireTouch(wireTouches, InventoryContainerMap.CraftResultFlat,
                        InventoryContainerMap.CreatedOutput,
                        InventoryContainerMap.CraftingResultWireSlot);
                    break;

                case InventoryStackActionKind.Create:
                    // Idempotent ack after CraftRecipe/CraftCreative wrote CraftResultFlat.
                    if (GetSlot(player, InventoryContainerMap.CraftResultFlat).IsEmpty)
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
                    if (!TryDrop(player, action.From, count))
                    {
                        ok = false;
                        break;
                    }

                    AddWireTouch(wireTouches, action.From, action.FromWire);
                    break;
                }

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

        if (!ok)
        {
            inventory.RestoreSnapshot(invSnap);
            player.CraftUi.RestoreSnapshot(craftSnap);
            if (chestPos is { } cp && chestSnap is not null && chestSnap.Length == ChestStore.Size)
                _world.Chests.RestoreSnapshot(cp.X, cp.Y, cp.Z, chestSnap);
            protocol.SendItemStackResponseError(intent.RequestId);
            protocol.SendInventoryContent(inventory);
            protocol.SendUiInventoryContent(player);
            if (chestPos is { } open)
                protocol.SendChestContent(_world.Chests, open.X, open.Y, open.Z);
            return;
        }

        protocol.SendItemStackResponseOk(intent.RequestId, player, wireTouches);
        if (player.InventoryWindowOpen)
            protocol.SendUiInventoryContent(player);
        if (chestPos is { } openAfter)
            protocol.SendChestContent(_world.Chests, openAfter.X, openAfter.Y, openAfter.Z);
        _world.PersistInventory(player.Uuid, inventory);
        if (chestPos is { } openChest)
            _world.PersistChest(openChest.X, openChest.Y, openChest.Z);
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
                    if (!protocol.MatchesAdvertisedStackNetId(action.From, action.FromWire.StackNetworkId))
                        return false;
                    break;
            }
        }

        return true;
    }

    private InventorySlot GetSlot(global::Zenith.Player.Player player, int flat)
    {
        if (InventoryContainerMap.IsChestFlat(flat))
        {
            if (player.OpenChest is not { } pos) return InventorySlot.Empty;
            return _world.Chests.Get(pos.X, pos.Y, pos.Z, flat - InventoryContainerMap.ChestBase);
        }

        if (InventoryContainerMap.IsCraftGridFlat(flat))
            return player.CraftUi.GetGrid(flat - InventoryContainerMap.CraftUiBase);

        if (flat == InventoryContainerMap.CraftResultFlat)
            return player.CraftUi.Result;

        return player.Inventory.Get(flat);
    }

    private bool TrySetSlot(global::Zenith.Player.Player player, int flat, InventorySlot value)
    {
        if (InventoryContainerMap.IsChestFlat(flat))
        {
            if (player.OpenChest is not { } pos) return false;
            return _world.Chests.TrySet(pos.X, pos.Y, pos.Z, flat - InventoryContainerMap.ChestBase, value);
        }

        if (InventoryContainerMap.IsCraftGridFlat(flat))
            return player.CraftUi.TrySetGrid(flat - InventoryContainerMap.CraftUiBase, value);

        if (flat == InventoryContainerMap.CraftResultFlat)
            return player.CraftUi.TrySetResult(value);

        return player.Inventory.TrySet(flat, value.RuntimeId, value.IsEmpty ? 0 : value.Count);
    }

    private bool TryTransfer(global::Zenith.Player.Player player, int from, int to, int count)
    {
        if (from == to) return false;
        if (!IsValidFlat(player, from) || !IsValidFlat(player, to)) return false;
        if (count <= 0 || !PlayerInventory.IsValidStackCount(count)) return false;

        var src = GetSlot(player, from);
        if (src.IsEmpty || count > src.Count) return false;

        var dst = GetSlot(player, to);
        if (!dst.IsEmpty && dst.RuntimeId != src.RuntimeId) return false;

        var space = dst.IsEmpty ? PlayerInventory.MaxStack : PlayerInventory.MaxStack - dst.Count;
        if (count > space) return false;

        var newDstCount = (dst.IsEmpty ? 0 : dst.Count) + count;
        var newSrcCount = src.Count - count;

        if (!TrySetSlot(player, to, new InventorySlot(src.RuntimeId, newDstCount))) return false;
        if (!TrySetSlot(player, from, newSrcCount == 0 ? InventorySlot.Empty : src with { Count = newSrcCount }))
            return false;
        return true;
    }

    private bool TryDrop(global::Zenith.Player.Player player, int from, int count)
    {
        if (!IsValidFlat(player, from)) return false;
        if (count <= 0 || !PlayerInventory.IsValidStackCount(count)) return false;

        var src = GetSlot(player, from);
        if (src.IsEmpty || count > src.Count) return false;

        var left = src.Count - count;
        return TrySetSlot(player, from, left == 0 ? InventorySlot.Empty : src with { Count = left });
    }

    private bool TrySwap(global::Zenith.Player.Player player, int a, int b)
    {
        if (a == b) return false;
        if (!IsValidFlat(player, a) || !IsValidFlat(player, b)) return false;

        var sa = GetSlot(player, a);
        var sb = GetSlot(player, b);
        if (!TrySetSlot(player, a, sb)) return false;
        if (!TrySetSlot(player, b, sa)) return false;
        return true;
    }

    private static bool IsValidFlat(global::Zenith.Player.Player player, int flat)
    {
        if (InventoryContainerMap.IsChestFlat(flat))
            return player.OpenChest.HasValue;
        if (InventoryContainerMap.IsCraftUiFlat(flat))
            return true;
        return PlayerInventory.IsValidLocation(flat);
    }

    private static void AddWireTouch(List<WireTouch> touches, int flat, WireSlot wire)
    {
        if (wire.ContainerId == 0 && InventoryContainerMap.TryToWire(flat, out var c, out var s))
            AddWireTouch(touches, flat, c, s);
        else
            AddWireTouch(touches, flat, wire.ContainerId, wire.Slot);
    }

    private static void AddWireTouch(List<WireTouch> touches, int flat, byte containerId, byte slot)
    {
        foreach (var t in touches)
        {
            if (t.Flat == flat && t.ContainerId == containerId && t.Slot == slot)
                return;
        }

        touches.Add(new WireTouch(flat, containerId, slot));
    }
}
