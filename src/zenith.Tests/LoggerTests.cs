using System.Text;
using Zenith.Log;
using Zenith.Raknet.Enumerator;
using Xunit;

namespace Zenith.Tests;

public sealed class LoggerTests
{
    [Fact]
    public async Task Log_drains_ordered_console_and_plain_file_lines()
    {
        var console = new StringWriter(new StringBuilder());
        var file = new StringWriter(new StringBuilder());
        await using var sink = new AsyncLogSink(console, file);
        var logger = new Logger(sink) { LogLevel = LogLevel.All };

        logger.Info("<b>first</b>");
        logger.Warning("<color=\"red\">second</color>");
        await sink.DisposeAsync();

        Assert.Contains("first", console.ToString());
        Assert.Contains("second", console.ToString());
        Assert.True(console.ToString().IndexOf("first", StringComparison.Ordinal) <
                    console.ToString().IndexOf("second", StringComparison.Ordinal));
        Assert.Contains("INFO", file.ToString());
        Assert.Contains("first", file.ToString());
        Assert.DoesNotContain("<b>", file.ToString());
        Assert.DoesNotContain("<color", file.ToString());
    }

    [Fact]
    public async Task Log_disabled_level_does_not_enqueue_output()
    {
        var console = new StringWriter();
        await using var sink = new AsyncLogSink(console);
        var logger = new Logger(sink) { LogLevel = LogLevel.Error };

        logger.Info("ignored");
        await sink.DisposeAsync();

        Assert.Equal(string.Empty, console.ToString());
    }
}
