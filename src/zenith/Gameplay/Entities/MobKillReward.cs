using Zenith.Player;

using Zenith.Gameplay.Survival;
using Zenith.Gameplay.WorldInteraction;
using Zenith.World;
namespace Zenith.Gameplay.Entities;

/// <summary>
/// Player-caused mob kill → experience orbs (Phase XI.4, orbs in plan 003 phase A). Extracted (Phase XII) from the identical copy
/// that had grown independently in <c>ZombieSystem</c> and <c>SkeletonSystem</c> — two concrete
/// mob systems, one small shared step, not a mob/loot framework.
/// </summary>
static class MobKillReward
{
    /// <summary>
    /// A kill's <see cref="DamageSource"/> carries the attacker's runtime id for both the melee and projectile paths; an unattributed
    /// cause (fall, void, ...) simply grants nothing. The points leave the mob as orbs, split into the vanilla sizes, popping out with
    /// a small random velocity; whoever touches them collects them (<see cref="ExperienceOrbSystem"/>). Orbs the store has no room for
    /// are credited to the killer at once, so a full store never loses experience.
    /// </summary>
    public static void DropExperience(
        DamageSource source, int amount, IReadOnlyList<Player.Player> online, World.World world, PlayerManager players,
        Random random, float x, float y, float z)
    {
        if (amount <= 0) return;
        if (source.OwnerRuntimeId is not { } ownerRuntimeId) return;

        Player.Player? killer = null;
        foreach (var candidate in online)
            if (candidate.RuntimeId == ownerRuntimeId) { killer = candidate; break; }
        if (killer is null) return;
        Grant(killer, amount, online, world, players, random, x, y, z);
    }

    /// <summary>Gives <paramref name="amount"/> points to <paramref name="recipient"/> as orbs at the spot (any source: a kill, a mined ore).</summary>
    public static void Grant(
        Player.Player recipient, int amount, IReadOnlyList<Player.Player> online, World.World world, PlayerManager players,
        Random random, float x, float y, float z)
    {
        if (amount <= 0) return;
        var killer = recipient;
        var credited = 0;
        foreach (var points in ExperienceOrbSizes.Split(amount))
        {
            var pose = new FloorDropMotion(
                x, y, z,
                (random.NextSingle() * 0.2f - 0.1f) * 2f, random.NextSingle() * 0.4f, (random.NextSingle() * 0.2f - 0.1f) * 2f);
            var orb = new ExperienceOrbEntry { EntityId = players.AllocateRuntimeId(), Value = points, Pose = pose };
            if (!world.ExperienceOrbs.TrySpawn(orb))
            {
                credited += points;
                continue;
            }

            ExperienceOrbSystem.Announce(online, orb);
        }

        if (credited > 0)
        {
            killer.AddExperience(credited);
            killer.Client.Entity.SendPlayerAttributes(killer);
        }
    }
}
