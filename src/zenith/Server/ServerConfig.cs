using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Zenith.Server;

/// <summary>Config operacional carregada de <c>zenith.yml</c>.</summary>
sealed class ServerConfig
{
    public ServerSection Server { get; set; } = new();
    public WorldSection World { get; set; } = new();
    public AuthSection Auth { get; set; } = new();
    public ChatSection Chat { get; set; } = new();
    public NetworkSection Network { get; set; } = new();

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
    }

    public sealed class AuthSection
    {
        public bool RequireChainSignatures { get; set; }
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

        if (World.SpawnChunkRadius is < 0 or > 32)
            throw new InvalidOperationException($"world.spawn-chunk-radius must be 0..32 (got {World.SpawnChunkRadius}).");
        if (string.IsNullOrWhiteSpace(World.Name))
            throw new InvalidOperationException("world.name must not be empty.");

        if (Chat.MaxLength < 1)
            throw new InvalidOperationException($"chat.max-length must be >= 1 (got {Chat.MaxLength}).");
        if (Chat.RateCapacity <= 0)
            throw new InvalidOperationException($"chat.rate-capacity must be > 0 (got {Chat.RateCapacity}).");
        if (Chat.RateRefillPerSecond <= 0)
            throw new InvalidOperationException($"chat.rate-refill-per-second must be > 0 (got {Chat.RateRefillPerSecond}).");

        if (Network.CompressionThreshold < 0)
            throw new InvalidOperationException($"network.compression-threshold must be >= 0 (got {Network.CompressionThreshold}).");
    }
}

/// <summary>LoadOrCreate + validate de <c>zenith.yml</c>.</summary>
static class ServerConfigLoader
{
    public const string DefaultFileName = "zenith.yml";

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
        if (!File.Exists(path))
        {
            var defaults = new ServerConfig();
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

        ServerConfig config;
        try
        {
            // Sem IgnoreUnmatchedProperties: typo de chave (ex. mx-players) falha no deserialize.
            config = Deserializer.Deserialize<ServerConfig>(text)
                     ?? throw new InvalidOperationException($"Config '{path}' deserialized to null.");
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            throw new InvalidOperationException(
                $"Invalid zenith.yml ({path}): {ex.Message}",
                ex);
        }

        config.Validate();
        return config;
    }
}
