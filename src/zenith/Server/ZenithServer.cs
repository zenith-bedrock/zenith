using Zenith.Event;
using Zenith.Gameplay;
using Zenith.Gameplay.Runtime;
using Zenith.Gameplay.Systems;
using Zenith.Log;
using Zenith.Network;
using Zenith.Network.Session;
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
    private readonly IChunkStorage _chunkStorage;
    private readonly ILogger _logger;

    public RakNetServer RakNetServer { get; }
    public ServerContext Context { get; }
    public GameLoop GameLoop { get; }
    public ServerConfig Config { get; }

    public ZenithServer(ServerConfig config)
    {
        Config = config;
        var logger = new Logger();
        _logger = logger;
        logger.Info($"Zenith {ServerIdentity.ProductVersion} (protocol {ServerIdentity.ProtocolVersion} / {ServerIdentity.VersionName})");

        LoginIdentity.RequireChainSignatures = config.Auth.RequireChainSignatures;
        if (!config.Auth.RequireChainSignatures)
        {
            logger.Warning(
                "*** AUTH WARNING: auth.require-chain-signatures is false. " +
                "Any client can claim any username/UUID. Enable before public exposure. ***");
        }

        var players = new PlayerManager();
        var clock = new GameClock();
        var gameLoop = new GameLoop(clock, logger);
        gameLoop.Register(new TimeSyncSystem(players));
        gameLoop.Register(new MovementSystem(players));
        gameLoop.Register(new EquipmentSystem(players));
        gameLoop.Register(new ChatSystem(players));

        var blockPalette = BlockPaletteLoader.FromEmbeddedResource();
        Blocks.Load(blockPalette);
        logger.Info($"Block palette loaded (air={Blocks.Air}, stone={Blocks.Stone}, grass={Blocks.GrassBlock})");

        var itemPalette = ItemPaletteLoader.FromEmbeddedResource();
        // Boot contract: placeable starter blocks + air must exist in item palette.
        _ = itemPalette.Require("minecraft:air");
        _ = itemPalette.Require("minecraft:stone");
        _ = itemPalette.Require("minecraft:grass_block");
        _ = itemPalette.Require("minecraft:dirt");
        _ = itemPalette.Require("minecraft:oak_planks");
        _ = itemPalette.Require("minecraft:oak_log");
        _ = itemPalette.Require("minecraft:sand");
        _ = itemPalette.Require("minecraft:chest");
        logger.Info($"Item palette loaded ({itemPalette.Count} entries)");

        _chunkStorage = CreateChunkStorage(config, logger);
        var world = new World.World(_chunkStorage, logger);
        var recipes = RecipeRegistry.CreateDefault();
        var creative = CreativeCatalog.CreateDefault();
        gameLoop.Register(new BlockSystem(players, world));
        gameLoop.Register(new InventorySystem(players, world, recipes, creative));
        gameLoop.Register(new ChunkStreamSystem(players, world));

        Context = new ServerContext(logger, players, new EventBus(logger), clock, world, config, blockPalette, itemPalette, recipes, creative);
        GameLoop = gameLoop;

        RakNetServer = new RakNetServer(config.Server.Port)
        {
            Logger = logger,
            SessionListener = new ZenithSessionListener(Context),
            MaxConnections = (uint)config.Server.MaxPlayers,
            MaxConnectionsPerAddress = (uint)config.Server.MaxPlayersPerIp,
            Motd = config.Server.Motd,
            SubMotd = config.Server.SubMotd,
            ListGameMode = config.Server.Gamemode,
            ProtocolVersion = ServerIdentity.ProtocolVersion,
            VersionName = ServerIdentity.VersionName
        };
    }

    private static IChunkStorage CreateChunkStorage(ServerConfig config, Zenith.Raknet.Log.ILogger logger)
    {
        var path = config.World.Path?.Trim() ?? "";
        if (string.IsNullOrEmpty(path))
        {
            logger.Info("World storage: InMemoryChunkStorage (world.path empty)");
            return new InMemoryChunkStorage();
        }

        var dataRoot = ResolveDataRoot(path);
        var name = string.IsNullOrWhiteSpace(config.World.Name) ? "world" : config.World.Name.Trim();
        var storageDir = Path.Combine(dataRoot, "worlds", name);

        if (LooksLikeWorldsFolderMisconfig(path, dataRoot))
        {
            logger.Warning(
                $"*** WORLD PATH: '{path}' looks like a worlds/ folder, not the server data root. " +
                $"LevelDB will live at '{storageDir}' (…/worlds/<name>/ under the root). " +
                "Use world.path: . (next to the binary) or an absolute data root such as /app — not './worlds'.");
        }

        WarnIfOrphanedFlatLevelDb(dataRoot, storageDir, logger);

        try
        {
            Directory.CreateDirectory(storageDir);
            var storage = new LevelDbChunkStorage(storageDir);
            logger.Info($"World storage: LevelDbChunkStorage ({storageDir}) [root={dataRoot}, name={name}]");
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

    public async Task StartAsync()
    {
        var token = _lifetime.Token;
        var gameLoopTask = GameLoop.RunAsync(token);
        var raknetTask = RakNetServer.StartAsync();
        await Task.WhenAll(gameLoopTask, raknetTask);
    }

    public async Task ShutdownAsync()
    {
        _lifetime.Cancel();

        try
        {
            using var flushCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await Context.World.FlushPersistenceAsync(flushCts.Token).ConfigureAwait(false);
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

        await RakNetServer.ShutdownAsync().ConfigureAwait(false);
    }
}
