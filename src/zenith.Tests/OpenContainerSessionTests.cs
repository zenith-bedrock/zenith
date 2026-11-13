using Zenith.Player;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

public class OpenContainerSessionTests
{
    [Fact]
    public void Stale_close_does_not_clear_the_active_container()
    {
        var player = new Player.Player("viewer", session: null!, runtimeId: 1, uuid: Guid.NewGuid());
        var opened = player.Windows.OpenChestContainer(2, 0, OpenChestView.Single(1, 64, 1));

        Assert.False(player.Windows.TryCloseContainer(windowId: 0, windowType: 0xff, out _));
        Assert.Equal(opened, player.Windows.OpenContainer);
        Assert.True(player.Windows.OpenChest.HasValue);

        Assert.True(player.Windows.TryCloseContainer(windowId: 2, windowType: 0, out var closed));
        Assert.Equal(opened, closed);
        Assert.Null(player.Windows.OpenContainer);
    }

    [Fact]
    public void Every_open_receives_a_new_generation_even_when_reopening_the_same_target()
    {
        var player = new Player.Player("viewer", session: null!, runtimeId: 1, uuid: Guid.NewGuid());
        var first = player.Windows.OpenPlayerContainer(0, 0xff);
        var second = player.Windows.OpenPlayerContainer(0, 0xff);

        Assert.True(second.Generation > first.Generation);
        Assert.Equal(OpenContainerSession.TargetKind.PlayerInventory, second.Target);
    }
}
