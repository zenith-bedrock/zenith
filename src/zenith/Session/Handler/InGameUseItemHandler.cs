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
            use.HotbarSlot,
            use.ClickedBlockRuntimeId,
            use.HeldItem);
    }

    private static void HandleUseItem(
        NetworkSession session,
        Player.Player player,
        int useActionType,
        int blockX,
        int blockY,
        int blockZ,
        byte blockFace,
        int hotbarSlot,
        int clickedBlockRuntimeId,
        in DecodedTransactionItem heldItem)
    {
        if (useActionType is < InventoryTransactionPacket.UseClickBlock or > InventoryTransactionPacket.UseAsAttack)
        {
            session.Context.Logger.Debug($"Rejected UseItem: unknown action {useActionType} from {player.Username}.");
            return;
        }

        if (!TryApplyTransactionHeldStack(session, player, hotbarSlot, heldItem, "UseItem"))
            return;

        if (useActionType == InventoryTransactionPacket.UseDestroyBlock)
        {
            session.Context.Logger.Debug(
                $"UseItem destroy from {player.Username} @ {blockX},{blockY},{blockZ}");
            TrySubmitBreak(player, blockX, blockY, blockZ);
            return;
        }

        if (useActionType == InventoryTransactionPacket.UseClickAir)
        {
            var heldStack = player.Inventory.Get(hotbarSlot);
            if (heldStack.Count > 0 && FoodItems.TryGetNutrition(session.Context.ItemPalette, heldStack.Id, out _))
            {
                player.SubmitEatIntent();
                return;
            }

            // UseClickAir and AuthInput.MissedSwing are peer-animation signals only. A real
            // entity hit carries its runtime id in ItemUseOnActor; turning an untargeted swing
            // into an attack would let system iteration pick an arbitrary nearby actor.
            PlayerVisibility.RelaySwingArm(
                player,
                session.Context.PlayerManager.SnapshotOnline(),
                swingSource: "attack");
            return;
        }

        // The first projectile vertical slice intentionally uses the distinct Bedrock
        // attack-style item-use action. It is a bounded input only; ProjectileSystem owns
        // creation, movement, collision, damage and removal on the gameplay tick.
        if (useActionType == InventoryTransactionPacket.UseAsAttack)
        {
            player.SubmitProjectileIntent();
            PlayerVisibility.RelaySwingArm(
                player,
                session.Context.PlayerManager.SnapshotOnline(),
                swingSource: "projectile");
            return;
        }

        if (useActionType != InventoryTransactionPacket.UseClickBlock) return;

        var stack = player.Inventory.Get(hotbarSlot);
        var world = session.Context.World;
        var clicked = world.GetBlock(blockX, blockY, blockZ);
        // A real client normally supplies the target block runtime id. Some compatible
        // automation clients, however, encode the optional/prediction field as its zero
        // default. Zero is not a Zenith block-state id, so it can safely mean “no client
        // precondition supplied”: retain the explicit-id stale-view guard without rejecting
        // an otherwise valid, authoritative interaction merely because that hint is absent.
        if (clickedBlockRuntimeId != 0 && clicked != clickedBlockRuntimeId)
        {
            session.Context.Logger.Debug(
                $"Rejected UseClickBlock from {player.Username}: client={clickedBlockRuntimeId}, server={clicked} @ {blockX},{blockY},{blockZ}");
            session.Protocol.World.SendUpdateBlock(blockX, blockY, blockZ, clicked);
            return;
        }

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

        var intent = BlockEditIntent.Set(tx, ty, tz, runtimeId, hotbarSlot, stack.Id);
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
