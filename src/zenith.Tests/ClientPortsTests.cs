using Zenith.Ecs;
using Zenith.World;
using Zenith.Gameplay.Entities;
using Zenith.Gameplay.Ports;
using Zenith.Gameplay.Replication;
using Xunit;

namespace Zenith.Tests;

/// <summary>Gameplay is observable through the client ports alone: no packet, session or transport is needed to assert on it.</summary>
public sealed class ClientPortsTests
{
    [Fact]
    public void A_zombie_near_a_player_is_announced_through_the_entity_port_with_its_health()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("watcher");
        player.Chunks.Radius = 1;
        player.Chunks.RememberMany([(0, 0)]);
        var client = new RecordingClientChannel();
        player.AttachClient(client);
        var system = new ZombieSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette);
        system.SpawnZombie(player.PositionX + 6, Blocks.FlatSpawnY, player.PositionZ);

        system.Tick(fx.Clock, fx.Players.Online);

        var add = Assert.Single(client.Of("SendAddMob"));
        Assert.Equal(MobKind.Zombie, add[0]);
        Assert.Equal(1, client.Count("SendHealth"));
    }

    [Fact]
    public void A_players_health_change_is_relayed_to_peers_only_through_their_entity_port()
    {
        var fx = new IntentTestFixture();
        var subject = fx.AddInGamePlayer("subject");
        var peer = fx.AddInGamePlayer("peer");
        var subjectClient = new RecordingClientChannel();
        var peerClient = new RecordingClientChannel();
        subject.AttachClient(subjectClient);
        peer.AttachClient(peerClient);

        PlayerVisibility.RelayHealth(subject, fx.Players.Online);

        Assert.Equal(1, peerClient.Count("SendHealth"));
        Assert.Equal(0, subjectClient.Count("SendHealth")); // the subject's own HUD uses its attribute seed, not a peer relay
    }

    [Fact]
    public void A_headless_player_without_a_world_fails_loudly_instead_of_with_a_null_reference()
    {
        var player = new Zenith.Player.Player("ghost", session: null!, runtimeId: 1, uuid: Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() => player.Level);
        Assert.Throws<InvalidOperationException>(() => player.ItemPalette);
    }
}
