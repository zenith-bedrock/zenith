# ADR §97 — Choose execution model by behavior, not by available abstraction

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §97`.

**Choice:** Choose the mechanism for a feature from its behavior — `IGameSystem`/tick, immediate runtime call, pending intent consumed on tick, domain event, or scheduled work — rather than from the abstraction already available. The motivating runtime review questioned whether `ChatSystem`/`GameModeSystem`/`EquipmentSystem` belong in `IGameSystem`; the important conclusion was to avoid replacing "everything is a System" with the equally rigid "everything discrete becomes a Command/Event".

1. **Strict invariants (never trade without a new ADR + concrete forcing feature):** transport doesn't decide gameplay; serialization doesn't know domain; packets are wire-only; handlers interpret and call a runtime API, they don't become the decision; authoritative world/player mutation normally has one writer in its gameplay execution context (the tick where ordering matters; exceptions must be explicit and justified); thread ownership is explicit; async work returns a result to the owning context instead of mutating authoritative state itself.
2. **Flexible mechanisms (choose per feature, revisit freely):** `IGameSystem`, Command, Event, Intent/pending-state, Queue, Dirty flag, Scheduler, direct runtime call, Dispatcher, Service. None of these should become a universal rule.
3. **Existing proof the heuristic already works in Zenith:** `Player.SelectedHotbarSlot` (ADR §80) is written directly by the handler with no lock, no queue — because it's a single idempotent, self-correcting scalar. That is the template for "immediate runtime operation," not an exception to feel uneasy about.
4. The full decision heuristic and invariant/mechanism split are in [`docs/adr/0097-runtime-execution-model.md`](0097-runtime-execution-model.md).

**Non-goals:** this ADR does not mandate introducing Command/Event/Scheduler types now — no current feature has ≥2 real consumers or a genuine future-scheduling need beyond what `GravityPendingStore` already covers. It also does not reverse any `IGameSystem` registration: `Chat`/`GameMode`/`Equipment` remain Systems because their ordering guarantees remain useful; only `EquipmentSystem`'s registration order changes.

**Verification:** N/A — this is a decision/heuristic ADR, not a shipped code change. Enforcement is documentation-level: `ARCHITECTURE.md`/`AGENTS.md` updated with the invariant/mechanism split and a pre-implementation checklist (this session).

**Status (ago 2026):** Shipped. `ArchitectureBoundaryTests` protects the mechanically verifiable layer boundaries; the feature-level checklist remains a review responsibility.
