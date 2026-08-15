using System.Diagnostics;
using Zenith.Server;
using Zenith.World;

namespace Zenith.Benchmarks;

/// <summary>
/// Scenario benchmark for world-generation scale. This is intentionally a harness instead of a
/// BenchmarkDotNet job: radius 256 represents 263,169 columns and must have cancellation/budgets.
/// Run with <c>--worldgen-scale</c>.
/// </summary>
internal static class WorldgenScaleHarness
{
    private const int Seed = 42;
    private const int DefaultTimeoutSeconds = 120;
    private static readonly int[] DefaultRadii = [0, 1, 2, 4, 8, 16, 24, 32, 48, 64, 96, 128, 160, 192, 224, 256];
    private static readonly int[] DefaultPlayerCounts = [1, 2, 4, 8, 16];

    public static int Run(string[] args)
    {
        Blocks.EnsureLoaded();

        var radii = ParseIntList(args, "--radii") ?? DefaultRadii;
        var playerCounts = ParseIntList(args, "--players") ?? DefaultPlayerCounts;
        var timeoutSeconds = ParseInt(args, "--timeout-seconds") ?? DefaultTimeoutSeconds;
        var workers = ParseInt(args, "--workers") ?? 2;
        var includeExtremeMultiplayer = args.Contains("--include-extreme-multiplayer", StringComparer.Ordinal);
        if (timeoutSeconds is <= 0 or > 3_600)
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds), "Timeout must be 1..3600 seconds.");
        if (workers is < 1 or > 64)
            throw new ArgumentOutOfRangeException(nameof(workers), "Workers must be 1..64.");

        Console.WriteLine("scenario,radius,players,workers,expectedChunks,completedPlayers,generated,coalesced,failed,queuePeak,queueBackpressure,surfaceAvgMs,cavesAvgMs,featuresAvgMs,payloadAvgMs,elapsedMs,msPerRequestedChunk,allocatedBytes,workingSetBytes,status");
        foreach (var radius in radii)
        {
            ValidateRadius(radius);
            foreach (var players in playerCounts)
            {
                ValidatePlayers(players);
                // Large squares are useful for asymptotic behavior, but multiplying them by many
                // players would turn a diagnostic run into an uncontrolled OOM experiment.
                if (!includeExtremeMultiplayer && radius > 16 && players > 1)
                    continue;

                RunScenario(radius, players, workers, timeoutSeconds);
            }
        }

        return 0;
    }

    private static void RunScenario(int radius, int players, int workers, int timeoutSeconds)
    {
        var expectedChunks = checked((radius * 2 + 1) * (radius * 2 + 1));
        var diagnostics = new ServerRuntimeDiagnostics();
        var world = new global::Zenith.World.World(
            new InMemoryChunkStorage(),
            terrain: new NoiseTerrainProvider(Seed, diagnostics.Worldgen),
            generationDiagnostics: diagnostics.Worldgen,
            generationWorkers: workers);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));

        var beforeAllocated = GC.GetTotalAllocatedBytes(precise: true);
        var beforeWorkingSet = Environment.WorkingSet;
        var watch = Stopwatch.StartNew();
        var completedPlayers = 0;
        string status;

        try
        {
            var tasks = new Task<int>[players];
            for (var player = 0; player < players; player++)
            {
                // Same center intentionally models simultaneous joins to the same area. The
                // current implementation is a baseline: generation deduplication is measured
                // later against this workload rather than assumed here.
                tasks[player] = CountRadiusAsync(world, radius, timeout.Token);
            }

            Task.WhenAll(tasks).GetAwaiter().GetResult();
            completedPlayers = tasks.Length;
            status = "ok";
        }
        catch (OperationCanceledException)
        {
            status = "timeout-or-cancelled";
        }
        catch (Exception exception)
        {
            status = $"failed:{exception.GetType().Name}";
        }
        finally
        {
            watch.Stop();
        }

        var allocatedBytes = Math.Max(0, GC.GetTotalAllocatedBytes(precise: true) - beforeAllocated);
        var elapsedMs = watch.Elapsed.TotalMilliseconds;
        var requested = (long)expectedChunks * players;
        var msPerRequestedChunk = requested == 0 ? 0d : elapsedMs / requested;
        var snapshot = diagnostics.Runtime.CaptureSnapshot();
        Console.WriteLine(string.Join(',',
            "same-center-joins",
            radius,
            players,
            workers,
            expectedChunks,
            completedPlayers,
            MetricValue(snapshot, "gameplay.worldgen.columns.generated"),
            MetricValue(snapshot, "gameplay.worldgen.columns.coalesced"),
            MetricValue(snapshot, "gameplay.worldgen.columns.failed"),
            MetricValue(snapshot, "gameplay.worldgen.queue.peak"),
            MetricValue(snapshot, "gameplay.worldgen.queue.backpressure"),
            MetricAverageMs(snapshot, "gameplay.worldgen.column.surface"),
            MetricAverageMs(snapshot, "gameplay.worldgen.column.caves"),
            MetricAverageMs(snapshot, "gameplay.worldgen.column.features"),
            MetricAverageMs(snapshot, "gameplay.worldgen.column.payload"),
            elapsedMs.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
            msPerRequestedChunk.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
            allocatedBytes,
            Math.Max(beforeWorkingSet, Environment.WorkingSet),
            status));
    }

    private static async Task<int> CountRadiusAsync(
        global::Zenith.World.World world,
        int radius,
        CancellationToken ct)
    {
        var count = 0;
        await foreach (var _ in world.StreamRadiusAsync(0, 0, radius, ct).ConfigureAwait(false))
            count++;
        return count;
    }

    private static long MetricValue(Zenith.Diagnostics.DiagnosticsSnapshot snapshot, string name) =>
        snapshot.Metrics.First(metric => metric.Name == name).Value;

    private static string MetricAverageMs(Zenith.Diagnostics.DiagnosticsSnapshot snapshot, string name)
    {
        var metric = snapshot.Metrics.First(item => item.Name == name);
        var average = metric.Count == 0
            ? 0d
            : metric.TotalStopwatchTicks * 1_000d / snapshot.StopwatchFrequency / metric.Count;
        return average.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static int[]? ParseIntList(string[] args, string option)
    {
        var value = ParseOption(args, option);
        if (value is null) return null;

        var values = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (values.Length == 0) throw new ArgumentException($"{option} must contain at least one integer.");
        return values.Select(int.Parse).ToArray();
    }

    private static int? ParseInt(string[] args, string option)
    {
        var value = ParseOption(args, option);
        return value is null ? null : int.Parse(value);
    }

    private static string? ParseOption(string[] args, string option)
    {
        for (var index = 0; index < args.Length; index++)
        {
            if (!string.Equals(args[index], option, StringComparison.Ordinal)) continue;
            if (index + 1 >= args.Length) throw new ArgumentException($"Missing value for {option}.");
            return args[index + 1];
        }

        return null;
    }

    private static void ValidateRadius(int radius)
    {
        if (radius is < 0 or > 256)
            throw new ArgumentOutOfRangeException(nameof(radius), "Benchmark radius must be 0..256.");
    }

    private static void ValidatePlayers(int players)
    {
        if (players is < 1 or > 64)
            throw new ArgumentOutOfRangeException(nameof(players), "Benchmark player count must be 1..64.");
    }
}
