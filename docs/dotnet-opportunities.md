# .NET / C# opportunities (2026-11-13)

Survey of platform features Zenith does not use yet and what each would buy. Method: grep of `src/` and `libs/` for each feature, plus the Dockerfile and build props. **Nothing here except item 1 has been implemented or measured**; "expected benefit" is a hypothesis until a benchmark says otherwise (use `--tick-jitter` / `--runtime-load` and BenchmarkDotNet, both already in the repo).

Toolchain today: SDK 10.0.303, `net10.0`, `LangVersion 14`, Nullable on, analyzers `latest`. A clean build (`dotnet build zenith.sln -c Release --no-incremental`) has 0 warnings; incremental builds hide warnings of projects that were not recompiled, so always check with `--no-incremental`.

## Already used well (no action)
`Span`/`stackalloc` (105 files), `ArrayPool`, `BinaryPrimitives`, `Channels`, `readonly record struct` (57), `required`, primary constructors, collection expressions, `GeneratedRegex`, a source generator (`zenith.PacketGenerator`), `FrozenDictionary` in one file, `field` keyword in four places.

## 1. Debug logging that allocated while disabled — DONE
`ILogger.Debug(string)` received an already-built string, so every `Debug($"...")` formatted and allocated even with Debug off (about 110 call sites across Session, Gameplay, RakNet).
Now `ILogger.Debug(ref DebugLogHandler)` (default interface method, `libs/logging/DebugLogHandler.cs`) is picked by the compiler for interpolated strings: with Debug off nothing is formatted or allocated and the interpolated expressions are not evaluated; with Debug on the output is identical. No call site changed. `DebugLoggingTests` pins both behaviours and that 10,000 disabled calls allocate 0 bytes.
`nethernet` is a packable library that depends on no other Zenith package, so `INetherNetLogger` carries its own copy of the mechanism (`IsDebugEnabled`, `Debug(ref NetherNetDebugHandler)`, `NetherNetDebugHandler`); the Zenith adapter forwards `IsDebugEnabled`, and the `Null`/`Prefixed` loggers honour it. Its string-concatenation Debug calls were rewritten as single interpolations so they take the same path. Tests: `src/nethernet.Tests/Axolotl/DebugLoggingTests.cs`.
Scope and limits: only Debug has a handler, because it is the only level meant to be off in production; Info/Warning/Error calls are emitted by default, so building their text is the work, not waste. `IsDebugEnabled` is still needed when the arguments are prepared outside the string (LINQ/joins, e.g. `NetworkSession` outbound).

## 2. First tick and GC settings — open, needs measurement
- The earlier tick-jitter work found max 2-4 ms stalls caused by first-tick JIT. `PublishReadyToRun` in the publish (and a `TieredCompilation`/`TieredPGO` check; PGO is on by default) targets that. The Dockerfile publishes without ReadyToRun and without a `runtimeconfig` template.
- GC pauses are not measured anywhere (no `GC.GetTotalPauseDuration`, `GCSettings`, `GC.GetGCMemoryInfo`). Add them to the diagnostics lib first. For one hot loop thread, `ConcurrentGarbageCollection=false` (no background GC thread) is the likely candidate; do not adopt it without a before/after run.

## 3. `TimeProvider` for time-dependent tests — open
0 uses. Real clocks caused the best-of-5 hack in `TickPacingTests` and the lifecycle race fixed in `5d645d9`. `TimeProvider` plus `FakeTimeProvider` (Microsoft.Extensions.TimeProvider.Testing) in `TickSchedule`, `PreciseWaiter`, session timeouts and rate limiters would make those tests deterministic and fast. Ties into audit item 2 (serial 2m30s suite).

## 4. `System.Threading.Lock` — open
14 `private readonly object _x` lock fields, 0 uses of `Lock`. Mechanical swap; slightly cheaper and the compiler/analyzers reject misuse (locking on the wrong type). Fits the rule that locks live only at network handoff boundaries.

## 5. `FrozenDictionary` / `FrozenSet` for read-only catalogs — open, profile first
Item, block, recipe, mob and spawn catalogs are built at boot and only read. Use only where a profile shows hot lookups; one file uses it today.

## 6. Warnings as errors — DONE (analysis level still default)
`TreatWarningsAsErrors` is on in `Directory.Build.props` (Release and Debug both verified with `--no-incremental`). NuGet audit advisories (`NU1901`-`NU1904`) are excluded via `WarningsNotAsErrors`: they come from an external feed and would otherwise break unrelated builds. Still open from this item: a higher `AnalysisMode` and `EnforceCodeStyleInBuild`, which would first need their existing findings fixed. Original note: A clean build has 0 warnings (11 were fixed on 2026-11-13: xUnit analyzer findings in tests, and a dead `length > MaxLength` check in `nethernet` `Der.Length`), so `TreatWarningsAsErrors` costs nothing now and stops regressions. Also consider `AnalysisMode` above default and `EnforceCodeStyleInBuild`. No third-party analyzers are used beyond the Microsoft ones.

## 7. `System.Diagnostics.Metrics` as a bridge — open, operational value only
`libs/diagnostics` is a custom metrics runtime. Publishing its counters through a `Meter` would let `dotnet-counters` and OpenTelemetry exporters read them. Keep the custom analysis; add the bridge, do not replace it.

## Considered and not recommended
- **Native AOT:** YamlDotNet and reflection use make it costly; ReadyToRun (item 2) gets most of the startup benefit.
- **`System.IO.Pipelines`:** transport is UDP datagrams (RakNet/NetherNet), not a byte stream.
- **SIMD (`Vector128`), `InlineArray`, `SearchValues`, `unsafe`:** only if a profiler points at them. The obvious scalar candidate, `FastNoiseLite`, is not a measured bottleneck.
- **C# 14 extension members and other new syntax:** marginal ergonomics.
- **Running systems in parallel inside a tick:** the largest raw-performance lever, but it contradicts the single-writer rule (ADR §97). An architecture decision, not a missing language feature.
