using System.Diagnostics;
using System.Net;
using Zenith.Diagnostics;
using Zenith.Event;
using Zenith.Gameplay.Runtime;
using Zenith.Player;
using Zenith.Raknet;
using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Log;
using Zenith.Raknet.Stream;
using Zenith.Server;
using Zenith.Session;
using Zenith.Session.Handler;
using Zenith.World;
using Zenith.Gameplay.Entities;
using Zenith.Gameplay.Inventory;
using Zenith.Gameplay.Replication;
using Zenith.Gameplay.Survival;
using Zenith.Gameplay.WorldInteraction;

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
        if (options.WorldgenStream)
        {
            foreach (var playerCount in options.PlayerCounts)
                Print(RunWorldgenStream(
                    playerCount, options.Ticks, options.WorldgenRadius, options.WorldgenWorkers));
            return 0;
        }
        if (options.WorldInteraction)
        {
            foreach (var playerCount in options.PlayerCounts)
                Print(RunWorldInteraction(playerCount, options.Ticks));
            return 0;
        }
        if (options.ZombieBehavior)
        {
            foreach (var actorCount in options.ActorCounts)
            {
                foreach (var mode in new[] { ZombieWorkloadMode.Idle, ZombieWorkloadMode.Direct, ZombieWorkloadMode.Obstacle })
                    Print(RunZombieBehavior(options.ActorPlayers, actorCount, options.ActorTicks, mode));
            }
            return 0;
        }
        if (options.InterestScaling)
        {
            foreach (var actorCount in options.ActorCounts)
            {
                foreach (var layout in new[] { InterestLayout.Clustered, InterestLayout.Distributed, InterestLayout.MovingObservers })
                    Print(RunInterestScaling(options.ActorPlayers, actorCount, options.ActorTicks, layout));
            }
            return 0;
        }
        if (options.ActivationPressure)
        {
            foreach (var actorCount in options.ActorCounts)
                foreach (var scenario in Enum.GetValues<ActivationScenario>())
                {
                    Print(RunActivationPressure(options.ActorPlayers, actorCount, options.ActorTicks, scenario, useProjectiles: false));
                    Print(RunActivationPressure(options.ActorPlayers, actorCount, options.ActorTicks, scenario, useProjectiles: true));
                }
            return 0;
        }
        if (options.MixedRoster)
        {
            foreach (var actorCount in options.ActorCounts)
                Print(RunMixedRoster(options.ActorPlayers, actorCount, options.ActorTicks));
            return 0;
        }

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

    private static LoadResult RunWorldgenStream(int playerCount, int ticks, int radius, int workers)
    {
        using var host = new RuntimeHost(
            playerCount,
            streamChunks: true,
            chunkRadius: radius,
            noiseTerrain: true,
            generationWorkers: workers,
            instrumentWorldgen: true);
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
            Thread.Sleep(1);
        }

        var result = LoadResult.Create(
            "worldgen-stream",
            playerCount,
            ticks,
            elapsed,
            GC.GetAllocatedBytesForCurrentThread() - allocationBefore,
            GcCounts.Capture() - gcBefore,
            host.Transport.Datagrams,
            host.Transport.Bytes);
        host.PrintWorldgenDiagnostics(radius);
        return result;
    }

    /// <summary>
    /// Ten-or-more isolated players repeatedly place then authoritatively break one stone cell.
    /// Every twentieth tick a concrete dirt floor drop is also created at each player's feet and
    /// later collected through the production pickup system. This measures gameplay ownership,
    /// projection and wire egress together; it is not a client-protocol benchmark.
    /// </summary>
    private static LoadResult RunWorldInteraction(int playerCount, int ticks)
    {
        var host = new RuntimeHost(playerCount, streamChunks: false, includeWorldInteractionDiagnostics: true);
        host.PrepareWorldInteraction();
        for (var warmup = 0; warmup < 20; warmup++)
        {
            host.SubmitWorldInteraction(warmup);
            host.Tick();
        }
        host.Transport.Reset();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocationBefore = GC.GetAllocatedBytesForCurrentThread();
        var gcBefore = GcCounts.Capture();
        var elapsed = new long[ticks];
        for (var tick = 20; tick < ticks + 20; tick++)
        {
            host.SubmitWorldInteraction(tick);
            var started = Stopwatch.GetTimestamp();
            host.Tick();
            elapsed[tick - 20] = Stopwatch.GetTimestamp() - started;
        }
        host.ValidateWorldInteraction();
        host.PrintWorldInteractionTimings();

        return LoadResult.Create("world-loop", playerCount, ticks, elapsed,
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
            actorCount, host.Projectiles!.Projectiles.Count,
            host.Projectiles.ReplicatedSpawnCount, host.Projectiles.RemovalCount, host.Projectiles.ReplicatedMoveCount,
            host.Projectiles.ReplicatedRemovalCount, host.Projectiles.ReplicatedMoveSkippedCount);
    }

    private static LoadResult RunZombieBehavior(int playerCount, int actorCount, int ticks, ZombieWorkloadMode mode)
    {
        var host = new RuntimeHost(
            playerCount,
            streamChunks: false,
            includeZombieSystem: true,
            includeWorldInteractionDiagnostics: true);
        host.ConfigureActorInterest(enabled: true);
        host.SeedZombies(actorCount, mode);
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
        }
        host.ValidateZombieCount(actorCount);
        host.PrintZombieTimings(mode);

        return LoadResult.Create($"zombie-{mode.ToString().ToLowerInvariant()}", playerCount, ticks, elapsed,
            GC.GetAllocatedBytesForCurrentThread() - allocationBefore,
            GcCounts.Capture() - gcBefore,
            host.Transport.Datagrams, host.Transport.Bytes,
            actorCount, host.Zombies!.Zombies.Count,
            host.Zombies.ReplicatedSpawnCount, 0, host.Zombies.ReplicatedMoveCount,
            host.Zombies.ReplicatedRemovalCount, host.Zombies.ReplicatedMoveSkippedCount);
    }

    private static LoadResult RunInterestScaling(int observerCount, int actorCount, int ticks, InterestLayout layout)
    {
        var host = new RuntimeHost(observerCount, streamChunks: false, includeChunkStream: false, includeZombieSystem: true,
            includeWorldInteractionDiagnostics: true);
        host.SeedInterestZombies(actorCount, layout);
        host.ConfigureInterestLayout(layout);
        host.DisableInterestTargets();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocationBefore = GC.GetAllocatedBytesForCurrentThread();
        var gcBefore = GcCounts.Capture();
        var elapsed = new long[ticks];
        for (var tick = 0; tick < ticks; tick++)
        {
            if (layout == InterestLayout.MovingObservers)
                host.AdvanceInterestObservers(tick, actorCount);
            var started = Stopwatch.GetTimestamp();
            host.Tick();
            elapsed[tick] = Stopwatch.GetTimestamp() - started;
        }
        host.PrintInterestZombieTimings(layout);

        return LoadResult.Create($"interest-{layout.ToString().ToLowerInvariant()}", observerCount, ticks, elapsed,
            GC.GetAllocatedBytesForCurrentThread() - allocationBefore,
            GcCounts.Capture() - gcBefore,
            host.Transport.Datagrams, host.Transport.Bytes,
            actorCount, host.Zombies!.Zombies.Count,
            host.Zombies.ReplicatedSpawnCount, 0, host.Zombies.ReplicatedMoveCount,
            host.Zombies.ReplicatedRemovalCount, host.Zombies.ReplicatedMoveSkippedCount);
    }

    private static LoadResult RunActivationPressure(int observerCount, int actorCount, int ticks,
        ActivationScenario scenario, bool useProjectiles)
    {
        var host = new RuntimeHost(observerCount, streamChunks: false,
            includeProjectileSystem: useProjectiles,
            includeZombieSystem: !useProjectiles,
            includeWorldInteractionDiagnostics: true);
        host.ConfigureActivationScenario(scenario);
        if (useProjectiles)
            host.SeedProjectiles(actorCount, clustered: true);
        else
            host.SeedActivationZombies(actorCount);

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
        }
        host.PrintActivationTimings(scenario, useProjectiles);
        var active = useProjectiles ? host.Projectiles!.Projectiles.Count : host.Zombies!.Zombies.Count;
        return LoadResult.Create($"activation-{(useProjectiles ? "projectile" : "zombie")}-{scenario.ToString().ToLowerInvariant()}",
            observerCount, ticks, elapsed, GC.GetAllocatedBytesForCurrentThread() - allocationBefore,
            GcCounts.Capture() - gcBefore, host.Transport.Datagrams, host.Transport.Bytes,
            actorCount, active);
    }

    /// <summary>
    /// Phase XXII, Part 29 — every ECS-authoritative species (Zombie/Minecart/Cow/Skeleton/Spider/
    /// Projectile) ticking together under one GameLoop, roughly evenly split across actorCount, plus
    /// a steady trickle of projectiles fired into the mob cluster so ProjectileSystem's cross-species
    /// DamageDispatch query (Query.With(Health, Position)) is actually exercised against a mixed
    /// population each tick, not just a single species at a time like the other actor benchmarks.
    /// </summary>
    private static LoadResult RunMixedRoster(int playerCount, int actorCount, int ticks)
    {
        var host = new RuntimeHost(playerCount, streamChunks: false, includeMixedRoster: true,
            includeWorldInteractionDiagnostics: true);
        host.ConfigureActorInterest(enabled: true);
        host.SeedMixedRoster(actorCount);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocationBefore = GC.GetAllocatedBytesForCurrentThread();
        var gcBefore = GcCounts.Capture();
        var elapsed = new long[ticks];
        for (var tick = 0; tick < ticks; tick++)
        {
            host.FireMixedRosterProjectile(tick);
            var started = Stopwatch.GetTimestamp();
            host.Tick();
            elapsed[tick] = Stopwatch.GetTimestamp() - started;
        }
        host.PrintMixedRosterTimings();

        return LoadResult.Create("mixed-roster", playerCount, ticks, elapsed,
            GC.GetAllocatedBytesForCurrentThread() - allocationBefore,
            GcCounts.Capture() - gcBefore,
            host.Transport.Datagrams, host.Transport.Bytes,
            actorCount, host.MixedRosterActiveActorCount);
    }

    private static void Print(LoadResult result) =>
        Console.WriteLine(
            $"{result.Scenario,-11} players={result.PlayerCount,3} ticks={result.TickCount,3} " +
            $"avg={result.AverageMs:F3}ms p50={result.P50Ms:F3}ms p95={result.P95Ms:F3}ms p99={result.P99Ms:F3}ms max={result.MaxMs:F3}ms " +
            $"alloc={result.AllocatedBytes / (double)result.TickCount:F0}B/tick " +
            $"gc={result.GcCounts.Gen0}/{result.GcCounts.Gen1}/{result.GcCounts.Gen2} " +
            $"egress={result.Datagrams} datagrams/{result.Bytes}B" +
            (result.TargetActorCount == 0 ? "" :
                $" actors={result.ActiveActorCount}/{result.TargetActorCount} spawnFanout={result.SpawnFanout} moveFanout={result.MoveFanout} moveSkipped={result.MoveSkippedFanout} actorRemoved={result.RemovedActors} removeFanout={result.RemoveFanout}"));

    private sealed class RuntimeHost : IDisposable
    {
        private readonly List<Player.Player> _players = [];
        private readonly List<NetworkSession> _sessions = [];
        private readonly PlayerManager _playerManager;
        private bool _disposed;

        public RecordingRakNetServer Transport { get; } = new();
        public GameLoop Loop { get; }
        public ProjectileSystem? Projectiles { get; }
        public ZombieSystem? Zombies { get; }
        public MinecartSystem? Minecarts { get; }
        public CowSystem? Cows { get; }
        public SpiderSystem? Spiders { get; }
        public SkeletonSystem? Skeletons { get; }
        public World.World World { get; }
        private WorldInteractionDiagnostics? WorldDiagnostics { get; }
        private ServerRuntimeDiagnostics? RuntimeDiagnostics { get; }

        public RuntimeHost(
            int playerCount,
            bool streamChunks,
            bool includeChunkStream = true,
            bool includeProjectileSystem = false,
            bool includeZombieSystem = false,
            bool includeMixedRoster = false,
            bool includeWorldInteractionDiagnostics = false,
            int chunkRadius = 1,
            bool noiseTerrain = false,
            int generationWorkers = 1,
            bool instrumentWorldgen = false)
        {
            if (playerCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(playerCount));

            Blocks.EnsureLoaded();
            var logger = new SilentLogger();
            var players = new PlayerManager();
            _playerManager = players;
            var clock = new GameClock();
            RuntimeDiagnostics = instrumentWorldgen ? new ServerRuntimeDiagnostics() : null;
            var terrain = noiseTerrain
                ? new NoiseTerrainProvider(seed: 42, RuntimeDiagnostics?.Worldgen)
                : null;
            var config = new ServerConfig();
            config.World.ChunkGenerationWorkers = generationWorkers;
            var world = new World.World(
                new InMemoryChunkStorage(),
                logger,
                terrain,
                RuntimeDiagnostics?.Worldgen,
                generationWorkers);
            World = world;
            WorldDiagnostics = includeWorldInteractionDiagnostics ? new WorldInteractionDiagnostics() : null;
            var blockPalette = BlockPaletteLoader.FromEmbeddedResource();
            var itemPalette = ItemPaletteLoader.FromEmbeddedResource();
            var context = new ServerContext(logger, players, new EventBus(logger), clock, world,
                config, blockPalette, itemPalette, RecipeRegistry.CreateDefault(), CreativeCatalog.CreateDefault(),
                RuntimeDiagnostics);

            Loop = new GameLoop(
                clock,
                players,
                logger,
                RuntimeDiagnostics?.Runtime ?? WorldDiagnostics?.Runtime,
                RuntimeDiagnostics?.Tick ?? WorldDiagnostics?.Tick ?? default);
            Loop.Register(new TimeSyncSystem());
            Loop.Register(new MovementSystem(players));
            Loop.Register(new ChatSystem());
            Loop.Register(new GameModeSystem());
            ZombieSystem? zombieSystem = null;
            if (includeZombieSystem || includeProjectileSystem)
            {
                var entities = new Zenith.Ecs.EntityRuntime();
                zombieSystem = new ZombieSystem(world, players, entities, itemPalette);
                Zombies = zombieSystem;
                if (includeZombieSystem)
                {
                    if (WorldDiagnostics is { } zombieTimings)
                        Loop.Register(zombieSystem, zombieTimings.Zombie);
                    else
                        Loop.Register(zombieSystem);
                }

                if (includeProjectileSystem)
                {
                    var minecartSystem = new MinecartSystem(world, players, entities, itemPalette);
                    var damage = new Zenith.Gameplay.Entities.DamageDispatch();
                    damage.Register(zombieSystem.Owns, zombieSystem.TryApplyDamage);
                    damage.Register(minecartSystem.Owns, minecartSystem.TryApplyDamage);
                    Projectiles = new ProjectileSystem(world, players, entities, damage);
                    if (WorldDiagnostics is { } projectileTimings)
                        Loop.Register(Projectiles, projectileTimings.Projectile);
                    else
                        Loop.Register(Projectiles);
                }
            }
            else if (includeMixedRoster)
            {
                // Mirrors ZenithServer.cs's real composition-root order: DamageDispatch built once,
                // Zombie/Minecart/Cow/Spider registered, ProjectileSystem constructed on top of that
                // dispatch, then SkeletonSystem constructed (it needs a live ProjectileSystem to
                // shoot back) and registered into the same dispatch last.
                var entities = new Zenith.Ecs.EntityRuntime();
                zombieSystem = new ZombieSystem(world, players, entities, itemPalette);
                Zombies = zombieSystem;
                var minecartSystem = new MinecartSystem(world, players, entities, itemPalette);
                Minecarts = minecartSystem;
                var cowSystem = new CowSystem(world, players, entities, itemPalette);
                Cows = cowSystem;
                var spiderSystem = new SpiderSystem(world, players, entities, itemPalette);
                Spiders = spiderSystem;
                var damage = new Zenith.Gameplay.Entities.DamageDispatch();
                damage.Register(zombieSystem.Owns, zombieSystem.TryApplyDamage);
                damage.Register(minecartSystem.Owns, minecartSystem.TryApplyDamage);
                damage.Register(cowSystem.Owns, cowSystem.TryApplyDamage);
                damage.Register(spiderSystem.Owns, spiderSystem.TryApplyDamage);
                Projectiles = new ProjectileSystem(world, players, entities, damage);
                var skeletonSystem = new SkeletonSystem(world, players, entities, Projectiles, itemPalette);
                Skeletons = skeletonSystem;
                damage.Register(skeletonSystem.Owns, skeletonSystem.TryApplyDamage);

                if (WorldDiagnostics is { } mixedTimings)
                {
                    Loop.Register(zombieSystem, mixedTimings.Zombie);
                    Loop.Register(minecartSystem, mixedTimings.Minecart);
                    Loop.Register(cowSystem, mixedTimings.Cow);
                    Loop.Register(spiderSystem, mixedTimings.Spider);
                    Loop.Register(Projectiles, mixedTimings.Projectile);
                    Loop.Register(skeletonSystem, mixedTimings.Skeleton);
                }
                else
                {
                    Loop.Register(zombieSystem);
                    Loop.Register(minecartSystem);
                    Loop.Register(cowSystem);
                    Loop.Register(spiderSystem);
                    Loop.Register(Projectiles);
                    Loop.Register(skeletonSystem);
                }
            }
            Loop.Register(new BlockDigSystem(world));
            if (WorldDiagnostics is { } timings)
                Loop.Register(new BlockEditSystem(players, world), timings.BlockEdit);
            else
                Loop.Register(new BlockEditSystem(players, world));
            Loop.Register(new GravitySystem(world, players));
            if (WorldDiagnostics is { } floorTimings)
                Loop.Register(new FloorDropSystem(world), floorTimings.FloorDrop);
            else
                Loop.Register(new FloorDropSystem(world));
            if (WorldDiagnostics is { } inventoryTimings)
                Loop.Register(new InventorySystem(players, world, context.Recipes, context.Creative), inventoryTimings.Inventory);
            else
                Loop.Register(new InventorySystem(players, world, context.Recipes, context.Creative));
            Loop.Register(new EquipmentSystem());
            if (includeChunkStream)
            {
                var chunkStream = new ChunkStreamSystem(world, RuntimeDiagnostics?.Worldgen);
                if (RuntimeDiagnostics is { } diagnostics)
                    Loop.Register(chunkStream, diagnostics.System("chunk-stream"));
                else
                    Loop.Register(chunkStream);
            }

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
                    PositionY = noiseTerrain ? world.SampleSpawnFeetY(0, 0) : Blocks.FlatSpawnY,
                    PositionZ = 0,
                };
                player.Chunks.Radius = streamChunks ? chunkRadius : -1;
                // Survival normally receives the curated starter hotbar. The harness owns its
                // fixture state, so clear it before establishing the one conserved test stack.
                for (var slot = 0; slot < PlayerInventory.FullInventorySize; slot++)
                    player.Inventory.TrySetBlock(slot, Blocks.Air, 0);
                player.Inventory.TrySetBlock(0, Blocks.Dirt, PlayerInventory.MaxStack);
                session.Player = player;
                if (!players.TryAdd(player))
                    throw new InvalidOperationException("Synthetic player registration failed.");
                _players.Add(player);
                _sessions.Add(session);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            foreach (var session in _sessions)
            {
                if (!session.RakSession.IsClosed)
                    session.RakSession.Disconnect(DisconnectReason.ServerDisconnect);

                session.HandleClose(DisconnectReason.ServerDisconnect);
            }

            World.StopGenerationAsync().AsTask().GetAwaiter().GetResult();
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

        public void PrintWorldgenDiagnostics(int radius)
        {
            if (RuntimeDiagnostics is not { } diagnostics)
                return;

            var snapshot = diagnostics.Runtime.CaptureSnapshot();
            static long Value(Zenith.Diagnostics.DiagnosticsSnapshot snapshot, string name) =>
                snapshot.Metrics.First(metric => metric.Name == name).Value;
            Console.WriteLine(
                $"worldgen-stream radius={radius} generated={Value(snapshot, "gameplay.worldgen.columns.generated")} " +
                $"coalesced={Value(snapshot, "gameplay.worldgen.columns.coalesced")} " +
                $"queuePeak={Value(snapshot, "gameplay.worldgen.queue.peak")} " +
                $"backpressure={Value(snapshot, "gameplay.worldgen.queue.backpressure")} " +
                $"payloadMs={AverageMilliseconds(snapshot, "gameplay.worldgen.column.payload"):F3}");
        }

        private static double AverageMilliseconds(Zenith.Diagnostics.DiagnosticsSnapshot snapshot, string name)
        {
            var metric = snapshot.Metrics.First(item => item.Name == name);
            return metric.Count == 0
                ? 0d
                : metric.TotalStopwatchTicks * 1000d / snapshot.StopwatchFrequency / metric.Count;
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
            var start = Projectiles.Projectiles.Count;
            for (var i = start; i < targetCount; i++)
            {
                var x = clustered ? (i % 16) + 0.25f : (i % 100) * 2f;
                var z = clustered ? ((i / 16) % 16) + 0.25f : (i / 100) * 2f;
                if (!Projectiles.TrySpawnFromActor(owner.RuntimeId, x, 100f, z, 0.05f, 0f, 0f, _playerManager.Online))
                    break;
            }
        }

        public void ReplenishProjectiles(int targetCount, bool clustered) => SeedProjectiles(targetCount, clustered);

        public int MixedRosterActiveActorCount =>
            (Zombies?.Zombies.Count ?? 0) + (Minecarts?.Minecarts.Count ?? 0) + (Cows?.Cows.Count ?? 0) +
            (Spiders?.Spiders.Count ?? 0) + (Skeletons?.Skeletons.Count ?? 0) + (Projectiles?.Projectiles.Count ?? 0);

        /// <summary>
        /// Splits actorCount roughly evenly across the six ECS-authoritative species and spreads
        /// them in a shared grid near the origin so ProjectileSystem's cross-species query and each
        /// species' own AI actually have to look past the others every tick, not just their own kind.
        /// </summary>
        public void SeedMixedRoster(int actorCount)
        {
            if (Zombies is null || Minecarts is null || Cows is null || Spiders is null || Skeletons is null || Projectiles is null)
                throw new InvalidOperationException("Mixed-roster workload requires includeMixedRoster.");

            var perSpecies = Math.Max(1, actorCount / 6);
            var side = (int)Math.Ceiling(Math.Sqrt(perSpecies));
            void Grid(Action<float, float> spawn)
            {
                for (var i = 0; i < perSpecies; i++)
                    spawn((i % side) * 1.5f, (i / side) * 1.5f);
            }

            Grid((x, z) => Zombies.SpawnZombie(x, Blocks.FlatSpawnY, z));
            Grid((x, z) => Minecarts.SpawnMinecart(x + 200f, Blocks.FlatSpawnY, z));
            Grid((x, z) => Cows.SpawnCow(x + 400f, Blocks.FlatSpawnY, z));
            Grid((x, z) => Spiders.SpawnSpider(x + 600f, Blocks.FlatSpawnY, z));
            Grid((x, z) => Skeletons.SpawnSkeleton(x + 800f, Blocks.FlatSpawnY, z));
        }

        /// <summary>Every fifth tick, fires one projectile from the first player toward whichever
        /// species cluster that tick lands on, keeping DamageDispatch under continuous cross-species
        /// load for the whole run rather than a single burst at the start.</summary>
        public void FireMixedRosterProjectile(int tick)
        {
            if (Projectiles is null || tick % 5 != 0) return;
            var owner = _players[0];
            var clusterOffsets = new[] { 0f, 200f, 400f, 600f, 800f };
            var targetX = clusterOffsets[(tick / 5) % clusterOffsets.Length] + 0.25f;
            Projectiles.TrySpawnFromActor(owner.RuntimeId, targetX - 0.3f, Blocks.FlatSpawnY, 0.25f, 0.05f, 0f, 0f, _playerManager.Online);
        }

        public void PrintMixedRosterTimings() => WorldDiagnostics?.PrintMixedRoster();

        public void SeedInterestZombies(int targetCount, InterestLayout layout)
        {
            if (Zombies is null) throw new InvalidOperationException("Interest workload requires ZombieSystem.");
            for (var i = 0; i < targetCount; i++)
            {
                var x = layout == InterestLayout.Clustered ? 0.25f + (i % 32) * 0.02f : (i % 64) * 16f + 0.25f;
                var z = layout == InterestLayout.Clustered ? 0.25f + (i / 32) * 0.02f : (i / 64) * 16f + 0.25f;
                Zombies.SpawnZombie(x, Blocks.FlatSpawnY, z);
            }
        }

        public void DisableInterestTargets()
        {
            foreach (var player in _players)
            {
                player.PositionX = 100_000f;
                player.PositionZ = 100_000f;
            }
        }

        public void ConfigureActivationScenario(ActivationScenario scenario)
        {
            for (var index = 0; index < _players.Count; index++)
            {
                var player = _players[index];
                player.Chunks.Radius = 1;
                var known = new List<(int X, int Z)>();
                player.Chunks.CopyKnown(known);
                foreach (var entry in known)
                    player.Chunks.Forget(entry.X, entry.Z);
                var observed = scenario == ActivationScenario.Observed && index == 0;
                var active = scenario != ActivationScenario.NoObservers;
                player.IsInGame = active;
                player.PositionX = observed ? 0 : 100_000;
                player.PositionZ = observed ? 0 : 100_000;
                if (observed)
                    player.Chunks.RememberMany([(0, 0), (-1, 0), (0, -1), (1, 0), (0, 1)]);
                else
                    player.Chunks.RememberMany([(6_250, 6_250)]);
            }
        }

        public void SeedActivationZombies(int targetCount)
        {
            if (Zombies is null) throw new InvalidOperationException("Activation workload requires ZombieSystem.");
            for (var i = 0; i < targetCount; i++)
            {
                var x = (i % 100) * 0.25f;
                var z = (i / 100) * 0.25f;
                Zombies.SpawnZombie(x, Blocks.FlatSpawnY, z);
            }
        }

        public void ConfigureInterestLayout(InterestLayout layout)
        {
            for (var i = 0; i < _players.Count; i++)
            {
                var chunk = layout == InterestLayout.Clustered ? (i == 0 ? 0 : 100 + i) : i * 4;
                ConfigureObserverKnowledge(i, chunk, layout == InterestLayout.Clustered && i != 0);
            }
        }

        public void AdvanceInterestObservers(int tick, int actorCount)
        {
            for (var i = 0; i < _players.Count; i++)
                ConfigureObserverKnowledge(i, (tick / 5 + i) % Math.Max(1, (actorCount + 63) / 64), false);
        }

        private void ConfigureObserverKnowledge(int observerIndex, int chunk, bool farObserver)
        {
            var player = _players[observerIndex];
            var known = new List<(int X, int Z)>();
            player.Chunks.CopyKnown(known);
            foreach (var entry in known) player.Chunks.Forget(entry.X, entry.Z);
            player.Chunks.Radius = 1;
            var x = farObserver ? 100 + observerIndex : chunk;
            var radius = farObserver ? 0 : 1;
            for (var dx = -radius; dx <= radius; dx++)
                for (var dz = -radius; dz <= radius; dz++)
                    player.Chunks.RememberMany([(x + dx, dz)]);
        }

        public void SeedZombies(int targetCount, ZombieWorkloadMode mode)
        {
            if (Zombies is null) throw new InvalidOperationException("Zombie workload requires ZombieSystem.");
            for (var i = 0; i < targetCount; i++)
            {
                var x = mode == ZombieWorkloadMode.Idle
                    ? 100f + (i % 32) * 1.25f
                    : 4f + (i % 16) * 0.35f;
                var z = mode == ZombieWorkloadMode.Idle
                    ? 100f + (i / 32) * 1.25f
                    : (i / 16) * 0.35f;
                Zombies.SpawnZombie(x, Blocks.FlatSpawnY, z);
            }

            if (mode == ZombieWorkloadMode.Obstacle)
            {
                for (var x = 1; x <= 3; x++)
                {
                    World.SetBlock(x, Blocks.FlatSpawnY, 0, Blocks.Stone);
                    World.SetBlock(x, Blocks.FlatSpawnY + 1, 0, Blocks.Stone);
                }
            }
        }

        public void ValidateZombieCount(int expected)
        {
            if (Zombies is null || Zombies.Zombies.Count != expected)
                throw new InvalidOperationException($"Zombie workload lifecycle changed actor count: expected {expected}.");
        }

        public void PrintZombieTimings(ZombieWorkloadMode mode) => WorldDiagnostics?.PrintZombie(mode);

        public void PrintInterestZombieTimings(InterestLayout layout) => WorldDiagnostics?.PrintInterestZombie(layout);

        public void PrintActivationTimings(ActivationScenario scenario, bool projectiles) =>
            WorldDiagnostics?.PrintActivation(scenario, projectiles);

        public void PrepareWorldInteraction()
        {
            Tools.EnsureLoaded();
            for (var i = 0; i < _players.Count; i++)
            {
                var player = _players[i];
                player.PositionX = i * 8f;
                player.PositionY = 90f;
                player.PositionZ = 0f;
                for (var slot = 0; slot < PlayerInventory.FullInventorySize; slot++)
                    player.Inventory.TrySetBlock(slot, Blocks.Air, 0);
                player.Inventory.TrySetBlock(0, Blocks.Stone, PlayerInventory.MaxStack);
                player.Inventory.TrySetItem(1, Tools.Require("minecraft:wooden_pickaxe"), 1);
                player.Inventory.TrySetBlock(2, Blocks.Dirt, PlayerInventory.MaxStack - 1);
            }
        }

        public void SubmitWorldInteraction(int tick)
        {
            for (var i = 0; i < _players.Count; i++)
            {
                var player = _players[i];
                var x = i * 8 + 2;
                const int y = 90;
                if ((tick & 1) == 0)
                {
                    player.SelectedHotbarSlot = 0;
                    if (!player.SubmitBlockEdit(BlockEditIntent.Set(x, y, 0, Blocks.Stone, hotbarSlot: 0)))
                        throw new InvalidOperationException("World-loop placement queue refused.");
                }
                else
                {
                    player.SelectedHotbarSlot = 1;
                    var need = Blocks.BreakTicks(Blocks.Stone, player.Inventory.GetStackId(1));
                    var started = Loop.Clock.CurrentTick > (ulong)Math.Max(need, 0)
                        ? Loop.Clock.CurrentTick - (ulong)Math.Max(need, 0)
                        : 0;
                    if (!player.SubmitBlockEdit(BlockEditIntent.BreakWithDig(x, y, 0, started, need)))
                        throw new InvalidOperationException("World-loop break queue refused.");
                }

                if (tick % 20 == 0 && !FloorDropFanout.TryDeposit(
                        World, _playerManager, _players, (int)player.PositionX, y, 0,
                        StackId.FromBlock(Blocks.Dirt), 1))
                {
                    throw new InvalidOperationException("World-loop pickup seed could not commit.");
                }
            }
        }

        public void ValidateWorldInteraction()
        {
            foreach (var player in _players)
            {
                var stone = 0;
                var dirt = 0;
                for (var slot = 0; slot < PlayerInventory.FullInventorySize; slot++)
                {
                    var stack = player.Inventory.Get(slot);
                    if (stack.Id == StackId.FromBlock(Blocks.Stone)) stone += stack.Count;
                    if (stack.Id == StackId.FromBlock(Blocks.Dirt)) dirt += stack.Count;
                }

                if (stone != PlayerInventory.MaxStack || dirt < PlayerInventory.MaxStack)
                    throw new InvalidOperationException($"World-loop conservation failed for {player.Username}: stone={stone}, dirt={dirt}.");
            }
        }

        public void PrintWorldInteractionTimings() => WorldDiagnostics?.Print();

    }

    /// <summary>Fixed diagnostics layout for the world-loop probe; recording remains outside gameplay decisions.</summary>
    private sealed class WorldInteractionDiagnostics
    {
        public DiagnosticsRuntime Runtime { get; }
        public TimingMetric Tick { get; }
        public TimingMetric BlockEdit { get; }
        public TimingMetric FloorDrop { get; }
        public TimingMetric Inventory { get; }
        public TimingMetric Zombie { get; }
        public TimingMetric Projectile { get; }
        public TimingMetric Minecart { get; }
        public TimingMetric Cow { get; }
        public TimingMetric Spider { get; }
        public TimingMetric Skeleton { get; }

        public WorldInteractionDiagnostics()
        {
            var builder = new DiagnosticsBuilder();
            Tick = builder.Timing("tick");
            BlockEdit = builder.Timing("tick.system.block-edit", "tick");
            FloorDrop = builder.Timing("tick.system.floor-drop", "tick");
            Inventory = builder.Timing("tick.system.inventory", "tick");
            Zombie = builder.Timing("tick.system.zombie", "tick");
            Projectile = builder.Timing("tick.system.projectile", "tick");
            Minecart = builder.Timing("tick.system.minecart", "tick");
            Cow = builder.Timing("tick.system.cow", "tick");
            Spider = builder.Timing("tick.system.spider", "tick");
            Skeleton = builder.Timing("tick.system.skeleton", "tick");
            Runtime = builder.Build();
        }

        public void Print()
        {
            var snapshot = Runtime.CaptureSnapshot();
            var parts = snapshot.Metrics
                .Where(metric => metric.Count > 0)
                .Select(metric =>
                    $"{metric.Name}={metric.TotalStopwatchTicks * 1000d / snapshot.StopwatchFrequency / metric.Count:F3}ms avg")
                .ToArray();
            Console.WriteLine($"world-loop timings {string.Join(" ", parts)}");
        }

        public void PrintZombie(ZombieWorkloadMode mode)
        {
            var snapshot = Runtime.CaptureSnapshot();
            var metric = snapshot.Metrics.First(m => m.Name == "tick.system.zombie");
            var average = metric.TotalStopwatchTicks * 1000d / snapshot.StopwatchFrequency / metric.Count;
            Console.WriteLine($"zombie timings mode={mode.ToString().ToLowerInvariant()} avg={average:F3}ms");
        }

        public void PrintInterestZombie(InterestLayout layout)
        {
            var snapshot = Runtime.CaptureSnapshot();
            var metric = snapshot.Metrics.First(m => m.Name == "tick.system.zombie");
            var average = metric.TotalStopwatchTicks * 1000d / snapshot.StopwatchFrequency / metric.Count;
            Console.WriteLine($"interest timings layout={layout.ToString().ToLowerInvariant()} zombie={average:F3}ms");
        }

        public void PrintActivation(ActivationScenario scenario, bool projectiles)
        {
            var snapshot = Runtime.CaptureSnapshot();
            var name = projectiles ? "tick.system.projectile" : "tick.system.zombie";
            var metric = snapshot.Metrics.First(m => m.Name == name);
            var average = metric.TotalStopwatchTicks * 1000d / snapshot.StopwatchFrequency / metric.Count;
            Console.WriteLine($"activation timings actor={(projectiles ? "projectile" : "zombie")} scenario={scenario.ToString().ToLowerInvariant()} avg={average:F3}ms");
        }

        public void PrintMixedRoster()
        {
            var snapshot = Runtime.CaptureSnapshot();
            var names = new[]
            {
                "tick.system.zombie", "tick.system.minecart", "tick.system.cow",
                "tick.system.spider", "tick.system.skeleton", "tick.system.projectile"
            };
            var parts = names.Select(name =>
            {
                var metric = snapshot.Metrics.First(m => m.Name == name);
                var average = metric.Count == 0 ? 0d : metric.TotalStopwatchTicks * 1000d / snapshot.StopwatchFrequency / metric.Count;
                return $"{name["tick.system.".Length..]}={average:F3}ms";
            });
            Console.WriteLine($"mixed-roster timings {string.Join(" ", parts)}");
        }
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
        long RemoveFanout = 0, long MoveSkippedFanout = 0)
    {
        public static LoadResult Create(string scenario, int playerCount, int ticks, long[] elapsed, long allocatedBytes,
            GcCounts gcCounts, long datagrams, long bytes,
            int targetActorCount = 0, int activeActorCount = 0, long spawnFanout = 0, long removedActors = 0, long moveFanout = 0,
            long removeFanout = 0, long moveSkippedFanout = 0)
        {
            var sorted = elapsed.Order().ToArray();
            static double Ms(long value) => value * 1000d / Stopwatch.Frequency;
            return new LoadResult(scenario, playerCount, ticks,
                elapsed.Average(Ms), Ms(sorted[(int)Math.Ceiling(ticks * .50) - 1]), Ms(sorted[(int)Math.Ceiling(ticks * .95) - 1]),
                Ms(sorted[(int)Math.Ceiling(ticks * .99) - 1]), Ms(sorted[^1]), allocatedBytes, gcCounts, datagrams, bytes,
                targetActorCount, activeActorCount, spawnFanout, removedActors, moveFanout, removeFanout, moveSkippedFanout);
        }
    }

    private readonly record struct GcCounts(int Gen0, int Gen1, int Gen2)
    {
        public static GcCounts Capture() => new(GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
        public static GcCounts operator -(GcCounts after, GcCounts before) =>
            new(after.Gen0 - before.Gen0, after.Gen1 - before.Gen1, after.Gen2 - before.Gen2);
    }

    private enum ZombieWorkloadMode : byte
    {
        Idle,
        Direct,
        Obstacle
    }

    private readonly record struct LoadOptions(
        int[] PlayerCounts,
        int Ticks,
        int ChunkTicks,
        int[] ActorCounts,
        int ActorPlayers,
        int ActorTicks,
        bool WorldInteraction,
        bool ZombieBehavior,
        bool InterestScaling,
        bool ActivationPressure,
        bool MixedRoster,
        bool WorldgenStream,
        int WorldgenRadius,
        int WorldgenWorkers)
    {
        public static LoadOptions Parse(string[] args)
        {
            var counts = new[] { 10, 100, 500 };
            var ticks = DefaultTicks;
            var actorCounts = new[] { 100, 1_000 };
            var actorPlayers = 10;
            var worldInteraction = false;
            var zombieBehavior = false;
            var interestScaling = false;
            var activationPressure = false;
            var mixedRoster = false;
            var worldgenStream = false;
            var worldgenRadius = 4;
            var worldgenWorkers = 2;
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
                else if (args[i] == "--world-interaction")
                    worldInteraction = true;
                else if (args[i] == "--zombie-behavior")
                    zombieBehavior = true;
                else if (args[i] == "--interest-scaling")
                    interestScaling = true;
                else if (args[i] == "--activation-pressure")
                    activationPressure = true;
                else if (args[i] == "--mixed-roster")
                    mixedRoster = true;
                else if (args[i] == "--worldgen-stream")
                    worldgenStream = true;
                else if (args[i] == "--radius" && i + 1 < args.Length)
                    worldgenRadius = int.Parse(args[++i]);
                else if (args[i] == "--workers" && i + 1 < args.Length)
                    worldgenWorkers = int.Parse(args[++i]);
            }

            if (counts.Any(count => count <= 0) || actorCounts.Any(count => count <= 0) || actorPlayers <= 0 || ticks <= 0 ||
                worldgenRadius is < 0 or > 32 || worldgenWorkers is < 1 or > 64)
                throw new ArgumentOutOfRangeException(nameof(args), "Player counts and ticks must be positive.");
            return new LoadOptions(counts, ticks, Math.Min(ticks, 5), actorCounts, actorPlayers, ticks, worldInteraction, zombieBehavior, interestScaling, activationPressure, mixedRoster, worldgenStream, worldgenRadius, worldgenWorkers);
        }
    }

    private enum InterestLayout : byte
    {
        Clustered,
        Distributed,
        MovingObservers
    }

    private enum ActivationScenario : byte
    {
        Observed,
        NoObservers,
        FarObservers
    }
}
