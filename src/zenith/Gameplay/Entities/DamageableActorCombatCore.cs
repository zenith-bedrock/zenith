using Zenith.Gameplay.Loot;
using Zenith.Player;
using Zenith.World;
using Zenith.Geometry;

using Zenith.Gameplay.Survival;
using Zenith.Gameplay.Replication;
namespace Zenith.Gameplay.Entities;

/// <summary>
/// A read-only snapshot of exactly the state <see cref="DamageableActorCombatCore"/> needs, cheap
/// to produce: <see cref="DamageableActorCombat"/> builds one from three <c>ComponentStore&lt;T&gt;.TryGet</c> calls.
/// <see cref="Health"/> is a reference to the same authoritative <see cref="HealthState"/> instance the actor
/// already owns — mutating it here (via <see cref="HealthState.Apply"/>) is visible to the
/// owner with no write-back step, since <c>HealthComponent</c> only ever wraps that one shared
/// instance (see <c>docs/ecs.md</c>'s Shared components table).
/// </summary>
internal readonly struct DamageableActorView(
    long entityKey, ulong runtimeId, float x, float y, float z, HealthState health)
{
    /// <summary><c>ActorIdentity.ActorUniqueId</c>: the key the caller's per-viewer
    /// <c>replicated</c> set is keyed on.</summary>
    public long EntityKey { get; } = entityKey;
    public ulong RuntimeId { get; } = runtimeId;
    public float X { get; } = x;
    public float Y { get; } = y;
    public float Z { get; } = z;
    public HealthState Health { get; } = health;
}

/// <summary>
/// The one place the actual damage/loot/XP/replication/destruction sequence lives (it used to exist
/// once per storage backend; every species is an ECS actor now, so there is one backend).
/// <see cref="DamageableActorCombat"/> is the adapter: it builds a <see cref="DamageableActorView"/>
/// from the components, calls this, and translates the destroy callback.
/// </summary>
internal static class DamageableActorCombatCore
{
    public static bool TryApplyPlayerMeleeAttack(
        DamageableActorView view,
        IReadOnlyList<Player.Player> online,
        MobDefinition definition,
        ItemPalette itemPalette,
        ulong currentTick,
        Func<DamageSource, float, IReadOnlyList<Player.Player>, ulong, bool> tryApplyDamage)
    {
        var (width, height) = definition.Dimensions;
        var bounds = Aabb.FromCenterSize(view.X, view.Y, view.Z, width, height);
        foreach (var player in online)
        {
            var reach = EntityHitboxes.MeleeReach(player.GameMode);
            var reachSquared = reach * reach;
            var inReach = bounds.DistanceSquaredTo(player.PositionX, player.PositionY + Blocks.PlayerEyeHeight,
                player.PositionZ) <= reachSquared;
            if (!player.Intents.TryConsumeAttackIntent(checked((long)view.RuntimeId), allowUntargeted: inReach)) continue;
            if (!player.IsInGame || player.IsDead || !inReach) continue;
            if (tryApplyDamage(DamageSource.MeleeFrom(player.RuntimeId), PlayerMeleeDamage.Resolve(player, itemPalette), online, currentTick)) return true;
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
        LootTable lootTable,
        Random random,
        int killExperience,
        ItemPalette itemPalette,
        string actorName,
        ulong currentTick,
        Action destroy,
        Action<Player.Player>? onDeathReplicatedToPeer = null,
        int deathAnimationTicks = 0,
        Action<long[]>? leaveCorpse = null)
    {
        var health = view.Health;
        var result = health.Apply(source, amount, currentTick);
        if (!result.WasApplied) return false;

        foreach (var peer in online)
            if (replicated.Contains((view.EntityKey, peer.RuntimeId)))
            {
                peer.Client.Entity.SendHealth(view.RuntimeId, health.Current, health.Maximum);
                if (!result.CausedDeath)
                    peer.Client.Entity.SendHurt(view.RuntimeId);
            }

        if (!result.CausedDeath) return true;

        // Loot capacity must never veto a kill (ADR §135): a fatal blow always lands, even if the
        // floor-drop area is saturated (global SoftCap, or every nearby cell already holding a
        // mismatched/full stack) — the loot silently does not drop rather than leaving the mob
        // perpetually just-above-zero HP server-wide until unrelated drops elsewhere despawn.
        // FloorDropStore already logs once when SoftCap refuses a cell (TryAddOrMerge).
        MobLoot.Drop(lootTable, MobLoot.ContextOf(source, online), random, world, players, online, view.X, view.Y, view.Z, itemPalette);

        MobKillReward.DropExperience(source, killExperience, online, world, players, random, view.X, view.Y, view.Z);

        // With an animation, peers keep the actor for deathAnimationTicks and the corpse entity removes it then
        // (DeathAnimationSystem); without one it goes at once.
        var animate = deathAnimationTicks > 0 && leaveCorpse is not null;
        List<long>? witnesses = null;
        foreach (var peer in online)
            if (replicated.Remove((view.EntityKey, peer.RuntimeId)))
            {
                peer.Client.Entity.SendDeath(view.RuntimeId);
                if (animate) (witnesses ??= []).Add(peer.RuntimeId);
                else peer.Client.Entity.SendRemoveActor(view.EntityKey);
                onDeathReplicatedToPeer?.Invoke(peer);
            }

        if (witnesses is not null) leaveCorpse!([.. witnesses]);

        destroy();
        return true;
    }
}
