using Zenith.Session;
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
        fx.World.FloorDrops.TryAddOrMerge(5, 64, 5, drop, 1, entityRuntimeIdIfNew: 1001, out _);
        fx.World.FloorDrops.TryAddOrMerge(20, 64, 5, drop, 1, entityRuntimeIdIfNew: 1002, out _);

        ColumnSend.EmitFloorDropsInColumn(player.Session, fx.World, chunkX: 0, chunkZ: 0);
        player.Session.Transport.FlushOutgoing();

        Assert.Single(fx.Transport.Captured);
    }

    [Fact]
    public void EmitFloorDropsInColumn_sends_nothing_for_a_column_with_no_drops()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("empty-column");

        ColumnSend.EmitFloorDropsInColumn(player.Session, fx.World, chunkX: 0, chunkZ: 0);
        player.Session.Transport.FlushOutgoing();

        Assert.Empty(fx.Transport.Captured);
    }

    [Fact]
    public void ResyncColumns_with_no_columns_sends_nothing()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("no-columns");

        ColumnSend.ResyncColumns(player.Session, fx.World, []);
        player.Session.Transport.FlushOutgoing();

        Assert.Empty(fx.Transport.Captured);
    }

    [Fact]
    public async Task ResyncColumns_resends_a_column_edited_after_it_was_sent_and_only_that_one()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("resync");
        var column = await fx.World.GetOrCreateColumnAsync(0, 0);
        ColumnSend.EmitToSession(player.Session, column, orderChannel: 0);
        player.Session.Transport.FlushOutgoing();
        var afterFirstSend = BytesSentTo(fx, player);

        ColumnSend.ResyncColumns(player.Session, fx.World, [(0, 0)]);
        player.Session.Transport.FlushOutgoing();
        Assert.Equal(afterFirstSend, BytesSentTo(fx, player)); // unchanged since it was sent: nothing to repeat

        fx.World.SetBlock(5, 64, 5, Blocks.Stone);
        ColumnSend.ResyncColumns(player.Session, fx.World, [(0, 0)]);
        player.Session.Transport.FlushOutgoing();
        Assert.True(BytesSentTo(fx, player) > afterFirstSend);
    }

    /// <summary>
    /// A client must never receive a column older than the world: the LevelChunk is serialised from the live sections
    /// at send time, so an edit made after the column was read is already in it (and in its edit version).
    /// </summary>
    [Fact]
    public async Task A_column_sent_after_an_edit_carries_the_edit()
    {
        var fx = new IntentTestFixture();
        var column = await fx.World.GetOrCreateColumnAsync(0, 0);
        var before = column.GetWire();

        fx.World.SetBlock(5, 64, 5, Blocks.Stone);
        var after = column.GetWire();

        Assert.NotEqual(before.Payload, after.Payload);
        Assert.True(after.Version > before.Version);
    }

    private static int BytesSentTo(IntentTestFixture fx, Zenith.Player.Player player)
    {
        var total = 0;
        foreach (var (endPoint, datagram) in fx.Transport.CapturedByEndpoint)
            if (endPoint.Equals(player.Session.RakSession().EndPoint))
                total += datagram.Length;
        return total;
    }
}
