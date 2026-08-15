namespace Zenith.Gameplay.Entities;

/// <summary>
/// Phase XX, Priority 4 — extracted after a dedicated side-by-side audit of the twelve
/// near-identical viewer-reconciliation loops living in every ActorInterest-replicated system
/// (Zombie, Skeleton, Cow, Creeper, Enderman, Projectile, Bat, Spider, Villager, Golem, Minecart,
/// Fish). Every one of them was byte-for-byte the same seven-step shape — relevance check,
/// remove-and-notify on exit, add-and-notify on enter — differing only in what "notify" sends.
/// That crossed this project's own updated evidence bar (see
/// docs/history/phases/phase-xx-runtime-relationships-findings.md, Priority 4): eleven-then-twelve
/// byte-identical copies is itself meaningful maintenance evidence, even with zero measured drift
/// bugs.
///
/// Deliberately minimal: no type parameter, no interface, no gameplay knowledge. It does not know
/// what a "mob" or an "actor" is — only an <c>entityId</c> for the replicated-key shape and two
/// caller-supplied projection callbacks. It does not decide interest policy (<paramref
/// name="isRelevant"/> is supplied by the caller — <c>ActorInterest.Includes</c> for every current
/// consumer, but nothing here assumes that). This is intentionally NOT a
/// <c>ReplicationSystem</c>/<c>ReplicationComponent</c>: it owns membership bookkeeping only, never
/// spawning, damage, AI, or any gameplay decision.
/// </summary>
static class ViewerReconciliation
{
    /// <summary>
    /// Diffs one actor's viewer set against <paramref name="online"/> for one tick.
    /// <paramref name="onEnter"/> fires the first tick a peer becomes relevant (send spawn/health/
    /// whatever else that consumer needs); <paramref name="onExit"/> fires the tick a
    /// previously-relevant peer stops being relevant (send removal, clear any per-peer cache).
    /// Both run at most once per peer per call, matching every hand-written copy this replaced.
    /// </summary>
    public static void Sync(
        long entityId,
        IReadOnlyList<Player.Player> online,
        HashSet<(long EntityId, long PlayerId)> replicated,
        Func<Player.Player, bool> isRelevant,
        Action<Player.Player> onEnter,
        Action<Player.Player> onExit)
    {
        foreach (var peer in online)
        {
            var key = (entityId, peer.RuntimeId);
            if (!isRelevant(peer))
            {
                if (replicated.Remove(key))
                    onExit(peer);
                continue;
            }
            if (!replicated.Add(key)) continue;
            onEnter(peer);
        }
    }
}
