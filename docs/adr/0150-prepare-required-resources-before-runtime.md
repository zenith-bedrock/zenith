# ADR §150 — Prepare required resources before starting runtime

> Part of the [decision log](README.md).

**Context:** Boot already loads palettes, recipes and creative data, but block loot can load another item palette on its first gameplay lookup. Spawn entries can reference unregistered species and silently never spawn.

**Choice:** The composition root prepares palettes, inventory catalogs and mob definitions before opening world storage. Entity composition then validates spawn references against registered species and freezes registration before starting gameplay or transports. Resource owners implement preparation; boot coordinates direct calls and reports named stage durations and contextual failures. Failed construction releases acquired resources in reverse order.

**Why:** A synthetic tick changes authoritative state and does not cover every first-use path. Explicit composition follows the existing creative-catalog freeze pattern. No external implementation was copied or used as evidence for startup performance.

**Invariants:** Preparation does not advance the clock, spawn actors or send packets. Natural-spawn entries must resolve to registered species with population counters. Registration cannot change after finalization. Startup failures preserve the original error while attempting all cleanup.

**Non-goals:** Plugin API, dependency injection container, automatic discovery, generic startup scheduler, arbitrary world pre-generation or JIT warmup.

**Deferred:** Plugin contribution phases require concrete extension use cases; first-login latency requires measurement against the real server rather than the mixed-roster benchmark.

**Verification:** Resource preparation and spawner finalization tests plus the full server test suite. No retail-client or startup-latency claim.

**Status (November 2026):** Implemented in `Server/Composition/ServerResources.cs`, `ZenithServer` and `MobSpawner`.
