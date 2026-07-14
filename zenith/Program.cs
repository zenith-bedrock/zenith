using Zenith.Server;

ServerConfig config;
try
{
    config = ServerConfigLoader.LoadOrCreate(ServerConfigLoader.DefaultFileName);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Fatal: failed to load {ServerConfigLoader.DefaultFileName}: {ex.Message}");
    if (ex.InnerException is not null)
        Console.Error.WriteLine(ex.InnerException.Message);
    Environment.Exit(1);
    return;
}

var server = new ZenithServer(config);
await server.StartAsync();
