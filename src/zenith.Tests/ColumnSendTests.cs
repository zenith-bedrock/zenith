using Xunit;
using Zenith.Protocol;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>
/// <see cref="ColumnSend"/>'s actual per-column filtering logic — shared by PreSpawn and
/// ChunkStreamSystem catch-up — had zero direct test coverage before this file.
/// </summary>
public class ColumnSendTests
{
    public ColumnSendTests() => Blocks.EnsureLoaded();

    [Fact]
    public void EmitFloorDropsInColumn_sends_only_drops_whose_chunk_matches_the_requested_one()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("column-drops");
        var drop = StackId.FromBlock(Blocks.Stone);

        // Chunk (0,0) covers blocks 0..15; chunk (1,0) covers 16..31.
        fx.World.FloorDrops.AddOrMerge(5, 64, 5, drop, 1, entityRuntimeIdIfNew: 1001);
        fx.World.FloorDrops.AddOrMerge(20, 64, 5, drop, 1, entityRuntimeIdIfNew: 1002);

        ColumnSend.EmitFloorDropsInColumn(player.Session, fx.World, chunkX: 0, chunkZ: 0);
        player.Session.RakSession.Tick();

        Assert.Single(fx.Transport.Captured);
    }

    [Fact]
    public void EmitFloorDropsInColumn_sends_nothing_for_a_column_with_no_drops()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("empty-column");

        ColumnSend.EmitFloorDropsInColumn(player.Session, fx.World, chunkX: 0, chunkZ: 0);
        player.Session.RakSession.Tick();

        Assert.Empty(fx.Transport.Captured);
    }

    [Fact]
    public void EmitOverlaysToSession_with_no_columns_sends_nothing()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("no-columns");

        ColumnSend.EmitOverlaysToSession(player.Session, fx.World, []);
        player.Session.RakSession.Tick();

        Assert.Empty(fx.Transport.Captured);
    }

    [Fact]
    public void EmitOverlaysToSession_sends_overlays_present_in_the_requested_columns()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("overlay-catchup");
        fx.World.SetBlock(5, 64, 5, Blocks.Stone);

        ColumnSend.EmitOverlaysToSession(player.Session, fx.World, [(0, 0)]);
        player.Session.RakSession.Tick();

        Assert.NotEmpty(fx.Transport.Captured);
    }

    /// <summary>
    /// Regression for a real desync found via a reference-parity audit: a background-streamed
    /// column's overlay list used to be captured once, at read time, and could sit in a bounded
    /// channel/worker queue for a while before actually being sent — a block edited by another
    /// player in that window would never reach this joining client for that cell. EmitToSession
    /// must re-read overlays live at send time, not trust whatever ColumnReadResult.Overlays held
    /// when the column was originally read.
    /// </summary>
    [Fact]
    public async Task EmitToSession_reflects_a_live_edit_that_landed_after_the_column_was_read()
    {
        var fx = new IntentTestFixture();
        var baselinePlayer = fx.AddInGamePlayer("baseline");
        var lateEditPlayer = fx.AddInGamePlayer("late-edit");

        // Baseline: no live edit between read and send — establishes what "just LevelChunk" looks
        // like. Compares total bytes sent, not datagram count: RakNet can batch multiple queued
        // frames into one UDP datagram under the MTU, so an extra UpdateBlock might not add a whole
        // extra datagram, but it always adds bytes.
        var baselineColumn = await fx.World.GetOrCreateColumnAsync(0, 0);
        ColumnSend.EmitToSession(baselinePlayer.Session, baselineColumn, orderChannel: 0);
        baselinePlayer.Session.RakSession.Tick();
        var baselineBytes = BytesSentTo(fx, baselinePlayer);

        // Same read, but this time a live edit lands in the window between the read and the send —
        // exactly the race window a background-streamed join sits in for its outer view radius.
        var column = await fx.World.GetOrCreateColumnAsync(0, 0);
        fx.World.SetBlock(5, 64, 5, Blocks.Stone);

        ColumnSend.EmitToSession(lateEditPlayer.Session, column, orderChannel: 0);
        lateEditPlayer.Session.RakSession.Tick();
        var lateEditBytes = BytesSentTo(fx, lateEditPlayer);

        Assert.True(lateEditBytes > baselineBytes);
    }

    private static int BytesSentTo(IntentTestFixture fx, Zenith.Player.Player player)
    {
        var total = 0;
        foreach (var (endPoint, datagram) in fx.Transport.CapturedByEndpoint)
            if (endPoint.Equals(player.Session.RakSession.EndPoint))
                total += datagram.Length;
        return total;
    }
}
