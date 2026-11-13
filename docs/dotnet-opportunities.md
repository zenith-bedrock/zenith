# .NET / C# opportunities (2026-11-13)

Survey of platform features Zenith does not use yet and what each would buy. Method: grep of `src/` and `libs/` for each feature, plus the Dockerfile and build props. **Nothing here except item 1 has been implemented or measured**; "expected benefit" is a hypothesis until a benchmark says otherwise (use `--tick-jitter` / `--runtime-load` and BenchmarkDotNet, both already in the repo).

Toolchain today: SDK 10.0.303, `net10.0`, `LangVersion 14`, Nullable on, analyzers `latest`, 0 warnings.

## Already used well (no action)
`Span`/`stackalloc` (105 files), `ArrayPool`, `BinaryPrimitives`, `Channels`, `readonly record struct` (57), `required`, primary constructors, collection expressions, `GeneratedRegex`, a source generator (`zenith.PacketGenerator`), `FrozenDictionary` in one file, `field` keyword in four places.

## 1. Debug logging that allocated while disabled — DONE
`ILogger.Debug(string)` received an already-built string, so every `Debug($"...")` formatted and allocated even with Debug off (about 110 call sites across Session, Gameplay, RakNet).
Now `ILogger.Debug(ref DebugLogHandler)` (default interface method, `libs/logging/DebugLogHandler.cs`) is picked by the compiler for interpolated strings: with Debug off nothing is formatted or allocated and the interpolated expressions are not evaluated; with Debug on the output is identical. No call site changed. `DebugLoggingTests` pins both behaviours and that 10,000 disabled calls allocate 0 bytes.
Limits: `IsDebugEnabled` is still needed when the arguments are prepared outside the string (LINQ/joins, e.g. `NetworkSession` outbound). `nethernet` has its own `INetherNetLogger` (no `IsDebugEnabled`, no reference to `libs/logging`), so its ~30 interpolated Debug calls (connection setup and error paths, not per-tick) still allocate; unifying it onto `ILogger` would fix that.

## 2. First tick and GC settings — open, needs measurement
- The earlier tick-jitter work found max 2-4 ms stalls caused by first-tick JIT. `PublishReadyToRun` in the publish (and a `TieredCompilation`/`TieredPGO` check; PGO is on by default) targets that. The Dockerfile publishes without ReadyToRun and without a `runtimeconfig` template.
- GC pauses are not measured anywhere (no `GC.GetTotalPauseDuration`, `GCSettings`, `GC.GetGCMemoryInfo`). Add them to the diagnostics lib first. For one hot loop thread, `ConcurrentGarbageCollection=false` (no background GC thread) is the likely candidate; do not adopt it without a before/after run.

## 3. `TimeProvider` for time-dependent tests — open
0 uses. Real clocks caused the best-of-5 hack in `TickPacingTests` and the lifecycle race fixed in `5d645d9`. `TimeProvider` plus `FakeTimeProvider` (Microsoft.Extensions.TimeProvider.Testing) in `TickSchedule`, `PreciseWaiter`, session timeouts and rate limiters would make those tests deterministic and fast. Ties into audit item 2 (serial 2m30s suite).

## 4. `System.Threading.Lock` — open
14 `private readonly object _x` lock fields, 0 uses of `Lock`. Mechanical swap; slightly cheaper and the compiler/analyzers reject misuse (locking on the wrong type). Fits the rule that locks live only at network handoff boundaries.

## 5. `FrozenDictionary` / `FrozenSet` for read-only catalogs — open, profile first
Item, block, recipe, mob and spawn catalogs are built at boot and only read. Use only where a profile shows hot lookups; one file uses it today.

## 6. Stricter build — open, cheap
Build is already at 0 warnings, so `TreatWarningsAsErrors` costs nothing now and stops regressions. Also consider `AnalysisMode` above default and `EnforceCodeStyleInBuild`. No third-party analyzers are used beyond the Microsoft ones.

## 7. `System.Diagnostics.Metrics` as a bridge — open, operational value only
`libs/diagnostics` is a custom metrics runtime. Publishing its counters through a `Meter` would let `dotnet-counters` and OpenTelemetry exporters read them. Keep the custom analysis; add the bridge, do not replace it.

## Considered and not recommended
- **Native AOT:** YamlDotNet and reflection use make it costly; ReadyToRun (item 2) gets most of the startup benefit.
- **`System.IO.Pipelines`:** transport is UDP datagrams (RakNet/NetherNet), not a byte stream.
- **SIMD (`Vector128`), `InlineArray`, `SearchValues`, `unsafe`:** only if a profiler points at them. The obvious scalar candidate, `FastNoiseLite`, is not a measured bottleneck.
- **C# 14 extension members and other new syntax:** marginal ergonomics.
- **Running systems in parallel inside a tick:** the largest raw-performance lever, but it contradicts the single-writer rule (ADR §97). An architecture decision, not a missing language feature.
