using System.Collections.Concurrent;
using System.Net;
using Zenith.Ecs;
using Zenith.Event;
using Zenith.Gameplay.Runtime;
using Zenith.Packets;
using Zenith.Protocol;
using Zenith.Session;
using Zenith.Session.Handler;
using Zenith.Player;
using Zenith.Raknet;
using Zenith.Logging;
using Zenith.Raknet.Network;
using Zenith.Binary;
using Zenith.Server;
using Zenith.World;
using Xunit;
using Zenith.Gameplay.Entities;
using Zenith.Gameplay.Inventory;
using Zenith.Gameplay.Replication;
using Zenith.Gameplay.Survival;
using Zenith.Gameplay.WorldInteraction;
using static Zenith.Tests.IntentTestHelpers;

namespace Zenith.Tests;

/// <summary>ChunkStreamSystem: pre-spawn and in-game streaming publishes only through the tick.</summary>
public class ChunkStreamIntentTests
{

    [Fact]
    public void ChunkStreamSystem_overlay_resync_on_NeedsOverlayResync()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("resync");
        player.Chunks.Radius = 1;
        player.Chunks.RememberMany([(0, 0)]);
        fx.World.SetBlock(3, 64, 3, Blocks.OakPlanks);
        player.Chunks.NeedsOverlayResync = true;

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        new ChunkStreamSystem(fx.World).Tick(fx.Clock, fx.Players.Online);
        FlushRaknet(fx.Players);

        Assert.False(player.Chunks.NeedsOverlayResync);
        Assert.True(fx.Transport.Captured.Count >= 1,
            "NeedsOverlayResync must emit UpdateBlock for known-column overlays");
    }


    [Fact]
    public void ChunkStreamSystem_floor_drop_catchup_on_NeedsOverlayResync()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("dropresync");
        player.Chunks.Radius = 1;
        player.Chunks.RememberMany([(0, 0)]);
        Assert.True(fx.World.FloorDrops.TryAddOrMerge(
            3, 64, 3, StackId.FromBlock(Blocks.Dirt), 2, entityRuntimeIdIfNew: 77, out _));
        player.Chunks.NeedsOverlayResync = true;

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        new ChunkStreamSystem(fx.World).Tick(fx.Clock, fx.Players.Online);
        FlushRaknet(fx.Players);

        Assert.False(player.Chunks.NeedsOverlayResync);
        Assert.Equal(1, fx.World.FloorDrops.Count);
        Assert.True(fx.Transport.Captured.Count >= 1,
            "NeedsOverlayResync must emit AddItemActor for floor drops in known columns");
    }


    [Fact]
    public void ChunkStreamSystem_streams_while_IsSpawning_without_IsInGame()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("spawning");
        player.IsInGame = false;
        player.IsSpawning = true;
        player.Chunks.Radius = 0;
        player.PositionX = 0.5f;
        player.PositionY = 64f;
        player.PositionZ = 0.5f;
        _ = player.Chunks.PublisherCenterChanged(0, 0);

        new ChunkStreamSystem(fx.World).Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.Chunks.Knows(0, 0),
            "IsSpawning must allow ChunkStream to begin columns before IsInGame (ADR §70)");
    }


    [Fact]
    public void ChunkStreamSystem_skips_when_neither_in_game_nor_spawning()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("waiting");
        player.IsInGame = false;
        player.IsSpawning = false;
        player.Chunks.Radius = 0;
        player.PositionX = 0.5f;
        player.PositionZ = 0.5f;
        _ = player.Chunks.PublisherCenterChanged(0, 0);

        new ChunkStreamSystem(fx.World).Tick(fx.Clock, fx.Players.Online);

        Assert.False(player.Chunks.Knows(0, 0));
    }


    [Fact]
    public async Task ChunkStreamSystem_emits_completed_stream_only_when_tick_drains_it()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("stream-result");
        player.Chunks.Radius = -1; // This test controls the only stream slot explicitly.
        var column = await fx.World.GetOrCreateColumnAsync(0, 0);
        Assert.True(player.Chunks.TryBegin(0, 0, out var epoch));

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        player.Chunks.CompleteStream(0, 0, epoch, column);
        FlushRaknet(fx.Players);
        Assert.Empty(fx.Transport.Captured);

        new ChunkStreamSystem(fx.World).Tick(fx.Clock, fx.Players.Online);
        Assert.True(SpinWait.SpinUntil(
            () =>
            {
                FlushRaknet(fx.Players);
                return fx.Transport.Captured.Count > 0;
            },
            TimeSpan.FromSeconds(60)));
        FlushRaknet(fx.Players);
        Assert.NotEmpty(fx.Transport.Captured);
    }


    [Fact]
    public async Task ChunkStreamSystem_abandons_completed_stream_when_player_leaves_before_tick()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("stream-leave");
        player.Chunks.Radius = -1;
        var column = await fx.World.GetOrCreateColumnAsync(0, 0);
        Assert.True(player.Chunks.TryBegin(0, 0, out var epoch));
        player.Chunks.CompleteStream(0, 0, epoch, column);
        player.IsInGame = false;
        player.IsSpawning = false;

        new ChunkStreamSystem(fx.World).Tick(fx.Clock, fx.Players.Online);

        Assert.False(player.Chunks.Knows(0, 0));
    }


    /// <summary>
    /// Phase XXVI post-impl audit: this used to drive the legacy bulk `CompletePreSpawn` shortcut,
    /// which is dead in production since `StartPendingPreSpawn` always goes through the streaming
    /// `PreSpawnLoad` path now. Rewritten to submit a real pre-spawn request and prove publication
    /// still only happens through `Tick` (via `PumpStreamingPreSpawn`), not synchronously at submit.
    /// </summary>
    [Fact]
    public void ChunkStreamSystem_streaming_pre_spawn_publishes_only_through_tick()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddPlayer("pre-spawn", isInGame: false);
        Assert.True(player.Chunks.TrySubmitPreSpawn(viewRadius: 0));

        Assert.False(player.IsSpawning);
        Assert.False(player.Chunks.Knows(0, 0));
        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        var system = new ChunkStreamSystem(fx.World);
        Assert.True(SpinWait.SpinUntil(() =>
        {
            FlushRaknet(fx.Players);
            system.Tick(fx.Clock, fx.Players.Online);
            return player.IsSpawning;
        }, TimeSpan.FromSeconds(60)));

        Assert.True(player.Chunks.Knows(0, 0));
        FlushRaknet(fx.Players);
        Assert.NotEmpty(fx.Transport.Captured);
    }


    /// <summary>
    /// Phase XXVI post-impl audit: the original version of this test also drove the dead bulk
    /// `CompletePreSpawn` path, so it never actually exercised `PumpStreamingPreSpawn`'s own
    /// per-tick publish budget — the entire point of ADR §111's "never stall a tick" guarantee.
    /// Rewritten to submit a real, oversized pre-spawn request and assert the known-column count
    /// never grows by more than the configured per-tick budget in any single tick.
    /// </summary>
    [Fact]
    public void ChunkStreamSystem_bounds_streaming_pre_spawn_publication_per_tick()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddPlayer("large-pre-spawn", isInGame: false);
        const int viewRadius = 3; // 49 columns, comfortably above the per-tick budget
        Assert.True(player.Chunks.TrySubmitPreSpawn(viewRadius));

        var system = new ChunkStreamSystem(fx.World);
        var fullViewCount = (viewRadius * 2 + 1) * (viewRadius * 2 + 1);
        var budget = Math.Min(
            ChunkStreamSystem.MaxPreSpawnColumnsPerTick,
            fx.Context.Config.World.PreSpawnColumnsPerTick);

        var known = new List<(int X, int Z)>();
        var previousCount = 0;
        var sawAnyPublish = false;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (previousCount < fullViewCount && DateTime.UtcNow < deadline)
        {
            FlushRaknet(fx.Players);
            system.Tick(fx.Clock, fx.Players.Online);
            player.Chunks.CopyKnown(known);
            var delta = known.Count - previousCount;
            Assert.True(delta <= budget, $"tick published {delta} columns, budget is {budget}");
            if (delta > 0) sawAnyPublish = true;
            previousCount = known.Count;
            if (delta == 0) Thread.Yield();
        }

        Assert.True(sawAnyPublish, "expected at least one tick to publish columns within the budget");
        Assert.Equal(fullViewCount, previousCount);
    }


    /// <summary>
    /// ADR §114 — residency for the whole pre-spawn square must be acquired synchronously in the
    /// same tick the request is consumed, before StreamRadiusAsync's hydration even starts.
    /// Otherwise a same-tick eviction sweep could evict a chunk mid-join (hydrated, zero viewers)
    /// before RememberMany ever gets a chance to acquire it later, column by column.
    /// </summary>
    [Fact]
    public void ChunkStreamSystem_acquires_residency_for_the_whole_pre_spawn_square_before_streaming_starts()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddPlayer("pre-spawn-residency", isInGame: false);
        const int viewRadius = 2;
        Assert.True(player.Chunks.TrySubmitPreSpawn(viewRadius));

        var system = new ChunkStreamSystem(fx.World);
        FlushRaknet(fx.Players);
        system.Tick(fx.Clock, fx.Players.Online); // consumes the pre-spawn request this tick

        for (var x = -viewRadius; x <= viewRadius; x++)
        for (var z = -viewRadius; z <= viewRadius; z++)
            Assert.True(fx.World.ChunkResidency.HasViewers(x, z), $"chunk ({x},{z}) should have a viewer");
    }


    [Fact]
    public void ChunkStreamSystem_releases_spawn_after_ready_radius_before_full_view_is_loaded()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddPlayer("streaming-pre-spawn", isInGame: false);
        const int viewRadius = 4;
        Assert.True(player.Chunks.TrySubmitPreSpawn(viewRadius));

        var system = new ChunkStreamSystem(fx.World);
        var fullViewCount = (viewRadius * 2 + 1) * (viewRadius * 2 + 1);
        var known = new List<(int X, int Z)>();
        var readyObserved = SpinWait.SpinUntil(() =>
        {
            FlushRaknet(fx.Players);
            system.Tick(fx.Clock, fx.Players.Online);
            player.Chunks.CopyKnown(known);
            return player.IsSpawning;
        }, TimeSpan.FromSeconds(60));

        Assert.True(readyObserved, "spawn must be released after the central ready radius is transmitted");
        player.Chunks.CopyKnown(known);
        Assert.InRange(known.Count, 1, fullViewCount - 1);
        Assert.False(player.IsInGame);
    }


    [Fact]
    public void ChunkStreamSystem_applies_spawn_ready_only_while_spawn_is_current()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddPlayer("spawn-ready", isInGame: false);
        player.IsSpawning = true;
        player.Intents.SubmitSpawnReady();

        new ChunkStreamSystem(fx.World).Tick(fx.Clock, fx.Players.Online);

        Assert.True(player.IsInGame);
        Assert.False(player.IsSpawning);
    }


    [Fact]
    public void ChunkStreamSystem_discards_late_spawn_ready_after_spawn_is_no_longer_current()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddPlayer("stale-spawn-ready", isInGame: false);
        player.Intents.SubmitSpawnReady();

        new ChunkStreamSystem(fx.World).Tick(fx.Clock, fx.Players.Online);

        Assert.False(player.IsInGame);
        Assert.False(player.IsSpawning);
        Assert.False(player.Intents.TryConsumeSpawnReady());
    }
}
