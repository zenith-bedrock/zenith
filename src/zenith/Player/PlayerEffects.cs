using Zenith.Gameplay.Survival;

namespace Zenith.Player;

/// <summary>
/// One player's status effects: the active ones (held by the player's <see cref="Body"/>, which turns them into attribute modifiers;
/// GameLoop-only, <see cref="Gameplay.Survival.EffectSystem"/> is the sole writer on tick) and the pending effect command the network or a
/// command handler hands over (overwrite-latest, like the game mode).
/// </summary>
sealed class PlayerEffects(Func<bool> isDead, Body body)
{
    private readonly PendingValue<EffectIntent> _pending = new();

    /// <summary>Network/command-to-gameplay handoff for one effect command (Phase XI.3).</summary>
    public void SubmitEffect(EffectIntent intent)
    {
        if (isDead()) return;
        _pending.Submit(intent);
    }

    public bool TryConsumeEffectIntent(out EffectIntent intent) => _pending.TryConsume(out intent);

    /// <summary>The effects currently active.</summary>
    public IReadOnlyDictionary<EffectType, ActiveEffect> Active => body.Effects.Active;

    /// <summary>
    /// Vanilla/PocketMine "stronger wins" (see <see cref="EffectSet.ApplyOrRefresh"/>). Returns whether the new instance actually took effect,
    /// so the caller knows whether to tell the client. The body rebuilds the attribute modifiers the effect grants.
    /// </summary>
    public bool ApplyOrRefreshEffect(EffectType type, int amplifier, ulong expiresAtTick, ulong currentTick) =>
        body.Give(type, amplifier, expiresAtTick, currentTick);

    /// <summary>The level of an active effect (amplifier + 1), 0 when it is not active: what a rule multiplies by.</summary>
    public int LevelOf(EffectType type) => body.Effects.LevelOf(type);

    public bool RemoveEffect(EffectType type) => body.Remove(type);

    public bool ClearEffects() => body.Clear();
}
