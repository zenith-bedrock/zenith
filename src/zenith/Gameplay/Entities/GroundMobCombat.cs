using Zenith.Player;
using Zenith.World;

using Zenith.Gameplay.Survival;
using Zenith.Gameplay.Replication;
namespace Zenith.Gameplay.Entities;

/// <summary>
/// Phase XIV.1 — the minimum contract <see cref="GroundMobCombat"/> needs to apply damage/loot/XP
/// bookkeeping to a mob without knowing its concrete type. Deliberately data-only: no
/// <c>Update</c>/<c>Move</c>/<c>Attack</c>/<c>Think</c>, no inheritance, no base class. AI,
/// movement, and targeting stay entirely on the concrete <c>Zombie</c>/<c>Skeleton</c>/<c>Cow</c>
/// types and their owning systems.
///
/// Renamed from <c>IGroundMob</c> in Phase XX: by then the contract had ten real implementers,
/// several of them not "ground" (Bat, a flying mob) and not "mob" (Villager, an NPC; Minecart, a
/// vehicle) by any gameplay definition. What every implementer actually shares is exactly what the
/// members below say — identity, position, health, the ability to take damage, and a removal
/// lifecycle — nothing about walking, nothing about AI. <c>IDamageableActor</c> names that
/// precisely without reaching for a broader ontology word (`Entity`/`Actor` alone/`LivingEntity`)
/// this project has deliberately avoided everywhere else. See
/// docs/history/phases/phase-xx-runtime-relationships-findings.md for the naming review that produced this.
/// </summary>
interface IDamageableActor
{
    long EntityId { get; }
    ulong RuntimeId { get; }
    float PositionX { get; }
    float PositionY { get; }
    float PositionZ { get; }
    HealthState Health { get; }
    bool IsActive { get; }
    DamageResult ApplyDamage(DamageSource source, float amount, ulong currentTick);
    void Remove();
}

/// <summary>
/// Shared ground-mob combat bookkeeping (Phase XIV.1) — extracted after Cow made
/// <c>ApplyPlayerAttacks</c>/<c>TryApplyDamage</c>/<c>CanDropLoot</c> byte-identical across
/// ZombieSystem, SkeletonSystem and CowSystem (see
/// docs/history/phases/phase-xiv-gameplay-expansion-findings.md). Owns exactly: reach-checked melee
/// consumption, damage application, health replication, death detection, loot deposit, XP credit,
/// and death replication/store removal. Does not own, and must never grow to own: spawning, AI,
/// movement, targeting, or pathfinding — those stay concrete per mob.
/// <para/>
/// A thin adapter over <see cref="DamageableActorCombatCore"/> since Phase XXI's ECS split left the
/// actual damage/loot/XP/replication sequence duplicated byte-for-byte between this class and
/// <see cref="DamageableActorCombat"/> — only how a mob's state is read (object properties here, ECS
/// component lookups there) ever differed. Closed without touching either backend's storage model,
/// which stays split for the documented reasons in <c>docs/ecs.md</c>'s retirement gate. Every
/// public signature below is unchanged; every caller of this class is untouched.
/// </summary>
static class GroundMobCombat
{
    /// <summary>
    /// Reach-checked melee: consumes at most one player's attack intent per call, same shape every
    /// ground mob used independently before this extraction.
    /// </summary>
    public static void ApplyPlayerMeleeAttacks<TMob>(
        TMob mob,
        IReadOnlyList<Player.Player> online,
        float attackDistance,
        float attackDamage,
        ulong currentTick,
        Func<TMob, DamageSource, float, IReadOnlyList<Player.Player>, ulong, bool> tryApplyDamage)
        where TMob : IDamageableActor
    {
        DamageableActorCombatCore.TryApplyPlayerMeleeAttack(
            ToView(mob), online, attackDistance, attackDamage, currentTick,
            (source, amount, peers, tick) => tryApplyDamage(mob, source, amount, peers, tick));
    }

    /// <summary>
    /// One authoritative damage attempt against a ground mob: guards against a lethal hit the
    /// world can't accept the loot for, applies damage, replicates health, and — on death — deposits
    /// loot, credits XP (<see cref="MobKillReward"/>), replicates removal, and removes the mob from
    /// its store. <paramref name="removeFromStore"/> and <paramref name="onDeathReplicatedToPeer"/>
    /// are the only two points still owned by the caller, because they're the only two things that
    /// differ per mob (which store, and whether extra per-peer state like a move-projection cache
    /// needs clearing).
    /// </summary>
    public static bool TryApplyDamage<TMob>(
        TMob mob,
        DamageSource source,
        float amount,
        IReadOnlyList<Player.Player> online,
        World.World world,
        PlayerManager players,
        HashSet<(long EntityId, long PlayerId)> replicated,
        StackId lootItem,
        int killExperience,
        string mobName,
        ulong currentTick,
        Action<TMob> removeFromStore,
        Action<Player.Player>? onDeathReplicatedToPeer = null)
        where TMob : IDamageableActor
    {
        if (!mob.IsActive) return false;

        return DamageableActorCombatCore.TryApplyDamage(
            ToView(mob), source, amount, online, world, players, replicated, lootItem, killExperience, mobName,
            currentTick,
            destroy: () =>
            {
                mob.Remove();
                removeFromStore(mob);
            },
            onDeathReplicatedToPeer);
    }

    private static DamageableActorView ToView<TMob>(TMob mob) where TMob : IDamageableActor =>
        new(mob.EntityId, mob.RuntimeId, mob.PositionX, mob.PositionY, mob.PositionZ, mob.Health);
}
