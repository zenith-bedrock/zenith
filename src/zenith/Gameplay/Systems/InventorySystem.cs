using System.Collections.Generic;
using Zenith.Gameplay.Runtime;
using Zenith.Network.Protocol;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay.Systems;

/// <summary>
/// Aplica <see cref="InventoryStackIntent"/> no tick e responde ItemStackResponse (same-session).
/// Slots flat ≥ <see cref="InventoryContainerMap.ChestBase"/> → <see cref="ChestStore"/> do OpenChest.
/// </summary>
sealed class InventorySystem : IGameSystem
{
    private readonly PlayerManager _players;
    private readonly World.World _world;
    private readonly RecipeRegistry _recipes;

    public InventorySystem(PlayerManager players, World.World world, RecipeRegistry recipes)
    {
        _players = players;
        _world = world;
        _recipes = recipes;
    }

    public void Tick(GameClock clock)
    {
        _ = clock;
        if (_players.Count == 0) return;

        foreach (var player in _players.Online)
        {
            while (player.TryConsumeInventoryStack(out var intent))
                Apply(player, intent);
        }
    }

    private void Apply(global::Zenith.Player.Player player, in InventoryStackIntent intent)
    {
        var protocol = player.Session.Protocol.Inventory;

        if (intent.CraftRecipeNetId is { } recipeId)
        {
            if (!_recipes.TryCraft(player.Inventory, recipeId))
            {
                protocol.SendItemStackResponseError(intent.RequestId);
                protocol.SendInventoryContent(player.Inventory);
                return;
            }

            protocol.SendItemStackResponseOk(intent.RequestId, player, []);
            protocol.SendInventoryContent(player.Inventory);
            return;
        }

        var inventory = player.Inventory;
        var invSnap = inventory.CaptureSnapshot();
        InventorySlot[]? chestSnap = null;
        (int X, int Y, int Z)? chestPos = player.OpenChest;
        if (chestPos is { } pos)
            chestSnap = _world.Chests.CaptureSnapshot(pos.X, pos.Y, pos.Z);

        var touched = new List<int>();
        var ok = true;

        foreach (var action in intent.Actions)
        {
            if (action.Kind == InventoryStackActionKind.Swap)
            {
                if (!TrySwap(player, action.From, action.To))
                {
                    ok = false;
                    break;
                }

                AddTouched(touched, action.From);
                AddTouched(touched, action.To);
            }
            else
            {
                var count = action.Count;
                if (count == 0)
                    count = GetSlot(player, action.From).Count;
                if (!TryTransfer(player, action.From, action.To, count))
                {
                    ok = false;
                    break;
                }

                AddTouched(touched, action.From);
                AddTouched(touched, action.To);
            }
        }

        if (!ok)
        {
            inventory.RestoreSnapshot(invSnap);
            if (chestPos is { } cp && chestSnap is not null && chestSnap.Length == ChestStore.Size)
                _world.Chests.RestoreSnapshot(cp.X, cp.Y, cp.Z, chestSnap);
            protocol.SendItemStackResponseError(intent.RequestId);
            protocol.SendInventoryContent(inventory);
            if (chestPos is { } open)
                protocol.SendChestContent(_world.Chests, open.X, open.Y, open.Z);
            return;
        }

        protocol.SendItemStackResponseOk(intent.RequestId, player, touched);
    }

    private InventorySlot GetSlot(global::Zenith.Player.Player player, int flat)
    {
        if (InventoryContainerMap.IsChestFlat(flat))
        {
            if (player.OpenChest is not { } pos) return InventorySlot.Empty;
            return _world.Chests.Get(pos.X, pos.Y, pos.Z, flat - InventoryContainerMap.ChestBase);
        }

        return player.Inventory.Get(flat);
    }

    private bool TrySetSlot(global::Zenith.Player.Player player, int flat, InventorySlot value)
    {
        if (InventoryContainerMap.IsChestFlat(flat))
        {
            if (player.OpenChest is not { } pos) return false;
            return _world.Chests.TrySet(pos.X, pos.Y, pos.Z, flat - InventoryContainerMap.ChestBase, value);
        }

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
        return PlayerInventory.IsValidLocation(flat);
    }

    private static void AddTouched(List<int> touched, int flat)
    {
        if (!touched.Contains(flat))
            touched.Add(flat);
    }
}
