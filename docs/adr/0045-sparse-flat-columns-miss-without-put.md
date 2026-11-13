# ADR §45 — Sparse flat columns (miss without Put)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §45`.

**Choice:** `GetOrCreateColumnAsync` on storage miss returns shared in-memory flat payload **without** `PutAsync` under `c:x:z`. Existing terrain blobs reused. Legacy empty/corrupt (`!LooksLikeTerrainPayload`) still regenerates flat + Put (disk self-heal). Overlays remain `ov:` only.

**Why:** Exploring virgin chunks previously filled the MemTable with identical flat blobs. Flat authority is procedural + overlay — `c:` was already optional.

**Smoke:** Explore far without LevelDB growth proportional to columns visited; overlays still persist across restart.
