# ADR §32 — Entity coherence — defer WorldEntity

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §32`.

**Choice:** Keep `FloorDropStore` / `ChestStore` without a shared entity concept. **Do not** introduce `WorldEntity` until the named feature **drop-entity wire** (§26 Deferred) is pulled as work. When that feature is pulled: minimal dropped-item entity only (position, runtime id, count, entity id for Add/Remove) — not ECS/mobs.

**Why:** Three RAM workarounds converging is Rule 7 signal, but Creative (§31) and current chests do **not** require entity wire. A generic entity layer “for mobs someday” violates decisions Item 4 / ARCHITECTURE Rule 7.

**Status (jul 2026):** Drop-entity **wire** shipped as §26 adendo — entity id lives on the floor-drop **cell**, not a `WorldEntity` type. Mob AI / persistence / collision remain Deferred.

**Deferred (after minimal dropped-item entity):** mob AI, Mojang BlockActor, entity persistence, collision — each its own ADR.
