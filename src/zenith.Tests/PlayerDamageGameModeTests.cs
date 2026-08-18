using System.Net;
using Zenith.Packets;
using Zenith.Player;
using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Network;
using Zenith.Raknet.Stream;
using Xunit;
using Zenith.Gameplay.Survival;

namespace Zenith.Tests;

/// <summary>Phase XXIII-B — real-client report: mobs could damage a Creative-mode player. GameMode was never checked at all before this fix.</summary>
public sealed class PlayerDamageGameModeTests
{
    [Fact]
    public void Creative_player_takes_no_melee_damage()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("creative-tank", GameMode.Creative);

        // ApplyCore alone, no Conclude call — proves the decision itself needs no `online` at all,
        // not just that it's unused for this outcome.
        var applied = PlayerDamage.ApplyCore(player, fx.Players, DamageSource.Melee, 10f, 0).Applied;

        Assert.False(applied);
        Assert.Equal(player.MaxHealth, player.Health);
    }

    [Fact]
    public void Creative_player_takes_no_projectile_or_generic_damage()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("creative-tank-2", GameMode.Creative);

        Assert.False(PlayerDamage.ApplyCore(player, fx.Players, DamageSource.Projectile(1), 10f, 0).Applied);
        Assert.False(PlayerDamage.ApplyCore(player, fx.Players, DamageSource.Generic, 10f, 0).Applied);
        Assert.Equal(player.MaxHealth, player.Health);
    }

    [Fact]
    public void Survival_player_still_takes_melee_damage()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("survival-target", GameMode.Survival);

        var result = PlayerDamage.ApplyCore(player, fx.Players, DamageSource.Melee, 10f, 0);
        result.Conclude(fx.Players.Online);

        Assert.True(result.Applied);
        Assert.Equal(player.MaxHealth - 10f, player.Health);
    }

    /// <summary>Void is the deliberate exception — matches vanilla (falling out of the world kills even Creative players) and pre-existing Zenith behavior (ADR §40/§73).</summary>
    [Fact]
    public void Creative_player_still_dies_to_void_damage()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("creative-void", GameMode.Creative);

        var result = PlayerDamage.ApplyCore(player, fx.Players, DamageSource.Void, player.MaxHealth, 0);
        result.Conclude(fx.Players.Online);

        Assert.True(result.Applied);
        Assert.True(player.IsDead);
    }

    /// <summary>
    /// Phase XXIII-B real-client finding: no real player-facing knockback existed at all. A non-zero
    /// direction vector must reach the hit player's own wire as a SetActorMotion packet; an
    /// omitted/zero direction (the pre-existing call shape, still used by Fall/Starve/Magic/Void)
    /// must not — matches vanilla not knocking back for those causes. Decodes the actual outbound
    /// bytes (not just a datagram count) since RakNet batches multiple packets per session into one
    /// frame, which a mere count assertion can't distinguish.
    /// </summary>
    [Fact]
    public void A_nonzero_knockback_direction_sends_a_set_actor_motion_packet_a_zero_direction_does_not()
    {
        var fx = new IntentTestFixture();
        var withDirection = fx.AddInGamePlayer("knocked");
        var withoutDirection = fx.AddInGamePlayer("not-knocked");

        var withDirectionResult = PlayerDamage.ApplyCore(withDirection, fx.Players, DamageSource.Melee, 4f, 0, 1f, 0f);
        withDirectionResult.Conclude(fx.Players.Online);
        var withoutDirectionResult = PlayerDamage.ApplyCore(withoutDirection, fx.Players, DamageSource.Melee, 4f, 0);
        withoutDirectionResult.Conclude(fx.Players.Online);
        Assert.True(withDirectionResult.Applied);
        Assert.True(withoutDirectionResult.Applied);
        Flush(fx);

        Assert.True(SentSetActorMotion(fx, withDirection.Session.RakSession.EndPoint));
        Assert.False(SentSetActorMotion(fx, withoutDirection.Session.RakSession.EndPoint));
    }

    private static void Flush(IntentTestFixture fx)
    {
        foreach (var p in fx.Players.Online)
            p.Session.RakSession.Tick();
    }

    private static bool SentSetActorMotion(IntentTestFixture fx, IPEndPoint recipient)
    {
        foreach (var captured in fx.Transport.CapturedByEndpoint)
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
                    var id = packetStream.ReadUnsignedVarInt();
                    packetStream.Dispose();
                    if (id == (int)ProtocolInfo.SET_ACTOR_MOTION_PACKET)
                    {
                        gameStream.Dispose();
                        return true;
                    }
                }

                gameStream.Dispose();
            }
        }
        return false;
    }
}
