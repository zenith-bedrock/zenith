using System.Diagnostics;
using System.Net;
using Zenith.Event;
using Zenith.Gameplay;
using Zenith.Gameplay.Runtime;
using Zenith.Gameplay.Systems;
using Zenith.Player;
using Zenith.Raknet;
using Zenith.Raknet.Log;
using Zenith.Raknet.Stream;
using Zenith.Server;
using Zenith.Session;
using Zenith.Session.Handler;
using Zenith.World;

namespace Zenith.Benchmarks;

/// <summary>
/// Deterministic, in-process runtime baseline. It executes the production GameLoop ordering
/// with synthetic sessions; it is not a replacement for real Bedrock-client smoke coverage.
/// </summary>
internal static class RuntimeLoadHarness
{
    private const int DefaultTicks = 200;

    public static int Run(string[] args)
    {
        var options = LoadOptions.Parse(args);
        foreach (var playerCount in options.PlayerCounts)
        {
            var steady = RunSteady(playerCount, options.Ticks);
            Print(steady);
            var chunkBurst = RunChunkBurst(playerCount, options.ChunkTicks);
            Print(chunkBurst);
        }

        foreach (var actorCount in options.ActorCounts)
        {
            Print(RunActorChurn(options.ActorPlayers, actorCount, options.ActorTicks, useChunkInterest: false));
            Print(RunActorChurn(options.ActorPlayers, actorCount, options.ActorTicks, useChunkInterest: true));
        }

        return 0;
    }

    private static LoadResult RunSteady(int playerCount, int ticks)
    {
        var host = new RuntimeHost(playerCount, streamChunks: false);
        host.Warmup();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocationBefore = GC.GetAllocatedBytesForCurrentThread();
        var gcBefore = GcCounts.Capture();
        var elapsed = new long[ticks];
        for (var tick = 0; tick < ticks; tick++)
        {
            host.SubmitSteadyInputs(tick);
            var started = Stopwatch.GetTimestamp();
            host.Tick();
            elapsed[tick] = Stopwatch.GetTimestamp() - started;
            host.ValidateSteadyState();
        }

        return LoadResult.Create("steady", playerCount, ticks, elapsed,
            GC.GetAllocatedBytesForCurrentThread() - allocationBefore,
            GcCounts.Capture() - gcBefore,
            host.Transport.Datagrams, host.Transport.Bytes);
    }

    private static LoadResult RunChunkBurst(int playerCount, int ticks)
    {
        var host = new RuntimeHost(playerCount, streamChunks: true);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocationBefore = GC.GetAllocatedBytesForCurrentThread();
        var gcBefore = GcCounts.Capture();
        var elapsed = new long[ticks];
        for (var tick = 0; tick < ticks; tick++)
        {
            var started = Stopwatch.GetTimestamp();
            host.Tick();
            elapsed[tick] = Stopwatch.GetTimestamp() - started;
            Thread.Yield(); // lets the real async column completion publish through its normal handoff.
        }

        return LoadResult.Create("chunk-burst", playerCount, ticks, elapsed,
            GC.GetAllocatedBytesForCurrentThread() - allocationBefore,
            GcCounts.Capture() - gcBefore,
            host.Transport.Datagrams, host.Transport.Bytes);
    }

    private static LoadResult RunActorChurn(int playerCount, int actorCount, int ticks, bool useChunkInterest)
    {
        var host = new RuntimeHost(playerCount, streamChunks: false, includeProjectileSystem: true);
        host.ConfigureActorInterest(useChunkInterest);
        // Both variants use the same actor state. Only the observer knowledge differs.
        host.SeedProjectiles(actorCount, clustered: true);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocationBefore = GC.GetAllocatedBytesForCurrentThread();
        var gcBefore = GcCounts.Capture();
        var elapsed = new long[ticks];
        for (var tick = 0; tick < ticks; tick++)
        {
            host.ReplenishProjectiles(actorCount, clustered: true);
            var started = Stopwatch.GetTimestamp();
            host.Tick();
            elapsed[tick] = Stopwatch.GetTimestamp() - started;
        }

        return LoadResult.Create(useChunkInterest ? "actor-interest" : "actor-global", playerCount, ticks, elapsed,
            GC.GetAllocatedBytesForCurrentThread() - allocationBefore,
            GcCounts.Capture() - gcBefore,
            host.Transport.Datagrams, host.Transport.Bytes,
            actorCount, host.Projectiles!.Projectiles.Active.Count,
            host.Projectiles.ReplicatedSpawnCount, host.Projectiles.RemovalCount, host.Projectiles.ReplicatedMoveCount,
            host.Projectiles.ReplicatedRemovalCount);
    }

    private static void Print(LoadResult result) =>
        Console.WriteLine(
            $"{result.Scenario,-11} players={result.PlayerCount,3} ticks={result.TickCount,3} " +
            $"avg={result.AverageMs:F3}ms p50={result.P50Ms:F3}ms p95={result.P95Ms:F3}ms p99={result.P99Ms:F3}ms max={result.MaxMs:F3}ms " +
            $"alloc={result.AllocatedBytes / (double)result.TickCount:F0}B/tick " +
            $"gc={result.GcCounts.Gen0}/{result.GcCounts.Gen1}/{result.GcCounts.Gen2} " +
            $"egress={result.Datagrams} datagrams/{result.Bytes}B" +
            (result.TargetActorCount == 0 ? "" :
                $" actors={result.ActiveActorCount}/{result.TargetActorCount} spawnFanout={result.SpawnFanout} moveFanout={result.MoveFanout} actorRemoved={result.RemovedActors} removeFanout={result.RemoveFanout}"));

    private sealed class RuntimeHost
    {
        private readonly List<Player.Player> _players = [];

        public RecordingRakNetServer Transport { get; } = new();
        public GameLoop Loop { get; }
        public ProjectileSystem? Projectiles { get; }

        public RuntimeHost(int playerCount, bool streamChunks, bool includeProjectileSystem = false)
        {
            if (playerCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(playerCount));

            Blocks.EnsureLoaded();
            var logger = new SilentLogger();
            var players = new PlayerManager();
            var clock = new GameClock();
            var world = new World.World(new InMemoryChunkStorage(), logger);
            var blockPalette = BlockPaletteLoader.FromEmbeddedResource();
            var itemPalette = ItemPaletteLoader.FromEmbeddedResource();
            var context = new ServerContext(logger, players, new EventBus(logger), clock, world,
                new ServerConfig(), blockPalette, itemPalette, RecipeRegistry.CreateDefault(), CreativeCatalog.CreateDefault());

            Loop = new GameLoop(clock, players, logger);
            Loop.Register(new TimeSyncSystem());
            Loop.Register(new MovementSystem(players));
            Loop.Register(new ChatSystem());
            Loop.Register(new GameModeSystem());
            if (includeProjectileSystem)
            {
                var zombies = new ZombieStore();
                Projectiles = new ProjectileSystem(world, players, new ProjectileStore(), new ZombieSystem(world, players, zombies, itemPalette));
                Loop.Register(Projectiles);
            }
            Loop.Register(new BlockDigSystem(world));
            Loop.Register(new BlockEditSystem(players, world));
            Loop.Register(new GravitySystem(world, players));
            Loop.Register(new FloorDropSystem(world));
            Loop.Register(new InventorySystem(players, world, context.Recipes, context.Creative));
            Loop.Register(new EquipmentSystem());
            Loop.Register(new ChunkStreamSystem(world));

            for (var i = 0; i < playerCount; i++)
            {
                var rak = new RakNetSession
                {
                    EndPoint = new IPEndPoint(IPAddress.Loopback, 20000 + i),
                    Id = i + 1,
                    Server = Transport,
                    MTU = 1400
                };
                var session = new NetworkSession(rak, new InGameSessionHandler(), context);
                var player = new Player.Player($"load-{i}", session, players.AllocateRuntimeId(), Guid.NewGuid(), GameMode.Survival)
                {
                    IsInGame = true,
                    PositionX = i * 4,
                    PositionY = Blocks.FlatSpawnY,
                    PositionZ = 0,
                };
                player.Chunks.Radius = streamChunks ? 1 : -1;
                // Survival normally receives the curated starter hotbar. The harness owns its
                // fixture state, so clear it before establishing the one conserved test stack.
                for (var slot = 0; slot < PlayerInventory.FullInventorySize; slot++)
                    player.Inventory.TrySetBlock(slot, Blocks.Air, 0);
                player.Inventory.TrySetBlock(0, Blocks.Dirt, PlayerInventory.MaxStack);
                session.Player = player;
                if (!players.TryAdd(player))
                    throw new InvalidOperationException("Synthetic player registration failed.");
                _players.Add(player);
            }
        }

        public void Warmup()
        {
            SubmitSteadyInputs(0);
            Tick();
            Transport.Reset();
        }

        public void SubmitSteadyInputs(int tick)
        {
            foreach (var player in _players)
            {
                var phase = (tick + player.RuntimeId) & 1;
                player.SubmitMovementInput(MovementInputState.From(
                    player.PositionX + (phase == 0 ? 0.05f : -0.05f), player.PositionY, player.PositionZ,
                    pitch: 0, yaw: phase == 0 ? 90 : -90));

                // A valid two-slot swap every ten ticks exercises the authoritative inventory
                // transaction system without creating/destroying resources.
                if (tick % 10 == 0)
                    player.SubmitInventoryStack(InventoryStackIntent.Create(tick * 10000 + (int)player.RuntimeId,
                        [InventoryStackAction.Swap(0, 9)]));
            }
        }

        public void Tick()
        {
            Loop.TickOnce();
            foreach (var player in _players)
                player.Session.RakSession.Tick();
        }

        public void ValidateSteadyState()
        {
            foreach (var player in _players)
            {
                var dirt = 0;
                for (var slot = 0; slot < PlayerInventory.FullInventorySize; slot++)
                {
                    var stack = player.Inventory.Get(slot);
                    if (stack.Id == StackId.FromBlock(Blocks.Dirt))
                        dirt += stack.Count;
                }

                if (dirt != PlayerInventory.MaxStack)
                    throw new InvalidOperationException(
                        $"Inventory conservation failed for {player.Username}: expected {PlayerInventory.MaxStack} dirt, got {dirt}.");
            }
        }

        public void ConfigureActorInterest(bool enabled)
        {
            if (!enabled) return;
            for (var i = 0; i < _players.Count; i++)
            {
                var player = _players[i];
                player.Chunks.Radius = 1;
                if (i != 0)
                {
                    // Keep the other synthetic observers' normal chunk stream far from the
                    // actor cluster. This changes only their confirmed columns, never actor
                    // simulation or the GameLoop ordering.
                    player.PositionX = (100 + i) * 16f;
                    player.PositionZ = (100 + i) * 16f;
                }
                player.Chunks.RememberMany([i == 0 ? (0, 0) : (100 + i, 100 + i)]);
            }
        }

        public void SeedProjectiles(int targetCount, bool clustered)
        {
            if (Projectiles is null) throw new InvalidOperationException("Actor churn requires ProjectileSystem.");
            var owner = _players[0];
            var start = Projectiles.Projectiles.Active.Count;
            for (var i = start; i < targetCount; i++)
            {
                var entityId = 1_000_000L + i + (long)Loop.Clock.CurrentTick * 10_000L;
                var projectile = new Projectile(
                    entityId, (ulong)entityId, owner.RuntimeId,
                    clustered ? (i % 16) + 0.25f : (i % 100) * 2f,
                    100f,
                    clustered ? ((i / 16) % 16) + 0.25f : (i / 100) * 2f,
                    0.05f, 0f, 0f);
                if (!Projectiles.Projectiles.TryAdd(projectile)) break;
            }
        }

        public void ReplenishProjectiles(int targetCount, bool clustered) => SeedProjectiles(targetCount, clustered);

    }

    private sealed class RecordingRakNetServer : RakNetServer
    {
        private long _bytes;
        private long _datagrams;

        public RecordingRakNetServer() : base(port: 0) { }
        public long Bytes => Interlocked.Read(ref _bytes);
        public long Datagrams => Interlocked.Read(ref _datagrams);

        public override void Send(IPEndPoint endPoint, ReadOnlySpan<byte> buffer)
        {
            Interlocked.Increment(ref _datagrams);
            Interlocked.Add(ref _bytes, buffer.Length);
        }

        public void Reset()
        {
            Interlocked.Exchange(ref _bytes, 0);
            Interlocked.Exchange(ref _datagrams, 0);
        }
    }

    private sealed class SilentLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }

    private readonly record struct LoadResult(
        string Scenario, int PlayerCount, int TickCount, double AverageMs, double P50Ms, double P95Ms, double P99Ms,
        double MaxMs, long AllocatedBytes, GcCounts GcCounts, long Datagrams, long Bytes,
        int TargetActorCount = 0, int ActiveActorCount = 0, long SpawnFanout = 0, long RemovedActors = 0, long MoveFanout = 0,
        long RemoveFanout = 0)
    {
        public static LoadResult Create(string scenario, int playerCount, int ticks, long[] elapsed, long allocatedBytes,
            GcCounts gcCounts, long datagrams, long bytes,
            int targetActorCount = 0, int activeActorCount = 0, long spawnFanout = 0, long removedActors = 0, long moveFanout = 0,
            long removeFanout = 0)
        {
            var sorted = elapsed.Order().ToArray();
            static double Ms(long value) => value * 1000d / Stopwatch.Frequency;
            return new LoadResult(scenario, playerCount, ticks,
                elapsed.Average(Ms), Ms(sorted[(int)Math.Ceiling(ticks * .50) - 1]), Ms(sorted[(int)Math.Ceiling(ticks * .95) - 1]),
                Ms(sorted[(int)Math.Ceiling(ticks * .99) - 1]), Ms(sorted[^1]), allocatedBytes, gcCounts, datagrams, bytes,
                targetActorCount, activeActorCount, spawnFanout, removedActors, moveFanout, removeFanout);
        }
    }

    private readonly record struct GcCounts(int Gen0, int Gen1, int Gen2)
    {
        public static GcCounts Capture() => new(GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
        public static GcCounts operator -(GcCounts after, GcCounts before) =>
            new(after.Gen0 - before.Gen0, after.Gen1 - before.Gen1, after.Gen2 - before.Gen2);
    }

    private readonly record struct LoadOptions(int[] PlayerCounts, int Ticks, int ChunkTicks, int[] ActorCounts, int ActorPlayers, int ActorTicks)
    {
        public static LoadOptions Parse(string[] args)
        {
            var counts = new[] { 10, 100, 500 };
            var ticks = DefaultTicks;
            var actorCounts = new[] { 100, 1_000 };
            var actorPlayers = 10;
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] == "--players" && i + 1 < args.Length)
                    counts = args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(int.Parse).ToArray();
                else if (args[i] == "--ticks" && i + 1 < args.Length)
                    ticks = int.Parse(args[++i]);
                else if (args[i] == "--actors" && i + 1 < args.Length)
                    actorCounts = args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(int.Parse).ToArray();
                else if (args[i] == "--actor-players" && i + 1 < args.Length)
                    actorPlayers = int.Parse(args[++i]);
            }

            if (counts.Any(count => count <= 0) || actorCounts.Any(count => count <= 0) || actorPlayers <= 0 || ticks <= 0)
                throw new ArgumentOutOfRangeException(nameof(args), "Player counts and ticks must be positive.");
            return new LoadOptions(counts, ticks, Math.Min(ticks, 5), actorCounts, actorPlayers, ticks);
        }
    }
}
