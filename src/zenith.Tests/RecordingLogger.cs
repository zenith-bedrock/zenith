using Zenith.Logging;

namespace Zenith.Tests;

/// <summary>A logger that keeps what was said, so a journey test can show why a client was turned away.</summary>
sealed class RecordingLogger : ILogger
{
    private readonly List<string> _lines = [];

    public bool IsDebugEnabled => true;

    public IReadOnlyList<string> Lines
    {
        get { lock (_lines) return [.. _lines]; }
    }

    public void Debug(string message) => Add("debug", message);
    public void Info(string message) => Add("info", message);
    public void Warning(string message) => Add("warning", message);
    public void Error(string message) => Add("error", message);

    private void Add(string level, string message)
    {
        lock (_lines) _lines.Add($"{level}: {message}");
    }
}
