using Zenith.Server;

var configPath = Path.Combine(AppContext.BaseDirectory, ServerConfigLoader.DefaultFileName);
ServerConfig config;
try
{
    config = ServerConfigLoader.LoadOrCreate(configPath);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Fatal: failed to load {configPath}: {ex.Message}");
    if (ex.InnerException is not null)
        Console.Error.WriteLine(ex.InnerException.Message);
    Environment.Exit(1);
    return;
}

var server = new ZenithServer(config);
await server.StartAsync();
