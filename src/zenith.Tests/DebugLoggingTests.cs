using Zenith.Logging;
using Xunit;

namespace Zenith.Tests;

/// <summary>
/// <c>logger.Debug($"...")</c> goes through <see cref="DebugLogHandler"/>: with Debug off nothing is formatted, allocated or evaluated;
/// with Debug on the message is identical to a plain interpolated string.
/// </summary>
public class DebugLoggingTests
{
    private sealed class CapturingLogger(bool debugEnabled) : ILogger
    {
        public List<string> Debugs { get; } = [];
        public bool IsDebugEnabled => debugEnabled;
        public void Debug(string message) => Debugs.Add(message);
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }

    private static int _evaluations;

    private static int Evaluate()
    {
        _evaluations++;
        return 7;
    }

    [Fact]
    public void Disabled_debug_neither_evaluates_nor_sends_the_interpolated_message()
    {
        ILogger logger = new CapturingLogger(debugEnabled: false);
        _evaluations = 0;

        logger.Debug($"value {Evaluate()}");

        Assert.Equal(0, _evaluations);
        Assert.Empty(((CapturingLogger)logger).Debugs);
    }

    [Fact]
    public void Enabled_debug_formats_like_a_plain_interpolated_string()
    {
        ILogger logger = new CapturingLogger(debugEnabled: true);
        _evaluations = 0;
        var name = "steve";
        var hp = 12.5f;

        logger.Debug($"hit {name,-6}| {hp:F2} {Evaluate()} {(ReadOnlySpan<char>)"span"} {null as string}end");

        Assert.Equal(1, _evaluations);
        Assert.Equal([$"hit {name,-6}| {hp:F2} 7 span end"], ((CapturingLogger)logger).Debugs);
    }

    [Fact]
    public void Plain_string_overload_is_unchanged()
    {
        ILogger logger = new CapturingLogger(debugEnabled: true);

        logger.Debug("plain");

        Assert.Equal(["plain"], ((CapturingLogger)logger).Debugs);
    }

    [Fact]
    public void Disabled_debug_allocates_nothing_per_call()
    {
        ILogger logger = new CapturingLogger(debugEnabled: false);
        var name = "steve";
        var count = 12345;
        logger.Debug($"warm {name} {count}");

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++)
            logger.Debug($"Dropped place from {name}: queue {count} {i}");
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }
}
