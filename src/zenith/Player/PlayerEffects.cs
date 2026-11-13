using Zenith.Gameplay.Survival;

namespace Zenith.Player;

/// <summary>
/// One player's status effects: the active ones (GameLoop-only, <see cref="Gameplay.Survival.EffectSystem"/> is the sole writer on
/// tick) and the pending effect command the network or a command handler hands over (overwrite-latest, like the game mode).
/// </summary>
sealed class PlayerEffects(Func<bool> isDead)
{
    private readonly PendingValue<EffectIntent> _pending = new();
    private readonly Dictionary<EffectType, ActiveEffect> _active = new();

    /// <summary>Network/command-to-gameplay handoff for one effect command (Phase XI.3).</summary>
    public void SubmitEffect(EffectIntent intent)
    {
        if (isDead()) return;
        _pending.Submit(intent);
    }

    public bool TryConsumeEffectIntent(out EffectIntent intent) => _pending.TryConsume(out intent);

    /// <summary>The effects currently active.</summary>
    public IReadOnlyDictionary<EffectType, ActiveEffect> Active => _active;

    /// <summary>
    /// Vanilla/PocketMine "stronger wins": a currently-active effect is kept, not overwritten, unless the new instance is a strictly
    /// higher amplifier or the same amplifier with more remaining duration, so a weak potion cannot downgrade or shorten a stronger
    /// one already ticking. Returns whether the new instance actually took effect, so the caller knows whether to tell the client.
    /// </summary>
    public bool ApplyOrRefreshEffect(EffectType type, int amplifier, ulong expiresAtTick, ulong currentTick)
    {
        if (_active.TryGetValue(type, out var existing) && !existing.HasExpired(currentTick))
        {
            if (existing.Amplifier > amplifier) return false;
            if (existing.Amplifier == amplifier && existing.ExpiresAtTick >= expiresAtTick) return false;
        }

        _active[type] = new ActiveEffect(type, amplifier, expiresAtTick);
        return true;
    }

    public bool RemoveEffect(EffectType type) => _active.Remove(type);

    public bool ClearEffects()
    {
        if (_active.Count == 0) return false;
        _active.Clear();
        return true;
    }
}
