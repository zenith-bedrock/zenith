using Zenith.Player;
using Zenith.World;

using Zenith.Gameplay.Survival;
using Zenith.Gameplay.Replication;
namespace Zenith.Gameplay.Entities;

/// <summary>
/// A read-only snapshot of exactly the state <see cref="DamageableActorCombatCore"/> needs, cheap
/// for either combat backend to produce: <see cref="GroundMobCombat"/> builds one from an
/// <see cref="IDamageableActor"/> object's own properties; <see cref="DamageableActorCombat"/>
/// builds one from three <c>ComponentStore&lt;T&gt;.TryGet</c> calls. <see cref="Health"/> is a
/// reference to the same authoritative <see cref="HealthState"/> instance either backend
/// already owns — mutating it here (via <see cref="HealthState.Apply"/>) is visible to the
/// owner with no write-back step, since <c>HealthComponent</c> only ever wraps that one shared
/// instance (see <c>docs/ecs.md</c>'s Shared components table).
/// </summary>
internal readonly struct DamageableActorView(
    long entityKey, ulong runtimeId, float x, float y, float z, HealthState health)
{
    /// <summary><c>IDamageableActor.EntityId</c> or <c>ActorIdentity.ActorUniqueId</c> — whichever
    /// key the caller's per-viewer <c>replicated</c> set is keyed on.</summary>
    public long EntityKey { get; } = entityKey;
    public ulong RuntimeId { get; } = runtimeId;
    public float X { get; } = x;
    public float Y { get; } = y;
    public float Z { get; } = z;
    public HealthState Health { get; } = health;
}

/// <summary>
/// The one place the actual damage/loot/XP/replication/destruction sequence lives (Phase XXI shipped
/// this logic twice — once per storage backend — and it stayed byte-for-byte identical except for
/// how a mob's state was read; this is that duplication closed without touching either backend's
/// storage model, which stays split for the documented, evidence-based reasons in
/// <c>docs/ecs.md</c>'s retirement gate). <see cref="GroundMobCombat"/> and
/// <see cref="DamageableActorCombat"/> are thin adapters: build a <see cref="DamageableActorView"/>
/// from their own backend, call this, translate the destroy callback. Neither adapter's public
/// signature changed — every one of the 12 species' call sites is untouched by this.
/// </summary>
internal static class DamageableActorCombatCore
{
    public static bool TryApplyPlayerMeleeAttack(
        DamageableActorView view,
        IReadOnlyList<Player.Player> online,
        float attackDistance,
        float attackDamage,
        ulong currentTick,
        Func<DamageSource, float, IReadOnlyList<Player.Player>, ulong, bool> tryApplyDamage)
    {
        var reachSquared = attackDistance * attackDistance;
        foreach (var player in online)
        {
            if (!player.IsInGame || player.IsDead) continue;
            var dx = view.X - player.PositionX;
            var dz = view.Z - player.PositionZ;
            if (dx * dx + dz * dz > reachSquared) continue;
            if (!player.TryConsumeAttackIntent(checked((long)view.RuntimeId))) continue;
            if (tryApplyDamage(DamageSource.MeleeFrom(player.RuntimeId), attackDamage, online, currentTick)) return true;
        }
        return false;
    }

    public static bool TryApplyDamage(
        DamageableActorView view,
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
        Action destroy,
        Action<Player.Player>? onDeathReplicatedToPeer = null)
    {
        var health = view.Health;
        var result = health.Apply(source, amount, currentTick);
        if (!result.WasApplied) return false;

        foreach (var peer in online)
            if (replicated.Contains((view.EntityKey, peer.RuntimeId)))
            {
                peer.Session.Protocol.Entity.SendHealth(view.RuntimeId, health.Current, health.Maximum);
                if (!result.CausedDeath)
                    peer.Session.Protocol.Entity.SendHurt(view.RuntimeId);
            }

        if (!result.CausedDeath) return true;

        // Loot capacity must never veto a kill (ADR §135): a fatal blow always lands, even if the
        // floor-drop area is saturated (global SoftCap, or every nearby cell already holding a
        // mismatched/full stack) — the loot silently does not drop rather than leaving the mob
        // perpetually just-above-zero HP server-wide until unrelated drops elsewhere despawn.
        // FloorDropStore already logs once when SoftCap refuses a cell (TryAddOrMerge).
        _ = FloorDropFanout.TryDeposit(
            world, players, online, (int)MathF.Floor(view.X), (int)MathF.Floor(view.Y), (int)MathF.Floor(view.Z), lootItem, 1);

        MobKillReward.AwardExperience(source, killExperience, online);

        foreach (var peer in online)
            if (replicated.Remove((view.EntityKey, peer.RuntimeId)))
            {
                peer.Session.Protocol.Entity.SendDeath(view.RuntimeId);
                peer.Session.Protocol.Entity.SendRemoveActor(view.EntityKey);
                onDeathReplicatedToPeer?.Invoke(peer);
            }

        destroy();
        return true;
    }
}
