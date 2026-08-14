using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Zenith.Raknet.Enumerator;
using Zenith.World;

namespace Zenith.Server;

/// <summary>Config operacional carregada de <c>zenith.yml</c>.</summary>
sealed class ServerConfig
{
    public ServerSection Server { get; set; } = new();
    public WorldSection World { get; set; } = new();
    public AuthSection Auth { get; set; } = new();
    public ChatSection Chat { get; set; } = new();
    public NetworkSection Network { get; set; } = new();
    public LogSection Log { get; set; } = new();

    public sealed class ServerSection
    {
        public int Port { get; set; } = 19132;
        public string Motd { get; set; } = "Zenith Bedrock";
        public string SubMotd { get; set; } = "Test";
        public int MaxPlayers { get; set; } = 20;
        public int MaxPlayersPerIp { get; set; } = 3;
        public string Gamemode { get; set; } = "Survival";
    }

    public sealed class WorldSection
    {
        /// <summary>World folder name under <c>{Path}/worlds/{Name}/</c> (default <c>world</c>).</summary>
        public string Name { get; set; } = "world";

        /// <summary>
        /// Server data root. Empty = InMemory (volatile). Non-empty ⇒ LevelDB at
        /// <c>{Path}/worlds/{Name}/</c> (ADR §20). Relative paths resolve against
        /// <see cref="AppContext.BaseDirectory"/> (next to the DLL), not the process cwd.
        /// Use <c>.</c> for persistence beside the binary; Docker sample uses <c>/app</c>.
        /// Do not set this to <c>./worlds</c> — that folder is created under the root.
        /// </summary>
        public string Path { get; set; } = "";

        public int SpawnChunkRadius { get; set; } = 4;

        /// <summary>
        /// Columns published before <c>PLAYER_SPAWN</c> (ADR §70 ready-disk).
        /// Must be <c>0..SpawnChunkRadius</c>; default 2. View radius stays on the tracker for ChunkStream.
        /// </summary>
        public int SpawnReadyRadius { get; set; } = 2;

        /// <summary>Base terrain generator: <c>flat</c> (default) or <c>noise</c> (ADR §63).</summary>
        public string Terrain { get; set; } = "flat";

        /// <summary>Seed for <c>noise</c> terrain; ignored for flat.</summary>
        public int Seed { get; set; } = 1;
    }

    /// <summary>
    /// Login accept modes (<c>auth.accept</c>). Names: xbox | self-signed | offline.
    /// </summary>
    public sealed class AuthSection
    {
        public const string ModeXbox = "xbox";
        public const string ModeSelfSigned = "self-signed";
        public const string ModeOffline = "offline";

        public static readonly string[] AllowedModes = [ModeXbox, ModeSelfSigned, ModeOffline];

        /// <summary>Default LAN: all three modes.</summary>
        public List<string> Accept { get; set; } = [ModeXbox, ModeSelfSigned, ModeOffline];

        private HashSet<string> _modes = new(StringComparer.Ordinal);

        [YamlIgnore]
        public bool AllowsXbox => _modes.Contains(ModeXbox);

        [YamlIgnore]
        public bool AllowsSelfSigned => _modes.Contains(ModeSelfSigned);

        [YamlIgnore]
        public bool AllowsOfflineFallback => _modes.Contains(ModeOffline);

        /// <summary>True when accept is exactly xbox — strict chain signature checks.</summary>
        [YamlIgnore]
        public bool RequireStrictXbox =>
            _modes.Count == 1 && _modes.Contains(ModeXbox);

        [YamlIgnore]
        public string EffectiveAcceptSummary =>
            _modes.Count == 0 ? "(none)" : string.Join(", ", AllowedModes.Where(_modes.Contains));

        internal void NormalizeAndValidate()
        {
            if (Accept is null || Accept.Count == 0)
            {
                throw new InvalidOperationException(
                    "auth.accept must list one or more of: xbox, self-signed, offline.");
            }

            _modes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in Accept)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    throw new InvalidOperationException(
                        "auth.accept entries must not be empty. Allowed: xbox, self-signed, offline.");
                }

                var mode = raw.Trim().ToLowerInvariant();
                if (mode is not (ModeXbox or ModeSelfSigned or ModeOffline))
                {
                    throw new InvalidOperationException(
                        $"auth.accept contains unknown mode '{raw}'. Allowed: xbox, self-signed, offline.");
                }

                _modes.Add(mode);
            }

            Accept = AllowedModes.Where(_modes.Contains).ToList();
        }
    }

    public sealed class ChatSection
    {
        public int MaxLength { get; set; } = 512;
        public int RateCapacity { get; set; } = 8;
        public double RateRefillPerSecond { get; set; } = 4;
    }

    public sealed class NetworkSection
    {
        public int CompressionThreshold { get; set; } = 256;
    }

    /// <summary>
    /// Independent severity for Bedrock/server vs RakNet transport.
    /// Aliases are severity ladders mapped onto <see cref="LogLevel"/> flags
    /// (<c>info</c> = Info|Warning|Error, not Info alone).
    /// </summary>
    public sealed class LogSection
    {
        public string Server { get; set; } = "info";
        public string Raknet { get; set; } = "warn";

        /// <summary>
        /// When true, also writes every log line to <c>{data-root}/logs/zenith-{timestamp}.log</c>
        /// (one file per server run) and periodic diagnostics snapshots to a paired
        /// <c>zenith-diagnostics-{timestamp}.jsonl</c> — off by default so normal runs don't
        /// accumulate files.
        /// </summary>
        public bool ToFile { get; set; } = false;
    }

    /// <summary>
    /// Maps yaml aliases to flag masks. <c>info</c> keeps Warning/Error;
    /// <c>debug</c>/<c>all</c> → <see cref="LogLevel.All"/>.
    /// </summary>
    internal static LogLevel ParseLogLevel(string raw, string key)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException(
                $"log.{key} must not be empty. Allowed: none, info, warn, warning, error, debug, all.");
        }

        var level = raw.Trim().ToLowerInvariant();
        return level switch
        {
            "none" => LogLevel.None,
            "info" => LogLevel.Info | LogLevel.Warning | LogLevel.Error,
            "warn" or "warning" => LogLevel.Warning | LogLevel.Error,
            "error" => LogLevel.Error,
            "debug" or "all" => LogLevel.All,
            _ => throw new InvalidOperationException(
                $"log.{key} contains unknown level '{raw}'. Allowed: none, info, warn, warning, error, debug, all.")
        };
    }

    public void Validate()
    {
        if (Server.Port is < 1 or > 65535)
            throw new InvalidOperationException($"server.port must be 1..65535 (got {Server.Port}).");
        if (Server.MaxPlayers < 1)
            throw new InvalidOperationException($"server.max-players must be >= 1 (got {Server.MaxPlayers}).");
        if (Server.MaxPlayersPerIp < 1)
            throw new InvalidOperationException($"server.max-players-per-ip must be >= 1 (got {Server.MaxPlayersPerIp}).");
        if (string.IsNullOrWhiteSpace(Server.Motd))
            throw new InvalidOperationException("server.motd must not be empty.");
        if (string.IsNullOrWhiteSpace(Server.Gamemode))
            throw new InvalidOperationException("server.gamemode must not be empty.");
        if (Server.Gamemode is not ("Survival" or "Creative"))
            throw new InvalidOperationException(
                $"server.gamemode must be Survival or Creative (got '{Server.Gamemode}').");

        if (World.SpawnChunkRadius is < 0 or > 32)
            throw new InvalidOperationException($"world.spawn-chunk-radius must be 0..32 (got {World.SpawnChunkRadius}).");
        if (World.SpawnReadyRadius is < 0 or > 32)
            throw new InvalidOperationException($"world.spawn-ready-radius must be 0..32 (got {World.SpawnReadyRadius}).");
        if (World.SpawnReadyRadius > World.SpawnChunkRadius)
            throw new InvalidOperationException(
                $"world.spawn-ready-radius must be 0..spawn-chunk-radius " +
                $"(got ready={World.SpawnReadyRadius}, view={World.SpawnChunkRadius}).");
        if (string.IsNullOrWhiteSpace(World.Name))
            throw new InvalidOperationException("world.name must not be empty.");
        World.Terrain = TerrainProviders.NormalizeMode(World.Terrain);
        if (World.Terrain is not ("flat" or "noise"))
            throw new InvalidOperationException(
                $"world.terrain must be flat or noise (got '{World.Terrain}').");

        Auth.NormalizeAndValidate();

        if (Chat.MaxLength < 1)
            throw new InvalidOperationException($"chat.max-length must be >= 1 (got {Chat.MaxLength}).");
        if (Chat.RateCapacity <= 0)
            throw new InvalidOperationException($"chat.rate-capacity must be > 0 (got {Chat.RateCapacity}).");
        if (Chat.RateRefillPerSecond <= 0)
            throw new InvalidOperationException($"chat.rate-refill-per-second must be > 0 (got {Chat.RateRefillPerSecond}).");

        if (Network.CompressionThreshold < 0)
            throw new InvalidOperationException($"network.compression-threshold must be >= 0 (got {Network.CompressionThreshold}).");

        Log ??= new LogSection();
        _ = ParseLogLevel(Log.Server, "server");
        _ = ParseLogLevel(Log.Raknet, "raknet");
    }
}

/// <summary>LoadOrCreate + validate de <c>zenith.yml</c>.</summary>
static class ServerConfigLoader
{
    public const string DefaultFileName = "zenith.yml";

    private const string ObsoleteAuthKeyMessage =
        "auth.require-chain-signatures was removed. " +
        "Use auth.accept: [xbox], [xbox, self-signed], or [xbox, self-signed, offline]. " +
        "See deploy/zenith.yml.";

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(HyphenatedNamingConvention.Instance)
        .Build();

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(HyphenatedNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    /// <summary>
    /// Carrega config do path. Se o arquivo não existir, grava defaults.
    /// Falha de I/O na escrita ou chave desconhecida / bounds inválidos = exception (abort boot).
    /// </summary>
    public static ServerConfig LoadOrCreate(string path)
    {
        // Docker bind-mount of a missing host *file* creates a directory; File.Exists is false then.
        if (Directory.Exists(path))
        {
            throw new InvalidOperationException(
                $"Config path '{path}' is a directory. " +
                "If you mounted a config file, ensure the host file exists before deploy. " +
                "Docker creates a directory when the source file is missing. " +
                "Prefer a single volume on /data with ZENITH_DATA (see deploy/README.md). " +
                "Remove any bogus directory at that mount path and redeploy.");
        }

        if (!File.Exists(path))
        {
            var defaults = new ServerConfig();
            defaults.Validate();
            try
            {
                var yaml = Serializer.Serialize(defaults);
                File.WriteAllText(path, yaml);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to write default config to '{path}'. Zenith will not start without a writable config file.",
                    ex);
            }
        }

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to read config '{path}'.", ex);
        }

        RejectObsoleteAuthKeys(text, path);

        ServerConfig config;
        try
        {
            // Sem IgnoreUnmatchedProperties: typo de chave (ex. mx-players) falha no deserialize.
            config = Deserializer.Deserialize<ServerConfig>(text)
                     ?? throw new InvalidOperationException($"Config '{path}' deserialized to null.");
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            var msg = ex.Message;
            if (msg.Contains("require-chain-signatures", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Invalid zenith.yml ({path}): {ObsoleteAuthKeyMessage}",
                    ex);
            }

            throw new InvalidOperationException(
                $"Invalid zenith.yml ({path}): {ex.Message}",
                ex);
        }

        config.Validate();
        return config;
    }

    private static void RejectObsoleteAuthKeys(string yamlText, string path)
    {
        // Loud abort — do not migrate or rewrite the operator's file.
        foreach (var line in yamlText.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('#')) continue;
            if (trimmed.StartsWith("require-chain-signatures:", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Invalid zenith.yml ({path}): {ObsoleteAuthKeyMessage}");
            }
        }
    }
}
