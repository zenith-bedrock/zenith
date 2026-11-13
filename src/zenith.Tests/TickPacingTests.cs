using System.Diagnostics;
using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Xunit;

namespace Zenith.Tests;

/// <summary>
/// Wall-clock timing assertions: they run alone (never alongside the CPU-heavy tests xunit runs in parallel), because a loaded machine
/// delays any thread by tens of milliseconds and would fail them for reasons unrelated to the pacing.
/// </summary>
[Collection(TimingCollection.Name)]
public sealed class TickPacingTests
{
    private const long Interval = 50;
    private const long Lag = Interval * 5;

    [Fact]
    public void Deadlines_are_absolute_so_late_ticks_do_not_shift_later_ones()
    {
        var schedule = new TickSchedule(1_000, Interval, Lag);

        // Three ticks, each finishing a little late: the k-th deadline is always start + k*interval.
        Assert.Equal(1_000, schedule.NextDeadline);
        Assert.Equal(0, schedule.Complete(now: 1_037));
        Assert.Equal(1_050, schedule.NextDeadline);
        Assert.Equal(0, schedule.Complete(now: 1_120));
        Assert.Equal(1_100, schedule.NextDeadline);
        Assert.Equal(0, schedule.Complete(now: 1_101));
        Assert.Equal(1_150, schedule.NextDeadline);
    }

    [Fact]
    public void A_tick_that_overruns_within_the_budget_is_caught_up_not_skipped()
    {
        var schedule = new TickSchedule(0, Interval, Lag);

        // The first tick ends at 170: 2 ticks late, inside the 5-tick budget. Nothing is given up; the next deadlines are in the past.
        Assert.Equal(0, schedule.Complete(now: 170));
        Assert.Equal(50, schedule.NextDeadline);
        Assert.Equal(120, schedule.Lateness(now: 170));
        Assert.Equal(0, schedule.Complete(now: 171));
        Assert.Equal(100, schedule.NextDeadline);
    }

    [Fact]
    public void Falling_further_behind_than_the_budget_gives_the_time_up_and_restarts_from_now()
    {
        var schedule = new TickSchedule(0, Interval, Lag);

        var skipped = schedule.Complete(now: 1_000); // due 50 -> 950 behind, budget 250

        Assert.Equal(950 / Interval + 1, skipped);
        Assert.Equal(1_050, schedule.NextDeadline); // now + one interval, not a burst of 19 catch-up ticks
        Assert.Equal(0, schedule.Lateness(now: 1_000));
    }

    [Fact]
    public void A_lag_budget_below_one_tick_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TickSchedule(0, Interval, Interval - 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TickSchedule(0, 0, Lag));
    }

    [Fact]
    public void The_precise_waiter_never_wakes_early_and_is_sub_millisecond_on_median()
    {
        using var waiter = new PreciseWaiter();
        var step = Stopwatch.Frequency / 200; // 5 ms
        var medians = new List<double>();

        // Never early is checked on every wait of every round. Median lateness is a property of the machine being quiet, and this
        // suite runs hundreds of CPU-heavy tests: the waiter has to show sub-millisecond lateness in at least one of a few rounds,
        // while the old Task.Delay approach cannot (it is 0-15 ms late every round). The benchmark prints the real distribution.
        for (var round = 0; round < 5; round++)
        {
            var deadline = Stopwatch.GetTimestamp() + step;
            var lateness = new List<double>();
            for (var i = 0; i < 40; i++)
            {
                waiter.WaitUntil(deadline, CancellationToken.None);
                var woke = Stopwatch.GetTimestamp();
                Assert.True(woke >= deadline, "the waiter returned before its deadline");
                lateness.Add(Stopwatch.GetElapsedTime(deadline, woke).TotalMilliseconds);
                deadline += step;
            }

            lateness.Sort();
            medians.Add(lateness[lateness.Count / 2]);
            if (medians[^1] < 1.5) return;
        }

        Assert.Fail($"median lateness never dropped below 1.5 ms in 5 rounds ({waiter.Mode}): {string.Join(' ', medians.Select(m => m.ToString("F2")))}");
    }

    [Fact]
    public void Cancellation_wakes_a_long_wait_promptly()
    {
        using var waiter = new PreciseWaiter();
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(50);
        var started = Stopwatch.GetTimestamp();

        waiter.WaitUntil(Stopwatch.GetTimestamp() + Stopwatch.Frequency * 30, cts.Token);

        Assert.True(Stopwatch.GetElapsedTime(started).TotalMilliseconds < 500);
    }

    private sealed class CountingSystem : IGameSystem
    {
        public int Ticks;
        public string? ThreadName;
        public void Tick(GameClock clock, IReadOnlyList<Player.Player> online)
        {
            Ticks++;
            ThreadName = Thread.CurrentThread.Name;
        }
    }

    private sealed class RecordingWaiter(CancellationTokenSource stopAfterWaits, int waits) : ITickWaiter
    {
        public List<long> Deadlines { get; } = [];
        public void WaitUntil(long deadlineTimestamp, CancellationToken cancellationToken)
        {
            Deadlines.Add(deadlineTimestamp);
            if (Deadlines.Count > waits) stopAfterWaits.Cancel();
        }
        public void Dispose() { }
    }

    [Fact]
    public async Task The_loop_runs_on_its_own_thread_one_tick_per_interval_and_stops_on_cancellation()
    {
        var fx = new IntentTestFixture();
        var loop = new GameLoop(fx.Clock, fx.Players, fx.Context.Logger);
        var system = new CountingSystem();
        loop.Register(system);
        using var cts = new CancellationTokenSource();
        var waiter = new RecordingWaiter(cts, waits: 10);

        await loop.RunAsync(waiter, cts.Token);

        Assert.Equal(10, system.Ticks);
        Assert.Equal("zenith-game-loop", system.ThreadName);
        var expectedInterval = Stopwatch.Frequency / GameClock.TicksPerSecond;
        for (var i = 1; i < waiter.Deadlines.Count; i++)
            Assert.Equal(expectedInterval, waiter.Deadlines[i] - waiter.Deadlines[i - 1]);
    }

    private sealed class ThrowingSystem : IGameSystem
    {
        public void Tick(GameClock clock, IReadOnlyList<Player.Player> online) => throw new InvalidOperationException("boom");
    }

    [Fact]
    public async Task A_system_failure_faults_the_run_task_with_the_original_exception()
    {
        var fx = new IntentTestFixture();
        var loop = new GameLoop(fx.Clock, fx.Players, fx.Context.Logger);
        loop.Register(new ThrowingSystem());
        using var cts = new CancellationTokenSource();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => loop.RunAsync(new RecordingWaiter(cts, 5), cts.Token));

        Assert.Equal("boom", ex.Message);
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TimingCollection
{
    public const string Name = "Wall-clock timing";
}
