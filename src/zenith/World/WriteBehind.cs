using Zenith.Logging;

namespace Zenith.World;

/// <summary>
/// Write-behind: the storage call enqueues the write and returns; nobody waits for it. A ValueTask must still be consumed, so a
/// completed one is read and a pending or faulted one is observed here, where a failure is logged instead of vanishing as an
/// unobserved task exception.
/// </summary>
sealed class WriteBehind
{
    private readonly ILogger? _logger;

    public WriteBehind(ILogger? logger) => _logger = logger;

    public void Observe(ValueTask write, string what)
    {
        if (write.IsCompletedSuccessfully)
        {
            write.GetAwaiter().GetResult();
            return;
        }

        _ = ObserveAsync(write, what);
    }

    private async Task ObserveAsync(ValueTask write, string what)
    {
        try
        {
            await write.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger?.Warning($"World write-behind '{what}' failed: {exception.Message}");
        }
    }
}
