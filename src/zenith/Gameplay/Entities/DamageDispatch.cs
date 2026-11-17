using Zenith.Ecs;

using Zenith.Gameplay.Survival;
namespace Zenith.Gameplay.Entities;

/// <summary>
/// Phase XXII — the ECS-native damage dispatch seam. Replaces the Phase XXI two-branch
/// <c>if (_zombies.Owns(id)) ... else if (_minecarts.Owns(id)) ...</c> chain in
/// <c>ProjectileSystem</c>, which was explicitly documented as acceptable only until a third
/// ECS-damageable category arrived — Cow/Skeleton/Spider's migration this phase crossed that bar
/// (five ECS-damageable species now, not two).
///
/// This is NOT a generic event bus. It is a fixed list, built once at composition-root time: each
/// migrated system registers a narrow <c>(Owns, TryApplyDamage)</c> pair for itself — one line per
/// species, visible at startup, not runtime pub/sub, reflection, or dynamic discovery. Finding
/// *candidate* targets remains capability-based (<c>ProjectileSystem.FindHitDamageableActor</c>
/// still queries by <c>HealthComponent</c>+<c>Position</c> presence, unchanged); this only replaces
/// "which system's <c>TryApplyDamage</c> do I call for this entity" — a lookup that must stay
/// feature-owned because death consequences (loot item, dismount, knockback, ...) differ per
/// species and were never going to be absorbed into the ECS itself.
/// </summary>
sealed class DamageDispatch
{
    private readonly List<(Func<EntityId, bool> Owns, Func<EntityId, DamageSource, float, IReadOnlyList<Player.Player>, ulong, bool> TryApplyDamage)> _handlers = [];

    public void Register(
        Func<EntityId, bool> owns,
        Func<EntityId, DamageSource, float, IReadOnlyList<Player.Player>, ulong, bool> tryApplyDamage) =>
        _handlers.Add((owns, tryApplyDamage));

    /// <summary>
    /// What the target's effects do to a hit before its species sees it (Resistance, Fire Resistance); returns the amount left, 0 to refuse it.
    /// Set once at composition, from the mob bodies.
    /// </summary>
    public Func<EntityId, DamageSource, float, float>? Mitigate { get; set; }

    public void Register(IDamageableSpecies species) => Register(species.Owns, species.TryApplyDamage);

    /// <summary>Dispatches to whichever registered system owns <paramref name="id"/>; false if none does.</summary>
    public bool TryApplyDamage(EntityId id, DamageSource source, float amount, IReadOnlyList<Player.Player> online, ulong currentTick)
    {
        foreach (var (owns, tryApplyDamage) in _handlers)
            if (owns(id))
            {
                if (Mitigate is not null)
                {
                    amount = Mitigate(id, source, amount);
                    if (amount <= 0f) return false;
                }

                return tryApplyDamage(id, source, amount, online, currentTick);
            }
        return false;
    }
}
