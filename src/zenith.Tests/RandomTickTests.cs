using Xunit;
using Zenith.Gameplay.Runtime;
using Zenith.Gameplay.WorldInteraction;
using Zenith.Gameplay.WorldInteraction.RandomTicks;
using Zenith.World;

namespace Zenith.Tests;

public sealed class SectionTickCountTests
{
    public SectionTickCountTests() => Blocks.EnsureLoaded();

    [Fact]
    public void Grass_is_a_random_ticking_block_and_dirt_is_not()
    {
        Assert.True(Blocks.IsRandomTicking(Blocks.GrassBlock));
        Assert.False(Blocks.IsRandomTicking(Blocks.Dirt));
        Assert.False(Blocks.IsRandomTicking(Blocks.Air));
    }

    [Fact]
    public void A_uniform_section_counts_all_or_none()
    {
        Assert.Equal(Section.Volume, Section.Uniform(Blocks.GrassBlock).RandomTickableCount);
        Assert.Equal(0, Section.Uniform(Blocks.Stone).RandomTickableCount);
    }

    [Fact]
    public void FromIds_counts_the_tickable_cells()
    {
        var ids = new int[Section.Volume];
        Array.Fill(ids, Blocks.Stone);
        ids[5] = Blocks.GrassBlock;
        ids[900] = Blocks.GrassBlock;
        Assert.Equal(2, Section.FromIds(ids).RandomTickableCount);
    }

    [Fact]
    public void Set_keeps_the_count_exact_in_both_directions()
    {
        var section = Section.Uniform(Blocks.Stone);
        section.Set(10, Blocks.GrassBlock);
        section.Set(11, Blocks.GrassBlock);
        Assert.Equal(2, section.RandomTickableCount);

        section.Set(10, Blocks.Dirt);
        section.Set(11, Blocks.Air);
        Assert.Equal(0, section.RandomTickableCount);
    }

    [Fact]
    public void A_column_sleeps_until_a_section_gets_a_tickable_block()
    {
        var column = new WorldColumn(new ChunkCoord(0, 0), 0, 1);
        column.Set(1, 0, 1, Blocks.Stone);
        Assert.Equal(0, column.RandomTickMask);

        column.Set(2, 0, 2, Blocks.GrassBlock); // section 4
        Assert.Equal(1 << 4, column.RandomTickMask);

        column.Set(2, 0, 2, Blocks.Dirt);
        Assert.Equal(0, column.RandomTickMask);
    }

    [Fact]
    public void Every_RandomTick_block_has_a_behaviour_and_the_reverse()
    {
        RandomTickBehaviors.Default().Validate();
    }

    [Fact]
    public void A_behaviour_for_an_unmarked_block_is_refused_at_startup()
    {
        var behaviors = RandomTickBehaviors.Default();
        behaviors.Register(Blocks.Stone, new GrassSpreadBehavior());
        Assert.Throws<InvalidOperationException>(behaviors.Validate);
    }
}

public sealed class RandomTickSystemTests
{
    public RandomTickSystemTests() => Blocks.EnsureLoaded();

    private static void Run(IntentTestFixture fx, RandomTickSystem system, int ticks)
    {
        var clock = new GameClock();
        var online = fx.Players.Online;
        for (var i = 0; i < ticks; i++)
        {
            clock.AdvanceBy(1);
            system.Tick(clock, online);
        }
    }

    /// <summary>A section has 4096 cells, so one sample per tick finds a given block about once in 4096 ticks; tests sample densely instead of waiting.</summary>
    private const string DenseSampling = "4096";

    private static async Task<IntentTestFixture> FixtureWithViewedColumn()
    {
        var fx = new IntentTestFixture();
        Assert.True(fx.World.Rules.TrySet(Rulebook.GameRules.RandomTickSpeedName, DenseSampling, out _));
        fx.AddInGamePlayer("ticker");
        await fx.World.GetOrCreateColumnAsync(0, 0);
        fx.World.ChunkResidency.Acquire(0, 0);
        return fx;
    }

    [Fact]
    public async Task Grass_spreads_to_dirt_beside_it()
    {
        var fx = await FixtureWithViewedColumn();
        fx.World.SetBlock(5, 64, 5, Blocks.Stone);
        fx.World.SetBlock(6, 64, 5, Blocks.Dirt);
        fx.World.SetBlock(5, 64, 5, Blocks.GrassBlock);
        var system = new RandomTickSystem(fx.World, RandomTickBehaviors.Default(), new Random(1));

        Run(fx, system, 100);

        Assert.Equal(Blocks.GrassBlock, fx.World.GetBlock(6, 64, 5));
    }

    [Fact]
    public async Task Grass_does_not_spread_to_dirt_with_a_block_on_top()
    {
        var fx = await FixtureWithViewedColumn();
        fx.World.SetBlock(5, 64, 5, Blocks.GrassBlock);
        fx.World.SetBlock(6, 64, 5, Blocks.Dirt);
        fx.World.SetBlock(6, 65, 5, Blocks.Stone);
        var system = new RandomTickSystem(fx.World, RandomTickBehaviors.Default(), new Random(1));

        Run(fx, system, 100);

        Assert.Equal(Blocks.Dirt, fx.World.GetBlock(6, 64, 5));
    }

    [Fact]
    public async Task A_column_nobody_views_is_not_ticked()
    {
        var fx = new IntentTestFixture();
        fx.AddInGamePlayer("far-away");
        Assert.True(fx.World.Rules.TrySet(Rulebook.GameRules.RandomTickSpeedName, DenseSampling, out _));
        await fx.World.GetOrCreateColumnAsync(0, 0);
        fx.World.SetBlock(5, 64, 5, Blocks.GrassBlock);
        fx.World.SetBlock(6, 64, 5, Blocks.Dirt);
        var system = new RandomTickSystem(fx.World, RandomTickBehaviors.Default(), new Random(1));

        Run(fx, system, 100);

        Assert.Equal(Blocks.Dirt, fx.World.GetBlock(6, 64, 5));
    }

    [Fact]
    public async Task Ticking_never_loads_a_chunk()
    {
        var fx = await FixtureWithViewedColumn();
        var system = new RandomTickSystem(fx.World, RandomTickBehaviors.Default(), new Random(1));
        var before = fx.World.SyncLoadCount;

        Run(fx, system, 500);

        Assert.Equal(before, fx.World.SyncLoadCount);
    }

    [Fact]
    public async Task Speed_zero_turns_random_ticks_off()
    {
        var fx = await FixtureWithViewedColumn();
        fx.World.SetBlock(5, 64, 5, Blocks.GrassBlock);
        fx.World.SetBlock(6, 64, 5, Blocks.Dirt);
        Assert.True(fx.World.Rules.TrySet(Rulebook.GameRules.RandomTickSpeedName, "0", out _));
        var system = new RandomTickSystem(fx.World, RandomTickBehaviors.Default(), new Random(1));

        Run(fx, system, 100);

        Assert.Equal(Blocks.Dirt, fx.World.GetBlock(6, 64, 5));
    }
}
