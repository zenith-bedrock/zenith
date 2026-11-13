# ADR §5 — Overlay persistence instead of column copy-on-write

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §5`.

**Choice:** Permanent sparse `ov:` keys (+ RAM map); send `UpdateBlock` after base `LevelChunk`.

**Why:** Early worlds are flat + edits. Rewriting subchunk palettes on every place is expensive and couples storage to encode. Overlays scale poorly forever (documented unbounded RAM map), but they ship correct multiplayer edit visibility **now** without a storage redesign.
