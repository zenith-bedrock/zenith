using System.Text;
using Xunit;
using Zenith.Gameplay.Commands;
using Zenith.Gameplay.Modules;
using Zenith.Gameplay.Policy;
using Zenith.Modules.Protect;

namespace Zenith.Tests;

/// <summary>
/// Plan 002: a region resists every environment cause unless told to let one through, one cause at a time. The setting is a command,
/// survives a restart, and old saved regions (version 1) are still read, strict.
/// </summary>
public sealed class ProtectCauseFlagsTests
{
    private sealed class MemoryState : IModuleStateStore
    {
        public Dictionary<string, byte[]> Saved { get; } = [];
        public byte[]? Load(string module) => Saved.GetValueOrDefault(module);
        public void Save(string module, byte[] state) => Saved[module] = state;
    }

    private static CommandFeedback Run(ProtectModule module, IntentTestFixture fx, string line)
    {
        var admin = fx.Players.Get("admin") ?? fx.AddOperator("admin");
        return new CommandRuntime(fx.Players, fx.Context.Permissions, contributions: [module.Command]).Execute(admin, line);
    }

    private static bool Allowed(ProtectModule module, EnvironmentCause cause, int x = 5)
    {
        var policy = module.EnvironmentPolicy;
        return policy.Evaluate(new EnvironmentChangeProposal(cause, 0, new BlockCellChange(x, 70, 5, 1, 0))).IsAllowed;
    }

    [Fact]
    public void A_new_region_is_strict_and_allow_opens_exactly_one_cause()
    {
        var fx = new IntentTestFixture();
        var module = new ProtectModule(fx.Context.Permissions);
        Assert.Equal(CommandOutcome.Applied, Run(module, fx, "/protect create farm 0 60 0 10 80 10").Outcome);
        foreach (var cause in Enum.GetValues<EnvironmentCause>()) Assert.False(Allowed(module, cause), cause.ToString());

        Assert.Equal(CommandOutcome.Applied, Run(module, fx, "/protect allow farm growth").Outcome);

        foreach (var cause in Enum.GetValues<EnvironmentCause>())
            Assert.Equal(cause == EnvironmentCause.Growth, Allowed(module, cause));
        Assert.True(Allowed(module, EnvironmentCause.Explosion, x: 50)); // outside the region nothing changed

        Assert.Equal(CommandOutcome.Applied, Run(module, fx, "/protect deny farm growth").Outcome);
        Assert.False(Allowed(module, EnvironmentCause.Growth));
    }

    [Fact]
    public void Overlapping_regions_all_have_to_allow_a_cause()
    {
        var fx = new IntentTestFixture();
        var module = new ProtectModule(fx.Context.Permissions);
        Run(module, fx, "/protect create a 0 60 0 10 80 10");
        Run(module, fx, "/protect create b 0 60 0 10 80 10");
        Run(module, fx, "/protect allow a decay");
        Assert.False(Allowed(module, EnvironmentCause.Decay));

        Run(module, fx, "/protect allow b decay");
        Assert.True(Allowed(module, EnvironmentCause.Decay));
    }

    [Fact]
    public void Unknown_regions_and_causes_are_rejected_and_only_a_manager_may_use_it()
    {
        var fx = new IntentTestFixture();
        var module = new ProtectModule(fx.Context.Permissions);
        Run(module, fx, "/protect create farm 0 60 0 10 80 10");

        Assert.Equal(CommandOutcome.Rejected, Run(module, fx, "/protect allow nowhere growth").Outcome);
        Assert.Equal(CommandOutcome.Rejected, Run(module, fx, "/protect allow farm sunshine").Outcome);

        var plain = fx.AddInGamePlayer("plain");
        var runtime = new CommandRuntime(fx.Players, fx.Context.Permissions, contributions: [module.Command]);
        Assert.Equal(CommandOutcome.Rejected, runtime.Execute(plain, "/protect allow farm growth").Outcome);
        Assert.False(Allowed(module, EnvironmentCause.Growth));
    }

    [Fact]
    public void The_setting_survives_a_restart_and_list_shows_it()
    {
        var fx = new IntentTestFixture();
        var state = new MemoryState();
        var first = new ProtectModule(fx.Context.Permissions, state);
        Run(first, fx, "/protect create farm 0 60 0 10 80 10");
        Run(first, fx, "/protect allow farm growth");
        Run(first, fx, "/protect allow farm spread");

        var second = new ProtectModule(fx.Context.Permissions, state);

        Assert.Equal(first.Regions.Snapshot.ToArray(), second.Regions.Snapshot.ToArray());
        Assert.True(Allowed(second, EnvironmentCause.Growth));
        Assert.True(Allowed(second, EnvironmentCause.Spread));
        Assert.False(Allowed(second, EnvironmentCause.Decay));
        Assert.Contains("allows growth, spread", Run(second, fx, "/protect list").Message);
    }

    [Fact]
    public void A_version_1_document_is_still_read_and_strict_and_an_unknown_cause_stops_the_load()
    {
        var fx = new IntentTestFixture();
        var v1 = new MemoryState();
        v1.Saved[ProtectModule.StateName] = Encoding.UTF8.GetBytes("{\"Version\":1,\"Regions\":[{\"Name\":\"old\",\"Min\":[0,0,0],\"Max\":[9,99,9]}]}");
        var module = new ProtectModule(fx.Context.Permissions, v1);
        Assert.False(Allowed(module, EnvironmentCause.Growth));

        var bad = new MemoryState();
        bad.Saved[ProtectModule.StateName] = Encoding.UTF8.GetBytes("{\"Version\":2,\"Regions\":[{\"Name\":\"x\",\"Min\":[0,0,0],\"Max\":[9,99,9],\"Allow\":[\"Sunshine\"]}]}");
        Assert.Throws<InvalidDataException>(() => new ProtectModule(fx.Context.Permissions, bad));
    }
}
