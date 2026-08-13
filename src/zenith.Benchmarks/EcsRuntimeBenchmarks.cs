using System.Diagnostics;
using Zenith.Ecs;

namespace Zenith.Benchmarks;

/// <summary>
/// Phase XXI ECS-core micro-benchmarks. Measures the ECS layer in isolation (allocator, component
/// storage, runtime-id lookup) — separate from <see cref="RuntimeLoadHarness"/>, which measures
/// full-tick gameplay (Zombie/Minecart/Projectile) including replication under the production
/// GameLoop. Neither harness substitutes for the other.
/// </summary>
internal static class EcsRuntimeBenchmarks
{
    public static int Run(string[] args)
    {
        var counts = args.Length == 0 ? new[] { 100, 1_000, 10_000 } : args
            .SelectMany(arg => arg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(int.Parse).ToArray();

        foreach (var count in counts) RunLifecycle(count);
        foreach (var count in counts) RunComponentIteration(count);
        foreach (var count in counts) RunRuntimeIdLookup(count);
        return 0;
    }

    /// <summary>Create N, destroy every other one, recreate that half (slot reuse), destroy all.</summary>
    private static void RunLifecycle(int count)
    {
        var world = new EntityWorld();
        var ids = new EntityId[count];

        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var gcBefore = Gc.Capture();
        var started = Stopwatch.GetTimestamp();

        for (var i = 0; i < count; i++) ids[i] = world.Create();
        for (var i = 0; i < count; i += 2) world.Destroy(ids[i]);
        for (var i = 0; i < count; i += 2) ids[i] = world.Create(); // reuses freed slots
        for (var i = 0; i < count; i++) world.Destroy(ids[i]);

        var elapsedMs = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
        var result = Result.From(elapsedMs, GC.GetAllocatedBytesForCurrentThread() - allocated, Gc.Capture() - gcBefore);
        Console.WriteLine($"ecs-lifecycle   entities={count,6} total={result.Ms:F3}ms " +
            $"perEntity={result.Ms * 1000 / (count * 4):F1}us alloc/entity={result.Allocated / (double)(count * 4):F1}B " +
            $"gc={result.Gc.Gen0}/{result.Gc.Gen1}/{result.Gc.Gen2}");
    }

    /// <summary>Iterates a two-component query (Position+Health) over N live entities, M ticks.</summary>
    private static void RunComponentIteration(int count)
    {
        const int ticks = 200;
        var world = new EntityWorld();
        var positions = new ComponentStore<Position>(world);
        var health = new ComponentStore<HealthComponent>(world);

        for (var i = 0; i < count; i++)
        {
            var id = world.Create();
            positions.Set(id, new Position { X = i, Y = 64, Z = 0 });
            if (i % 2 == 0) health.Set(id, new HealthComponent()); // half don't carry Health — exercises the Has-filter path
        }

        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var gcBefore = Gc.Capture();
        var started = Stopwatch.GetTimestamp();

        long sum = 0;
        for (var tick = 0; tick < ticks; tick++)
        {
            var query = Query.With(health, positions);
            foreach (var id in query)
                if (positions.TryGet(id, out var pos))
                    sum += (long)pos.X;
        }

        var elapsedMs = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
        var result = Result.From(elapsedMs, GC.GetAllocatedBytesForCurrentThread() - allocated, Gc.Capture() - gcBefore);
        Console.WriteLine($"ecs-query       entities={count,6} matched={count / 2,6} ticks={ticks} total={result.Ms:F3}ms " +
            $"perTick={result.Ms / ticks:F4}ms alloc/tick={result.Allocated / (double)ticks:F1}B " +
            $"gc={result.Gc.Gen0}/{result.Gc.Gen1}/{result.Gc.Gen2} checksum={sum}");
    }

    /// <summary>RuntimeId -> EntityId resolution, the hot path for incoming Bedrock packets referencing an actor.</summary>
    private static void RunRuntimeIdLookup(int count)
    {
        const int lookupsPerEntity = 50;
        var world = new EntityWorld();
        var index = new RuntimeIdIndex();
        var runtimeIds = new ulong[count];
        for (var i = 0; i < count; i++)
        {
            var id = world.Create();
            var runtimeId = (ulong)(i + 1);
            runtimeIds[i] = runtimeId;
            index.Register(runtimeId, id);
        }

        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var gcBefore = Gc.Capture();
        var started = Stopwatch.GetTimestamp();

        var hits = 0;
        for (var pass = 0; pass < lookupsPerEntity; pass++)
            for (var i = 0; i < count; i++)
                if (index.TryResolve(runtimeIds[i], out _)) hits++;

        var elapsedMs = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
        var totalLookups = (long)count * lookupsPerEntity;
        var result = Result.From(elapsedMs, GC.GetAllocatedBytesForCurrentThread() - allocated, Gc.Capture() - gcBefore);
        Console.WriteLine($"ecs-lookup      entities={count,6} lookups={totalLookups,8} total={result.Ms:F3}ms " +
            $"perLookup={result.Ms * 1000_000 / totalLookups:F1}ns alloc/lookup={result.Allocated / (double)totalLookups:F2}B " +
            $"gc={result.Gc.Gen0}/{result.Gc.Gen1}/{result.Gc.Gen2} hits={hits}");
    }

    private readonly record struct Gc(int Gen0, int Gen1, int Gen2)
    {
        public static Gc Capture() => new(GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
        public static Gc operator -(Gc after, Gc before) => new(after.Gen0 - before.Gen0, after.Gen1 - before.Gen1, after.Gen2 - before.Gen2);
    }

    private readonly record struct Result(double Ms, long Allocated, Gc Gc)
    {
        public static Result From(double ms, long allocated, Gc gc) => new(ms, allocated, gc);
    }
}
