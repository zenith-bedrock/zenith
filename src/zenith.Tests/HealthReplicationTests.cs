using System.Net;
using Zenith.Gameplay.Runtime;
using Zenith.Packets;
using Zenith.Player;
using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Network;
using Zenith.Raknet.Stream;
using Zenith.World;
using Xunit;
using Zenith.Gameplay.Survival;

namespace Zenith.Tests;

public class HealthReplicationTests
{
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

        Assert.Contains(ReadHealthUpdates(fx, peer.Session.RakSession.EndPoint), update =>
            update.ActorRuntimeId == (ulong)subject.RuntimeId &&
            update.Value == subject.Health &&
            update.Maximum == subject.MaxHealth);
    }

    private static void FlushRaknet(PlayerManager players)
    {
        foreach (var player in players.Online)
            player.Session.RakSession.Tick();
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
        player.SubmitMovementInput(input);
    }

    private static void SubmitLanding(Player.Player player, float y)
    {
        var input = MovementInputState.From(0f, y, 0f, 0f, 0f);
        input.OnGround = true;
        player.SubmitMovementInput(input);
    }

    private static void Drain(IntentTestFixture fx)
    {
        while (fx.Transport.Captured.TryDequeue(out _)) { }
        while (fx.Transport.CapturedByEndpoint.TryDequeue(out _)) { }
    }

    private static List<(ulong ActorRuntimeId, float Value, float Maximum)> ReadHealthUpdates(
        IntentTestFixture fx,
        IPEndPoint recipient)
    {
        var updates = new List<(ulong ActorRuntimeId, float Value, float Maximum)>();
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
                    if (packetStream.ReadUnsignedVarInt() != (int)ProtocolInfo.UPDATE_ATTRIBUTES_PACKET)
                    {
                        packetStream.Dispose();
                        continue;
                    }

                    var actorRuntimeId = (ulong)packetStream.ReadUnsignedVarLong();
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
