using Zenith.Raknet.Log;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

/// <summary>
/// Phase XXIV — the seed is world identity, not a per-restart config knob (see WorldIdentity.cs's
/// doc comment for why this matters: base terrain is never persisted, so a silently changed seed
/// would regenerate every un-visited chunk as a different world next to already-explored terrain).
/// </summary>
public class WorldIdentityTests
{
    private sealed class SilentLogger : ILogger
    {
        public string? LastWarning { get; private set; }
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) => LastWarning = message;
        public void Error(string message) { }
    }

    [Fact]
    public void A_brand_new_world_commits_the_configured_identity()
    {
        var storage = new InMemoryChunkStorage();
        var logger = new SilentLogger();

        var resolved = WorldIdentity.Reconcile(storage, new WorldMetadata("noise", 12345), logger);

        Assert.Equal("noise", resolved.Terrain);
        Assert.Equal(12345, resolved.Seed);
        Assert.Null(logger.LastWarning);

        var committed = storage.GetWorldMetadataAsync().AsTask().GetAwaiter().GetResult();
        Assert.Equal(new WorldMetadata("noise", 12345), committed);
    }

    [Fact]
    public void A_matching_config_on_restart_is_silent()
    {
        var storage = new InMemoryChunkStorage();
        var logger = new SilentLogger();
        _ = WorldIdentity.Reconcile(storage, new WorldMetadata("noise", 42), logger);

        var resolved = WorldIdentity.Reconcile(storage, new WorldMetadata("noise", 42), logger);

        Assert.Equal(new WorldMetadata("noise", 42), resolved);
        Assert.Null(logger.LastWarning);
    }

    /// <summary>The actual bug this closes: a changed seed must never silently take effect on an existing world.</summary>
    [Fact]
    public void A_changed_seed_on_restart_is_rejected_in_favor_of_the_committed_one()
    {
        var storage = new InMemoryChunkStorage();
        var logger = new SilentLogger();
        _ = WorldIdentity.Reconcile(storage, new WorldMetadata("noise", 42), logger);

        var resolved = WorldIdentity.Reconcile(storage, new WorldMetadata("noise", 99), logger);

        Assert.Equal(42, resolved.Seed); // the world's original identity wins, not the new config
        Assert.NotNull(logger.LastWarning);
        Assert.Contains("MISMATCH", logger.LastWarning);
    }

    [Fact]
    public void A_changed_terrain_mode_on_restart_is_also_rejected()
    {
        var storage = new InMemoryChunkStorage();
        var logger = new SilentLogger();
        _ = WorldIdentity.Reconcile(storage, new WorldMetadata("flat", 1), logger);

        var resolved = WorldIdentity.Reconcile(storage, new WorldMetadata("noise", 1), logger);

        Assert.Equal("flat", resolved.Terrain);
        Assert.NotNull(logger.LastWarning);
    }
}
