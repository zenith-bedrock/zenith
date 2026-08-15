using Xunit;
using Zenith.Player;

namespace Zenith.Tests;

public sealed class PlayerManagerRuntimeIdIndexTests
{
    [Fact]
    public void GetByRuntimeId_resolves_a_player_added_via_TryAdd()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("indexed");

        Assert.Same(player, fx.Players.GetByRuntimeId(player.RuntimeId));
    }

    [Fact]
    public void GetByRuntimeId_returns_null_after_Remove()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("removed");
        var runtimeId = player.RuntimeId;

        fx.Players.Remove(player);

        Assert.Null(fx.Players.GetByRuntimeId(runtimeId));
    }

    [Fact]
    public void GetByRuntimeId_returns_null_for_an_id_never_assigned_to_a_player()
    {
        var fx = new IntentTestFixture();
        var mobRuntimeId = fx.Players.AllocateRuntimeId();

        Assert.Null(fx.Players.GetByRuntimeId(mobRuntimeId));
    }
}
