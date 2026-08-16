using Xunit;
using Zenith.Gameplay.Entities;

namespace Zenith.Tests;

public sealed class PlayerSpatialIndexTests
{
    [Fact]
    public void Rebuild_indexes_every_online_player_by_current_position()
    {
        var fx = new IntentTestFixture();
        var near = fx.AddInGamePlayer("near");
        var far = fx.AddInGamePlayer("far");
        far.PositionX = 500f;
        var index = new PlayerSpatialIndex();

        index.Rebuild(fx.Players.Online);

        Assert.Contains(near, Collect(index, near.PositionX, near.PositionZ, radius: 1f));
        Assert.DoesNotContain(far, Collect(index, near.PositionX, near.PositionZ, radius: 1f));
    }

    [Fact]
    public void Rebuild_reflects_a_position_changed_since_the_previous_rebuild()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("mover");
        var index = new PlayerSpatialIndex();
        index.Rebuild(fx.Players.Online);
        Assert.Contains(player, Collect(index, player.PositionX, player.PositionZ, radius: 1f));

        player.PositionX += 100f;
        index.Rebuild(fx.Players.Online);

        Assert.DoesNotContain(player, Collect(index, 0f, 0f, radius: 1f));
        Assert.Contains(player, Collect(index, player.PositionX, player.PositionZ, radius: 1f));
    }

    [Fact]
    public void Rebuild_drops_a_player_who_is_no_longer_online()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("leaving");
        var index = new PlayerSpatialIndex();
        index.Rebuild(fx.Players.Online);
        Assert.Contains(player, Collect(index, player.PositionX, player.PositionZ, radius: 1f));

        fx.Players.Remove(player);
        index.Rebuild(fx.Players.Online);

        Assert.Empty(Collect(index, player.PositionX, player.PositionZ, radius: 1f));
    }

    /// <summary>
    /// The index inserts every online player regardless of IsInGame/IsDead — those remain the
    /// caller's exact-state checks, exactly as before this index existed (Phase XXVIII's
    /// fundamental rule: the index provides candidates only).
    /// </summary>
    [Fact]
    public void Rebuild_includes_a_dead_or_not_in_game_player_as_a_raw_candidate()
    {
        var fx = new IntentTestFixture();
        var notInGame = fx.AddPlayer("pending", isInGame: false);
        var index = new PlayerSpatialIndex();

        index.Rebuild(fx.Players.Online);

        Assert.Contains(notInGame, Collect(index, notInGame.PositionX, notInGame.PositionZ, radius: 1f));
    }

    private static List<Player.Player> Collect(PlayerSpatialIndex index, float x, float z, float radius)
    {
        var found = new List<Player.Player>();
        foreach (var player in index.EnumerateNearby(x, z, radius))
            found.Add(player);
        return found;
    }
}
