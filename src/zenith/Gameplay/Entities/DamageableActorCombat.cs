using Zenith.Ecs;
using Zenith.Player;
using Zenith.World;

using Zenith.Gameplay.Survival;
using Zenith.Gameplay.Replication;
namespace Zenith.Gameplay.Entities;

/// <summary>
/// Combat bookkeeping for every actor species in <see cref="EntityRuntime"/>: reach-checked melee consumption, damage application,
/// health replication, death detection, loot deposit, XP credit and death replication/destruction. It owns none of what a species
/// decides (spawning, AI, movement, targeting). A thin adapter over <see cref="DamageableActorCombatCore"/>: it reads
/// <see cref="Position"/>/<see cref="HealthComponent"/> components keyed by <see cref="EntityId"/> and hands the core a view.
/// </summary>
static class DamageableActorCombat
{
    /// <summary>Reach-checked melee: consumes at most one player's attack intent per call, the same for every species.</summary>
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
    /// One authoritative damage attempt against an ECS-backed damageable actor. Guard, deposit, XP, replication,
    /// then destruction: <paramref name="destroyActor"/> is the caller's only hook, because destruction goes through
    /// <see cref="EntityRuntime.DestroyActor"/> and every feature system's own pre-destroy cleanup.
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
