# ADR §21 — Future extension surface form (not a schedule)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §21`.

**Choice:** When (if) external extension opens, the first surface is **`EventBus.Subscribe<T>`** — already typed, exception-isolated, used for login/quit. Do **not** expose `GameLoop.Register` to foreign assemblies or hook `Protocol.Send*`.

**Why:** Keeps decide≠transmit; avoids early plugin loader / DI. `ChunkStreamSystem` / block+inventory systems stay internal composition-root concerns.

**Deferred:** Domain event types beyond login/quit; `Unsubscribe`; priority; real plugin API (still frozen).
