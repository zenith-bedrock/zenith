using Xunit;
using Zenith.NetherNet.Axolotl.Signaling.Http;
using Zenith.NetherNet.Axolotl.Utils;

namespace Zenith.NetherNet.Tests.Axolotl;

/// <summary>Same contract as Zenith.Logging.ILogger: with Debug off an interpolated Debug call formats, allocates and evaluates nothing.</summary>
public class DebugLoggingTests
{
    private sealed class CapturingLogger(bool debugEnabled) : INetherNetLogger
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
    public void Disabled_debug_evaluates_and_sends_nothing()
    {
        var capture = new CapturingLogger(debugEnabled: false);
        INetherNetLogger logger = capture;
        _evaluations = 0;

        logger.Debug($"value {Evaluate()}");

        Assert.Equal(0, _evaluations);
        Assert.Empty(capture.Debugs);
    }

    [Fact]
    public void Enabled_debug_matches_a_plain_interpolated_string()
    {
        var capture = new CapturingLogger(debugEnabled: true);
        INetherNetLogger logger = capture;
        var peer = "10.0.0.1";

        logger.Debug($"peer {peer,-10}| {Evaluate():D3}");

        Assert.Equal([$"peer {peer,-10}| 007"], capture.Debugs);
    }

    [Fact]
    public void The_null_logger_has_debug_off()
    {
        INetherNetLogger logger = NullNetherNetLogger.Instance;
        _evaluations = 0;

        logger.Debug($"value {Evaluate()}");

        Assert.False(logger.IsDebugEnabled);
        Assert.Equal(0, _evaluations);
    }

    [Fact]
    public void A_prefixed_logger_follows_its_inner_logger_and_prefixes_when_on()
    {
        var off = new CapturingLogger(debugEnabled: false);
        var on = new CapturingLogger(debugEnabled: true);
        INetherNetLogger prefixedOff = new PrefixedNetherNetLogger(off, "http");
        INetherNetLogger prefixedOn = new PrefixedNetherNetLogger(on, "http");
        _evaluations = 0;

        prefixedOff.Debug($"value {Evaluate()}");
        prefixedOn.Debug($"value {Evaluate()}");

        Assert.False(prefixedOff.IsDebugEnabled);
        Assert.Empty(off.Debugs);
        Assert.Equal(["[http] value 7"], on.Debugs);
        Assert.Equal(1, _evaluations);
    }

    [Fact]
    public void Disabled_debug_allocates_nothing_per_call()
    {
        INetherNetLogger logger = NullNetherNetLogger.Instance;
        var address = "10.0.0.1";
        logger.Debug($"warm {address} {1}");

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++)
            logger.Debug($"Dropped a signaling connection from {address} {i}");
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }
}
