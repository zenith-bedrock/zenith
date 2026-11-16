using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;
using Zenith.Gameplay.WorldInteraction;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>How much a large flood costs the tick (ADR §171): a flat 3x3-chunk floor with many sources, run until it settles.</summary>
[Collection(ExclusiveCollection.Name)]
public sealed class FluidFloodCostTests(ITestOutputHelper output)
{
    private const int Floor = 199;
    private const int Level = 200;
    private const int TicksToRun = 400;

    private readonly record struct Result(double[] Millis, int Water, int Sources, int PeakScheduled, int PeakQueued);

    [Fact]
    public async Task A_flood_over_nine_chunks_keeps_every_tick_inside_the_budget_and_the_queues_under_their_caps()
    {
        _ = await Flood(); // warm-up: JIT and first-touch costs are not the flood's
        var run = await Flood();

        var millis = run.Millis;
        var worst = string.Join(", ", millis.Select((ms, tick) => (ms, tick)).OrderByDescending(t => t.ms).Take(6).Select(t => $"#{t.tick}={t.ms:F1}"));
        output.WriteLine($"worst ticks: {worst}");
        Array.Sort(millis);
        output.WriteLine(
            $"sources={run.Sources} waterCells={run.Water} ticks={TicksToRun} p50={millis[TicksToRun / 2]:F3}ms " +
            $"p99={millis[(int)(TicksToRun * 0.99)]:F3}ms max={millis[^1]:F3}ms peakScheduled={run.PeakScheduled} peakQueued={run.PeakQueued}");

        Assert.True(run.Water > run.Sources * 20, "the flood should have spread");
        Assert.True(run.PeakScheduled <= ScheduledTicks.SoftCap);
        Assert.True(run.PeakQueued <= BlockUpdateQueue.SoftCap);
        var p99 = millis[(int)(TicksToRun * 0.99)];
        Assert.True(p99 < 50, $"p99 tick took {p99:F1} ms, over the 50 ms budget");
    }

    private static async Task<Result> Flood()
    {
        Blocks.EnsureLoaded();
        var fx = new IntentTestFixture();
        for (var cx = -1; cx <= 1; cx++)
            for (var cz = -1; cz <= 1; cz++)
                await fx.World.GetOrCreateColumnAsync(cx, cz);

        for (var x = -16; x < 32; x++)
            for (var z = -16; z < 32; z++)
            {
                fx.World.SetBlock(x, Floor, z, Blocks.Stone);
                for (var y = Level; y < Level + 3; y++) fx.World.SetBlock(x, y, z, Blocks.Air);
            }

        fx.World.BlockUpdates.Clear(); // the floor's own writes are not part of the flood
        var sources = 0;
        for (var x = -12; x < 32; x += 8)
            for (var z = -12; z < 32; z += 8)
            {
                fx.World.SetBlock(x, Level, z, Blocks.Water);
                sources++;
            }

        var system = new BlockUpdateSystem(fx.World, fx.Players, fx.Context.EnvironmentPolicies);
        var millis = new double[TicksToRun];
        var peakScheduled = 0;
        var peakQueued = 0;
        for (var i = 0; i < TicksToRun; i++)
        {
            fx.Clock.AdvanceBy(1);
            var start = Stopwatch.GetTimestamp();
            system.Tick(fx.Clock, fx.Players.Online);
            millis[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            peakScheduled = Math.Max(peakScheduled, fx.World.ScheduledTicks.Count);
            peakQueued = Math.Max(peakQueued, fx.World.BlockUpdates.Count);
        }

        var water = 0;
        for (var x = -16; x < 32; x++)
            for (var z = -16; z < 32; z++)
                if (Blocks.IsFluid(fx.World.GetBlock(x, Level, z))) water++;

        return new Result(millis, water, sources, peakScheduled, peakQueued);
    }
}
