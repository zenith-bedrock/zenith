# ADR §104a — Diagnostics is a fixed-layout leaf, not a server metrics platform

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §104a`.

**Choice:** `libs/diagnostics` owns fixed counters, gauges, timings and hierarchical timing
names. Metrics are declared during composition and resolved to array indexes before runtime;
recording performs atomic array updates only. `CaptureSnapshot` creates a read-side immutable
view with console and JSON renderers. The server supplies its own concrete layout for tick/system
cost, gameplay counts, runtime/GC health, packet throughput and RakNet transport facts.

**Why:** beta operations need a lasting way to answer whether latency is in the authoritative
tick, a specific system, allocation/GC, packet fan-out, or UDP egress. A leaf with a fixed layout
keeps measurement available without per-incident logging changes and without teaching the library
about `Player`, `World`, `Packets` or RakNet.

**Boundary:** the diagnostics leaf has no Zenith, gameplay, protocol or transport reference. The
GameLoop records its own and registered-system timings; the session boundary records game packet
counts/bytes; the post-tick observer samples ownership-neutral facts. Snapshot/export is a
reader-side operation. There is no HTTP endpoint, registry mutation after startup, reflection,
global lock, Prometheus/OpenTelemetry adapter, dashboard or plugin API.

**Verification:** `diagnostics.Tests` proves metric/scope/snapshot semantics and zero allocations
after warmup; `GameLoopTests` proves whole-tick and per-system timings; the BenchmarkDotNet
`DiagnosticsOverheadBenchmarks` compares the disabled baseline to the recording path with
allocation diagnostics.

**Maturity addendum:** snapshots carry a build-time category; comparisons and diagnosis execute
only after capture. A fixed-size raw incident ring retains periodic and over-budget samples without
allocating on record; command-driven snapshot/compare/top-timing reads remain concrete
development tooling, not a plugin command framework or monitoring endpoint.
