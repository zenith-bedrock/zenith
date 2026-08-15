using System.Text;
using System.Threading.Channels;

namespace Zenith.Log;

/// <summary>
/// Serializes and batches log output away from callers. The sink is intentionally a small
/// infrastructure primitive: it does not own gameplay state or introduce a general scheduler.
/// </summary>
internal sealed class AsyncLogSink : IAsyncDisposable
{
    private readonly TextWriter _consoleWriter;
    private readonly Channel<LogEntry> _entries = Channel.CreateUnbounded<LogEntry>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false, AllowSynchronousContinuations = false });
    private readonly Task _worker;
    private TextWriter? _fileWriter;
    private int _completed;

    public AsyncLogSink(TextWriter? consoleWriter = null, TextWriter? fileWriter = null)
    {
        _consoleWriter = consoleWriter ?? Console.Out;
        _fileWriter = fileWriter;
        _worker = DrainAsync();
    }

    public TextWriter? FileWriter
    {
        get => Volatile.Read(ref _fileWriter);
        set => Volatile.Write(ref _fileWriter, value);
    }

    public void Enqueue(string consoleLine, string? fileLine)
    {
        if (Volatile.Read(ref _completed) != 0)
            return;

        // The queue is deliberately unbounded: silently losing an error because a burst filled
        // a bounded queue is worse than allowing the writer to catch up. The single worker and
        // batched writes keep normal operation cheap; shutdown drains the complete queue.
        _entries.Writer.TryWrite(new LogEntry(consoleLine, fileLine));
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _completed, 1) == 0)
            _entries.Writer.TryComplete();

        await _worker.ConfigureAwait(false);
    }

    private async Task DrainAsync()
    {
        try
        {
            await foreach (var first in _entries.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                var console = new StringBuilder(first.ConsoleLine.Length + 1).AppendLine(first.ConsoleLine);
                StringBuilder? file = first.FileLine is null ? null : new StringBuilder(first.FileLine.Length + 1).AppendLine(first.FileLine);

                while (_entries.Reader.TryRead(out var next))
                {
                    console.AppendLine(next.ConsoleLine);
                    if (next.FileLine is not null)
                        (file ??= new StringBuilder(next.FileLine.Length + 1)).AppendLine(next.FileLine);
                }

                _consoleWriter.Write(console.ToString());
                if (file is not null && Volatile.Read(ref _fileWriter) is { } fileWriter)
                    fileWriter.Write(file.ToString());
            }
        }
        finally
        {
            _consoleWriter.Flush();
            Volatile.Read(ref _fileWriter)?.Flush();
        }
    }

    private readonly record struct LogEntry(string ConsoleLine, string? FileLine);
}
