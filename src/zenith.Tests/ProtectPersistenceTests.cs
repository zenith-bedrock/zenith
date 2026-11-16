using Xunit;
using Zenith.Gameplay.Commands;
using Zenith.Gameplay.Modules;
using Zenith.Modules.Protect;
using Zenith.Server.Composition;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>
/// Regions are kept with the world: they survive a restart, a damaged record stops the server instead of silently dropping
/// protection, and the world stores the module's bytes without understanding them.
/// </summary>
public sealed class ProtectPersistenceTests
{
    private sealed class MemoryState : IModuleStateStore
    {
        public Dictionary<string, byte[]> Saved { get; } = [];
        public int Saves { get; private set; }

        public byte[]? Load(string module) => Saved.GetValueOrDefault(module);

        public void Save(string module, byte[] state)
        {
            Saves++;
            Saved[module] = state;
        }
    }

    private static CommandFeedback Run(ProtectModule module, IntentTestFixture fx, string line)
    {
        var admin = fx.Players.Get("admin") ?? fx.AddOperator("admin");
        var runtime = new CommandRuntime(fx.Players, fx.Context.Permissions, contributions: [module.Command]);
        return runtime.Execute(admin, line);
    }

    [Fact]
    public void A_created_region_is_there_after_a_restart()
    {
        var fx = new IntentTestFixture();
        var state = new MemoryState();
        var first = new ProtectModule(fx.Context.Permissions, state);
        Assert.Equal(CommandOutcome.Applied, Run(first, fx, "/protect create spawn 10 80 10 0 60 0").Outcome);
        Assert.Equal(CommandOutcome.Applied, Run(first, fx, "/protect create shop -5 0 -5 5 90 5").Outcome);

        var second = new ProtectModule(fx.Context.Permissions, state);

        Assert.Equal(first.Regions.Snapshot.ToArray(), second.Regions.Snapshot.ToArray());
        Assert.True(second.Regions.TryFind(5, 70, 5, out var found));
        Assert.Equal("spawn", found.Name);
    }

    [Fact]
    public void A_removed_region_stays_removed_after_a_restart()
    {
        var fx = new IntentTestFixture();
        var state = new MemoryState();
        var first = new ProtectModule(fx.Context.Permissions, state);
        Run(first, fx, "/protect create spawn 0 0 0 1 1 1");
        Run(first, fx, "/protect remove spawn");

        Assert.True(new ProtectModule(fx.Context.Permissions, state).Regions.Snapshot.IsEmpty);
    }

    [Fact]
    public void A_rejected_change_saves_nothing()
    {
        var fx = new IntentTestFixture();
        var state = new MemoryState();
        var module = new ProtectModule(fx.Context.Permissions, state);
        Run(module, fx, "/protect create spawn 0 0 0 1 1 1");
        var saves = state.Saves;

        Assert.Equal(CommandOutcome.Rejected, Run(module, fx, "/protect create spawn 5 5 5 6 6 6").Outcome);
        Assert.Equal(CommandOutcome.Rejected, Run(module, fx, "/protect remove nothing").Outcome);

        Assert.Equal(saves, state.Saves);
    }

    [Fact]
    public void Nothing_saved_yet_means_no_regions()
    {
        Assert.True(new ProtectModule(new IntentTestFixture().Context.Permissions, new MemoryState()).Regions.Snapshot.IsEmpty);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"version\":99,\"regions\":[]}")]
    [InlineData("{\"version\":1,\"regions\":[{\"name\":\"a\",\"min\":[0,0],\"max\":[1,1,1]}]}")]
    [InlineData("{\"version\":1,\"regions\":[{\"name\":\"a\",\"min\":[0,0,0],\"max\":[1,1,1]},{\"name\":\"A\",\"min\":[0,0,0],\"max\":[1,1,1]}]}")]
    public void Saved_bytes_that_cannot_be_trusted_fail_startup_instead_of_dropping_protection(string content)
    {
        var state = new MemoryState();
        state.Saved[ProtectModule.StateName] = System.Text.Encoding.UTF8.GetBytes(content);

        Assert.Throws<InvalidDataException>(() => new ProtectModule(new IntentTestFixture().Context.Permissions, state));
    }

    [Fact]
    public void Regions_kept_in_the_real_world_storage_survive_a_restart()
    {
        var fx = new IntentTestFixture();
        var first = new ProtectModule(fx.Context.Permissions, new WorldModuleStateStore(fx.World));
        Run(first, fx, "/protect create spawn 0 60 0 10 80 10");

        var second = new ProtectModule(fx.Context.Permissions, new WorldModuleStateStore(fx.World));

        Assert.Equal(first.Regions.Snapshot.ToArray(), second.Regions.Snapshot.ToArray());
    }

    [Fact]
    public void Modules_get_separate_records_that_cannot_be_mistaken_for_other_kinds()
    {
        var fx = new IntentTestFixture();
        fx.World.PersistModuleState("a", [1]);
        fx.World.PersistModuleState("b", [2]);

        Assert.Equal([1], fx.World.LoadModuleState("a"));
        Assert.Equal([2], fx.World.LoadModuleState("b"));
        Assert.Null(fx.World.LoadModuleState("c"));
        Assert.Equal(RecordKind.ModuleState, RecordKey.Module("a").Kind);
        Assert.NotEqual(RecordKey.Module("a"), RecordKey.Module("b"));
        Assert.Equal(1 + sizeof(long), RecordKey.Module("a").ToBytes().Length);
        Assert.Equal((byte)RecordKind.ModuleState, RecordKey.Module("a").ToBytes()[0]);
    }
}
