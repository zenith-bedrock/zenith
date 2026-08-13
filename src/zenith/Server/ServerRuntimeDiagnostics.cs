using Zenith.Diagnostics;
using Zenith.Raknet;

namespace Zenith.Server;

/// <summary>
/// Zenith's fixed diagnostics layout. This adapter owns names and sampled server facts while the
/// leaf library remains domain-free; no gameplay state is retained or mutated here.
/// </summary>
sealed class ServerRuntimeDiagnostics
{
    private readonly CounterMetric _overBudgetTicks;
    private readonly CounterMetric _packetsSent;
    private readonly CounterMetric _packetsReceived;
    private readonly CounterMetric _packetBytesSent;
    private readonly CounterMetric _packetBytesReceived;
    private readonly GaugeMetric _tps;
    private readonly GaugeMetric _players;
    private readonly GaugeMetric _actors;
    private readonly GaugeMetric _chunks;
    private readonly GaugeMetric _systems;
    private readonly GaugeMetric _allocations;
    private readonly GaugeMetric _managedBytes;
    private readonly GaugeMetric _gen0;
    private readonly GaugeMetric _gen1;
    private readonly GaugeMetric _gen2;
    private readonly GaugeMetric _datagramsSent;
    private readonly GaugeMetric _datagramsReceived;
    private readonly GaugeMetric _bandwidthOut;
    private readonly GaugeMetric _bandwidthIn;
    private long _lastSentBytes;
    private long _lastReceivedBytes;
    private int _sampleTicks;

    public DiagnosticsRuntime Runtime { get; }
    public TimingMetric Tick { get; }
    public IReadOnlyDictionary<string, TimingMetric> Systems { get; }
    public DiagnosticsIncidentBuffer Incidents { get; }

    public ServerRuntimeDiagnostics()
    {
        var builder = new DiagnosticsBuilder();
        Tick = builder.Timing("tick");
        Systems = new Dictionary<string, TimingMetric>(StringComparer.Ordinal)
        {
            ["time-sync"] = builder.Timing("tick.system.time-sync", "tick"),
            ["movement"] = builder.Timing("tick.system.movement", "tick"),
            ["player-melee"] = builder.Timing("tick.system.player-melee", "tick"),
            ["chat"] = builder.Timing("tick.system.chat", "tick"),
            ["game-mode"] = builder.Timing("tick.system.game-mode", "tick"),
            ["zombie"] = builder.Timing("tick.system.zombie", "tick"),
            ["projectile"] = builder.Timing("tick.system.projectile", "tick"),
            ["skeleton"] = builder.Timing("tick.system.skeleton", "tick"),
            ["cow"] = builder.Timing("tick.system.cow", "tick"),
            ["creeper"] = builder.Timing("tick.system.creeper", "tick"),
            ["enderman"] = builder.Timing("tick.system.enderman", "tick"),
            ["bat"] = builder.Timing("tick.system.bat", "tick"),
            ["spider"] = builder.Timing("tick.system.spider", "tick"),
            ["villager"] = builder.Timing("tick.system.villager", "tick"),
            ["golem"] = builder.Timing("tick.system.golem", "tick"),
            ["minecart"] = builder.Timing("tick.system.minecart", "tick"),
            ["fish"] = builder.Timing("tick.system.fish", "tick"),
            ["block-dig"] = builder.Timing("tick.system.block-dig", "tick"),
            ["block-edit"] = builder.Timing("tick.system.block-edit", "tick"),
            ["gravity"] = builder.Timing("tick.system.gravity", "tick"),
            ["floor-drop"] = builder.Timing("tick.system.floor-drop", "tick"),
            ["inventory"] = builder.Timing("tick.system.inventory", "tick"),
            ["hunger"] = builder.Timing("tick.system.hunger", "tick"),
            ["effect"] = builder.Timing("tick.system.effect", "tick"),
            ["equipment"] = builder.Timing("tick.system.equipment", "tick"),
            ["chunk-stream"] = builder.Timing("tick.system.chunk-stream", "tick")
        };
        _overBudgetTicks = builder.Counter("tick.over-budget");
        _tps = builder.Gauge("tick.tps");
        _players = builder.Gauge("gameplay.players");
        _actors = builder.Gauge("gameplay.actors");
        _chunks = builder.Gauge("gameplay.chunks.overlays");
        _systems = builder.Gauge("gameplay.systems");
        _allocations = builder.Gauge("runtime.allocations.thread");
        _managedBytes = builder.Gauge("runtime.managed-bytes");
        _gen0 = builder.Gauge("runtime.gc.gen0");
        _gen1 = builder.Gauge("runtime.gc.gen1");
        _gen2 = builder.Gauge("runtime.gc.gen2");
        _packetsSent = builder.Counter("network.packets.sent");
        _packetsReceived = builder.Counter("network.packets.received");
        _packetBytesSent = builder.Counter("network.packet-bytes.sent");
        _packetBytesReceived = builder.Counter("network.packet-bytes.received");
        _datagramsSent = builder.Gauge("network.datagrams.sent");
        _datagramsReceived = builder.Gauge("network.datagrams.received");
        _bandwidthOut = builder.Gauge("network.bandwidth.out-bytes-per-second");
        _bandwidthIn = builder.Gauge("network.bandwidth.in-bytes-per-second");
        Runtime = builder.Build();
        Incidents = new DiagnosticsIncidentBuffer(Runtime);
    }

    public TimingMetric System(string name) => Systems[name];

    public void RecordPacketSent(int count, int bytes)
    {
        Runtime.Increment(_packetsSent, count);
        Runtime.Increment(_packetBytesSent, bytes);
    }

    public void RecordPacketReceived(int bytes)
    {
        Runtime.Increment(_packetsReceived);
        Runtime.Increment(_packetBytesReceived, bytes);
    }

    public void RecordRuntimeHealth(
        TimeSpan elapsed, double tps, int players, int actors, int chunkOverlays, int systems, RakNetServer raknet)
    {
        if (elapsed.TotalMilliseconds > 50d) Runtime.Increment(_overBudgetTicks);
        Runtime.Set(_tps, (long)Math.Round(tps * 1_000d));
        Runtime.Set(_players, players);
        Runtime.Set(_actors, actors);
        Runtime.Set(_chunks, chunkOverlays);
        Runtime.Set(_systems, systems);
        Runtime.Set(_allocations, GC.GetAllocatedBytesForCurrentThread());
        Runtime.Set(_managedBytes, GC.GetTotalMemory(forceFullCollection: false));
        Runtime.Set(_gen0, GC.CollectionCount(0));
        Runtime.Set(_gen1, GC.CollectionCount(1));
        Runtime.Set(_gen2, GC.CollectionCount(2));
        Runtime.Set(_datagramsSent, raknet.SentDatagrams);
        Runtime.Set(_datagramsReceived, raknet.ReceivedDatagrams);

        var seconds = Math.Max(elapsed.TotalSeconds, double.Epsilon);
        var sentBytes = raknet.SentBytes;
        var receivedBytes = raknet.ReceivedBytes;
        Runtime.Set(_bandwidthOut, (long)((sentBytes - _lastSentBytes) / seconds));
        Runtime.Set(_bandwidthIn, (long)((receivedBytes - _lastReceivedBytes) / seconds));
        _lastSentBytes = sentBytes;
        _lastReceivedBytes = receivedBytes;

        var slowTick = elapsed.TotalMilliseconds > 50d;
        if (slowTick || ++_sampleTicks % 20 == 0)
            Incidents.Record(DateTimeOffset.UtcNow);
    }
}
