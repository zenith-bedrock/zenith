# ADR §171 — Water flows on scheduled ticks

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §171`.

**Context:** Step 3 of `plans/world-dynamics-2026-11-15.md`. Water existed only as sea-level source blocks, and `Blocks.IsFluid` knew a single runtime id (depth 0), so a flowing level would not have counted as fluid at all. §164 already gave the server a block-update queue and `ScheduledTicks`.

**Choice:**
- Water is the `minecraft:water` family with Bedrock's `liquid_depth`: 0 source, 1–7 flowing thinner, +8 falling. New `IntKey` and `StateKeys.LiquidDepth`; `BlockStateTable.TryGet/TrySet` for it.
- "Is a fluid" is now data: the `Fluid` trait on `minecraft:water` in `block_definitions.json` (kind stays `Unsupported`, so water is still not placeable, not breakable, not an item). `Blocks.FluidIds` is gone; every depth counts.
- `WaterFlow` is the rule set. When a neighbour changes, `BlockUpdateSystem` schedules a water cell five ticks out; when that comes due, `WaterFlow.Tick` runs: a flowing cell re-derives its level from what feeds it (water above → falling; best side neighbour + 1; none → dries up; two sources over solid ground or a source → becomes a source); then it falls into open space below, else spreads to the four sides with level + 1, up to 7. Every write is an ordinary world write, so it wakes its own neighbours through the existing queue and goes out in the same block-update packet.
- Open space is air or a ground-cover plant (replaced without a drop). A chunk that is not resident is never read as open and never loaded.

**Why:** No new system: the cost model, bounds (`MaxScheduledPerTick`, the queue and heap caps) and the single-writer rule of §164 carry over. A still lake costs nothing, since nothing wakes it until something next to it changes.

**Invariants:** A source on flat ground reaches seven blocks with levels 1..7; falling water runs down shafts; removing the source dries the flow; the infinite-source rule holds; flow stops at the loaded edge without loading (`WaterFlowTests`).

**Non-goals / Deferred:** Buckets, swimming, buoyancy, drowning and a player-visible way to place water (none exists yet). Vanilla's bias toward the nearest drop-off: water spreads in all four directions. Lava and its interaction with water, `flowing_water` as a distinct block, waterlogging, ocean-edge behaviour for chunks that stream in later.

**Verification:** Six unit tests. Not measured: tick cost of a large flood near the caps; nothing watched in front of a client, and the Bedrock rendering of falling and flowing depth values is assumed from the state names.

**Status (November 2026):** Shipped. `Gameplay/WorldInteraction/Fluids/WaterFlow.cs`, `BlockUpdateSystem.cs`, `World/States/{StateKey,StateKeys,BlockStateTable}.cs`, `World/Blocks.cs`.
