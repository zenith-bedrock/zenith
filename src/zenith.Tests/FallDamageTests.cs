using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.World;
using Xunit;
using Zenith.Gameplay.Survival;

namespace Zenith.Tests;

/// <summary>Fall damage on landing (ADR §96) — Health authority, no hunger/drowning yet.</summary>
public class FallDamageTests
{
    public FallDamageTests() => Blocks.EnsureLoaded();

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

    [Fact]
    public void Short_fall_within_safe_distance_deals_no_damage()
    {
        var fx = new IntentTestFixture();
        var mover = fx.AddInGamePlayer("mover");
        var system = new MovementSystem(fx.Players);

        SubmitAirborne(mover, Blocks.FlatSpawnY + 10f);
        system.Tick(fx.Clock, fx.Players.Online);
        SubmitLanding(mover, Blocks.FlatSpawnY + 8f); // 2-block fall, under the 3-block threshold

        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(20f, mover.Health);
        Assert.False(mover.IsDead);
    }

    [Fact]
    public void Fall_past_safe_distance_deals_one_damage_per_extra_block()
    {
        var fx = new IntentTestFixture();
        var mover = fx.AddInGamePlayer("mover");
        var system = new MovementSystem(fx.Players);

        SubmitAirborne(mover, Blocks.FlatSpawnY + 10f);
        system.Tick(fx.Clock, fx.Players.Online);
        SubmitLanding(mover, Blocks.FlatSpawnY); // 10-block fall: 7 damage past the 3-block safe distance

        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(13f, mover.Health);
        Assert.False(mover.IsDead);
    }

    [Fact]
    public void Lethal_fall_kills_and_reuses_the_death_respawn_handshake()
    {
        var fx = new IntentTestFixture();
        var mover = fx.AddInGamePlayer("mover");
        var system = new MovementSystem(fx.Players);

        SubmitAirborne(mover, Blocks.FlatSpawnY + 30f);
        system.Tick(fx.Clock, fx.Players.Online);
        SubmitLanding(mover, Blocks.FlatSpawnY); // 30-block fall: way past lethal

        system.Tick(fx.Clock, fx.Players.Online);

        Assert.True(mover.IsDead);
        Assert.Equal("fall", mover.DeathCause);
        Assert.Equal(0f, mover.Health);
    }

    [Fact]
    public void Creative_is_immune_to_fall_damage()
    {
        var fx = new IntentTestFixture();
        var mover = fx.AddInGamePlayer("mover");
        mover.SetGameMode(GameMode.Creative);
        var system = new MovementSystem(fx.Players);

        SubmitAirborne(mover, Blocks.FlatSpawnY + 30f);
        system.Tick(fx.Clock, fx.Players.Online);
        SubmitLanding(mover, Blocks.FlatSpawnY);

        system.Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(20f, mover.Health);
        Assert.False(mover.IsDead);
    }

    [Fact]
    public void Respawn_resets_fall_peak_so_the_next_fall_is_measured_fresh()
    {
        var fx = new IntentTestFixture();
        var mover = fx.AddInGamePlayer("mover");
        var system = new MovementSystem(fx.Players);

        SubmitAirborne(mover, Blocks.FlatSpawnY + 30f);
        system.Tick(fx.Clock, fx.Players.Online);
        SubmitLanding(mover, Blocks.FlatSpawnY);
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.True(mover.IsDead);

        mover.SubmitRespawn();
        system.Tick(fx.Clock, fx.Players.Online);
        Assert.False(mover.IsDead);
        Assert.Equal(20f, mover.Health);
        Assert.Equal(mover.PositionY, mover.FallPeakY);
    }
}
