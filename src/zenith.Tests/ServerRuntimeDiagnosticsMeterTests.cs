using System.Diagnostics.Metrics;
using Xunit;
using Zenith.Diagnostics;
using Zenith.Server;

namespace Zenith.Tests;

public class ServerRuntimeDiagnosticsMeterTests
{
    [Fact]
    public void The_server_layout_is_published_to_Metrics_collectors_with_units_and_the_gc_pause_gauge()
    {
        using var diagnostics = new ServerRuntimeDiagnostics();
        var units = new Dictionary<string, string?>();
        var names = new HashSet<string>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                // Several servers may exist at once in a parallel test run; only the names and units are asserted.
                if (instrument.Meter.Name != DiagnosticsMeter.DefaultName) return;
                lock (names)
                {
                    names.Add(instrument.Name);
                    units[instrument.Name] = instrument.Unit;
                }
            }
        };
        listener.Start();

        lock (names)
        {
            Assert.Contains("tick.over-budget", names);
            Assert.Contains("runtime.gc.pause-ms", names);
            Assert.Equal("ms", units["runtime.gc.pause-ms"]);
            Assert.Equal("By", units["runtime.managed-bytes"]);
            Assert.Equal("{milli-tps}", units["tick.tps"]);
            Assert.Contains("tick.system.chunk-stream.max", names); // a timing becomes count/total/max/last
        }
    }

    [Fact]
    public void Runtime_health_records_the_total_gc_pause()
    {
        using var diagnostics = new ServerRuntimeDiagnostics();
        GC.Collect(); // make sure at least one pause has happened in this process

        diagnostics.RecordRuntimeHealth(TimeSpan.FromMilliseconds(1), 20d, 0, 0, 0, 0, default);

        var pause = Assert.Single(diagnostics.Runtime.CaptureSnapshot().Metrics, m => m.Name == "runtime.gc.pause-ms");
        Assert.True(pause.Value >= 0);
        Assert.Equal(DiagnosticMetricKind.Gauge, pause.Kind);
    }
}
