using Zenith.Ecs;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Gameplay;

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
        Func<EntityId, DamageSource, float, IReadOnlyList<Player.Player>, bool> tryApplyDamage)
    {
        if (!stores.Positions.TryGet(id, out var pos)) return;
        if (!stores.Identities.TryGet(id, out var identity)) return;

        var reachSquared = attackDistance * attackDistance;
        foreach (var player in online)
        {
            if (!player.IsInGame || player.IsDead) continue;
            var dx = pos.X - player.PositionX;
            var dz = pos.Z - player.PositionZ;
            if (dx * dx + dz * dz > reachSquared) continue;
            if (!player.TryConsumeAttackIntent(checked((long)identity.ActorRuntimeId))) continue;
            if (tryApplyDamage(id, DamageSource.MeleeFrom(player.RuntimeId), attackDamage, online)) break;
        }
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
        Action<EntityId> destroyActor,
        Action<Player.Player>? onDeathReplicatedToPeer = null)
    {
        if (!stores.Health.TryGet(id, out var healthComponent)) return false;
        if (!stores.Identities.TryGet(id, out var identity)) return false;
        if (!stores.Positions.TryGet(id, out var pos)) return false;

        var health = healthComponent.State;
        var canDropLoot = FloorDropFanout.CanDeposit(
            world, (int)MathF.Floor(pos.X), (int)MathF.Floor(pos.Y), (int)MathF.Floor(pos.Z), lootItem, 1);
        if (health.Current <= amount && !canDropLoot) return false;

        var result = health.Apply(source, amount);
        if (!result.WasApplied) return false;

        foreach (var peer in online)
            if (replicated.Contains((identity.ActorUniqueId, peer.RuntimeId)))
                peer.Session.Protocol.Entity.SendHealth(identity.ActorRuntimeId, health.Current, health.Maximum);

        if (!result.CausedDeath) return true;

        if (!FloorDropFanout.TryDeposit(
                world, players, online, (int)MathF.Floor(pos.X), (int)MathF.Floor(pos.Y), (int)MathF.Floor(pos.Z), lootItem, 1))
            throw new InvalidOperationException($"A prevalidated {actorName} loot drop could not commit.");

        MobKillReward.AwardExperience(source, killExperience, online);

        foreach (var peer in online)
            if (replicated.Remove((identity.ActorUniqueId, peer.RuntimeId)))
            {
                peer.Session.Protocol.Entity.SendRemoveActor(identity.ActorUniqueId);
                onDeathReplicatedToPeer?.Invoke(peer);
            }

        destroyActor(id);
        return true;
    }
}
