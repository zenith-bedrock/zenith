using Xunit;
using Zenith.Gameplay.Policy;
using Zenith.Gameplay.Runtime;
using Zenith.Gameplay.WorldInteraction;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>
/// Plan 002, phase 4b: gravity asks the environment policies. A refused cell behaves like ground (a block does not start to fall
/// into it, does not pass it, does not land in it), a refused origin leaves the block where it is, and no refusal ever removes a
/// block: at every tick there is exactly one representation of each block that started. Each refusal has a control run without it.
/// </summary>
public sealed class EnvironmentGravityTests
{
    private const int X = 0;
    private const int Z = 0;
    private static readonly int Ground = Blocks.FlatGrassY;
    private static readonly int Origin = Ground + 6;

    public EnvironmentGravityTests() => Blocks.EnsureLoaded();

    /// <summary>A veto over a set of cells (plus an optional rule), standing in for a region.</summary>
    private sealed class Veto : IEnvironmentChangePolicy
    {
        public Func<int, int, int, int, bool> Denies { get; set; } = (_, _, _, _) => false;
        public int Asked;

        public PolicyDecision Evaluate(in EnvironmentChangeProposal proposal)
        {
            if (proposal.Cause != EnvironmentCause.Gravity) return PolicyDecision.Allow;
            Asked++;
            var c = proposal.Cell;
            return Denies(c.X, c.Y, c.Z, c.After) ? PolicyDecision.Deny("protected") : PolicyDecision.Allow;
        }
    }

    private sealed class Throwing : IEnvironmentChangePolicy
    {
        public PolicyDecision Evaluate(in EnvironmentChangeProposal proposal) => throw new InvalidOperationException("boom");
    }

    private sealed class Rig
    {
        public required World.World World { get; init; }
        public required GravitySystem Gravity { get; init; }
        public required RecordingLogger Log { get; init; }
        public required Veto Region { get; init; }
        public GameClock Clock { get; } = new();

        public static Rig Boot(bool withVeto = true, IEnvironmentChangePolicy? extra = null)
        {
            var log = new RecordingLogger();
            var world = new World.World(new InMemoryChunkStorage(), log);
            var policies = new EnvironmentChangePolicies();
            var region = new Veto();
            if (withVeto) policies.Register(region);
            if (extra is not null) policies.Register(extra);
            policies.Freeze();
            return new Rig { World = world, Gravity = new GravitySystem(world, new PlayerManager(), policies, log), Log = log, Region = region };
        }

        public void Tick()
        {
            Clock.AdvanceBy(1);
            Gravity.Tick(Clock, Array.Empty<Zenith.Player.Player>());
        }

        public List<string> Errors() => Log.Lines.Where(l => l.StartsWith("error")).ToList();

        public bool IsQuiet() => World.GravityPending.Count == 0 && World.FallingBlocks.Active.Count == 0;

        public void TickUntilQuiet(int budget = 300)
        {
            for (var i = 0; i < budget && !IsQuiet(); i++) Tick();
            Assert.True(IsQuiet(), "gravity did not settle");
        }

        public void Drop(int y, int block = 0)
        {
            World.SetBlock(X, y, Z, block == 0 ? Blocks.Sand : block);
            Assert.True(World.GravityPending.TryEnqueue(X, y, Z));
        }

        public int Count(int block)
        {
            var found = 0;
            for (var y = WorldColumn.MinY; y <= WorldColumn.MaxY; y++)
                if (World.GetBlock(X, y, Z) == block) found++;
            return found;
        }

        public int Representations(int block) => Count(block) + World.FallingBlocks.Active.Count(e => e.BlockRuntimeId == block);

        public int? HighestSand()
        {
            for (var y = WorldColumn.MaxY; y >= WorldColumn.MinY; y--)
                if (World.GetBlock(X, y, Z) == Blocks.Sand) return y;
            return null;
        }
    }

    [Fact]
    public void A_vetoed_origin_leaves_the_block_where_it_is_and_the_world_goes_quiet()
    {
        var control = Rig.Boot();
        control.Drop(Origin);
        control.TickUntilQuiet();
        Assert.Equal(Ground + 1, control.HighestSand());

        var rig = Rig.Boot();
        rig.Region.Denies = (_, y, _, _) => y == Origin;
        rig.Drop(Origin);
        rig.TickUntilQuiet();

        Assert.Equal(Origin, rig.HighestSand());
        Assert.Equal(1, rig.Count(Blocks.Sand));
        Assert.Empty(rig.World.FallingBlocks.Active);
        Assert.Equal(0, rig.World.GravityPending.Count);
        Assert.Empty(rig.Errors());

        // Nothing replays when the protection is lifted, and more ticks change nothing.
        rig.Region.Denies = (_, _, _, _) => false;
        for (var i = 0; i < 100; i++) rig.Tick();
        Assert.Equal(Origin, rig.HighestSand());
    }

    [Fact]
    public void A_protected_cell_below_acts_as_ground_and_the_block_rests_above_it()
    {
        var control = Rig.Boot();
        control.Drop(Origin);
        control.TickUntilQuiet();
        Assert.Equal(Ground + 1, control.HighestSand());

        var rig = Rig.Boot();
        rig.Region.Denies = (_, y, _, _) => y == Ground + 1;
        rig.Drop(Origin);
        rig.TickUntilQuiet();

        Assert.Equal(Ground + 2, rig.HighestSand());
        Assert.Equal(Blocks.Air, rig.World.GetBlock(X, Ground + 1, Z)); // the protected cell is untouched
        Assert.Equal(1, rig.Representations(Blocks.Sand));
    }

    [Fact]
    public void A_block_next_to_protected_ground_does_not_start_to_fall_into_it()
    {
        var rig = Rig.Boot();
        rig.Region.Denies = (_, y, _, _) => y == Ground + 1;
        rig.Drop(Ground + 2); // its only way down is the protected cell
        rig.Tick();

        Assert.Equal(Ground + 2, rig.HighestSand());
        Assert.Empty(rig.World.FallingBlocks.Active);
        Assert.True(rig.IsQuiet());
    }

    [Fact]
    public void A_region_created_mid_fall_makes_the_block_rest_above_it()
    {
        var rig = Rig.Boot();
        rig.Drop(Origin);
        rig.Tick();
        rig.Tick();
        Assert.Single(rig.World.FallingBlocks.Active);

        rig.Region.Denies = (_, y, _, _) => y == Ground + 1;
        rig.TickUntilQuiet();

        Assert.Equal(Ground + 2, rig.HighestSand());
        Assert.Equal(1, rig.Representations(Blocks.Sand));
    }

    [Fact]
    public void When_no_cell_may_take_the_block_it_stays_in_flight_is_logged_once_and_is_never_removed()
    {
        var rig = Rig.Boot();
        rig.Drop(Origin);
        rig.Tick();
        Assert.Single(rig.World.FallingBlocks.Active);

        rig.Region.Denies = (_, y, _, after) => y > Ground && after == Blocks.Sand; // everything above the ground, up to the sky
        for (var i = 0; i < 200; i++)
        {
            rig.Tick();
            Assert.Equal(1, rig.Representations(Blocks.Sand));
        }

        var entry = Assert.Single(rig.World.FallingBlocks.Active);
        Assert.True(entry.IsStuck);
        Assert.Single(rig.Errors());

        // Lifting the protection lets the retry land it; nothing was lost in between.
        rig.Region.Denies = (_, _, _, _) => false;
        rig.TickUntilQuiet();
        Assert.Equal(1, rig.Count(Blocks.Sand));
        Assert.Empty(rig.World.FallingBlocks.Active);
    }

    [Fact]
    public void A_full_store_leaves_the_block_at_its_origin_and_nothing_is_lost()
    {
        var rig = Rig.Boot();
        var dummies = new List<FallingBlockEntry>();
        for (var i = 0; i < FallingBlockStore.SoftCap; i++)
        {
            var dummy = new FallingBlockEntry
            {
                EntityId = 1000 + i, RuntimeId = (ulong)(1000 + i), BlockRuntimeId = Blocks.Gravel,
                LandX = 500, LandY = 100, LandZ = 500, Y = 100, IsStuck = true, RetryIn = int.MaxValue
            };
            Assert.True(rig.World.FallingBlocks.TrySpawn(dummy));
            dummies.Add(dummy);
        }

        rig.Drop(Origin);
        for (var i = 0; i < 5; i++)
        {
            rig.Tick();
            Assert.Equal(Origin, rig.HighestSand());
            Assert.Equal(1, rig.Representations(Blocks.Sand));
        }

        foreach (var dummy in dummies) rig.World.FallingBlocks.Remove(dummy);
        for (var i = 0; i < 300 && (rig.World.GravityPending.Count > 0 || rig.World.FallingBlocks.Active.Count > 0); i++) rig.Tick();

        Assert.Equal(Ground + 1, rig.HighestSand());
        Assert.Equal(1, rig.Representations(Blocks.Sand));
    }

    [Fact]
    public void A_shutdown_settle_under_a_region_keeps_every_block()
    {
        var rig = Rig.Boot();
        rig.Drop(Origin);
        rig.Tick();
        rig.Tick();
        rig.Drop(Origin + 1);
        rig.Region.Denies = (_, y, _, _) => y == Ground + 1;

        rig.Gravity.SettleAllPending();

        Assert.Empty(rig.World.FallingBlocks.Active);
        Assert.Equal(2, rig.Count(Blocks.Sand));
        Assert.Equal(Ground + 3, rig.HighestSand());
        Assert.True(rig.World.TryGetLoadedColumn(0, 0, out var column));
        Assert.Empty(column.PendingFalls);
    }

    [Fact]
    public void A_stack_keeps_every_block_at_every_tick_while_a_region_appears_in_its_path()
    {
        var rig = Rig.Boot();
        rig.Drop(Origin);
        rig.Drop(Origin + 1);
        rig.Drop(Origin + 2);
        for (var i = 0; i < 400; i++)
        {
            rig.Tick();
            if (i == 4) rig.Region.Denies = (_, y, _, _) => y == Ground + 2;
            Assert.Equal(3, rig.Representations(Blocks.Sand));
            if (rig.IsQuiet()) break;
        }

        Assert.True(rig.IsQuiet());
        Assert.Equal(3, rig.Count(Blocks.Sand));
        Assert.Equal(Blocks.Air, rig.World.GetBlock(X, Ground + 2, Z));
        Assert.Equal(Ground + 5, rig.HighestSand());
    }

    [Fact]
    public void A_policy_that_throws_denies_safely_and_loses_nothing()
    {
        var rig = Rig.Boot(withVeto: false, extra: new Throwing());
        rig.Drop(Origin);
        for (var i = 0; i < 50; i++)
        {
            rig.Tick();
            Assert.Equal(1, rig.Representations(Blocks.Sand));
        }

        Assert.Equal(Origin, rig.HighestSand()); // the origin question was refused, so it never began to fall
        Assert.Empty(rig.World.FallingBlocks.Active);
    }

    [Fact]
    public void Gravity_asks_nothing_when_nothing_falls()
    {
        var rig = Rig.Boot();
        for (var i = 0; i < 100; i++) rig.Tick();
        Assert.Equal(0, rig.Region.Asked);
    }
}
