using System.Threading;

namespace Zenith.Diagnostics;

/// <summary>Concrete cold-path investigation state for a single diagnostics runtime.</summary>
public sealed class DiagnosticsInvestigation
{
    private readonly DiagnosticsRuntime _runtime;
    private readonly DiagnosticsIncidentBuffer _incidents;
    private DiagnosticsSnapshot? _baseline;

    public DiagnosticsInvestigation(DiagnosticsRuntime runtime, DiagnosticsIncidentBuffer incidents)
    {
        _runtime = runtime;
        _incidents = incidents;
    }

    public DiagnosticsSnapshot CaptureBaseline()
    {
        var snapshot = _runtime.CaptureSnapshot();
        Interlocked.Exchange(ref _baseline, snapshot);
        return snapshot;
    }

    public bool TryCompareBaseline(out DiagnosticsComparison comparison)
    {
        var baseline = Interlocked.CompareExchange(ref _baseline, null, null);
        if (baseline is null)
        {
            comparison = null!;
            return false;
        }
        comparison = DiagnosticsComparison.Create(baseline, _runtime.CaptureSnapshot());
        return true;
    }

    public DiagnosticsMetricSnapshot[] CaptureTopTimings(int maximum = 5) => _runtime.CaptureSnapshot().Metrics
        .Where(metric => metric.Kind == DiagnosticMetricKind.Timing)
        .OrderByDescending(metric => metric.Value)
        .Take(maximum)
        .ToArray();

    public DiagnosticsSnapshot[] CaptureRecentIncidents() => _incidents.CaptureRecent();
}
