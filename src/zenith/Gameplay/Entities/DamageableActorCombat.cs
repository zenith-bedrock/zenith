using Zenith.Ecs;
using Zenith.Player;
using Zenith.World;

using Zenith.Gameplay.Survival;
using Zenith.Gameplay.Replication;
namespace Zenith.Gameplay.Entities;

/// <summary>
/// Phase XXI — the ECS-native sibling of <see cref="GroundMobCombat"/>, for actors migrated onto
/// <see cref="EntityRuntime"/>. Same responsibility (reach-checked melee consumption, damage
/// application, health replication, death detection, loot deposit, XP credit, death
/// replication/destruction) and the same non-goals (no spawning, AI, movement, or targeting) —
/// only the storage it reads/writes changed, from an <c>IDamageableActor</c> object to
/// <see cref="Position"/>/<see cref="HealthComponent"/> components keyed by <see cref="EntityId"/>.
///
/// <see cref="GroundMobCombat"/> was NOT deleted or adapted in place: it still correctly serves
/// every unmigrated ground mob (Skeleton, Cow, Creeper, Enderman, Bat, Spider, Villager, Golem,
/// Fish). Building a fake <c>IDamageableActor</c> wrapper around an ECS entity just to keep
/// calling <c>GroundMobCombat</c> unchanged was explicitly rejected — see
/// docs/history/phases/phase-xxi-ecs-foundation-findings.md, "IDamageableActor migration strategy."
///
/// A thin adapter over <see cref="DamageableActorCombatCore"/>, same as <see cref="GroundMobCombat"/>
/// now is — see that class's doc comment for why the two were unified. Public signatures below are
/// unchanged; every caller of this class is untouched.
/// </summary>
static class DamageableActorCombat
{
    /// <summary>Reach-checked melee: consumes at most one player's attack intent per call, same shape <see cref="GroundMobCombat.ApplyPlayerMeleeAttacks{TMob}"/> already used.</summary>
    public static void ApplyPlayerMeleeAttacks(
        EntityId id,
        EntityRuntime stores,
        IReadOnlyList<Player.Player> online,
        float attackDistance,
        float attackDamage,
        ulong currentTick,
        Func<EntityId, DamageSource, float, IReadOnlyList<Player.Player>, ulong, bool> tryApplyDamage)
    {
        if (!TryBuildView(id, stores, out var view)) return;

        DamageableActorCombatCore.TryApplyPlayerMeleeAttack(
            view, online, attackDistance, attackDamage, currentTick,
            (source, amount, peers, tick) => tryApplyDamage(id, source, amount, peers, tick));
    }

    /// <summary>
    /// One authoritative damage attempt against an ECS-backed damageable actor. Same guard/deposit/
    /// XP/replication/destruction sequence as <see cref="GroundMobCombat.TryApplyDamage{TMob}"/>;
    /// <paramref name="destroyActor"/> is the caller's only remaining hook (equivalent to that
    /// method's <c>removeFromStore</c>) because destruction now goes through
    /// <see cref="EntityRuntime.DestroyActor"/> and every feature system's own pre-destroy cleanup,
    /// not a store removal.
    /// </summary>
    public static bool TryApplyDamage(
        EntityId id,
        EntityRuntime stores,
        DamageSource source,
        float amount,
        IReadOnlyList<Player.Player> online,
        World.World world,
        PlayerManager players,
        HashSet<(long EntityId, long PlayerId)> replicated,
        StackId lootItem,
        int killExperience,
        string actorName,
        ulong currentTick,
        Action<EntityId> destroyActor,
        Action<Player.Player>? onDeathReplicatedToPeer = null)
    {
        if (!TryBuildView(id, stores, out var view)) return false;

        return DamageableActorCombatCore.TryApplyDamage(
            view, source, amount, online, world, players, replicated, lootItem, killExperience, actorName,
            currentTick,
            destroy: () => destroyActor(id),
            onDeathReplicatedToPeer);
    }

    private static bool TryBuildView(EntityId id, EntityRuntime stores, out DamageableActorView view)
    {
        view = default;
        if (!stores.Health.TryGet(id, out var healthComponent)) return false;
        if (!stores.Identities.TryGet(id, out var identity)) return false;
        if (!stores.Positions.TryGet(id, out var pos)) return false;

        view = new DamageableActorView(
            identity.ActorUniqueId, identity.ActorRuntimeId, pos.X, pos.Y, pos.Z, healthComponent.State);
        return true;
    }
}
