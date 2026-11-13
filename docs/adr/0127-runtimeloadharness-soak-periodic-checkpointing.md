# ADR §127 — `RuntimeLoadHarness --soak` — periodic checkpointing for growth-over-time soak runs

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §127`.

**Context:** Same external-review pass as ADR §124-126. This claim turned out to be half-right:
"falta um benchmark 100 players" was false — `RuntimeLoadHarness`'s default `LoadOptions.Parse`
already runs `[10, 100, 500]` players and `[100, 1_000]` actors through the real `GameLoop` end to
end. What's genuinely missing is a **soak** shape: every existing scenario (`RunSteady`, actor churn,
etc.) computes one aggregate before/after delta over the whole run, not a time series. A leak or
tick-time drift shows up as a *growth curve* ("hour 0 → hour 1 → hour 2 ... RSS climbing"), which a
single start/end delta can't distinguish from one big one-time cost.

**Choice:** Added `RunSoak` + `--soak`/`--soak-players`/`--soak-ticks`/`--soak-checkpoint-ticks` to
`RuntimeLoadHarness`, reusing `RunSteady`'s exact traffic shape (`SubmitSteadyInputs`/`Tick`/
`ValidateSteadyState`) but checkpointing periodically instead of once: RSS (`Environment.WorkingSet`),
managed heap (`GC.GetTotalMemory`), GC counts *since the last checkpoint* (not cumulative — isolates
which window a collection spike happened in), and tick p99 for that window. Defaults: 50 players, 1
simulated hour (72,000 ticks), checkpoint every 5 simulated minutes — override for the 6h/24h runs
the original review asked for.

**Scope note, stated plainly:** this ships the *checkpointing infrastructure*, proven correct with a
short smoke run (20 players, 400 ticks, 4 checkpoints — all reported cleanly, no anomalies, exactly
what a healthy short run should look like). It does **not** ship random join/leave/chunk-streaming/
inventory churn (`RunSteady`'s existing movement-only traffic shape is reused as-is), and nobody has
run this for the actual 6/24 hours yet — that's correctly a CI/ops decision (dedicated machine time),
not something to launch from a chat session. This ADR entry exists so that decision has concrete
infrastructure to point at instead of "we should soak-test this sometime."

**Status (ago 2026):** Shipped — `RuntimeLoadHarness.cs`. Invocation:
`dotnet run -c Release --project src/zenith.Benchmarks -- --runtime-load --soak --soak-players 50
--soak-ticks 1440000 --soak-checkpoint-ticks 72000` (24h, checkpointed hourly). No new automated test
— this is an operational/manual-invocation tool like the rest of `RuntimeLoadHarness`, not a unit-level
correctness surface; verified via the short smoke run above instead.
