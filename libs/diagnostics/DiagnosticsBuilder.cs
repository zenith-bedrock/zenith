namespace Zenith.Diagnostics;

/// <summary>
/// Defines a fixed metrics layout during composition. Defining metrics after startup is
/// intentionally unsupported: recording only indexes preallocated arrays.
/// </summary>
public sealed class DiagnosticsBuilder
{
    private readonly List<MetricDefinition> _definitions = [];
    private int _timingCount;
    private readonly Dictionary<string, int> _indexes = new(StringComparer.Ordinal);

    public CounterMetric Counter(string name, DiagnosticCategory category, string? parent = null) =>
        new(Add(name, DiagnosticMetricKind.Counter, category, parent));

    public CounterMetric Counter(string name, string? parent = null) => Counter(name, InferCategory(name), parent);

    public GaugeMetric Gauge(string name, DiagnosticCategory category, string? parent = null) =>
        new(Add(name, DiagnosticMetricKind.Gauge, category, parent));

    public GaugeMetric Gauge(string name, string? parent = null) => Gauge(name, InferCategory(name), parent);

    public TimingMetric Timing(string name, DiagnosticCategory category, string? parent = null) =>
        new(Add(name, DiagnosticMetricKind.Timing, category, parent));

    public TimingMetric Timing(string name, string? parent = null) => Timing(name, InferCategory(name), parent);

    public DiagnosticsRuntime Build() => new(_definitions.ToArray(), _timingCount);

    private int Add(string name, DiagnosticMetricKind kind, DiagnosticCategory category, string? parent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (_indexes.ContainsKey(name))
            throw new ArgumentException($"A diagnostic metric named '{name}' already exists.", nameof(name));

        // Validate everything before registering the name: a rejected definition must leave the layout exactly as it was.
        var parentIndex = -1;
        if (parent is not null && !_indexes.TryGetValue(parent, out parentIndex))
            throw new ArgumentException($"Parent metric '{parent}' must be defined first.", nameof(parent));

        _indexes.Add(name, _definitions.Count);

        _definitions.Add(new MetricDefinition(name, kind, category, parentIndex, kind == DiagnosticMetricKind.Timing ? _timingCount++ : -1));
        return _definitions.Count - 1;
    }

    private static string Prefix(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name.Split('.', 2)[0];
    }

    private static DiagnosticCategory InferCategory(string name) => Prefix(name) switch
    {
        "tick" => DiagnosticCategory.Tick,
        "runtime" => DiagnosticCategory.Runtime,
        "gameplay" => DiagnosticCategory.Gameplay,
        "network" => DiagnosticCategory.Network,
        _ => throw new ArgumentException($"Metric '{name}' needs an explicit diagnostic category.", nameof(name))
    };
}

/// <param name="HistogramSlot">The timing's row in the runtime's histogram table; -1 for counters and gauges.</param>
internal readonly record struct MetricDefinition(string Name, DiagnosticMetricKind Kind, DiagnosticCategory Category, int ParentIndex, int HistogramSlot);
