# ADR §104 — Runtime health is emitted as bounded server telemetry, not a metrics platform

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §104`.

**Choice:** the server periodically logs one bounded runtime snapshot: tick duration and
over-budget count, connected players, concrete actor count, RakNet sessions and packet bytes,
GC state, and the last graceful persistence-flush duration. RakNet counters are atomic transport
facts; GameLoop only reports elapsed tick duration after the authoritative tick has completed.

**Why:** Phase G needs enough evidence to diagnose beta operation and relate capacity runs to a
50-ms tick budget. This is deliberately cheaper and more honest than inventing an HTTP exporter,
metric registry, event pipeline or new runtime ownership model.

**Boundary:** telemetry neither changes gameplay nor packets, and it does not hold locks across
protocol send, I/O or callbacks. Persistence flush remains shutdown-owned. The reproducible
capacity and recovery limits are recorded in [`phase-g-beta-readiness.md`](../history/phases/phase-g-beta-readiness.md).

**Non-goals:** Prometheus/OpenTelemetry endpoint, dashboard, polling API, generic metric names or
alerting framework. Add an external integration only when an operational consumer requires it.
