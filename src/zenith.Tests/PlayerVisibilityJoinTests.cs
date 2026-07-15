using Zenith.Gameplay.Systems;
using Zenith.Player;
using Zenith.Session;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

public class PlayerVisibilityJoinTests
{
    public PlayerVisibilityJoinTests() => Blocks.EnsureLoaded();

    [Fact]
    public void AnnounceJoin_settles_existing_peer_with_Absolute_even_when_pose_not_dirty()
    {
        var fx = new IntentTestFixture();
        var alice = fx.AddInGamePlayer("alice");
        alice.PositionX = 3f;
        alice.PositionY = Blocks.FlatSpawnY;
        alice.PositionZ = 4f;
        alice.Pitch = 5f;
        alice.Yaw = 90f;
        alice.HeadYaw = 90f;
        // Standing still after ADR §44 dirty-check — LastReplicated matches current pose.
        alice.LastReplicatedX = alice.PositionX;
        alice.LastReplicatedY = alice.PositionY;
        alice.LastReplicatedZ = alice.PositionZ;
        alice.LastReplicatedPitch = alice.Pitch;
        alice.LastReplicatedYaw = alice.Yaw;
        alice.LastReplicatedHeadYaw = alice.HeadYaw;

        var bob = fx.AddInGamePlayer("bob");
        bob.IsInGame = false; // AnnounceJoin runs before InGame OnEnable

        while (fx.Transport.Captured.TryDequeue(out _)) { }

        PlayerVisibility.AnnounceJoin(bob, fx.Players.Online);
        foreach (var p in fx.Players.Online)
            p.Session.RakSession.Tick();

        Assert.True(fx.Transport.Captured.Count >= 1,
            "joiner must receive Absolute settle for standing peers (not only AddPlayer)");

        // Standing alice still must not re-fan via MovementSystem alone.
        var system = new MovementSystem(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }
        alice.SubmitMovementInput(MovementInputState.From(
            alice.PositionX, alice.PositionY, alice.PositionZ, alice.Pitch, alice.Yaw));
        system.Tick(fx.Clock);
        foreach (var p in fx.Players.Online)
            p.Session.RakSession.Tick();
        Assert.Empty(fx.Transport.Captured);
    }
}
