using Zenith.Raknet.Log;

namespace Zenith.World;

/// <summary>
/// Reconciles configured (<c>zenith.yml</c>) generator identity against what a world was actually
/// created with (Phase XXIV). The seed is world identity, not a per-restart config knob: base
/// terrain is never persisted (regenerated from <see cref="ITerrainProvider"/> on every chunk miss —
/// ADR §45), so if <c>world.seed</c>/<c>world.terrain</c> silently changed between restarts, every
/// not-yet-visited chunk would regenerate as a DIFFERENT world sitting right next to already-explored,
/// still-persisted-overlay terrain from the old one. A real, evidenced corruption risk this phase
/// found, not a hypothetical one — see docs/world-generation.md.
/// </summary>
static class WorldIdentity
{
    /// <summary>
    /// On a brand-new world (no metadata committed yet), <paramref name="configured"/> becomes that
    /// world's permanent identity and is persisted immediately. On every later start, the committed
    /// identity wins over config — a mismatch is a misconfiguration to warn about, not a silent
    /// world mutation to apply. Returns the identity the caller should actually build the terrain
    /// provider from (never <paramref name="configured"/> itself when it disagreed).
    /// </summary>
    public static WorldMetadata Reconcile(IChunkStorage storage, WorldMetadata configured, ILogger logger)
    {
        var committed = storage.GetWorldMetadataAsync().AsTask().GetAwaiter().GetResult();
        if (committed is null)
        {
            storage.PutWorldMetadataAsync(configured).AsTask().GetAwaiter().GetResult();
            return configured;
        }

        if (committed.Value == configured)
            return configured;

        logger.Warning(
            $"*** WORLD IDENTITY MISMATCH: this world was created with terrain='{committed.Value.Terrain}' " +
            $"seed={committed.Value.Seed}, but zenith.yml now says terrain='{configured.Terrain}' " +
            $"seed={configured.Seed}. Keeping the world's original identity — un-visited chunks would " +
            "otherwise generate as a different world next to already-explored terrain. Update zenith.yml " +
            "to match, or start a new world.path/world.name if a different world was actually intended.");
        return committed.Value;
    }
}
