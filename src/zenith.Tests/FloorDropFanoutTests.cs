using Zenith.Gameplay;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

public class FloorDropFanoutTests
{
    public FloorDropFanoutTests() => Blocks.EnsureLoaded();

    [Fact]
    public void TryDeposit_lands_on_origin_when_free()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("dropper");

        var ok = FloorDropFanout.TryDeposit(
            fx.World, fx.Players, fx.Players.Online, 10, 64, 10, StackId.FromBlock(Blocks.Dirt), 3);

        Assert.True(ok);
        Assert.True(fx.World.FloorDrops.TryTake(10, 64, 10, out var id, out var count, out _));
        Assert.Equal(StackId.FromBlock(Blocks.Dirt), id);
        Assert.Equal(3, count);
        _ = player;
    }

    [Fact]
    public void TryDeposit_spirals_to_a_free_cell_when_origin_holds_a_different_stack()
    {
        var fx = new IntentTestFixture();
        _ = fx.AddInGamePlayer("dropper");

        Assert.True(fx.World.FloorDrops.TryAddOrMerge(
            20, 64, 20, StackId.FromBlock(Blocks.Stone), 1, entityRuntimeIdIfNew: 1, out _));

        var ok = FloorDropFanout.TryDeposit(
            fx.World, fx.Players, fx.Players.Online, 20, 64, 20, StackId.FromBlock(Blocks.Dirt), 1, searchRadius: 1);

        Assert.True(ok);
        // Origin cell still holds the original Stone — Dirt landed on a neighboring cell.
        Assert.True(fx.World.FloorDrops.TryTake(20, 64, 20, out var originId, out _, out _));
        Assert.Equal(StackId.FromBlock(Blocks.Stone), originId);

        var foundNeighbor = false;
        for (var dx = -1; dx <= 1 && !foundNeighbor; dx++)
        for (var dz = -1; dz <= 1 && !foundNeighbor; dz++)
        {
            if (dx == 0 && dz == 0) continue;
            if (fx.World.FloorDrops.TryTake(20 + dx, 64, 20 + dz, out var id, out _, out _))
            {
                Assert.Equal(StackId.FromBlock(Blocks.Dirt), id);
                foundNeighbor = true;
            }
        }
        Assert.True(foundNeighbor, "expected the Dirt stack to land on a neighboring cell within radius 1");
    }

    [Fact]
    public void TryDeposit_returns_false_when_no_free_cell_within_radius()
    {
        var fx = new IntentTestFixture();
        _ = fx.AddInGamePlayer("dropper");

        // Fill origin + every cell within radius 1 with a different id so nothing can merge.
        for (var dx = -1; dx <= 1; dx++)
        for (var dz = -1; dz <= 1; dz++)
            Assert.True(fx.World.FloorDrops.TryAddOrMerge(
                30 + dx, 64, 30 + dz, StackId.FromBlock(Blocks.Stone), 1, entityRuntimeIdIfNew: 1, out _));

        var ok = FloorDropFanout.TryDeposit(
            fx.World, fx.Players, fx.Players.Online, 30, 64, 30, StackId.FromBlock(Blocks.Dirt), 1, searchRadius: 1);

        Assert.False(ok);
    }

    [Fact]
    public void TryDeposit_publishes_AddItemActor_to_online_peers()
    {
        var fx = new IntentTestFixture();
        var a = fx.AddInGamePlayer("alice");
        _ = fx.AddInGamePlayer("bob");

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        FloorDropFanout.TryDeposit(
            fx.World, fx.Players, fx.Players.Online, 40, 64, 40, StackId.FromBlock(Blocks.Dirt), 1);
        FlushRaknet(fx.Players);

        Assert.True(fx.Transport.Captured.Count >= 2, $"expected AddItemActor to reach both peers, got {fx.Transport.Captured.Count}");
        _ = a;
    }

    [Fact]
    public void TryDeposit_zero_count_or_empty_id_is_a_no_op_success()
    {
        var fx = new IntentTestFixture();
        _ = fx.AddInGamePlayer("dropper");

        Assert.True(FloorDropFanout.TryDeposit(
            fx.World, fx.Players, fx.Players.Online, 50, 64, 50, StackId.FromBlock(Blocks.Dirt), 0));
        Assert.False(fx.World.FloorDrops.TryTake(50, 64, 50, out _, out _, out _));
    }

    private static void FlushRaknet(Zenith.Player.PlayerManager players)
    {
        foreach (var p in players.Online)
            p.Session.RakSession.Tick();
    }
}
