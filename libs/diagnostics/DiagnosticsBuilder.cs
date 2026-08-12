namespace Zenith.Diagnostics;

/// <summary>
/// Defines a fixed metrics layout during composition. Defining metrics after startup is
/// intentionally unsupported: recording only indexes preallocated arrays.
/// </summary>
public sealed class DiagnosticsBuilder
{
    private readonly List<MetricDefinition> _definitions = [];
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

    public DiagnosticsRuntime Build() => new(_definitions.ToArray());

    private int Add(string name, DiagnosticMetricKind kind, DiagnosticCategory category, string? parent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!_indexes.TryAdd(name, _definitions.Count))
            throw new ArgumentException($"A diagnostic metric named '{name}' already exists.", nameof(name));

        var parentIndex = -1;
        if (parent is not null && !_indexes.TryGetValue(parent, out parentIndex))
            throw new ArgumentException($"Parent metric '{parent}' must be defined first.", nameof(parent));

        _definitions.Add(new MetricDefinition(name, kind, category, parentIndex));
        return _definitions.Count - 1;
    }

    private static DiagnosticCategory InferCategory(string name) => name.Split('.', 2)[0] switch
    {
        "tick" => DiagnosticCategory.Tick,
        "runtime" => DiagnosticCategory.Runtime,
        "gameplay" => DiagnosticCategory.Gameplay,
        "network" => DiagnosticCategory.Network,
        _ => throw new ArgumentException($"Metric '{name}' needs an explicit diagnostic category.", nameof(name))
    };
}

internal readonly record struct MetricDefinition(string Name, DiagnosticMetricKind Kind, DiagnosticCategory Category, int ParentIndex);
