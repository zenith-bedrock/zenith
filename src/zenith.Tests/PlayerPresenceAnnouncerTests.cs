using Zenith.Event;
using Zenith.World;
using Xunit;
using Zenith.Gameplay.Replication;

namespace Zenith.Tests;

public class PlayerPresenceAnnouncerTests
{
    public PlayerPresenceAnnouncerTests() => Blocks.EnsureLoaded();

    [Fact]
    public void OnEntered_broadcasts_to_in_game_peers_but_not_to_the_joining_player()
    {
        var fx = new IntentTestFixture();
        var alice = fx.AddInGamePlayer("alice");
        var joining = fx.AddInGamePlayer("bob");

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        PlayerPresenceAnnouncer.OnEntered(fx.Context, new PlayerEnteredWorld(joining.Uuid, joining.Username, joining.RuntimeId, 1));
        FlushRaknet(fx.Players);

        Assert.Single(fx.Transport.Captured); // alice only: the subject is never told about themselves
        _ = alice;
    }

    [Fact]
    public void OnLeft_broadcasts_to_the_remaining_in_game_peers()
    {
        var fx = new IntentTestFixture();
        _ = fx.AddInGamePlayer("alice");
        var gone = fx.AddInGamePlayer("bob");
        fx.Players.Remove(gone);

        FlushRaknet(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }

        PlayerPresenceAnnouncer.OnLeft(fx.Context, new PlayerLeftWorld(gone.Uuid, gone.Username, gone.RuntimeId, PlayerLeaveReason.Disconnected, 2));
        FlushRaknet(fx.Players);

        Assert.Single(fx.Transport.Captured);
    }

    [Fact]
    public void Presence_facts_carry_values_only_never_a_live_player()
    {
        foreach (var type in new[] { typeof(PlayerEnteredWorld), typeof(PlayerLeftWorld) })
        {
            foreach (var property in type.GetProperties().Where(p => p.Name != "EqualityContract"))
            {
                Assert.True(
                    property.PropertyType.IsValueType || property.PropertyType == typeof(string),
                    $"{type.Name}.{property.Name} is {property.PropertyType.Name}: presence facts must not hand observers live objects.");
            }
        }
    }

    private static void FlushRaknet(Zenith.Player.PlayerManager players)
    {
        foreach (var p in players.Online)
            p.Session.Transport.FlushOutgoing();
    }
}
