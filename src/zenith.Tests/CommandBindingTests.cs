using Xunit;
using Zenith.Gameplay.Commands;
using Zenith.Gameplay.Permissions;
using Zenith.Player;

namespace Zenith.Tests;

/// <summary>
/// A command is its grammar plus its handler, bound together before composition ends: adding one never edits the runtime, handlers see
/// values only, and every result says how far the command got (rejected, accepted-but-queued, or applied).
/// </summary>
public sealed class CommandBindingTests
{
    private static CommandRuntime Runtime(IntentTestFixture fx, params CommandBinding[] contributions) =>
        new(fx.Players, fx.Context.Permissions, contributions: contributions);

    private static CommandBinding Echo(string name, string node, Action<CommandInvocation>? seen = null) =>
        new(new CommandDefinition(name, "test", node, new CommandOverload(new StringCommandArgument("word", Optional: true))),
            (in CommandInvocation invocation) =>
            {
                seen?.Invoke(invocation);
                return new CommandFeedback("Echo", "ok", CommandOutcome.Applied);
            });

    [Fact]
    public void A_contributed_command_runs_with_the_callers_identity_position_and_parsed_arguments_as_values()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("caller");
        player.PositionX = 1.5f;
        player.PositionY = 65;
        player.PositionZ = -3.5f;
        CommandInvocation? seen = null;
        var runtime = Runtime(fx, Echo("echo", PermissionNodes.Help, invocation => seen = invocation));

        var feedback = runtime.Execute(player, "/echo hello");

        Assert.Equal(CommandOutcome.Applied, feedback.Outcome);
        Assert.NotNull(seen);
        Assert.Equal(player.Principal, seen.Value.Caller);
        Assert.Equal("caller", seen.Value.CallerName);
        Assert.Equal((1.5f, 65f, -3.5f), (seen.Value.X, seen.Value.Y, seen.Value.Z));
        Assert.Equal("hello", seen.Value.Arguments["word"]);
    }

    [Fact]
    public void A_contributed_command_the_caller_may_not_use_never_reaches_its_handler()
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("plain");
        var calls = 0;
        var runtime = Runtime(fx, Echo("echo", PermissionNodes.Diagnostics, _ => calls++));

        var feedback = runtime.Execute(player, "/echo");

        Assert.Equal(CommandOutcome.Rejected, feedback.Outcome);
        Assert.Equal(0, calls);
        Assert.DoesNotContain("echo", runtime.Suggest(player, ""));
    }

    [Fact]
    public void A_contribution_that_clashes_with_a_built_in_fails_composition_instead_of_shadowing_it()
    {
        var fx = new IntentTestFixture();

        Assert.Throws<InvalidOperationException>(() => Runtime(fx, Echo("help", PermissionNodes.Help)));
    }

    [Fact]
    public void Two_contributions_with_the_same_name_fail_composition()
    {
        var fx = new IntentTestFixture();

        Assert.Throws<InvalidOperationException>(() => Runtime(fx, Echo("echo", PermissionNodes.Help), Echo("echo", PermissionNodes.Help)));
    }

    [Fact]
    public void The_outcome_tells_queued_work_from_finished_work_from_nothing()
    {
        var fx = new IntentTestFixture();
        var admin = fx.AddOperator("admin");
        var plain = fx.AddInGamePlayer("plain");
        var commands = fx.Context.Commands;

        Assert.Equal(CommandOutcome.Applied, commands.Execute(admin, "/help").Outcome);
        Assert.Equal(CommandOutcome.Accepted, commands.Execute(admin, "/gamemode creative").Outcome);
        Assert.Equal(CommandOutcome.Rejected, commands.Execute(admin, "/nonsense").Outcome);
        Assert.Equal(CommandOutcome.Rejected, commands.Execute(plain, "/gamemode creative").Outcome);
        Assert.Equal(CommandOutcome.Rejected, commands.Execute(admin, "/gamemode sideways").Outcome);
    }

    [Fact]
    public void A_rejected_gamemode_command_queues_nothing()
    {
        var fx = new IntentTestFixture();
        var plain = fx.AddInGamePlayer("plain");

        fx.Context.Commands.Execute(plain, "/gamemode creative");

        Assert.False(plain.Intents.TryConsumeGameMode(out _));
    }
}
