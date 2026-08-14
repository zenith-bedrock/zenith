using System.Net;
using Zenith.Gameplay;
using Zenith.Packets;
using Zenith.Player;
using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Network;
using Zenith.Raknet.Stream;
using Xunit;

namespace Zenith.Tests;

/// <summary>Phase XXIII-B — real-client report: mobs could damage a Creative-mode player. GameMode was never checked at all before this fix.</summary>
public sealed class PlayerDamageGameModeTests
{
    [Fact]
    public void Creative_player_takes_no_melee_damage()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("creative-tank", GameMode.Creative);

        var applied = PlayerDamage.Apply(player, fx.Players, fx.Players.Online, DamageSource.Melee, 10f, 0);

        Assert.False(applied);
        Assert.Equal(player.MaxHealth, player.Health);
    }

    [Fact]
    public void Creative_player_takes_no_projectile_or_generic_damage()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("creative-tank-2", GameMode.Creative);

        Assert.False(PlayerDamage.Apply(player, fx.Players, fx.Players.Online, DamageSource.Projectile(1), 10f, 0));
        Assert.False(PlayerDamage.Apply(player, fx.Players, fx.Players.Online, DamageSource.Generic, 10f, 0));
        Assert.Equal(player.MaxHealth, player.Health);
    }

    [Fact]
    public void Survival_player_still_takes_melee_damage()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("survival-target", GameMode.Survival);

        var applied = PlayerDamage.Apply(player, fx.Players, fx.Players.Online, DamageSource.Melee, 10f, 0);

        Assert.True(applied);
        Assert.Equal(player.MaxHealth - 10f, player.Health);
    }

    /// <summary>Void is the deliberate exception — matches vanilla (falling out of the world kills even Creative players) and pre-existing Zenith behavior (ADR §40/§73).</summary>
    [Fact]
    public void Creative_player_still_dies_to_void_damage()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("creative-void", GameMode.Creative);

        var applied = PlayerDamage.Apply(player, fx.Players, fx.Players.Online, DamageSource.Void, player.MaxHealth, 0);

        Assert.True(applied);
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

        Assert.True(PlayerDamage.Apply(withDirection, fx.Players, fx.Players.Online, DamageSource.Melee, 4f, 0, 1f, 0f));
        Assert.True(PlayerDamage.Apply(withoutDirection, fx.Players, fx.Players.Online, DamageSource.Melee, 4f, 0));
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
                var gameStream = new BinaryStream(frame.Buffer);
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
