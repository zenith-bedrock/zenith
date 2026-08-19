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
}
