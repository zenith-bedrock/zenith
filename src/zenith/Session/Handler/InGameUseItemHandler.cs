using System.Collections.Generic;
using Zenith.Gameplay;
using Zenith.Raknet.Stream;
using Zenith.Packets;
using Zenith.Protocol;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Session.Handler;

/// <summary>Partial: see <see cref="InGameSessionHandler"/>.</summary>
partial class InGameSessionHandler
{
    private static void HandleUseItemInteraction(
        NetworkSession session,
        Player.Player player,
        in AuthItemInteraction use)
    {
        var face = use.Face is >= 0 and <= 255 ? (byte)use.Face : (byte)0;
        HandleUseItem(
            session,
            player,
            use.ActionType,
            use.BlockX,
            use.BlockY,
            use.BlockZ,
            face,
            use.HotbarSlot);
    }

    private static void HandleUseItem(
        NetworkSession session,
        Player.Player player,
        int useActionType,
        int blockX,
        int blockY,
        int blockZ,
        byte blockFace,
        int hotbarSlot)
    {
        if (!PlayerInventory.IsValidHotbarSlot(hotbarSlot))
        {
            session.Context.Logger.Debug($"Rejected UseItem: hotbar {hotbarSlot}");
            return;
        }

        player.SelectedHotbarSlot = hotbarSlot;

        if (useActionType == InventoryTransactionPacket.UseDestroyBlock)
        {
            session.Context.Logger.Debug(
                $"UseItem destroy from {player.Username} @ {blockX},{blockY},{blockZ}");
            TrySubmitBreak(player, blockX, blockY, blockZ);
            return;
        }

        if (useActionType is InventoryTransactionPacket.UseClickAir
            or InventoryTransactionPacket.UseAsAttack)
        {
            // Air punch / attack-style use — peer arm swing (§53). MissedSwing AuthInput also covers this.
            PlayerVisibility.RelaySwingArm(
                player,
                session.Context.PlayerManager.SnapshotOnline(),
                swingSource: "attack");
            return;
        }

        if (useActionType != InventoryTransactionPacket.UseClickBlock) return;

        var stack = player.Inventory.Get(hotbarSlot);
        var world = session.Context.World;
        var clicked = world.GetBlock(blockX, blockY, blockZ);

        // Chest interact (§56): empty-hand or non-sneak → open; sneak + held placeable → place on face.
        // Prefer pending AuthInput sneak (same packet as UseItem) over last-tick IsSneaking.
        if (Blocks.IsChest(clicked))
        {
            var sneaking = player.IsSneaking;
            if (player.TryPeekMovementInput(out var pendingMove))
                sneaking = pendingMove.Sneaking;

            var emptyHand = stack.IsEmpty || stack.Count <= 0;
            if (emptyHand || !sneaking)
            {
                if (!player.SubmitWindowIntent(InventoryWindowIntent.OpenChest(blockX, blockY, blockZ)))
                    session.Context.Logger.Debug($"Dropped chest open from {player.Username}: window queue full.");
                return;
            }
        }

        if (!PlayerInventory.IsValidStackCount(stack.Count) || stack.Count <= 0)
        {
            session.Context.Logger.Debug($"Rejected place: invalid/empty stack count {stack.Count}");
            return;
        }

        if (!stack.Id.IsBlock) return;
        var runtimeId = stack.Id.Value;
        if (runtimeId == World.World.AirRuntimeId) return;
        // Facing policy on live place only — BlockEditSystem / SubmitBlockEdit with raw Blocks.Chest stay as-given.
        if (Blocks.IsChest(runtimeId))
            runtimeId = ChestFacing.RuntimeIdFromYaw(player.Yaw);

        if (!Blocks.IsPlaceable(runtimeId))
        {
            session.Context.Logger.Debug(
                $"Rejected place: non-placeable runtime {runtimeId} from {player.Username}");
            return;
        }

        var (tx, ty, tz) = FaceOffset(blockX, blockY, blockZ, blockFace);
        if (Blocks.IsChest(runtimeId))
            runtimeId = ChestPairing.AlignFacingWithNeighbor(world, tx, ty, tz, runtimeId);

        var intent = BlockEditIntent.Set(tx, ty, tz, runtimeId, hotbarSlot);
        if (!intent.IsInWorldBounds())
        {
            session.Context.Logger.Debug($"Rejected place OOB from {player.Username}");
            return;
        }

        if (!player.SubmitBlockEdit(intent))
            session.Context.Logger.Debug($"Dropped place from {player.Username}: block-edit queue full.");
        else
        {
            PlayerVisibility.RelaySwingArm(
                player,
                session.Context.PlayerManager.SnapshotOnline(),
                swingSource: "build");
            session.Context.Logger.Debug(
                $"Place queued from {player.Username} @ {tx},{ty},{tz} rid={runtimeId}");
        }
    }

    private static (int X, int Y, int Z) FaceOffset(int x, int y, int z, byte face) => face switch
    {
        0 => (x, y - 1, z),
        1 => (x, y + 1, z),
        2 => (x, y, z - 1),
        3 => (x, y, z + 1),
        4 => (x - 1, y, z),
        5 => (x + 1, y, z),
        _ => (x, y, z)
    };

}
