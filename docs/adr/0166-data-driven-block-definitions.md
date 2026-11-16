# ADR §166 — Data-driven block definitions and SetBlockFlags

## Context
Block rules (kind, tool, tier, drops, slab pairing) were derived at startup from name heuristics. That put string
matching in the runtime, made exceptions hard to review, and hid mistakes until a block misbehaved.

## Decision
- `data/block_definitions.json` (embedded) holds one row per supported block. A block without a row is `Unsupported`.
- `BlockCatalog` applies the rows, validates each kind against its shape's `RequiredKeys` and slab `mergesInto`, and
  fails at startup with the full list of problems.
- `BlockLoot` resolves each row's `drop` against the item palette once; no drop rules remain in code.
- The old heuristics moved to `zenith.Tests/Generators` and only fill rows missing from the file
  (`ZENITH_REGEN_BLOCK_DEFINITIONS`); existing rows always win, so hand edits are safe.
- `World.TrySetBlock(..., SetBlockFlags)`: `NotifyNeighbors` (default) enqueues a block update; `None` is used when the
  writer is itself a reshape or a toggle, so it does not re-trigger the cascade.

## Consequences
Behaviour changes are data edits reviewed in diffs. Geometry (door halves, wall connections) stays in code shapes.
The generator produced the copper double slabs under the wrong names; the validation caught it and the rows were fixed.
