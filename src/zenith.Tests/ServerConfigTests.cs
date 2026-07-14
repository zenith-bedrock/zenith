using Xunit;
using Zenith.Server;

namespace Zenith.Tests;

public class ServerConfigLoaderTests
{
    [Fact]
    public void LoadOrCreate_writes_defaults_then_loads()
    {
        var path = Path.Combine(Path.GetTempPath(), $"zenith-cfg-{Guid.NewGuid():N}.yml");
        try
        {
            Assert.False(File.Exists(path));
            var config = ServerConfigLoader.LoadOrCreate(path);
            Assert.True(File.Exists(path));
            Assert.Equal(19132, config.Server.Port);
            Assert.Equal(20, config.Server.MaxPlayers);
            Assert.False(config.Auth.RequireChainSignatures);
            Assert.Equal("", config.World.Path);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void LoadOrCreate_applies_overrides()
    {
        var path = Path.Combine(Path.GetTempPath(), $"zenith-cfg-{Guid.NewGuid():N}.yml");
        try
        {
            File.WriteAllText(path, """
                server:
                  port: 25565
                  motd: Custom
                  max-players: 5
                auth:
                  require-chain-signatures: true
                """);
            var config = ServerConfigLoader.LoadOrCreate(path);
            Assert.Equal(25565, config.Server.Port);
            Assert.Equal("Custom", config.Server.Motd);
            Assert.Equal(5, config.Server.MaxPlayers);
            Assert.True(config.Auth.RequireChainSignatures);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Validate_rejects_invalid_port()
    {
        var config = new ServerConfig();
        config.Server.Port = 0;
        Assert.Throws<InvalidOperationException>(() => config.Validate());
    }

    [Fact]
    public void Validate_rejects_zero_rate_capacity()
    {
        var config = new ServerConfig();
        config.Chat.RateCapacity = 0;
        Assert.Throws<InvalidOperationException>(() => config.Validate());
    }

    [Fact]
    public void LoadOrCreate_rejects_unknown_key()
    {
        var path = Path.Combine(Path.GetTempPath(), $"zenith-cfg-{Guid.NewGuid():N}.yml");
        try
        {
            File.WriteAllText(path, """
                server:
                  mx-players: 10
                """);
            var ex = Assert.Throws<InvalidOperationException>(() => ServerConfigLoader.LoadOrCreate(path));
            Assert.Contains("Invalid zenith.yml", ex.Message);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Empty_world_path_is_in_memory_intent()
    {
        var config = new ServerConfig();
        Assert.True(string.IsNullOrEmpty(config.World.Path));
    }
}
