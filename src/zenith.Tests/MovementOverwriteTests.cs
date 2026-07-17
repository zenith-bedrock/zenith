using Zenith.Player;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

public class MovementOverwriteTests
{
    public MovementOverwriteTests() => Blocks.EnsureLoaded();

    [Fact]
    public void SubmitMovementInput_overwrites_latest_not_fifo()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("mover");

        player.SubmitMovementInput(MovementInputState.From(1f, 2f, 3f, pitch: 0f, yaw: 0f));
        player.SubmitMovementInput(MovementInputState.From(10f, 20f, 30f, pitch: 5f, yaw: 90f));

        Assert.True(player.TryConsumeMovementInput(out var input));
        Assert.Equal(10f, input.X);
        Assert.Equal(20f, input.Y);
        Assert.Equal(30f, input.Z);
        Assert.Equal(5f, input.Pitch);
        Assert.Equal(90f, input.Yaw);
        Assert.False(player.TryConsumeMovementInput(out _));
    }

    [Fact]
    public void SubmitBlockEdit_fifo_rejects_newest_when_full()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("builder");

        for (var i = 0; i < global::Zenith.Player.Player.MaxPendingBlockEdits; i++)
            Assert.True(player.SubmitBlockEdit(BlockEditIntent.Set(i, 64, 0, Blocks.Dirt, hotbarSlot: 0)));

        Assert.False(player.SubmitBlockEdit(BlockEditIntent.Set(99, 64, 0, Blocks.Dirt, hotbarSlot: 0)));

        Assert.True(player.TryConsumeBlockEdit(out var first));
        Assert.Equal(0, first.X);
    }
}
