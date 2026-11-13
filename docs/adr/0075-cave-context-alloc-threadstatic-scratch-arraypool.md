# ADR §75 — Cave context alloc — ThreadStatic scratch + ArrayPool

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §75`.

**Choice:** Cut the ADR §69 cave-context GC spike without changing carve semantics.

1. **`OverworldCaveContext`** becomes a disposable class (was readonly struct). Column path / point `IsCarved` use `using`.
2. **Collect** into a **ThreadStatic** `List<CaveSegment>` (clear between columns; parallel `GetRadiusAsync` safe).
3. **Segments + CSR indices** via `ArrayPool<T>.Shared` sized to exact count (no `List(4096)` + `ToArray` double buffer).
4. Bucket offsets stay a tiny `new int[37]` per build (negligible vs segment buffer).

**Why:** ShortRun showed `Noise_CaveContextOnly` ~190 KB Allocated with Gen2 — dominated by capacity-4096 list + ToArray. Encode was already cheap; join budget OK; next leaf was alloc honesty.

**Non-goals:** Persisting `c:` on miss; pooling the final LevelChunk payload `byte[]`; changing worm counts/lengths; SIMD.

**Status (jul 2026):** Shipped — pooled cave context + dispose on column/bench paths + leaf test.
