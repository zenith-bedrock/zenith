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
        var player = fx.AddOperator("solo-gamemode");

        var feedback = fx.Context.Commands.Execute(player, "/gamemode creative");

        Assert.Equal("Game mode", feedback.Title);
        Assert.Contains(player.Username, feedback.Message);
        Assert.True(player.Intents.TryConsumeGameMode(out var request));
        Assert.Equal(GameMode.Creative, request.Mode);
    }

    [Fact]
    public void Gamemode_command_with_a_target_queues_the_mode_for_the_target_not_the_source()
    {
        var fx = new IntentTestFixture();
        var source = fx.AddOperator("operator");
        var target = fx.AddInGamePlayer("bystander");

        var feedback = fx.Context.Commands.Execute(source, $"/gamemode survival {target.Username}");

        Assert.Contains(target.Username, feedback.Message);
        Assert.False(source.Intents.TryConsumeGameMode(out _));
        Assert.True(target.Intents.TryConsumeGameMode(out var request));
        Assert.Equal(GameMode.Survival, request.Mode);
    }

    [Fact]
    public void Help_command_lists_registered_command_names_and_aliases()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddOperator("help-seeker");

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
        var player = fx.AddOperator("effect-default");

        var feedback = fx.Context.Commands.Execute(player, "/effect give poison");

        Assert.Equal("Effect", feedback.Title);
        Assert.True(player.Effects.TryConsumeEffectIntent(out var intent));
        Assert.Equal(EffectType.Poison, intent.Type);
        Assert.Equal(0, intent.Amplifier);
        Assert.Equal(600, intent.DurationTicks);
    }

    [Fact]
    public void Effect_give_with_explicit_duration_and_amplifier_queues_exactly_those_values()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddOperator("effect-explicit");

        var feedback = fx.Context.Commands.Execute(player, "/effect give regeneration 200 2");

        Assert.True(player.Effects.TryConsumeEffectIntent(out var intent));
        Assert.Equal(EffectType.Regeneration, intent.Type);
        Assert.Equal(2, intent.Amplifier);
        Assert.Equal(200, intent.DurationTicks);
        Assert.Contains("Regeneration", feedback.Message);
    }

    [Fact]
    public void Effect_clear_queues_a_clear_intent_not_a_give()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddOperator("effect-clear");

        var feedback = fx.Context.Commands.Execute(player, "/effect clear");

        Assert.Contains("Clearing effects", feedback.Message);
        Assert.True(player.Effects.TryConsumeEffectIntent(out var intent));
        Assert.True(intent.ClearAll);
        Assert.Equal(player.Principal, intent.Issuer!.Value.Principal);
    }

    [Fact]
    public void Effect_command_with_a_target_queues_for_the_target_not_the_source()
    {
        var fx = new IntentTestFixture();
        var source = fx.AddOperator("effect-operator");
        var target = fx.AddInGamePlayer("effect-target");

        fx.Context.Commands.Execute(source, $"/effect give poison 100 0 {target.Username}");

        Assert.False(source.Effects.TryConsumeEffectIntent(out _));
        Assert.True(target.Effects.TryConsumeEffectIntent(out var intent));
        Assert.Equal(EffectType.Poison, intent.Type);
    }

    // ---- authorization

    [Theory]
    [InlineData("/gamemode creative")]
    [InlineData("/gm creative")]
    [InlineData("/effect give poison")]
    [InlineData("/effect clear")]
    [InlineData("/diagnostics snapshot")]
    public void A_default_player_cannot_run_administrative_commands_and_nothing_is_queued(string line)
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("plain");

        var feedback = fx.Context.Commands.Execute(player, line);

        Assert.Equal("You do not have permission.", feedback.Message);
        Assert.False(player.Intents.TryConsumeGameMode(out _));
        Assert.False(player.Effects.TryConsumeEffectIntent(out _));
    }

    [Fact]
    public void Help_shows_a_default_player_only_the_commands_they_may_use()
    {
        var fx = new IntentTestFixture();
        var plain = fx.AddInGamePlayer("plain");
        var admin = fx.AddOperator("admin");

        var plainList = fx.Context.Commands.Execute(plain, "/help").Message;
        var adminList = fx.Context.Commands.Execute(admin, "/help").Message;

        Assert.Contains("help", plainList);
        Assert.DoesNotContain("gamemode", plainList);
        Assert.DoesNotContain("effect", plainList);
        Assert.Contains("gamemode", adminList);
    }

    [Fact]
    public void Acting_on_others_is_a_separate_grant_from_acting_on_oneself()
    {
        var fx = new IntentTestFixture();
        var source = fx.AddInGamePlayer("selfish");
        var target = fx.AddInGamePlayer("other");
        // Only the self nodes, via explicit grants on an authenticated identity.
        var authenticated = fx.AddPlayer("granted", GameMode.Survival, isInGame: true, Gameplay.Permissions.IdentityTrust.Authenticated);
        fx.Context.Permissions.Replace(Gameplay.Permissions.PermissionPolicy.Create(
            fx.Context.Permissions.Registry, [],
            new Dictionary<string, List<string>> { [authenticated.Uuid.ToString()] = [Gameplay.Permissions.PermissionNodes.GameModeSelf] },
            null, false));

        var own = fx.Context.Commands.Execute(authenticated, "/gamemode creative");
        var others = fx.Context.Commands.Execute(authenticated, $"/gamemode creative {target.Username}");

        Assert.Equal("Game mode", own.Title);
        Assert.Contains("Queued", own.Message);
        Assert.Equal("You do not have permission.", others.Message);
        Assert.False(target.Intents.TryConsumeGameMode(out _));
        _ = source;
    }

    [Fact]
    public void Reading_a_game_rule_and_changing_it_are_separate_permissions()
    {
        var fx = new IntentTestFixture();
        var plain = fx.AddInGamePlayer("plain");
        var admin = fx.AddOperator("admin");
        var rule = fx.World.Rules.Snapshot[0].Name;

        Assert.DoesNotContain("permission", fx.Context.Commands.Execute(plain, $"/gamerule {rule}").Message);
        Assert.Equal("You do not have permission.", fx.Context.Commands.Execute(plain, $"/gamerule {rule} true").Message);
        Assert.DoesNotContain("permission", fx.Context.Commands.Execute(admin, $"/gamerule {rule}").Message);
    }

    [Fact]
    public void A_game_mode_queued_by_an_operator_is_not_applied_if_the_permission_is_revoked_before_the_tick()
    {
        var fx = new IntentTestFixture();
        var admin = fx.AddOperator("admin");
        var target = fx.AddInGamePlayer("target");
        fx.Context.Commands.Execute(admin, $"/gamemode creative {target.Username}");

        fx.Context.Permissions.Replace(Gameplay.Permissions.PermissionPolicy.Empty); // revoked between command and tick
        new Gameplay.Replication.GameModeSystem(fx.Context.Gate).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(GameMode.Survival, target.GameMode);
    }

    [Fact]
    public void A_game_mode_queued_by_an_operator_who_then_left_is_not_applied()
    {
        var fx = new IntentTestFixture();
        var admin = fx.AddOperator("admin");
        var target = fx.AddInGamePlayer("target");
        fx.Context.Commands.Execute(admin, $"/gamemode creative {target.Username}");

        fx.Players.Remove(admin);
        new Gameplay.Replication.GameModeSystem(fx.Context.Gate).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(GameMode.Survival, target.GameMode);
    }

    [Fact]
    public void A_confirmed_game_mode_change_still_applies()
    {
        var fx = new IntentTestFixture();
        var admin = fx.AddOperator("admin");
        var target = fx.AddInGamePlayer("target");
        fx.Context.Commands.Execute(admin, $"/gamemode creative {target.Username}");

        new Gameplay.Replication.GameModeSystem(fx.Context.Gate).Tick(fx.Clock, fx.Players.Online);

        Assert.Equal(GameMode.Creative, target.GameMode);
    }

    [Fact]
    public void An_effect_queued_by_an_operator_is_not_applied_after_the_revocation()
    {
        var fx = new IntentTestFixture();
        var admin = fx.AddOperator("admin");
        var target = fx.AddInGamePlayer("target");
        fx.Context.Commands.Execute(admin, $"/effect give poison 100 0 {target.Username}");

        fx.Context.Permissions.Replace(Gameplay.Permissions.PermissionPolicy.Empty);
        new EffectSystem(fx.Players, fx.Context.Gate).Tick(fx.Clock, fx.Players.Online);

        Assert.Empty(target.Effects.Active);
    }

    [Fact]
    public void A_self_signed_player_listed_as_operator_is_still_not_one_unless_the_development_switch_is_on()
    {
        var fx = new IntentTestFixture();
        var claimant = fx.AddPlayer("claimant", GameMode.Survival, isInGame: true, Gameplay.Permissions.IdentityTrust.SelfAsserted);
        void ListAsOperator(bool trustSelfAsserted) => fx.Context.Permissions.Replace(Gameplay.Permissions.PermissionPolicy.Create(
            fx.Context.Permissions.Registry, [claimant.Uuid.ToString()], null, null, trustSelfAsserted));

        ListAsOperator(false);
        Assert.Equal("You do not have permission.", fx.Context.Commands.Execute(claimant, "/gamemode creative").Message);

        ListAsOperator(true);
        Assert.Contains("Queued", fx.Context.Commands.Execute(claimant, "/gamemode creative").Message);
    }
}
