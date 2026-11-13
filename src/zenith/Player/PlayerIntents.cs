using Zenith.Gameplay.Survival;

namespace Zenith.Player;

/// <summary>
/// The mailboxes through which the network (or a command) hands one player's requests to the GameLoop. Each pending slot is
/// either overwrite-latest (a value or a signal) or a bounded FIFO; nothing here mutates gameplay state, the owning system consumes
/// it on tick. Dig and window intents live with their state in <see cref="PlayerDig"/> / <see cref="PlayerWindows"/>, effects in
/// <see cref="PlayerEffects"/>.
/// </summary>
sealed class PlayerIntents(Func<bool> isDead, Func<GameMode> currentGameMode)
{
    public const int MaxPendingBlockEdits = 8;
    public const int MaxPendingInventoryStacks = 8;
    public const int MaxPendingChat = 8;

    private readonly object _movementInputLock = new();
    private MovementInputState _movementInput;

    // A target is present only for InventoryTransaction.ItemUseOnActor. AuthInput missed swings
    // and air use intentionally remain untargeted, so combat may resolve the first valid target.
    private readonly PendingValue<long?> _attack = new();
    private readonly PendingSignal _projectile = new();
    private readonly PendingSignal _eat = new();
    private readonly PendingValue<long> _interact = new();
    private readonly PendingMailbox<BlockEditIntent> _blockEdits = new(MaxPendingBlockEdits);
    private readonly PendingMailbox<InventoryStackIntent> _inventoryStacks = new(MaxPendingInventoryStacks);
    private readonly PendingMailbox<string> _chat = new(MaxPendingChat);
    private readonly PendingValue<GameMode> _gameMode = new();
    private readonly PendingSignal _respawn = new();
    private readonly PendingSignal _spawnReady = new();

    public void SubmitMovementInput(in MovementInputState input)
    {
        lock (_movementInputLock)
            _movementInput = input;
    }

    /// <summary>Peek pending AuthInput without consuming; UseItem may need same-packet sneak (§56).</summary>
    public bool TryPeekMovementInput(out MovementInputState input)
    {
        lock (_movementInputLock)
        {
            if (!_movementInput.HasValue)
            {
                input = default;
                return false;
            }

            input = _movementInput;
            return true;
        }
    }

    public bool TryConsumeMovementInput(out MovementInputState input)
    {
        lock (_movementInputLock)
        {
            if (!_movementInput.HasValue)
            {
                input = default;
                return false;
            }

            input = _movementInput;
            _movementInput = default;
            return true;
        }
    }

    /// <summary>Queues place/break (FIFO). Overflow rejects the newest so already-accepted actions keep their order.</summary>
    public bool SubmitBlockEdit(in BlockEditIntent intent) => _blockEdits.Submit(intent);

    public bool TryConsumeBlockEdit(out BlockEditIntent intent) => _blockEdits.TryConsume(out intent);

    /// <summary>Queues a rearrange ISR (FIFO). Overflow rejects the newest.</summary>
    public bool SubmitInventoryStack(in InventoryStackIntent intent) => _inventoryStacks.Submit(intent);

    public bool TryConsumeInventoryStack(out InventoryStackIntent intent) => _inventoryStacks.TryConsume(out intent);

    /// <summary>A message already validated by ChatProtocol; fanned out by ChatSystem (FIFO, overflow rejects the newest, §54).</summary>
    public bool SubmitChat(string message) => _chat.Submit(message);

    public bool TryConsumeChat(out string message)
    {
        if (_chat.TryConsume(out message)) return true;
        message = "";
        return false;
    }

    /// <summary>Overwrite-latest runtime mode (§52). Applied on the GameLoop, not by handlers.</summary>
    public void SubmitGameMode(GameMode mode)
    {
        if (isDead()) return;
        _gameMode.Submit(mode);
    }

    /// <summary>No pending change reports the current mode (not <c>default</c>) so callers always get a real value.</summary>
    public bool TryConsumeGameMode(out GameMode mode) => _gameMode.TryConsume(currentGameMode(), out mode);

    /// <summary>Client Respawn CLIENT_READY / PlayerAction RESPAWN: overwrite-latest one-shot.</summary>
    public void SubmitRespawn()
    {
        if (!isDead()) return;
        _respawn.Submit();
    }

    public bool TryConsumeRespawn() => _respawn.TryConsume();

    /// <summary>
    /// Client confirmation after PLAYER_SPAWN. The network thread records only this one-shot; the gameplay owner performs the visible
    /// join/session transition in ChunkStreamSystem.
    /// </summary>
    public void SubmitSpawnReady() => _spawnReady.Submit();

    public bool TryConsumeSpawnReady() => _spawnReady.TryConsume();

    /// <summary>
    /// Network-to-gameplay handoff for one attack swing. <paramref name="targetActorRuntimeId"/> is populated only by ItemUseOnActor;
    /// reach and target ownership remain tick-validated.
    /// </summary>
    public void SubmitAttackIntent(long? targetActorRuntimeId = null) => _attack.Submit(targetActorRuntimeId);

    /// <summary>Consumes a pending attack whatever its target; production consumers use the targeted overloads, this one lets tests assert nothing was left pending.</summary>
    public bool TryConsumeAttackIntent() => _attack.TryConsume(out _);

    /// <summary>
    /// Consumes an attack only when it was untargeted or named this actor. A targeted wire attack must never be claimed by another
    /// nearby actor merely because that system ticks first.
    /// </summary>
    public bool TryConsumeAttackIntent(long expectedTargetActorRuntimeId) =>
        _attack.TryConsumeIf(target => target is null || target.Value == expectedTargetActorRuntimeId, out _);

    /// <summary>
    /// Strict targeted variant for consumers whose candidate set contains players: an absent target can never be promoted into a hit.
    /// <paramref name="isKnownTarget"/> lets the caller resolve the id against its own index without pre-selecting a candidate; this
    /// only consumes when the pending target resolves to something the caller owns, so an id from a different owner (a mob, in the
    /// shared RuntimeId space) is left for that owner's own system.
    /// </summary>
    public bool TryConsumeTargetedAttackIntent(Predicate<long> isKnownTarget, out long targetActorRuntimeId)
    {
        if (_attack.TryConsumeIf(target => target is { } id && isKnownTarget(id), out var consumed))
        {
            targetActorRuntimeId = consumed!.Value;
            return true;
        }

        targetActorRuntimeId = default;
        return false;
    }

    /// <summary>Network-to-gameplay handoff for the first short-lived projectile slice.</summary>
    public void SubmitProjectileIntent() => _projectile.Submit();

    public bool TryConsumeProjectileIntent() => _projectile.TryConsume();

    /// <summary>Network-to-gameplay handoff for eating the held food stack (Phase XI.1).</summary>
    public void SubmitEatIntent() => _eat.Submit();

    public bool TryConsumeEatIntent() => _eat.TryConsume();

    /// <summary>
    /// Network-to-gameplay handoff for a non-attack actor interact (Phase XVIII): right-click on an entity. Carries only the target's
    /// runtime id; which gameplay system owns that id is resolved on the tick, one candidate system at a time.
    /// </summary>
    public void SubmitInteractIntent(long targetActorRuntimeId) => _interact.Submit(targetActorRuntimeId);

    /// <summary>Consumes the pending interact only if it targets <paramref name="expectedTargetRuntimeId"/>; otherwise leaves it for another system.</summary>
    public bool TryConsumeInteractIntent(long expectedTargetRuntimeId) =>
        _interact.TryConsumeIf(target => target == expectedTargetRuntimeId, out _);
}
