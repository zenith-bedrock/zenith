using System.Net;
using Zenith.Packets;
using Zenith.Player;
using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Network;
using Zenith.Raknet.Stream;
using Zenith.Session;
using Zenith.World;
using Xunit;
using Zenith.Gameplay.Survival;

namespace Zenith.Tests;

public class PlayerVisibilityJoinTests
{
    public PlayerVisibilityJoinTests() => Blocks.EnsureLoaded();

    [Fact]
    public void AnnounceJoin_sends_peer_bootstrap_even_when_pose_not_dirty()
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
            "joiner must receive the PlayerList/AddPlayer bootstrap for standing peers");

        var bootstrapPacketIds = ReadUnsplitPacketIds(fx, bob.Session.RakSession.EndPoint);
        Assert.Contains((int)ProtocolInfo.ADD_PLAYER_PACKET, bootstrapPacketIds);
        Assert.DoesNotContain((int)ProtocolInfo.MOVE_ACTOR_ABSOLUTE_PACKET, bootstrapPacketIds);
        Assert.DoesNotContain((int)ProtocolInfo.UPDATE_ATTRIBUTES_PACKET, bootstrapPacketIds);
        Assert.DoesNotContain((int)ProtocolInfo.PLAYER_SKIN_PACKET, bootstrapPacketIds);
        Assert.DoesNotContain((int)ProtocolInfo.MOB_ARMOR_EQUIPMENT_PACKET, bootstrapPacketIds);

        // Standing alice still must not re-fan via MovementSystem alone.
        var system = new MovementSystem(fx.Players);
        while (fx.Transport.Captured.TryDequeue(out _)) { }
        alice.SubmitMovementInput(MovementInputState.From(
            alice.PositionX, alice.PositionY, alice.PositionZ, alice.Pitch, alice.Yaw));
        system.Tick(fx.Clock, fx.Players.Online);
        foreach (var p in fx.Players.Online)
            p.Session.RakSession.Tick();
        Assert.Empty(fx.Transport.Captured);
    }

    private static List<int> ReadUnsplitPacketIds(IntentTestFixture fx, IPEndPoint recipient)
    {
        var ids = new List<int>();
        while (fx.Transport.CapturedByEndpoint.TryDequeue(out var captured))
        {
            if (!captured.EndPoint.Equals(recipient)) continue;

            var datagram = captured.Datagram;
            if (datagram.Length < 2 || (datagram[0] & 0xf0) != (byte)BitFlags.Valid) continue;

            var frameStream = new BinaryStream(datagram[1..]);
            var frameSet = new FrameSet();
            frameSet.Decode(ref frameStream);
            frameStream.Dispose();

            foreach (var frame in frameSet.Packets)
            {
                // PlayerList's skin payload may split. The packets asserted here are small and
                // therefore independently observable without testing RakNet reassembly.
                if (frame.IsSplit()) continue;

                var gameStream = new BinaryStream(frame.Buffer.ToArray());
                if (gameStream.ReadByte() != 0xfe || gameStream.ReadByte() != PacketCompression.NONE)
                {
                    gameStream.Dispose();
                    continue;
                }

                while (!gameStream.IsEndOfFile)
                {
                    var length = gameStream.ReadUnsignedVarInt();
                    var packet = new BinaryStream(gameStream.ReadSpan(length).ToArray());
                    ids.Add(packet.ReadUnsignedVarInt());
                    packet.Dispose();
                }

                gameStream.Dispose();
            }
        }

        return ids;
    }
}
