using Zenith.Gameplay.Replication;
using System.Collections.Generic;
using Zenith.Binary;
using Zenith.Packets;
using Zenith.Protocol;
using Zenith.Player;
using Zenith.World;
using Zenith.Gameplay.Survival;
using Zenith.Gameplay.WorldInteraction.Fluids;

namespace Zenith.Session.Handler;

/// <summary>Partial: see <see cref="InGameSessionHandler"/>.</summary>
partial class InGameSessionHandler
{
    // Eating particles/progress are client presentation. They never complete a use or mutate food.
    private static void HandleActorEvent(NetworkSession session, ref BinaryStream stream) =>
        _ = TryDecode<ActorEventPacket>(session, ref stream, "ActorEvent", out _);

    private static void HandleUseItemInteraction(
        NetworkSession session,
        Player.Player player,
        in AuthItemInteraction use)
    {
        if (use.Hand != InventoryTransactionPacket.HandMainhand)
        {
            session.Context.Logger.Debug($"Rejected UseItem from unsupported hand {use.Hand} for {player.Username}.");
            return;
        }

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
            use.ClickY,
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
        float clickY,
        in DecodedTransactionItem heldItem)
    {
        session.Context.Logger.Debug(
            $"UseItem received from {player.Username}: action={useActionType} block={blockX},{blockY},{blockZ} face={blockFace} slot={hotbarSlot}");
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
                if (!player.Intents.SubmitEatIntent(hotbarSlot, heldStack))
                    session.Context.Logger.Debug($"Dropped food start from {player.Username}: use queue full.");
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
            player.Intents.SubmitProjectileIntent();
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

        // A bucket works on water as much as on blocks, so it never falls through to block placement.
        if (session.Context.Buckets.IsBucket(stack.Id) || session.Context.Buckets.IsFlintAndSteel(stack.Id))
        {
            if (!player.Intents.SubmitBucket(new BucketIntent(blockX, blockY, blockZ, blockFace, hotbarSlot, stack.Id)))
                session.Context.Logger.Debug($"Dropped bucket use from {player.Username}: queue full.");
            else
                PlayerVisibility.RelaySwingArm(player, session.Context.PlayerManager.SnapshotOnline(), swingSource: "build");
            return;
        }

        // Chest / crafting table interact (§56 / ADR §139): empty-hand or non-sneak → open;
        // sneak + held placeable → place on face. Prefer pending AuthInput sneak (same packet as
        // UseItem) over last-tick IsSneaking.
        if (Blocks.IsChest(clicked) || Blocks.IsCraftingTable(clicked))
        {
            var sneaking = player.IsSneaking;
            if (player.Intents.TryPeekMovementInput(out var pendingMove))
                sneaking = pendingMove.Sneaking;

            var emptyHand = stack.IsEmpty || stack.Count <= 0;
            if (emptyHand || !sneaking)
            {
                var opened = Blocks.IsChest(clicked)
                    ? player.Windows.SubmitWindowIntent(InventoryWindowIntent.OpenChest(blockX, blockY, blockZ))
                    : player.Windows.SubmitWindowIntent(InventoryWindowIntent.OpenCraftingTable(blockX, blockY, blockZ));
                if (!opened)
                    session.Context.Logger.Debug($"Dropped container open from {player.Username}: window queue full.");
                return;
            }
        }

        // Doors, trapdoors and fence gates open and close on right click (sneaking with a block in hand places instead).
        if (BlockShapes.IsInteractive(clicked))
        {
            var sneakingAtDoor = player.IsSneaking;
            if (player.Intents.TryPeekMovementInput(out var doorMove))
                sneakingAtDoor = doorMove.Sneaking;
            if (stack.IsEmpty || stack.Count <= 0 || !sneakingAtDoor)
            {
                if (!player.Intents.SubmitBlockEdit(BlockEditIntent.Toggle(blockX, blockY, blockZ)))
                    session.Context.Logger.Debug($"Dropped door toggle from {player.Username}: block-edit queue full.");
                return;
            }
        }

        if (!PlayerInventory.IsValidStackCount(stack.Count) || stack.Count <= 0)
        {
            session.Context.Logger.Debug($"Rejected place: invalid/empty stack count {stack.Count}");
            return;
        }

        int runtimeId;
        if (stack.Id.IsBlock)
            runtimeId = stack.Id.Value;
        else if (stack.Id.IsItem &&
                 session.Context.ItemPalette.TryGetName(stack.Id.Value, out var heldItemName) &&
                 Blocks.TryGetDefaultRuntimeId(heldItemName, out var blockOfItem) &&
                 Blocks.Catalog.KindOf(blockOfItem) == BlockKind.Door)
            runtimeId = blockOfItem; // doors are items in the catalogue; their block form is placed
        else
            return;
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

        var (tx, ty, tz) = BlockFaces.Offset(blockX, blockY, blockZ, blockFace);
        var heldKind = Blocks.Catalog.KindOf(runtimeId);
        if (heldKind == BlockKind.Slab && world.GetBlock(blockX, blockY, blockZ) is var clickedForMerge &&
            BlockShapes.TryMergeSlab(clickedForMerge, runtimeId, requireOppositeHalf: false, blockFace, out var mergedAtClick))
        {
            // Second slab onto the open half of the slab that was clicked: the clicked cell becomes a double slab.
            SubmitPlacement(session, player, blockX, blockY, blockZ, mergedAtClick, hotbarSlot, stack.Id);
            return;
        }

        if (!Blocks.IsChest(runtimeId))
        {
            // The block's shape picks the state the placement implies (stairs, slabs, doors…) and refuses a spot
            // where it could not stand (a flower without ground, a torch without a wall) so nothing floats.
            if (!BlockShapes.TryResolve(runtimeId, blockFace, clickY, player.Yaw, out runtimeId) ||
                !BlockShapes.CanExist(world, tx, ty, tz, runtimeId))
            {
                session.Context.Logger.Debug($"Rejected place: no valid state/support at {tx},{ty},{tz} for {player.Username}");
                session.Protocol.World.SendUpdateBlock(tx, ty, tz, world.GetBlock(tx, ty, tz));
                return;
            }
        }

        if (Blocks.IsChest(runtimeId))
            runtimeId = ChestPairing.AlignFacingWithNeighbor(world, tx, ty, tz, runtimeId);

        if (heldKind == BlockKind.Slab &&
            BlockShapes.TryMergeSlab(world.GetBlock(tx, ty, tz), runtimeId, requireOppositeHalf: true, null, out var mergedAtTarget))
            runtimeId = mergedAtTarget;

        var intent = BlockEditIntent.Set(tx, ty, tz, runtimeId, hotbarSlot, stack.Id);
        if (!intent.IsInWorldBounds())
        {
            session.Context.Logger.Debug($"Rejected place OOB from {player.Username}");
            return;
        }

        if (!player.Intents.SubmitBlockEdit(intent))
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

    private static void SubmitPlacement(
        NetworkSession session, Player.Player player, int x, int y, int z, int runtimeId, int hotbarSlot, StackId held)
    {
        var intent = BlockEditIntent.Set(x, y, z, runtimeId, hotbarSlot, held);
        if (!intent.IsInWorldBounds() || !player.Intents.SubmitBlockEdit(intent))
        {
            session.Context.Logger.Debug($"Dropped merge place from {player.Username} @ {x},{y},{z}");
            return;
        }

        PlayerVisibility.RelaySwingArm(player, session.Context.PlayerManager.SnapshotOnline(), swingSource: "build");
        session.Context.Logger.Debug($"Place queued from {player.Username} @ {x},{y},{z} rid={runtimeId}");
    }


}
