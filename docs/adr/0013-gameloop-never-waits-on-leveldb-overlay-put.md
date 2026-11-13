# ADR §13 — GameLoop never waits on LevelDB overlay Put

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §13`.

**Choice:** `World.SetBlock` updates the in-RAM overlay map synchronously, then enqueues persistence (`LevelDbChunkStorage` overlay write queue). No `GetResult` / `.Wait()` on the tick thread.

**Why:** Overlay keys are idempotent (`ov:x:y:z`). Blocking 20 TPS on disk + the shared LevelDB `_gate` stalls every player for one builder's edits — opposite of the GameLoop “light and deterministic” rule.

**Satellites:** column `Get`/`Put` still share `_gate`. Crash mid-queue may lose unshed Puts until `Dispose` drains — best-effort flush on stop. PreSpawn no longer uses `GetResult` (see §14).
