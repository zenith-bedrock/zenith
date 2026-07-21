using System.Runtime.InteropServices;
using Zenith.Server;

var configPath = ServerConfigPaths.ResolveConfigPath();
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

var server = new ZenithServer(config, configPath);

var shutdownTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
void RequestShutdown() => shutdownTcs.TrySetResult();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    RequestShutdown();
};

// Docker/Dokploy send SIGTERM on stop/redeploy — CancelKeyPress alone misses that path (§41 flush).
using var sigInt = PosixSignalRegistration.Create(PosixSignal.SIGINT, _ => RequestShutdown());
using var sigTerm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, _ => RequestShutdown());

var runTask = server.StartAsync();
await Task.WhenAny(runTask, shutdownTcs.Task);
await server.ShutdownAsync();
try
{
    await runTask;
}
catch (OperationCanceledException)
{
    // Expected after CancelKeyPress / POSIX signal / ShutdownAsync.
}
