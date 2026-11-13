using System.Diagnostics;
using Zenith.Gameplay.Runtime;

namespace Zenith.Benchmarks;

/// <summary>
/// Measures how punctual the tick pacing is: for each tick, how far after its deadline the tick actually started, and what the pacing
/// costs in CPU. Compares the previous pacing (<c>Task.Delay</c> against <c>Environment.TickCount64</c>) with <see cref="PreciseWaiter"/>
/// on the same machine, with an empty tick so only the pacing is measured.
/// <code>dotnet run -c Release --project src/zenith.Benchmarks -- --tick-jitter [seconds]</code>
/// </summary>
static class TickJitterHarness
{
    public static int Run(string[] args)
    {
        var seconds = args.Length > 0 && int.TryParse(args[0], out var s) ? s : 10;
        var ticks = seconds * GameClock.TicksPerSecond;
        Console.WriteLine($"Tick pacing at {GameClock.TicksPerSecond} TPS for {seconds}s ({ticks} ticks), empty tick. OS={Environment.OSVersion} cpus={Environment.ProcessorCount}");

        Report("Task.Delay + TickCount64 (previous)", MeasureTaskDelay(ticks));
        using var waiter = new PreciseWaiter();
        Report($"PreciseWaiter [{waiter.Mode}, spin margin {waiter.SpinMargin.TotalMilliseconds} ms] (current)", MeasurePrecise(ticks, waiter));
        return 0;
    }

    private static (double[] LatenessMs, double CpuMs, double WallMs) MeasureTaskDelay(int ticks)
    {
        var lateness = new double[ticks];
        var cpu0 = Process.GetCurrentProcess().TotalProcessorTime;
        var wall = Stopwatch.StartNew();
        var interval = TimeSpan.FromMilliseconds(1000.0 / GameClock.TicksPerSecond);
        var loopStart = TimeSpan.FromMilliseconds(Environment.TickCount64);
        var startTs = Stopwatch.GetTimestamp();
        for (var i = 0; i < ticks; i++)
        {
            // The tick would run here. Lateness is read against the same absolute schedule on a precise clock.
            var due = startTs + (long)(i * Stopwatch.Frequency / (double)GameClock.TicksPerSecond);
            lateness[i] = Math.Max(0, Stopwatch.GetElapsedTime(due).TotalMilliseconds);
            var deadline = loopStart + interval * (i + 1);
            var delay = deadline - TimeSpan.FromMilliseconds(Environment.TickCount64);
            if (delay > TimeSpan.Zero) Task.Delay(delay).GetAwaiter().GetResult();
        }

        return (lateness, (Process.GetCurrentProcess().TotalProcessorTime - cpu0).TotalMilliseconds, wall.Elapsed.TotalMilliseconds);
    }

    private static (double[] LatenessMs, double CpuMs, double WallMs) MeasurePrecise(int ticks, PreciseWaiter waiter)
    {
        var lateness = new double[ticks];
        var cpu0 = Process.GetCurrentProcess().TotalProcessorTime;
        var wall = Stopwatch.StartNew();
        var interval = Stopwatch.Frequency / GameClock.TicksPerSecond;
        var schedule = new TickSchedule(Stopwatch.GetTimestamp(), interval, interval * GameLoop.MaxCatchUpTicks);
        for (var i = 0; i < ticks; i++)
        {
            waiter.WaitUntil(schedule.NextDeadline, CancellationToken.None);
            lateness[i] = Stopwatch.GetElapsedTime(schedule.NextDeadline).TotalMilliseconds;
            schedule.Complete(Stopwatch.GetTimestamp());
        }

        return (lateness, (Process.GetCurrentProcess().TotalProcessorTime - cpu0).TotalMilliseconds, wall.Elapsed.TotalMilliseconds);
    }

    private static void Report(string name, (double[] LatenessMs, double CpuMs, double WallMs) r)
    {
        var sorted = r.LatenessMs.Order().ToArray();
        double P(double q) => sorted[Math.Min(sorted.Length - 1, (int)(q * sorted.Length))];
        Console.WriteLine($"\n{name}");
        Console.WriteLine($"  lateness ms  p50={P(0.5):F3}  p95={P(0.95):F3}  p99={P(0.99):F3}  max={sorted[^1]:F3}  mean={sorted.Average():F3}");
        var outliers = r.LatenessMs.Select((l, i) => (l, i)).Where(x => x.l > 2).Take(8).Select(x => $"#{x.i}:{x.l:F1}ms");
        Console.WriteLine($"  >2ms ticks   {r.LatenessMs.Count(l => l > 2)} {string.Join(' ', outliers)}");
        Console.WriteLine($"  rate         {sorted.Length / (r.WallMs / 1000.0):F3} ticks/s (target {GameClock.TicksPerSecond})");
        Console.WriteLine($"  cpu          {r.CpuMs:F0} ms over {r.WallMs:F0} ms wall = {r.CpuMs / r.WallMs * 100:F1}% of one core");
    }
}
