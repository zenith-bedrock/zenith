using Xunit;
using Zenith.Raknet.Enumerator;
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
            Assert.True(config.Auth.AllowsXbox);
            Assert.True(config.Auth.AllowsSelfSigned);
            Assert.True(config.Auth.AllowsOfflineFallback);
            Assert.False(config.Auth.RequireStrictXbox);
            Assert.Equal("", config.World.Path);
            Assert.Equal("info", config.Log.Server);
            Assert.Equal("warn", config.Log.Raknet);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void LoadOrCreate_yaml_without_log_section_keeps_defaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"zenith-cfg-{Guid.NewGuid():N}.yml");
        try
        {
            File.WriteAllText(path, """
                server:
                  port: 19132
                  motd: Custom
                auth:
                  accept:
                    - xbox
                    - self-signed
                    - offline
                """);
            var config = ServerConfigLoader.LoadOrCreate(path);
            Assert.Equal("info", config.Log.Server);
            Assert.Equal("warn", config.Log.Raknet);
            Assert.Equal(
                LogLevel.Info | LogLevel.Warning | LogLevel.Error,
                ServerConfig.ParseLogLevel(config.Log.Server, "server"));
            Assert.Equal(
                LogLevel.Warning | LogLevel.Error,
                ServerConfig.ParseLogLevel(config.Log.Raknet, "raknet"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void LoadOrCreate_applies_log_overrides()
    {
        var path = Path.Combine(Path.GetTempPath(), $"zenith-cfg-{Guid.NewGuid():N}.yml");
        try
        {
            File.WriteAllText(path, """
                server:
                  port: 19132
                  motd: Custom
                auth:
                  accept:
                    - xbox
                log:
                  server: debug
                  raknet: error
                """);
            var config = ServerConfigLoader.LoadOrCreate(path);
            Assert.Equal("debug", config.Log.Server);
            Assert.Equal("error", config.Log.Raknet);
            Assert.Equal(LogLevel.All, ServerConfig.ParseLogLevel(config.Log.Server, "server"));
            Assert.Equal(LogLevel.Error, ServerConfig.ParseLogLevel(config.Log.Raknet, "raknet"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void LoadOrCreate_rejects_directory_path()
    {
        var path = Path.Combine(Path.GetTempPath(), $"zenith-cfg-dir-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => ServerConfigLoader.LoadOrCreate(path));
            Assert.Contains("is a directory", ex.Message);
        }
        finally
        {
            Directory.Delete(path);
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
                  accept:
                    - xbox
                """);
            var config = ServerConfigLoader.LoadOrCreate(path);
            Assert.Equal(25565, config.Server.Port);
            Assert.Equal("Custom", config.Server.Motd);
            Assert.Equal(5, config.Server.MaxPlayers);
            Assert.True(config.Auth.RequireStrictXbox);
            Assert.True(config.Auth.AllowsXbox);
            Assert.False(config.Auth.AllowsSelfSigned);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void LoadOrCreate_rejects_obsolete_require_chain_signatures()
    {
        var path = Path.Combine(Path.GetTempPath(), $"zenith-cfg-{Guid.NewGuid():N}.yml");
        try
        {
            File.WriteAllText(path, """
                server:
                  port: 19132
                  motd: Test
                auth:
                  require-chain-signatures: false
                """);
            var ex = Assert.Throws<InvalidOperationException>(() => ServerConfigLoader.LoadOrCreate(path));
            Assert.Contains("require-chain-signatures was removed", ex.Message);
            Assert.Contains("auth.accept", ex.Message);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Validate_rejects_unknown_auth_accept_mode()
    {
        var config = new ServerConfig();
        config.Auth.Accept = ["xbox", "guest"];
        var ex = Assert.Throws<InvalidOperationException>(() => config.Validate());
        Assert.Contains("unknown mode", ex.Message);
    }

    [Fact]
    public void Validate_rejects_empty_auth_accept()
    {
        var config = new ServerConfig();
        config.Auth.Accept = [];
        var ex = Assert.Throws<InvalidOperationException>(() => config.Validate());
        Assert.Contains("auth.accept", ex.Message);
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
    public void Validate_normalizes_terrain_case()
    {
        var config = new ServerConfig();
        config.World.Terrain = " Noise ";
        config.Validate();
        Assert.Equal("noise", config.World.Terrain);
    }

    [Fact]
    public void Validate_rejects_unsupported_terrain()
    {
        var config = new ServerConfig();
        config.World.Terrain = "caves";
        var ex = Assert.Throws<InvalidOperationException>(() => config.Validate());
        Assert.Contains("flat or noise", ex.Message);
    }

    [Fact]
    public void LoadOrCreate_parses_world_terrain_and_seed()
    {
        var path = Path.Combine(Path.GetTempPath(), $"zenith-test-{Guid.NewGuid():N}.yml");
        try
        {
            File.WriteAllText(path, """
                server:
                  port: 19132
                  motd: test
                  gamemode: Survival
                world:
                  name: world
                  terrain: noise
                  seed: 4242
                auth:
                  accept: [offline]
                """);
            var config = ServerConfigLoader.LoadOrCreate(path);
            Assert.Equal("noise", config.World.Terrain);
            Assert.Equal(4242, config.World.Seed);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Validate_rejects_unsupported_gamemode()
    {
        var config = new ServerConfig();
        config.Server.Gamemode = "Adventure";
        var ex = Assert.Throws<InvalidOperationException>(() => config.Validate());
        Assert.Contains("Survival or Creative", ex.Message);
    }

    [Fact]
    public void Validate_accepts_Survival_and_Creative()
    {
        var survival = new ServerConfig();
        survival.Server.Gamemode = "Survival";
        survival.Validate();

        var creative = new ServerConfig();
        creative.Server.Gamemode = "Creative";
        creative.Validate();
    }

    [Fact]
    public void Empty_world_path_is_in_memory_intent()
    {
        var config = new ServerConfig();
        Assert.True(string.IsNullOrEmpty(config.World.Path));
    }

    [Fact]
    public void ResolveDataRoot_relative_uses_BaseDirectory_not_cwd()
    {
        var expected = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "."));
        Assert.Equal(expected, ZenithServer.ResolveDataRoot("."));

        var nested = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "worlds"));
        Assert.Equal(nested, ZenithServer.ResolveDataRoot("./worlds"));
        Assert.StartsWith(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            ZenithServer.ResolveDataRoot("./worlds"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseLogLevel_maps_severity_ladders()
    {
        Assert.Equal(LogLevel.None, ServerConfig.ParseLogLevel("none", "server"));
        Assert.Equal(
            LogLevel.Info | LogLevel.Warning | LogLevel.Error,
            ServerConfig.ParseLogLevel("info", "server"));
        Assert.Equal(
            LogLevel.Warning | LogLevel.Error,
            ServerConfig.ParseLogLevel("warn", "raknet"));
        Assert.Equal(
            LogLevel.Warning | LogLevel.Error,
            ServerConfig.ParseLogLevel("WARNING", "raknet"));
        Assert.Equal(LogLevel.Error, ServerConfig.ParseLogLevel("error", "server"));
        Assert.Equal(LogLevel.All, ServerConfig.ParseLogLevel("debug", "server"));
        Assert.Equal(LogLevel.All, ServerConfig.ParseLogLevel("all", "raknet"));
    }

    [Fact]
    public void Validate_rejects_unknown_log_level()
    {
        var config = new ServerConfig();
        config.Log.Server = "verbose";
        var ex = Assert.Throws<InvalidOperationException>(() => config.Validate());
        Assert.Contains("log.server", ex.Message);
        Assert.Contains("unknown level", ex.Message);
    }

    [Fact]
    public void ResolveDataRoot_absolute_unchanged()
    {
        var abs = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "zenith-data-root"));
        Assert.Equal(abs, ZenithServer.ResolveDataRoot(abs));
    }

    [Fact]
    public void ResolveConfigPath_uses_base_directory_without_env()
    {
        var previous = Environment.GetEnvironmentVariable(ServerConfigPaths.DataDirEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(ServerConfigPaths.DataDirEnvironmentVariable, null);
            var expected = Path.Combine(AppContext.BaseDirectory, ServerConfigLoader.DefaultFileName);
            Assert.Equal(expected, ServerConfigPaths.ResolveConfigPath());
            Assert.Null(ServerConfigPaths.ResolveDataDirectory());
        }
        finally
        {
            Environment.SetEnvironmentVariable(ServerConfigPaths.DataDirEnvironmentVariable, previous);
        }
    }

    [Fact]
    public void ResolveConfigPath_uses_zenith_data_when_set()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), $"zenith-data-{Guid.NewGuid():N}");
        var previous = Environment.GetEnvironmentVariable(ServerConfigPaths.DataDirEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(ServerConfigPaths.DataDirEnvironmentVariable, dataDir);
            Assert.Equal(Path.GetFullPath(dataDir), ServerConfigPaths.ResolveDataDirectory());
            Assert.Equal(
                Path.Combine(Path.GetFullPath(dataDir), ServerConfigLoader.DefaultFileName),
                ServerConfigPaths.ResolveConfigPath());
        }
        finally
        {
            Environment.SetEnvironmentVariable(ServerConfigPaths.DataDirEnvironmentVariable, previous);
        }
    }

    [Fact]
    public void ResolvePersistentRoot_uses_base_directory_without_env()
    {
        var previous = Environment.GetEnvironmentVariable(ServerConfigPaths.DataDirEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(ServerConfigPaths.DataDirEnvironmentVariable, null);
            Assert.Equal(AppContext.BaseDirectory, ServerConfigPaths.ResolvePersistentRoot());
        }
        finally
        {
            Environment.SetEnvironmentVariable(ServerConfigPaths.DataDirEnvironmentVariable, previous);
        }
    }

    [Fact]
    public void ResolvePersistentRoot_uses_zenith_data_when_set()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), $"zenith-root-{Guid.NewGuid():N}");
        var previous = Environment.GetEnvironmentVariable(ServerConfigPaths.DataDirEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(ServerConfigPaths.DataDirEnvironmentVariable, dataDir);
            Assert.Equal(Path.GetFullPath(dataDir), ServerConfigPaths.ResolvePersistentRoot());
        }
        finally
        {
            Environment.SetEnvironmentVariable(ServerConfigPaths.DataDirEnvironmentVariable, previous);
        }
    }

    [Fact]
    public void ResolveWorldDataRoot_empty_without_zenith_data_is_null()
    {
        var previous = Environment.GetEnvironmentVariable(ServerConfigPaths.DataDirEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(ServerConfigPaths.DataDirEnvironmentVariable, null);
            Assert.Null(ZenithServer.ResolveWorldDataRoot(""));
            Assert.Null(ZenithServer.ResolveWorldDataRoot(null));
            Assert.Null(ZenithServer.ResolveWorldDataRoot("   "));
        }
        finally
        {
            Environment.SetEnvironmentVariable(ServerConfigPaths.DataDirEnvironmentVariable, previous);
        }
    }

    [Fact]
    public void ResolveWorldDataRoot_empty_with_zenith_data_uses_data_dir()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), $"zenith-world-root-{Guid.NewGuid():N}");
        var previous = Environment.GetEnvironmentVariable(ServerConfigPaths.DataDirEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(ServerConfigPaths.DataDirEnvironmentVariable, dataDir);
            Assert.Equal(Path.GetFullPath(dataDir), ZenithServer.ResolveWorldDataRoot(""));
        }
        finally
        {
            Environment.SetEnvironmentVariable(ServerConfigPaths.DataDirEnvironmentVariable, previous);
        }
    }

    [Fact]
    public void ResolveWorldDataRoot_explicit_path_wins_over_zenith_data()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), $"zenith-env-{Guid.NewGuid():N}");
        var absolutePath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"zenith-explicit-{Guid.NewGuid():N}"));
        var previous = Environment.GetEnvironmentVariable(ServerConfigPaths.DataDirEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(ServerConfigPaths.DataDirEnvironmentVariable, dataDir);
            Assert.Equal(absolutePath, ZenithServer.ResolveWorldDataRoot(absolutePath));
        }
        finally
        {
            Environment.SetEnvironmentVariable(ServerConfigPaths.DataDirEnvironmentVariable, previous);
        }
    }

    [Fact]
    public void ApplyFromEnvironment_overrides_server_port_when_set()
    {
        var previous = Environment.GetEnvironmentVariable(ServerConfigOverrides.ServerPortVariable);
        try
        {
            Environment.SetEnvironmentVariable(ServerConfigOverrides.ServerPortVariable, "25565");
            var config = new ServerConfig { Server = { Port = 19132 } };
            ServerConfigOverrides.ApplyFromEnvironment(config);
            Assert.Equal(25565, config.Server.Port);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ServerConfigOverrides.ServerPortVariable, previous);
        }
    }

    [Fact]
    public void ApplyFromEnvironment_leaves_port_when_unset_or_invalid()
    {
        var previous = Environment.GetEnvironmentVariable(ServerConfigOverrides.ServerPortVariable);
        try
        {
            Environment.SetEnvironmentVariable(ServerConfigOverrides.ServerPortVariable, null);
            var config = new ServerConfig { Server = { Port = 19132 } };
            ServerConfigOverrides.ApplyFromEnvironment(config);
            Assert.Equal(19132, config.Server.Port);

            Environment.SetEnvironmentVariable(ServerConfigOverrides.ServerPortVariable, "not-a-port");
            ServerConfigOverrides.ApplyFromEnvironment(config);
            Assert.Equal(19132, config.Server.Port);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ServerConfigOverrides.ServerPortVariable, previous);
        }
    }
}
