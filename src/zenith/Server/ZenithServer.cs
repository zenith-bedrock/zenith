using Zenith.Event;
using System.Diagnostics;
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

    public ZenithServer(ServerConfig config, string configPath)
    {
        Config = config;
        ConfigPath = configPath;
        var serverLogger = new Logger
        {
            LogLevel = ServerConfig.ParseLogLevel(config.Log.Server, "server")
        };
        var raknetLogger = new Logger
        {
            LogLevel = ServerConfig.ParseLogLevel(config.Log.Raknet, "raknet")
        };
        _logger = serverLogger;
        serverLogger.Info($"Zenith {ServerIdentity.ProductVersion} (protocol {ServerIdentity.ProtocolVersion} / {ServerIdentity.VersionName})");
        serverLogger.Info($"config: {configPath}");
        var dataDir = ServerConfigPaths.ResolveDataDirectory();
        if (dataDir is not null)
            serverLogger.Info($"data root ({ServerConfigPaths.DataDirEnvironmentVariable}): {dataDir}");
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
        var zombies = new ZombieStore();
        var projectiles = new ProjectileStore();
        var gameLoop = new GameLoop(clock, players, serverLogger);
        gameLoop.Register(new TimeSyncSystem());
        // Movement before Block/Inventory: IsSneaking must be applied before sneak-place / chest open (§53/§56).
        gameLoop.Register(new MovementSystem(players));
        gameLoop.Register(new ChatSystem());
        gameLoop.Register(new GameModeSystem());

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
        var terrain = TerrainProviders.Create(config.World.Terrain, config.World.Seed);
        serverLogger.Info($"world.terrain={config.World.Terrain} seed={config.World.Seed}");
        var world = new World.World(_chunkStorage, serverLogger, terrain);
        var recipes = RecipeRegistry.CreateDefault();
        var creative = CreativeCatalog.CreateDefault(itemPalette);
        var gravity = new GravitySystem(world, players);
        var zombieSystem = new ZombieSystem(world, players, zombies);
        gameLoop.Register(zombieSystem);
        gameLoop.Register(new ProjectileSystem(world, players, projectiles, zombieSystem));
        gameLoop.Register(new BlockDigSystem(world));
        gameLoop.Register(new BlockEditSystem(players, world));
        gameLoop.Register(gravity);
        gameLoop.Register(new FloorDropSystem(world));
        gameLoop.Register(new InventorySystem(players, world, recipes, creative));
        // Inventory/blocks may change the selected held stack; replicate the final same-tick state.
        gameLoop.Register(new EquipmentSystem());
        gameLoop.Register(new ChunkStreamSystem(world));

        var eventBus = new EventBus(serverLogger);
        Context = new ServerContext(serverLogger, players, eventBus, clock, world, config, blockPalette, itemPalette, recipes, creative);
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
        gameLoop.SetTickObserver(elapsed => _telemetry.RecordTick(elapsed, players.Count,
            zombies.Active.Count + projectiles.Active.Count + world.FallingBlocks.Active.Count + world.FloorDrops.Count,
            RakNetServer));
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

            if (_chunkStorage is IDisposable disposable)
                disposable.Dispose();
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
