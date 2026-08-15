using Zenith.Player;

using Zenith.Gameplay.Survival;
namespace Zenith.Gameplay.Entities;

/// <summary>
/// Player-caused mob kill → XP credit (Phase XI.4). Extracted (Phase XII) from the identical copy
/// that had grown independently in <c>ZombieSystem</c> and <c>SkeletonSystem</c> — two concrete
/// mob systems, one small shared step, not a mob/loot framework.
/// </summary>
static class MobKillReward
{
    /// <summary>
    /// A kill's <see cref="DamageSource"/> carries the attacker's runtime id for both the melee
    /// and projectile paths; an unattributed cause (fall, void, ...) simply grants nothing.
    /// </summary>
    public static void AwardExperience(DamageSource source, int amount, IReadOnlyList<Player.Player> online)
    {
        if (source.OwnerRuntimeId is not { } ownerRuntimeId) return;
        foreach (var candidate in online)
        {
            if (candidate.RuntimeId != ownerRuntimeId) continue;
            candidate.AddExperience(amount);
            candidate.Session.Protocol.Entity.SendPlayerAttributes(candidate);
            break;
        }
    }
}
