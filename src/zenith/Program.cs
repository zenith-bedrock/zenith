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

var shutdownTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    shutdownTcs.TrySetResult();
};

var runTask = server.StartAsync();
await Task.WhenAny(runTask, shutdownTcs.Task);
await server.ShutdownAsync();
try
{
    await runTask;
}
catch (OperationCanceledException)
{
    // Expected after CancelKeyPress / ShutdownAsync.
}
