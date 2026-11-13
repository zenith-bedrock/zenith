using Zenith.Diagnostics;

namespace Zenith.Gameplay.Runtime;

/// <summary>What the loop reports about its own punctuality: how late each tick started, and ticks it had to give up.</summary>
readonly record struct TickPacingMetrics(TimingMetric Lateness, CounterMetric SkippedTicks);
