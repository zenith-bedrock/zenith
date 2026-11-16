# World dynamics: ticks, liquids, fire, growth, weather, dimensions

**Status:** step 1 (update foundation) is implemented — see ADR §164; everything after it is still plan only. It answers the gap "the world is static: no random ticks,
liquids, fire, growth, weather, Nether or End; dimensions are protocol constants" from
[`dragonfly-gap-analysis-2026-11-15.md`](dragonfly-gap-analysis-2026-11-15.md). Each step is a separate, shippable
change with its own tests and wire smoke; none depends on a later one.

## What exists today

- `GravitySystem` (sand/gravel) is the only cell-reactive system: edits enqueue cells in `GravityPendingStore`, the
  system drains a bounded number per tick.
- `BlockEditSystem` releases an unsupported plant above a broken block (one level, no propagation).
- No scheduled ticks, no random ticks, no neighbour-update propagation, no fluid blocks (`Blocks.FluidIds` is water
  only and static), `WorldRules.DoTileDrops` is the only world rule consumed.

## Design constraints (from `AGENTS.md`)

Single-writer: the GameLoop owns world writes. Per ADR §97 the first question is whether this needs tick execution
at all — all of the below do (continuous evaluation, ordering). So one new `IGameSystem` per concern is justified,
but they share one foundation instead of each inventing a queue like gravity did.

## Step 1 — Block update foundation (prerequisite for everything else) — DONE, ADR §164

Implemented: `BlockUpdateQueue`, `ScheduledTicks`, `BlockUpdateSystem`, support rules in the per-kind shapes (ADR §165); the plant-support hack moved onto it, gravity deliberately stayed on its own store; no behaviour registry until liquids need one. Measured p99 0.54 ms at 256 changed cells/tick. The original design notes follow.

- `BlockUpdateQueue`: bounded, deduplicated set of cells to re-evaluate, fed by every successful world write
  (place, break, gravity landing, explosions). Neighbour fan-out (6 faces) happens when a cell is drained, not when
  it is enqueued, so one edit costs O(1) at write time.
- `ScheduledTicks`: `(cell, blockId, dueTick)` min-heap per world, deduplicated per cell; persisted only if a
  restart mid-flow matters (decision deferred; Dragonfly does not persist them either).
- A `BlockBehavior` registry **only** at the point two behaviours need it (plant support + liquid flow qualify):
  `OnNeighbourUpdate(world, cell)`, `OnScheduledTick(world, cell)`, `OnRandomTick(world, cell)`. Anything that is
  just "name → rule" stays data, as `BlockCatalog` already is.
- Migrate the plant-support hack and gravity onto it, behaviour unchanged (existing tests are the safety net).
- Exit: cell-update fan-out and tick cost measured with the diagnostics lib (p99 per tick) at a stated load.

## Step 2 — Random ticks — FIRST SLICE DONE, ADR §169 (grass spread; sections with nothing to tick cost nothing; leaves, crops and decay still open)

- `randomTickSpeed` game rule (default 1 on Bedrock; Dragonfly uses 3-style sampling). Per tick, per loaded chunk
  near a player: pick N random cells per sub-chunk and call `OnRandomTick` if the block has a behaviour.
- Cost control: iterate only chunks inside the simulation distance of an online player; cap cells per tick.
- First consumers: grass spread/decay, leaves decay (persistent_bit/update_bit already in the palette), farmland
  hydration, sapling growth. Each consumer is its own change.

## Step 3 — Liquids — DONE, ADR §171 (water, lava, their reaction, buckets, drowning, lava damage, mob buoyancy; open: fish buckets, powder snow mechanics, lava fire, wire smoke)

- Water and lava as blocks with `liquid_depth`, flowing via scheduled ticks (water 5 ticks, lava 30 in the
  overworld), source/flow/fall rules, infinite water sources, lava+water → obsidian/cobblestone/stone.
- Needs: `FluidIds` extended, player/mob buoyancy and swimming state, drowning (air supply attribute), lava damage and
  fire, bucket items. The first slice is flowing water + buckets; lava follows.
- Largest single step; do not start until step 1 is merged and measured.

## Step 4 — Fire

- Fire block (`age`), spread via random/scheduled ticks using `flammability`/`flameEncouragement` — already imported in
  `data/block_properties.json`, so the data side is done. Flint and steel, burning entities, extinguishing by rain/water.

## Step 5 — Crop and tree growth

- Crops (`growth`), saplings → trees (needs a tree-structure placer; world generation already has tree logic that can
  be reused), sugar cane / cactus / bamboo columns, melon/pumpkin stems. All random-tick consumers.

## Step 6 — Weather and day/night

- Weather state machine (clear / rain / thunder) with durations from the game rules, `LevelEvent` start/stop rain and
  thunder packets, lightning strikes, rain extinguishing fire and hydrating farmland, snow layers in cold biomes.
- Time of day already ticks; confirm `doDaylightCycle` handling when this lands.

## Step 7 — Dimensions (Nether and End)

- Today dimensions are protocol ids only. Needs: a dimension registry (`Dimension` already exists in `World/` for
  terrain), per-dimension chunk store (columns, ADR §167), `ChangeDimension` + load-screen handshake, portal blocks and
  the portal frame logic (not generation), Nether/End terrain generators, per-dimension spawn rules and coordinates
  scaling. This is a world-model change, not a feature — plan it separately with an ADR before touching code.

## Order and sizing

| Step | Size | Depends on |
|---|---|---|
| 1 Update foundation | M | — |
| 2 Random ticks | S | 1 |
| 5a Grass/leaves/sapling consumers | S each | 2 |
| 3 Liquids (water first) | L | 1 |
| 4 Fire | M | 1, 2 |
| 6 Weather | M | 2 (rain effects) |
| 7 Dimensions | XL | independent; needs ADR |

Do 1 → 2 → one consumer end to end (grass spread) before opening liquids, so the foundation is proven by a small
behaviour first.
