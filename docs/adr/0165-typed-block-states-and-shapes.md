# ADR §165 — Typed block states and per-kind shapes

The first block-family pass (§164 and the commits before it) decided states by building string dictionaries per
placement, looked kinds up through a name dictionary under a lock, and kept every family's rules in one switch-heavy
`BlockShapes` plus string literals scattered through placement and support code. It worked, but it allocated per
placement, made `Blocks.BlocksMovement` pay a lock and string hashing per call, and made a new family mean editing five
places.

**Decision.**

- `StateKeys` is the only file that spells property names and values. A key is typed (`BitKey`, `EnumKey<T>` over a
  byte-backed enum, `PresenceKey`) and carries the wire spelling of each value in enum order.
- `BlockFamily` stores all states of a block type as a mixed-radix table built from the palette; `BlockStateTable`
  edits a property by arithmetic on the state's ordinal and reads it back through precomputed wire↔local maps. No
  strings, no allocation at runtime. Families too large (> 65,536) or irregular stay classifiable but are not editable
  by key.
- The catalog classifies every family once at construction (kind, dig profile, `BlockTraits`, slab → double-slab link).
  `KindOf(id)` is one frozen-dictionary hit, lock-free. Name heuristics are confined to that build step.
- Each `BlockKind` has a `BlockShape` (placement, `CanExist`, `HasRoom`, `UpdateShape`, `TryInteract`, door halves).
  Shapes are stateless, read the world and report `BlockWrite`s; the tick commits. `BlockShapes` routes by kind.
- Neighbour-dependent state (walls) is recomputed by `UpdateShape` from the block-update tick (§164), replacing the
  hand-rolled "refresh neighbours on place/remove" code.

**Not done.** `Blocks` still has its static façade and string chest cardinals (§25/§112 debt); `BlockLoot` still reads
the item palette lazily for door items.

**Evidence.** `BlockStateTableTests` compares typed reads with the palette strings for every state; `BlockShapesTests`
and `BlockUpdateTests` cover doors, walls, slab merging and support reactions; `TrySet` measured at ~13 ns and 0 bytes
over 5 million calls (Release).
