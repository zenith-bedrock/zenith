# ADR §114 — Resident chunk state: a hydrate/evict design, not yet implemented

**Status: design only.** This ADR documents a problem and a recommended direction. It does not
ship code, and no file outside `docs/` changes as part of it. It exists so a future session that
picks this up doesn't have to re-derive the analysis from scratch.

## Context

`World` (`src/zenith/World/World.cs`) holds three kinds of mutable, non-regenerable player-authored
state entirely in RAM for the lifetime of the process:

- `_blockOverrides` / `_overlaysByChunk` — block edits (place/dig), keyed `(X, Y, Z)` and mirrored
  into a per-chunk secondary index (`GetOverlaysInColumn`, lines 30-34, 452).
- `ChestStore` (`Chests`) — chest contents.
- `FloorDropStore` (`FloorDrops`) — dropped items.

All three are loaded **entirely** at boot, in the `World` constructor (lines 79-86):

```csharp
storage.ForEachOverlayAsync(StoreOverlay).AsTask().GetAwaiter().GetResult();
storage.ForEachChestAsync((x, y, z, blob) => Chests.TryLoadFromBlob(x, y, z, blob)).AsTask().GetAwaiter().GetResult();
```

`IChunkStorage` (`src/zenith/World/IChunkStorage.cs`) only exposes bulk enumeration
(`ForEachOverlayAsync`, `ForEachChestAsync`) — there is no `LoadOverlaysForChunkAsync(chunkX, chunkZ)`
or equivalent. Nothing evicts any of this state once loaded; ADR §36's `OverrideSoftCap` (10,000)
only refuses *new* overlay keys once the cap is hit — it does not shrink what is already resident.
Overwrites of existing keys remain unlimited. RAM use is therefore monotonic in total edits ever
made across the world's lifetime, not bounded by what's currently relevant (near an online player).

**This is a real gap, but not an urgent one today** — alpha-scale worlds and uptimes haven't made it
operationally visible. It is recorded now because the audit that surfaced it also surfaced a
tempting-but-wrong shortcut (see "Rejected shortcut" below), and because the shape of the fix
constrains other future work (e.g. multi-world/dimension support would multiply this by world count).

### An existing precedent that looks applicable but isn't, directly

`World` already evicts something: `_baseColumnCache` (lines 27-29, 347-357) is a FIFO cache bounded
by `_generationCacheColumns` (default 1024) — when full, the oldest entry is evicted on insert. This
works because **base terrain is regenerable**: evicting a cached column loses nothing, since
`GenerateColumnCoreAsync` can reproduce it deterministically from the seed/generator on the next
request. Overlays, chests, and floor drops are the opposite — they are the *only* copy of a player's
edit until (or unless) it's persisted, and today nothing distinguishes "persisted, safe to drop from
RAM" from "resident, must stay." Reusing `_baseColumnCache`'s eviction shape verbatim for overlays
would silently drop unpersisted edits under memory pressure. This ADR exists specifically to avoid
that mistake.

### Rejected shortcut: "evict on last viewer" without hydrate

Dragonfly's `world.Column` (see prior research in this project's planning history) evicts a chunk
from RAM when its last viewer leaves, via `closeUnusedChunks`. Copying that shape directly — evict
`_blockOverrides`/`ChestStore`/`FloorDropStore` entries for a chunk when no player is nearby — was
considered and **rejected for Zenith as-is**, because dragonfly's eviction is paired with a
per-chunk *load* on chunk activation. Zenith has no such load path: everything is loaded once, at
boot, in bulk. Evicting without first building the missing half (hydrate-per-chunk) would mean a
chunk's overlays silently vanish the first time a player leaves its vicinity and never come back
until server restart — a data-loss bug disguised as a memory optimization.

## Decision (proposed direction, not yet built)

A four-stage per-chunk lifecycle, gated on chunk residency rather than a fixed RAM budget:

```text
chunk becomes active (a player is near it)
    -> hydrate: load that chunk's overlays/chests/floor-drops from IChunkStorage
    -> resident (served from RAM, same as today)
    -> last viewer leaves
    -> flush: persist anything not yet persisted
    -> evict: remove from all three side-tables in one step (not three separate removals —
       §36's own doc comment already flags "no drift" between _blockOverrides and
       _overlaysByChunk as an invariant; evict must preserve that same atomicity across all
       three stores, not just the two that already coordinate)
```

Hydrate must ship **before** evict is enabled for any store. Building only hydrate (with no evict
yet) is a safe, independently valuable intermediate slice: it doesn't reduce RAM by itself, but it
retires the "everything loads at boot" assumption and is a prerequisite either way.

### Open question this ADR does not resolve: how residency is tracked

Two candidate approaches, not yet chosen between:

1. **Viewer-count aggregation on `World`** (dragonfly's shape). Requires a new per-chunk counter in
   `World`, incremented/decremented as players' view radii cross chunk boundaries. `ChunkStreamSystem`
   already tracks per-player visibility (`player.Chunks`, a `PlayerChunkTracker`, with
   `TryBegin`/`ForgetOutsideRadius`) but that is *client-visibility* bookkeeping, not a *server-side*
   aggregate — today nothing sums "how many players currently have chunk (cx,cz) in view" into a
   single counter `World` can read. This would need to be added.
2. **Periodic tick-count GC sweep** (pocketmine's shape: `providerGarbageCollectionTicker`, fires
   every ~6000 ticks). Simpler to build (no new per-chunk counter, no coordination with
   `ChunkStreamSystem`), but less precise — a chunk with no current viewer could stay resident for
   up to one sweep interval, and the sweep itself needs *some* notion of "is anyone near this chunk
   right now" to decide what to evict, which circles back to needing viewer information anyway,
   just sampled periodically instead of maintained continuously.

Recommendation for whoever picks this up: start with option 2 (periodic sweep) for the first
implementation — it doesn't require adding new state to `ChunkStreamSystem`/`PlayerChunkTracker`,
and a coarse-grained sweep is enough to bound RAM even if it isn't the tightest possible bound.
Option 1 can be layered in later as a precision improvement if the sweep interval proves too coarse
in practice — but that should be a measured decision (an actual RAM-growth number from a real long
uptime run), not a default assumption baked in up front.

## API gap that must close before any of this ships

`IChunkStorage` needs a per-chunk read path. Proposed additions (naming to be finalized when this is
actually implemented):

```csharp
ValueTask<IReadOnlyList<BlockOverride>> LoadOverlaysForChunkAsync(int chunkX, int chunkZ, CancellationToken ct = default);
ValueTask<IReadOnlyList<(int X, int Y, int Z, byte[] Blob)>> LoadChestsForChunkAsync(int chunkX, int chunkZ, CancellationToken ct = default);
```

Both implementations of `IChunkStorage` (the in-memory test double in `IChunkStorage.cs` and
`LevelDbChunkStorage.cs`) need these. The LevelDB implementation in particular needs a real per-chunk
key-range scan rather than the full-table iteration `ForEachOverlayAsync` currently does — this is
itself nontrivial and should be scoped as its own step when implementation starts, not assumed free.

## Non-goals (this ADR)

- No code changes ship with this ADR. It is a design record only.
- Not a change to `OverrideSoftCap` (§36) — that remains as-is regardless of which residency
  approach is chosen later.
- Not a change to `_baseColumnCache` — it already works correctly for the reason explained above
  (regenerable data), and is out of scope here.
- Not a decision between the two residency-tracking approaches above — left open, with a
  recommendation, for whoever implements this to confirm against real measurements first.
- Not a multi-world/multi-dimension design — this ADR assumes today's single-`World`-per-process
  shape; widening it is separately gated the same way ADR §111 already gates dimension/generator-
  version widening for its own dedup key.

## Consequences of leaving this undone

RAM use for overlays/chests/floor-drops continues to grow monotonically with total edits made across
a world's lifetime, unbounded by current player proximity. This is the most likely long-uptime
operational risk among the architecture items surfaced by the 2026-08-16 audit, but is not urgent at
alpha scale — this ADR exists so the fix is designed correctly (hydrate before evict) whenever
uptime/RAM pressure make it worth building, rather than reached for as a quick patch under pressure
and shipping the data-loss bug described in "Rejected shortcut" above.

## References

- `src/zenith/World/World.cs` (constructor lines 79-86 boot-time load; `_baseColumnCache`
  lines 27-29, 347-357; `GetOverlaysInColumn` line 452)
- `src/zenith/World/IChunkStorage.cs` (`ForEachOverlayAsync`, `ForEachChestAsync`)
- `src/zenith/World/LevelDbChunkStorage.cs`
- ADR §36 (`OverrideSoftCap`) and its column-index addendum, `docs/decisions.md`
- ADR §111 (`docs/adr/0111-world-generation-pipeline.md`) — the dedup-key-widening non-goal pattern
  this ADR follows for its own "not multi-world yet" non-goal
- `docs/ecs.md` "Retirement gate" section — same "document the trigger, don't force the migration"
  spirit applied here to a different subsystem
