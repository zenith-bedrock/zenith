using System.Net;
using Zenith.Gameplay.Runtime;
using Zenith.Packets;
using Zenith.Player;
using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Network;
using Zenith.Binary;
using Zenith.World;
using Xunit;
using Zenith.Gameplay.Survival;
using Zenith.Gameplay.Replication;

namespace Zenith.Tests;

public class HealthReplicationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Initial_peer_projection_sends_wounded_health_after_add_player(bool refresh)
    {
        var fx = new IntentTestFixture();
        var subject = fx.AddInGamePlayer("wounded");
        // Keep PlayerList below MTU: this test exercises health ordering, not skin fragmentation.
        subject.Session.Skin = SerializedSkin.Default with
        {
            Image = new SkinImage { Width = 1, Height = 1, Data = [0, 0, 0, 255] }
        };
        subject.ApplyDamage(DamageSource.Generic, 13f, fx.Clock.CurrentTick);
        var peer = fx.AddInGamePlayer("late-observer");
        if (refresh) PlayerVisibility.RefreshPeerView(subject, fx.Players.Online);
        else PlayerVisibility.AnnounceJoin(peer, fx.Players.Online);
        FlushRaknet(fx.Players);
        var update = Assert.Single(ReadHealthUpdates(fx, peer.Session.RakSession().EndPoint, requireSpawn: true));
        Assert.Equal((ulong)subject.RuntimeId, update.ActorRuntimeId);
        Assert.Equal(7f, update.Value); Assert.Equal(20f, update.Maximum);
    }

    [Fact]
    public void Nonlethal_fall_emits_the_subjects_current_health_to_the_peer()
    {
        var fx = new IntentTestFixture();
        var subject = fx.AddInGamePlayer("subject");
        var peer = fx.AddInGamePlayer("peer");
        var movement = new MovementSystem(fx.Players);

        SubmitAirborne(subject, Blocks.FlatSpawnY + 10f);
        movement.Tick(fx.Clock, fx.Players.Online);
        FlushRaknet(fx.Players);
        Drain(fx);

        SubmitLanding(subject, Blocks.FlatSpawnY);
        movement.Tick(fx.Clock, fx.Players.Online);
        FlushRaknet(fx.Players);

        Assert.Contains(ReadHealthUpdates(fx, peer.Session.RakSession().EndPoint), update =>
            update.ActorRuntimeId == (ulong)subject.RuntimeId &&
            update.Value == subject.Health &&
            update.Maximum == subject.MaxHealth);
    }

    private static void FlushRaknet(PlayerManager players)
    {
        foreach (var player in players.Online)
            player.Session.Transport.FlushOutgoing();
    }

    /// <summary>
    /// Decode the small outbound path through RakNet framing and Bedrock's uncompressed game
    /// batch. This verifies the semantic packet emitted to the peer rather than relying on a
    /// datagram count, which is an implementation detail of batching.
    /// </summary>
    private static void SubmitAirborne(Player.Player player, float y)
    {
        var input = MovementInputState.From(0f, y, 0f, 0f, 0f);
        input.OnGround = false;
        player.Intents.SubmitMovementInput(input);
    }

    private static void SubmitLanding(Player.Player player, float y)
    {
        var input = MovementInputState.From(0f, y, 0f, 0f, 0f);
        input.OnGround = true;
        player.Intents.SubmitMovementInput(input);
    }

    private static void Drain(IntentTestFixture fx)
    {
        while (fx.Transport.Captured.TryDequeue(out _)) { }
        while (fx.Transport.CapturedByEndpoint.TryDequeue(out _)) { }
    }

    private static List<(ulong ActorRuntimeId, float Value, float Maximum)> ReadHealthUpdates(
        IntentTestFixture fx,
        IPEndPoint recipient, bool requireSpawn = false)
    {
        var updates = new List<(ulong ActorRuntimeId, float Value, float Maximum)>();
        var announced = new HashSet<ulong>();
        while (fx.Transport.CapturedByEndpoint.TryDequeue(out var captured))
        {
            if (!captured.EndPoint.Equals(recipient))
                continue;

            var datagram = captured.Datagram;
            if (datagram.Length < 2 || (datagram[0] & 0xf0) != (byte)BitFlags.Valid)
                continue;

            var frameStream = new BinaryStream(datagram[1..]);
            var frameSet = new FrameSet();
            frameSet.Decode(ref frameStream);
            frameStream.Dispose();

            foreach (var frame in frameSet.Packets)
            {
                var gameStream = new BinaryStream(frame.Buffer.ToArray());
                if (gameStream.ReadByte() != 0xfe || gameStream.ReadByte() != PacketCompression.NONE)
                {
                    gameStream.Dispose();
                    continue;
                }

                while (!gameStream.IsEndOfFile)
                {
                    var length = gameStream.ReadUnsignedVarInt();
                    var packetBytes = gameStream.ReadSpan(length).ToArray();
                    var packetStream = new BinaryStream(packetBytes);
                    var packetId = packetStream.ReadUnsignedVarInt();
                    if (packetId == (int)ProtocolInfo.ADD_PLAYER_PACKET)
                    {
                        _ = packetStream.ReadUuid(); _ = packetStream.ReadVarString();
                        announced.Add((ulong)packetStream.ReadUnsignedVarLong());
                    }
                    if (packetId != (int)ProtocolInfo.UPDATE_ATTRIBUTES_PACKET)
                    {
                        packetStream.Dispose();
                        continue;
                    }

                    var actorRuntimeId = (ulong)packetStream.ReadUnsignedVarLong();
                    if (requireSpawn) Assert.Contains(actorRuntimeId, announced);
                    var count = packetStream.ReadUnsignedVarInt();
                    for (var i = 0; i < count; i++)
                    {
                        _ = packetStream.ReadFloat(BinaryStream.Endianess.Little); // min
                        var maximum = packetStream.ReadFloat(BinaryStream.Endianess.Little);
                        var value = packetStream.ReadFloat(BinaryStream.Endianess.Little);
                        _ = packetStream.ReadFloat(BinaryStream.Endianess.Little); // default min
                        _ = packetStream.ReadFloat(BinaryStream.Endianess.Little); // default max
                        _ = packetStream.ReadFloat(BinaryStream.Endianess.Little); // default
                        var name = packetStream.ReadVarString();
                        _ = packetStream.ReadUnsignedVarInt(); // modifiers

                        if (name == "minecraft:health")
                            updates.Add((actorRuntimeId, value, maximum));
                    }

                    packetStream.Dispose();
                }

                gameStream.Dispose();
            }
        }

        return updates;
    }
}
