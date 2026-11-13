namespace leveldb.Tests;

internal static class TestWait
{
    /// <summary>Awaits <paramref name="task"/> for at most <paramref name="timeout"/>; false means it did not finish (the caller asserts with its own deadlock message). A faulted task rethrows.</summary>
    public static async Task<bool> CompletesAsync(Task task, TimeSpan timeout)
    {
        var finished = await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false);
        if (finished != task) return false;
        await task.ConfigureAwait(false);
        return true;
    }
}
