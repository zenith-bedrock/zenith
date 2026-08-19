using Zenith.Gameplay.Survival;
using Zenith.Player;
using Xunit;

namespace Zenith.Tests;

/// <summary>
/// <see cref="Zenith.Gameplay.Commands.CommandRuntime.Execute"/>'s gamemode/help/sample/effect
/// branches (including <c>ExecuteEffect</c>'s give/clear/default-duration-and-amplifier logic) had
/// zero coverage through the real dispatch path before this file — only <c>/diagnostics</c> was
/// exercised via <see cref="Zenith.Gameplay.Commands.CommandRuntime.Execute"/> in
/// <c>CommandCatalogTests</c>. These tests drive the actual production <c>CommandRuntime</c>
/// (<c>fx.Context.Commands</c>), not a hand-rolled catalog substitute.
/// </summary>
public sealed class CommandRuntimeExecuteTests
{
    [Fact]
    public void Gamemode_command_queues_the_requested_mode_for_the_source_player()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("solo-gamemode");

        var feedback = fx.Context.Commands.Execute(player, "/gamemode creative");

        Assert.Equal("Game mode", feedback.Title);
        Assert.Contains(player.Username, feedback.Message);
        Assert.True(player.TryConsumeGameMode(out var mode));
        Assert.Equal(GameMode.Creative, mode);
    }

    [Fact]
    public void Gamemode_command_with_a_target_queues_the_mode_for_the_target_not_the_source()
    {
        var fx = new IntentTestFixture();
        var source = fx.AddInGamePlayer("operator");
        var target = fx.AddInGamePlayer("bystander");

        var feedback = fx.Context.Commands.Execute(source, $"/gamemode survival {target.Username}");

        Assert.Contains(target.Username, feedback.Message);
        Assert.False(source.TryConsumeGameMode(out _));
        Assert.True(target.TryConsumeGameMode(out var mode));
        Assert.Equal(GameMode.Survival, mode);
    }

    [Fact]
    public void Help_command_lists_registered_command_names_and_aliases()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("help-seeker");

        var feedback = fx.Context.Commands.Execute(player, "/help");

        Assert.Equal("Commands", feedback.Title);
        Assert.Contains("gamemode", feedback.Message);
        Assert.Contains("gm", feedback.Message);
        Assert.Contains("effect", feedback.Message);
    }

    [Fact]
    public void Sample_command_without_an_argument_reports_the_default()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("sampler");

        var feedback = fx.Context.Commands.Execute(player, "/sample");

        Assert.Equal("Sample request: default.", feedback.Message);
    }

    [Fact]
    public void Sample_command_with_an_argument_reports_the_requested_tick_count()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("sampler-ticks");

        var feedback = fx.Context.Commands.Execute(player, "/sample 5");

        Assert.Equal("Sample request: 5 ticks.", feedback.Message);
    }

    [Fact]
    public void Effect_give_with_no_optional_arguments_queues_the_default_duration_and_amplifier()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("effect-default");

        var feedback = fx.Context.Commands.Execute(player, "/effect give poison");

        Assert.Equal("Effect", feedback.Title);
        Assert.True(player.TryConsumeEffectIntent(out var intent));
        Assert.Equal(EffectType.Poison, intent.Type);
        Assert.Equal(0, intent.Amplifier);
        Assert.Equal(600, intent.DurationTicks);
    }

    [Fact]
    public void Effect_give_with_explicit_duration_and_amplifier_queues_exactly_those_values()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("effect-explicit");

        var feedback = fx.Context.Commands.Execute(player, "/effect give regeneration 200 2");

        Assert.True(player.TryConsumeEffectIntent(out var intent));
        Assert.Equal(EffectType.Regeneration, intent.Type);
        Assert.Equal(2, intent.Amplifier);
        Assert.Equal(200, intent.DurationTicks);
        Assert.Contains("Regeneration", feedback.Message);
    }

    [Fact]
    public void Effect_clear_queues_a_clear_intent_not_a_give()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("effect-clear");

        var feedback = fx.Context.Commands.Execute(player, "/effect clear");

        Assert.Contains("Clearing effects", feedback.Message);
        Assert.True(player.TryConsumeEffectIntent(out var intent));
        Assert.Equal(EffectIntent.Clear, intent);
    }

    [Fact]
    public void Effect_command_with_a_target_queues_for_the_target_not_the_source()
    {
        var fx = new IntentTestFixture();
        var source = fx.AddInGamePlayer("effect-operator");
        var target = fx.AddInGamePlayer("effect-target");

        fx.Context.Commands.Execute(source, $"/effect give poison 100 0 {target.Username}");

        Assert.False(source.TryConsumeEffectIntent(out _));
        Assert.True(target.TryConsumeEffectIntent(out var intent));
        Assert.Equal(EffectType.Poison, intent.Type);
    }
}
