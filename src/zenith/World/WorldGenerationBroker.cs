using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Zenith.World;

/// <summary>
/// Per-world bounded broker for immutable base-column work. This is deliberately a worldgen
/// primitive, not a general scheduler: it owns request deduplication, queue backpressure and
/// worker lifetime, while <see cref="World"/> remains the domain owner.
/// </summary>
sealed class WorldGenerationBroker : IAsyncDisposable
{
    private const int QueueCapacity = 4096;

    private readonly Channel<Request> _queue;
    private readonly ConcurrentDictionary<ChunkCoord, TaskCompletionSource<ColumnReadResult>> _inFlight = new();
    private readonly Func<ChunkCoord, ValueTask<ColumnReadResult>> _generate;
    private readonly WorldGenerationDiagnostics? _diagnostics;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task[] _workers;
    private int _queued;
    private int _disposed;

    public WorldGenerationBroker(
        int workerCount,
        Func<ChunkCoord, ValueTask<ColumnReadResult>> generate,
        WorldGenerationDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(generate);
        if (workerCount is < 1 or > 64)
            throw new ArgumentOutOfRangeException(nameof(workerCount), workerCount, "Generation workers must be 1..64.");

        _generate = generate;
        _diagnostics = diagnostics;
        _queue = Channel.CreateBounded<Request>(new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        _workers = new Task[workerCount];
        for (var i = 0; i < _workers.Length; i++)
            _workers[i] = Task.Run(RunWorkerAsync);
    }

    public async ValueTask<(Task<ColumnReadResult> Task, bool Coalesced)> RequestAsync(ChunkCoord coordinate)
    {
        while (true)
        {
            if (_inFlight.TryGetValue(coordinate, out var existing))
                return (existing.Task, true);

            var completion = new TaskCompletionSource<ColumnReadResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_inFlight.TryAdd(coordinate, completion))
                continue;

            try
            {
                var wait = _diagnostics is null ? default : _diagnostics.BeginQueueWait();
                try
                {
                    var request = new Request(coordinate, completion);
                    var queued = Interlocked.Increment(ref _queued);
                    _diagnostics?.RecordQueueDepth(queued);
                    try
                    {
                        if (!_queue.Writer.TryWrite(request))
                        {
                            _diagnostics?.RecordQueueBackpressure();
                            await _queue.Writer.WriteAsync(request).ConfigureAwait(false);
                        }
                    }
                    catch
                    {
                        queued = Interlocked.Decrement(ref _queued);
                        _diagnostics?.RecordQueueDepth(queued);
                        throw;
                    }
                }
                finally
                {
                    wait.Dispose();
                }
                return (completion.Task, false);
            }
            catch (Exception exception)
            {
                Remove(coordinate, completion);
                completion.TrySetException(exception);
                throw;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _queue.Writer.TryComplete();
        _shutdown.Cancel();
        try
        {
            await Task.WhenAll(_workers).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutdown cancellation is expected; individual requests are settled below.
        }

        foreach (var pair in _inFlight)
        {
            if (Remove(pair.Key, pair.Value))
                pair.Value.TrySetCanceled();
        }

        Interlocked.Exchange(ref _queued, 0);
        _diagnostics?.RecordQueueDepth(0);

        _shutdown.Dispose();
    }

    private async Task RunWorkerAsync()
    {
        try
        {
            await foreach (var request in _queue.Reader.ReadAllAsync(_shutdown.Token).ConfigureAwait(false))
            {
                var queued = Interlocked.Decrement(ref _queued);
                _diagnostics?.RecordQueueDepth(queued);
                try
                {
                    var result = await _generate(request.Coordinate).ConfigureAwait(false);
                    request.Completion.TrySetResult(result);
                }
                catch (Exception exception)
                {
                    request.Completion.TrySetException(exception);
                }
                finally
                {
                    Remove(request.Coordinate, request.Completion);
                }
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            // The owner is shutting down; queued requests are settled by DisposeAsync.
        }
    }

    private bool Remove(ChunkCoord coordinate, TaskCompletionSource<ColumnReadResult> completion) =>
        ((ICollection<KeyValuePair<ChunkCoord, TaskCompletionSource<ColumnReadResult>>>)_inFlight)
            .Remove(new KeyValuePair<ChunkCoord, TaskCompletionSource<ColumnReadResult>>(coordinate, completion));

    private readonly record struct Request(
        ChunkCoord Coordinate,
        TaskCompletionSource<ColumnReadResult> Completion);
}
