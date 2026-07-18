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
    private static void HandleAuthInput(NetworkSession session, ref BinaryStream stream)
    {
        var packet = DataPacket.From<PlayerAuthInputPacket>(ref stream);
        var player = session.Player;
        if (player is null) return;

        // Death screen: still submit pose so MovementSystem can drain; ignore dig/use/pose modes.
        if (player.IsDead)
        {
            var deadInput = MovementInputState.FromClientAuthInput(
                packet.PositionX,
                packet.PositionY,
                packet.PositionZ,
                packet.Pitch,
                packet.Yaw);
            if (deadInput.IsSecure())
                player.SubmitMovementInput(deadInput);
            return;
        }

        var input = MovementInputState.FromClientAuthInput(
            packet.PositionX,
            packet.PositionY,
            packet.PositionZ,
            packet.Pitch,
            packet.Yaw,
            sneaking: packet.InputSneaking,
            sprintStart: packet.InputStartSprinting,
            sprintStop: packet.InputStopSprinting,
            missedSwing: packet.InputMissedSwing);

        if (!input.IsSecure())
        {
            session.Context.Logger.Warning($"Rejected AuthInput from {player.Username}: non-finite floats.");
            return;
        }

        player.SubmitMovementInput(input);

        if (packet.ItemInteraction is { } useItem)
            HandleUseItemInteraction(session, player, useItem);

        // AuthInput break order (§27): Abort → Start/Crack → Predict → Continue.
        // Abort-before-Predict clears cancelled dig; Start-before-Predict fixes cancel+redig
        // same-packet; Predict-before-Continue keeps chain-break DigAuthorized intact.
        var actions = packet.BlockActions;

        foreach (var action in actions)
        {
            if (action.Action != PlayerAuthInputPacket.ActionAbortBreak)
                continue;
            // Dig abort on tick — crack Stop + AbortBreak (§54). Do not dequeue BlockEditIntent.
            if (!player.SubmitDigAbort(action.BlockX, action.BlockY, action.BlockZ))
                session.Context.Logger.Debug($"Dropped dig abort from {player.Username}: dig queue full.");
        }

        foreach (var action in actions)
        {
            if (action.Action is PlayerAuthInputPacket.ActionStartBreak
                or PlayerAuthInputPacket.ActionCrackBreak)
            {
                HandleBreakProgress(
                    session, player, action.Action, action.BlockX, action.BlockY, action.BlockZ);
            }
        }

        foreach (var action in actions)
        {
            if (action.Action is PlayerAuthInputPacket.ActionPredictDestroy
                or PlayerActionPacket.ActionCreativeDestroy)
            {
                session.Context.Logger.Debug(
                    $"AuthInput break from {player.Username}: action={action.Action} @ {action.BlockX},{action.BlockY},{action.BlockZ}");
                TrySubmitBreak(player, action.BlockX, action.BlockY, action.BlockZ);
            }
        }

        foreach (var action in actions)
        {
            if (action.Action == PlayerAuthInputPacket.ActionContinueDestroy)
            {
                HandleBreakProgress(
                    session, player, action.Action, action.BlockX, action.BlockY, action.BlockZ);
            }
        }
    }

    private static void HandlePlayerAction(NetworkSession session, ref BinaryStream stream)
    {
        var packet = DataPacket.From<PlayerActionPacket>(ref stream);
        var player = session.Player;
        if (player is null) return;

        if (packet.Action == PlayerActionPacket.ActionRespawn)
        {
            if (player.IsDead)
                player.SubmitRespawn();
            return;
        }

        if (player.IsDead) return;

        if (packet.Action is not (PlayerActionPacket.ActionCreativeDestroy or PlayerActionPacket.ActionPredictDestroy))
        {
            if (packet.Action is not (PlayerActionPacket.ActionStartItemUseOn or PlayerActionPacket.ActionStopItemUseOn))
            {
                session.Context.Logger.Debug(
                    $"PlayerAction ignored from {player.Username}: action={packet.Action}");
            }
            return;
        }

        session.Context.Logger.Debug(
            $"PlayerAction break from {player.Username}: action={packet.Action} @ {packet.BlockX},{packet.BlockY},{packet.BlockZ}");
        TrySubmitBreak(player, packet.BlockX, packet.BlockY, packet.BlockZ);
    }

    private static void HandleRespawn(NetworkSession session, ref BinaryStream stream)
    {
        var packet = DataPacket.From<RespawnPacket>(ref stream);
        var player = session.Player;
        if (player is null || !player.IsDead) return;

        if (packet.State != RespawnPacket.StateClientReadyToSpawn)
            return;

        player.SubmitRespawn();
    }

    private static void HandleBreakProgress(
        NetworkSession session,
        Player.Player player,
        int action,
        int x,
        int y,
        int z)
    {
        if (player.GameMode == GameMode.Creative)
            return;

        if (player.IsBreakTarget(x, y, z) || player.TryGetDigAuth(x, y, z, out _, out _))
            return;

        var block = session.Context.World.GetBlock(x, y, z);
        var held = player.Inventory.Get(player.SelectedHotbarSlot);
        var heldId = held.IsEmpty ? default : held.Id;
        var need = Blocks.BreakTicks(block, heldId);
        // Unknown DigProfile → no Survival dig auth (ADR §55).
        if (need < 0)
        {
            session.Context.Logger.Debug(
                $"AuthInput dig ignored (no DigProfile) for {player.Username} @ {x},{y},{z} block={block}");
            return;
        }

        var tick = session.Context.Clock.CurrentTick;
        if (!player.SubmitDigStart(x, y, z, tick, need, heldId))
            session.Context.Logger.Debug($"Dropped dig start from {player.Username}: dig queue full.");
        else
        {
            var label = action switch
            {
                PlayerAuthInputPacket.ActionStartBreak => "start_break",
                PlayerAuthInputPacket.ActionCrackBreak => "crack_break",
                _ => "continue_destroy"
            };
            session.Context.Logger.Debug(
                $"AuthInput {label} from {player.Username} @ {x},{y},{z} need={need} ticks held={heldId}");
        }
    }

    private static void TrySubmitBreak(Player.Player player, int x, int y, int z)
    {
        if (player.IsDead) return;

        BlockEditIntent intent;
        if (player.GameMode != GameMode.Creative &&
            player.TryGetDigAuth(x, y, z, out var started, out var need) &&
            need > 0)
        {
            intent = BlockEditIntent.BreakWithDig(x, y, z, started, need);
            // Clear dig lock + cancel pending Start — StopCrack stays for ApplyEdit success (§27).
            player.ClearBreakTarget();
            player.CancelPendingDigStart(x, y, z);
        }
        else
        {
            intent = BlockEditIntent.Set(x, y, z, World.World.AirRuntimeId);
        }

        if (!intent.IsInWorldBounds()) return;
        if (!player.SubmitBlockEdit(intent))
            player.Session.Context.Logger.Debug($"Dropped break from {player.Username}: block-edit queue full.");
        else
        {
            // Creative instant / predict without dig auth — Survival dig swings on tick ApplyDig.
            if (player.GameMode == GameMode.Creative || !intent.DigAuthorized)
            {
                PlayerVisibility.RelaySwingArm(
                    player,
                    player.Session.Context.PlayerManager.SnapshotOnline(),
                    swingSource: "mine");
            }

            player.Session.Context.Logger.Debug(
                $"Break queued from {player.Username} @ {x},{y},{z} dig={intent.DigAuthorized}");
        }
    }

}
