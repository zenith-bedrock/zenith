using Zenith.Event;
using Zenith.Diagnostics;
using System.Diagnostics;
using Zenith.Ecs;
using Zenith.Gameplay;
using Zenith.Gameplay.Runtime;
using Zenith.Gameplay.Systems;
using Zenith.Log;
using Zenith.Protocol;
using Zenith.Session;
using Zenith.Player;
using Zenith.Raknet;
using Zenith.Raknet.Log;
using Zenith.World;

namespace Zenith.Server;

/// <summary>
/// Composition root do servidor. Monta ServerContext, runtime de gameplay (GameLoop/GameClock)
/// e o transporte raknet. Sistemas de domínio se registram no GameLoop aqui.
/// </summary>
class ZenithServer
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _lifecycleGate = new();
    private readonly IChunkStorage _chunkStorage;
    private readonly ILogger _logger;
    private readonly ZenithSessionListener _sessionListener;
    private readonly GravitySystem _gravity;
    private readonly RuntimeTelemetry _telemetry;
    private readonly AsyncLogSink _logSink;
    private readonly TextWriter? _logFileWriter;
    private readonly StreamWriter? _diagnosticsFileWriter;
    private Task? _runTask;
    private Task? _shutdownTask;
    private Task? _gameLoopTask;
    private Task? _raknetTask;
    private LifecycleState _lifecycleState;

    public RakNetServer RakNetServer { get; }
    public ServerContext Context { get; }
    public GameLoop GameLoop { get; }
    public ServerConfig Config { get; }
    public string ConfigPath { get; }
    /// <summary>Read-only diagnostics entry point; callers may capture console or JSON snapshots.</summary>
    public DiagnosticsRuntime Diagnostics => Context.Diagnostics.Runtime;

    public ZenithServer(ServerConfig config, string configPath)
    {
        Config = config;
        ConfigPath = configPath;

        // Debug tooling (Phase XXIII) — log.to-file: one text log + one diagnostics-snapshot jsonl
        // per run under {data-root}/logs/, sharing a timestamp so the pair is easy to correlate.
        string? logFilePath = null;
        if (config.Log.ToFile)
        {
            var logsDir = Path.Combine(ServerConfigPaths.ResolvePersistentRoot(), "logs");
            Directory.CreateDirectory(logsDir);
            var runStamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            logFilePath = Path.Combine(logsDir, $"zenith-{runStamp}.log");
            _logFileWriter = new StreamWriter(logFilePath, append: false);
            _diagnosticsFileWriter = new StreamWriter(
                Path.Combine(logsDir, $"zenith-diagnostics-{runStamp}.jsonl"), append: false) { AutoFlush = true };
        }

        _logSink = new AsyncLogSink(fileWriter: _logFileWriter);

        var serverLogger = new Logger(_logSink)
        {
            LogLevel = ServerConfig.ParseLogLevel(config.Log.Server, "server")
        };
        var raknetLogger = new Logger(_logSink)
        {
            LogLevel = ServerConfig.ParseLogLevel(config.Log.Raknet, "raknet")
        };
        _logger = serverLogger;
        serverLogger.Info($"Zenith {ServerIdentity.ProductVersion} (protocol {ServerIdentity.ProtocolVersion} / {ServerIdentity.VersionName})");
        serverLogger.Info($"config: {configPath}");
        var dataDir = ServerConfigPaths.ResolveDataDirectory();
        if (dataDir is not null)
            serverLogger.Info($"data root ({ServerConfigPaths.DataDirEnvironmentVariable}): {dataDir}");
        if (logFilePath is not null)
            serverLogger.Info($"log.to-file: writing {logFilePath} (+ diagnostics jsonl alongside it)");
        serverLogger.Info($"log.server={config.Log.Server} log.raknet={config.Log.Raknet}");
        serverLogger.Info($"auth.accept: {config.Auth.EffectiveAcceptSummary}");
        if (!config.Auth.RequireStrictXbox)
        {
            serverLogger.Warning(
                "*** AUTH WARNING: auth.accept includes self-signed and/or offline. " +
                "Clients may join without Xbox Live. Use accept: [xbox] before public exposure. ***");
        }
        var players = new PlayerManager();
        var clock = new GameClock();
        var entities = new EntityRuntime(); // Phase XXI/XXII — ECS-authoritative storage for Zombie/Minecart/Projectile/Cow/Skeleton/Spider.
        var creepers = new CreeperStore();
        var endermen = new EndermanStore();
        var bats = new BatStore();
        var villagers = new VillagerStore();
        var golems = new GolemStore();
        var fish = new FishStore();
        var diagnostics = new ServerRuntimeDiagnostics();
        var gameLoop = new GameLoop(clock, players, serverLogger, diagnostics.Runtime, diagnostics.Tick);
        RegisterEarlySystems(gameLoop, players, diagnostics);

        var blockPalette = BlockPaletteLoader.FromEmbeddedResource();
        Blocks.Load(blockPalette);
        serverLogger.Info($"Block palette loaded (air={Blocks.Air}, stone={Blocks.Stone}, grass={Blocks.GrassBlock})");

        var itemPalette = ItemPaletteLoader.FromEmbeddedResource();
        // Boot contract: placeable starter blocks + air must exist in item palette.
        _ = itemPalette.Require("minecraft:air");
        _ = itemPalette.Require("minecraft:stone");
        _ = itemPalette.Require("minecraft:grass_block");
        _ = itemPalette.Require("minecraft:dirt");
        _ = itemPalette.Require("minecraft:oak_planks");
        _ = itemPalette.Require("minecraft:oak_log");
        _ = itemPalette.Require("minecraft:oak_leaves");
        _ = itemPalette.Require("minecraft:sand");
        _ = itemPalette.Require("minecraft:gravel");
        _ = itemPalette.Require("minecraft:bedrock");
        _ = itemPalette.Require("minecraft:water");
        _ = itemPalette.Require("minecraft:cobblestone");
        _ = itemPalette.Require("minecraft:deepslate");
        _ = itemPalette.Require("minecraft:chest");
        Tools.Load(itemPalette);
        serverLogger.Info($"Item palette loaded ({itemPalette.Count} entries); curated tools ready");

        _chunkStorage = CreateChunkStorage(config, serverLogger);
        var worldIdentity = WorldIdentity.Reconcile(
            _chunkStorage, new WorldMetadata(config.World.Terrain, config.World.Seed), serverLogger);
        var terrain = TerrainProviders.Create(worldIdentity.Terrain, worldIdentity.Seed, diagnostics.Worldgen);
        serverLogger.Info($"world.terrain={worldIdentity.Terrain} seed={worldIdentity.Seed}");
        var world = new World.World(
            _chunkStorage,
            serverLogger,
            terrain,
            diagnostics.Worldgen,
            config.World.ChunkGenerationWorkers,
            config.World.ChunkGenerationCacheColumns);
        var recipes = RecipeRegistry.CreateDefault(itemPalette);
        var creative = CreativeCatalog.CreateDefault(itemPalette);
        var gravity = RegisterWorldSystems(
            gameLoop, diagnostics, world, players, entities, creepers, endermen,
            bats, villagers, golems, fish, itemPalette, recipes, creative);

        var eventBus = new EventBus(serverLogger);
        Context = new ServerContext(serverLogger, players, eventBus, clock, world, config, blockPalette, itemPalette, recipes, creative, diagnostics);
        GameLoop = gameLoop;
        _gravity = gravity;

        // First real EventBus domain consumers (ADR §78) — join/leave system chat.
        eventBus.Subscribe<PlayerLoginEvent>(e => PlayerPresenceAnnouncer.OnLogin(Context, e));
        eventBus.Subscribe<PlayerQuitEvent>(e => PlayerPresenceAnnouncer.OnQuit(Context, e));

        _sessionListener = new ZenithSessionListener(Context);
        var serverGuid = LoadOrCreateServerGuid(ServerConfigPaths.ResolvePersistentRoot(), serverLogger);
        RakNetServer = new RakNetServer(config.Server.Port, serverGuid)
        {
            Logger = raknetLogger,
            SessionListener = _sessionListener,
            MaxConnections = (uint)config.Server.MaxPlayers,
            MaxConnectionsPerAddress = (uint)config.Server.MaxPlayersPerIp,
            Motd = config.Server.Motd,
            SubMotd = config.Server.SubMotd,
            ListGameMode = config.Server.Gamemode,
            ProtocolVersion = ServerIdentity.ProtocolVersion,
            VersionName = ServerIdentity.VersionName,
            OnlinePlayerCount = () => Context.PlayerManager.Count,
            OnListening = _ => serverLogger.Info("Server ready.")
        };
        _telemetry = new RuntimeTelemetry(serverLogger);
        var diagnosticsFileTicks = 0L;
        const long diagnosticsFileEveryTicks = 20 * 30; // Same ~30s cadence as RuntimeTelemetry's console line.
        gameLoop.SetTickObserver(elapsed =>
        {
            var actors = entities.Entities.AliveCount +
                         creepers.Active.Count + endermen.Active.Count +
                         bats.Active.Count + villagers.Active.Count + golems.Active.Count +
                         fish.Active.Count + world.FallingBlocks.Active.Count + world.FloorDrops.Count;
            diagnostics.RecordRuntimeHealth(elapsed, clock.MeasuredTps, players.Count, actors, world.OverrideCount,
                diagnostics.Systems.Count, RakNetServer);
            _telemetry.RecordTick(elapsed, players.Count, actors, RakNetServer);

            if (_diagnosticsFileWriter is not null && ++diagnosticsFileTicks % diagnosticsFileEveryTicks == 0)
                _diagnosticsFileWriter.WriteLine(diagnostics.Runtime.CaptureSnapshot().ToJson(indented: false));
        });
    }

    /// <summary>
    /// Phase XIII.2: the systems that need only <see cref="PlayerManager"/> — registered before
    /// palettes/world exist. Grouped here so the constructor reads as one sequence of named steps
    /// instead of interleaving registration with palette/world bootstrap.
    /// </summary>
    private static void RegisterEarlySystems(GameLoop gameLoop, PlayerManager players, ServerRuntimeDiagnostics diagnostics)
    {
        gameLoop.Register(new TimeSyncSystem(), diagnostics.System("time-sync"));
        // Movement before Block/Inventory: IsSneaking must be applied before sneak-place / chest open (§53/§56).
        gameLoop.Register(new MovementSystem(players), diagnostics.System("movement"));
        // ItemUseOnActor player targets are resolved after movement established this tick's pose.
        gameLoop.Register(new PlayerMeleeSystem(players), diagnostics.System("player-melee"));
        gameLoop.Register(new ChatSystem(), diagnostics.System("chat"));
        gameLoop.Register(new GameModeSystem(), diagnostics.System("game-mode"));
    }

    /// <summary>
    /// Phase XIII.2: every system that needs the world/palettes/recipes, registered in the exact
    /// order the inline comments below require. Returns only <see cref="GravitySystem"/> — the sole
    /// system the constructor still needs a handle to after registration (graceful-shutdown flush).
    /// </summary>
    private static GravitySystem RegisterWorldSystems(
        GameLoop gameLoop,
        ServerRuntimeDiagnostics diagnostics,
        World.World world,
        PlayerManager players,
        EntityRuntime entities,
        CreeperStore creeperStore,
        EndermanStore endermanStore,
        BatStore batStore,
        VillagerStore villagerStore,
        GolemStore golemStore,
        FishStore fishStore,
        ItemPalette itemPalette,
        RecipeRegistry recipes,
        CreativeCatalog creative)
    {
        var gravity = new GravitySystem(world, players);
        // Phase XXI/XXII — ECS-authoritative roster. DamageDispatch (Phase XXII) is built once and
        // registered by every ECS-damageable system before Projectile is constructed — this is the
        // seam that replaced the old hardcoded Zombie/Minecart-only dispatch chain in
        // ProjectileSystem once a third (then fourth, fifth) ECS-damageable species arrived. Skeleton
        // is constructed after Projectile (it fires through it) and registers into the same dispatch.
        var damage = new DamageDispatch();
        var zombieSystem = new ZombieSystem(world, players, entities, itemPalette);
        damage.Register(zombieSystem.Owns, zombieSystem.TryApplyDamage);
        var minecartSystem = new MinecartSystem(world, players, entities, itemPalette);
        damage.Register(minecartSystem.Owns, minecartSystem.TryApplyDamage);
        var cowSystem = new CowSystem(world, players, entities, itemPalette);
        damage.Register(cowSystem.Owns, cowSystem.TryApplyDamage);
        var spiderSystem = new SpiderSystem(world, players, entities, itemPalette);
        damage.Register(spiderSystem.Owns, spiderSystem.TryApplyDamage);
        var projectileSystem = new ProjectileSystem(world, players, entities, damage);
        var skeletonSystem = new SkeletonSystem(world, players, entities, projectileSystem, itemPalette);
        damage.Register(skeletonSystem.Owns, skeletonSystem.TryApplyDamage);

        gameLoop.Register(zombieSystem, diagnostics.System("zombie"));
        gameLoop.Register(minecartSystem, diagnostics.System("minecart"));
        gameLoop.Register(projectileSystem, diagnostics.System("projectile"));
        gameLoop.Register(skeletonSystem, diagnostics.System("skeleton"));
        gameLoop.Register(cowSystem, diagnostics.System("cow"));
        gameLoop.Register(new CreeperSystem(world, players, creeperStore, itemPalette), diagnostics.System("creeper"));
        gameLoop.Register(new EndermanSystem(world, players, endermanStore, itemPalette), diagnostics.System("enderman"));
        gameLoop.Register(new BatSystem(world, players, batStore, itemPalette), diagnostics.System("bat"));
        gameLoop.Register(spiderSystem, diagnostics.System("spider"));
        gameLoop.Register(new VillagerSystem(world, players, villagerStore, itemPalette), diagnostics.System("villager"));
        gameLoop.Register(new GolemSystem(world, players, golemStore, itemPalette), diagnostics.System("golem"));
        gameLoop.Register(new FishSystem(world, players, fishStore, itemPalette), diagnostics.System("fish"));
        gameLoop.Register(new BlockDigSystem(world), diagnostics.System("block-dig"));
        gameLoop.Register(new BlockEditSystem(players, world), diagnostics.System("block-edit"));
        gameLoop.Register(gravity, diagnostics.System("gravity"));
        gameLoop.Register(new FloorDropSystem(world), diagnostics.System("floor-drop"));
        gameLoop.Register(new InventorySystem(players, world, recipes, creative), diagnostics.System("inventory"));
        // After Inventory: eating also mutates the held stack and must land before Equipment diffs it.
        gameLoop.Register(new HungerSystem(itemPalette, players), diagnostics.System("hunger"));
        gameLoop.Register(new EffectSystem(players), diagnostics.System("effect"));
        // Inventory/blocks may change the selected held stack; replicate the final same-tick state.
        gameLoop.Register(new EquipmentSystem(), diagnostics.System("equipment"));
        gameLoop.Register(new ChunkStreamSystem(world, diagnostics.Worldgen), diagnostics.System("chunk-stream"));

        return gravity;
    }

    /// <summary>Stable RakNet GUID across restarts so LAN list identity does not churn (§41).</summary>
    private static ulong LoadOrCreateServerGuid(string dataDir, ILogger logger)
    {
        var path = Path.Combine(dataDir, "server.guid");
        try
        {
            if (File.Exists(path) &&
                ulong.TryParse(File.ReadAllText(path).Trim(), out var existing) &&
                existing != 0)
            {
                return existing;
            }
        }
        catch (Exception ex)
        {
            logger.Warning($"Could not read server.guid: {ex.Message}");
        }

        Span<byte> bytes = stackalloc byte[8];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        var guid = BitConverter.ToUInt64(bytes);
        try
        {
            File.WriteAllText(path, guid.ToString());
            logger.Info($"Wrote stable RakNet GUID to {path}");
        }
        catch (Exception ex)
        {
            logger.Warning($"Could not persist server.guid: {ex.Message}");
        }

        return guid;
    }

    private static IChunkStorage CreateChunkStorage(ServerConfig config, Zenith.Raknet.Log.ILogger logger)
    {
        var configuredPath = config.World.Path?.Trim() ?? "";
        var dataRoot = ResolveWorldDataRoot(configuredPath);
        if (dataRoot is null)
        {
            logger.Info("World storage: InMemoryChunkStorage (world.path empty, no ZENITH_DATA)");
            return new InMemoryChunkStorage();
        }

        var name = string.IsNullOrWhiteSpace(config.World.Name) ? "world" : config.World.Name.Trim();
        var storageDir = Path.Combine(dataRoot, "worlds", name);
        var pathForMisconfigCheck = string.IsNullOrEmpty(configuredPath) ? dataRoot : configuredPath;

        if (LooksLikeWorldsFolderMisconfig(pathForMisconfigCheck, dataRoot))
        {
            logger.Warning(
                $"*** WORLD PATH: '{pathForMisconfigCheck}' looks like a worlds/ folder, not the server data root. " +
                $"LevelDB will live at '{storageDir}' (…/worlds/<name>/ under the root). " +
                "Use world.path: . (next to the binary), leave path empty with ZENITH_DATA set, or an absolute data root — not './worlds'.");
        }

        WarnIfOrphanedFlatLevelDb(dataRoot, storageDir, logger);

        try
        {
            Directory.CreateDirectory(storageDir);
            var storage = new LevelDbChunkStorage(storageDir);
            var via = string.IsNullOrEmpty(configuredPath) ? "ZENITH_DATA" : "world.path";
            logger.Info($"World storage: LevelDbChunkStorage ({storageDir}) [root={dataRoot}, name={name}, via={via}]");
            return storage;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to open LevelDB world at '{storageDir}'. Zenith will not fall back to in-memory storage.",
                ex);
        }
    }

    /// <summary>
    /// LevelDB data root, or null for InMemory.
    /// Empty <paramref name="worldPath"/> + <c>ZENITH_DATA</c> → that dir; empty without → null;
    /// non-empty → <see cref="ResolveDataRoot"/> (ADR §20).
    /// </summary>
    internal static string? ResolveWorldDataRoot(string? worldPath)
    {
        var path = worldPath?.Trim() ?? "";
        if (string.IsNullOrEmpty(path))
            return ServerConfigPaths.ResolveDataDirectory();
        return ResolveDataRoot(path);
    }

    /// <summary>
    /// Relative <c>world.path</c> is always under <see cref="AppContext.BaseDirectory"/> (next to the DLL),
    /// never the process cwd — same rule as <c>zenith.yml</c> (ADR §5 / §20).
    /// </summary>
    internal static string ResolveDataRoot(string path)
    {
        if (Path.IsPathRooted(path))
            return Path.GetFullPath(path);
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
    }

    private static bool LooksLikeWorldsFolderMisconfig(string configured, string resolvedRoot)
    {
        var leaf = Path.GetFileName(resolvedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.Equals(leaf, "worlds", StringComparison.OrdinalIgnoreCase))
            return true;
        var trimmed = configured.Trim().TrimEnd('/', '\\');
        return trimmed.EndsWith("worlds", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ops visibility: old flat LevelDB at world.path root would be silently abandoned when
    /// opening a new empty worlds/&lt;name&gt; (ADR §20).
    /// </summary>
    private static void WarnIfOrphanedFlatLevelDb(string root, string storageDir, Zenith.Raknet.Log.ILogger logger)
    {
        try
        {
            if (!Directory.Exists(root)) return;
            var rootCurrent = Path.Combine(root, "CURRENT");
            var hasRootArtifact = File.Exists(rootCurrent) ||
                                  Directory.EnumerateFiles(root, "*.ldb").Any();
            if (!hasRootArtifact) return;

            var storageLooksNew = !Directory.Exists(storageDir) ||
                                  (!File.Exists(Path.Combine(storageDir, "CURRENT")) &&
                                   !Directory.EnumerateFiles(storageDir, "*.ldb").Any());
            if (!storageLooksNew) return;

            logger.Warning(
                $"*** WORLD PATH: LevelDB artifacts found at data root '{root}' but storage will open empty at '{storageDir}'. " +
                "Move or recreate the world under worlds/<name>/ — Zenith will not silently use the old flat directory (ADR §20).");
        }
        catch
        {
            // Detection is best-effort; boot continues.
        }
    }

    /// <summary>
    /// Runs both critical loops. Repeated calls return the same lifetime task.
    /// An instance is single-use: a shutdown requested before its first run makes a later start invalid.
    /// An unexpected termination of either critical loop initiates coordinated shutdown.
    /// </summary>
    public Task RunAsync()
    {
        lock (_lifecycleGate)
        {
            if (_runTask is not null)
                return _runTask;

            if (_lifecycleState is LifecycleState.Stopping or LifecycleState.Stopped)
            {
                return Task.FromException(new InvalidOperationException(
                    "A stopped ZenithServer instance cannot be started. Create a new server instance."));
            }

            _lifecycleState = LifecycleState.Running;
            return _runTask = RunCoreAsync();
        }
    }

    private async Task RunCoreAsync()
    {
        var gameLoopTask = _gameLoopTask = GameLoop.RunAsync(_lifetime.Token);
        var raknetTask = _raknetTask = RakNetServer.StartAsync();
        var completed = await Task.WhenAny(gameLoopTask, raknetTask).ConfigureAwait(false);
        try
        {
            await completed.ConfigureAwait(false);
        }
        finally
        {
            await ShutdownAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Idempotent coordinated shutdown. Repeated calls return the same cleanup task and it completes
    /// only after critical loops, authoritative settle, and persistence cleanup stop.
    /// </summary>
    public Task ShutdownAsync()
    {
        lock (_lifecycleGate)
        {
            if (_shutdownTask is not null)
                return _shutdownTask;

            _lifecycleState = LifecycleState.Stopping;
            return _shutdownTask = ShutdownCoreAsync();
        }
    }

    private async Task ShutdownCoreAsync()
    {
        try
        {
            _lifetime.Cancel();
            _sessionListener.StopAccepting();

            // Kick with Bedrock DisconnectPacket before UDP dies (§41) — HandleClose enqueues inv Puts.
            _logger.Info("Disconnecting sessions...");
            _sessionListener.DisconnectAll("Server closed");

            await RakNetServer.ShutdownAsync().ConfigureAwait(false);

            Task? gameLoop;
            lock (_lifecycleGate)
                gameLoop = _gameLoopTask;
            if (gameLoop is not null)
            {
                try { await gameLoop.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    // RunAsync preserves the original failure for its caller; shutdown must still settle/flush.
                    _logger.Error($"GameLoop stopped with an error during shutdown: {ex}");
                }
            }

            // Settle in-flight sand/gravel into overlays before LevelDB flush (ADR §57).
            try
            {
                _gravity.SettleAllPending();
            }
            catch (Exception ex)
            {
                _logger.Warning($"Gravity settle failed during shutdown: {ex.Message}");
            }

            try
            {
                var flushStarted = Stopwatch.GetTimestamp();
                using var flushCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await Context.World.FlushPersistenceAsync(flushCts.Token).ConfigureAwait(false);
                _telemetry.RecordFlush(Stopwatch.GetElapsedTime(flushStarted));
            }
            catch (OperationCanceledException)
            {
                _logger.Warning("Persistence flush timed out after 5s during shutdown; some writes may be incomplete.");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Persistence flush failed during shutdown: {ex.Message}");
            }

            await Context.World.StopGenerationAsync().ConfigureAwait(false);

            if (_chunkStorage is IDisposable disposable)
                disposable.Dispose();

            await _logSink.DisposeAsync().ConfigureAwait(false);
            _logFileWriter?.Dispose();
            _diagnosticsFileWriter?.Flush();
            _diagnosticsFileWriter?.Dispose();
        }
        finally
        {
            lock (_lifecycleGate)
                _lifecycleState = LifecycleState.Stopped;
        }
    }

    /// <summary>
    /// Scheduling state only. A critical-loop fault remains observable through <see cref="RunAsync"/>'s
    /// returned task; after cleanup the server is terminally stopped rather than restartable.
    /// </summary>
    private enum LifecycleState
    {
        Created,
        Running,
        Stopping,
        Stopped
    }
}
