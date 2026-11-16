# ADR §171 — Water flows on scheduled ticks

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §171`.

**Context:** Step 3 of `plans/world-dynamics-2026-11-15.md`. Water existed only as sea-level source blocks, and `Blocks.IsFluid` knew a single runtime id (depth 0), so a flowing level would not have counted as fluid at all. §164 already gave the server a block-update queue and `ScheduledTicks`.

**Choice:**
- Water is the `minecraft:water` family with Bedrock's `liquid_depth`: 0 source, 1–7 flowing thinner, +8 falling. New `IntKey` and `StateKeys.LiquidDepth`; `BlockStateTable.TryGet/TrySet` for it.
- "Is a fluid" is now data: the `Fluid` trait on `minecraft:water` in `block_definitions.json` (kind stays `Unsupported`, so water is still not placeable, not breakable, not an item). `Blocks.FluidIds` is gone; every depth counts.
- `WaterFlow` follows the vanilla fluid algorithm. When a neighbour changes, `BlockUpdateSystem` schedules a water cell five ticks out; when due, `WaterFlow.Tick` runs. A flowing cell first re-derives its level (two sources beside it over solid ground or a source: a source; water above: falling; else the strongest side neighbour minus one; nothing: it dries up). Then it spreads: into open space below (sideways too only when three sources touch it), otherwise sideways, and sideways only towards the nearest drop-off found by a slope search of up to four blocks (all four ways when none is in reach). Spreading only fills cells that are not water; existing water re-derives itself when woken, so a source is never overwritten. Falling water spreads at level 1.
- Open space is air, or a plant or torch, which water destroys and drops like any world-caused break (`BlockSpill`, shared with the unsupported-block break). A chunk that is not resident is never read as open and never loaded.
- Buckets: `BucketItems` resolves `minecraft:bucket` and `minecraft:water_bucket`; the use handler routes any bucket click to a `BucketIntent` and `BucketSystem` (after block edits) applies it: an empty bucket takes a water source (the clicked cell or the one the click faced), a water bucket pours a source into the clicked cell if water could take it, else the faced one. Reach and the held stack are re-checked in the tick; creative keeps its bucket; a stack of empty buckets fills one and needs inventory room for the full one. A refused use resyncs both cells to the player.

**Why:** No new system: the cost model, bounds (`MaxScheduledPerTick`, the queue and heap caps) and the single-writer rule of §164 carry over. A still lake costs nothing, since nothing wakes it until something next to it changes.

**Invariants:** A source on flat ground reaches seven blocks with levels 1..7, or runs only towards a drop-off within four blocks; falling water runs down shafts; removing the source dries the flow; the infinite-source rule holds; a plant is washed away; a source is never overwritten; flow stops at the loaded edge without loading (`WaterFlowTests`). Buckets obey reach, held-stack and creative rules (`BucketTests`).

**Non-goals / Deferred:** Swimming, buoyancy and drowning (player and mob physics in water), bucket and flow sounds, lava and its interaction with water, `flowing_water` as a distinct block, waterlogging, other filled buckets (milk, fish, powder snow), water breaking doors and other non-plant blocks that vanilla washes away, and a wire smoke of a real client pouring water.

**Verification:** Unit tests (14 for flow and buckets). Not measured: tick cost of a large flood near the caps; nothing watched in front of a client, and the Bedrock rendering of falling and flowing depth values is assumed from the state names.

**Status (November 2026):** Shipped. `Gameplay/WorldInteraction/Fluids/WaterFlow.cs`, `BlockUpdateSystem.cs`, `World/States/{StateKey,StateKeys,BlockStateTable}.cs`, `World/Blocks.cs`, `Fluids/{BucketItems,BucketSystem}.cs`, `BlockSpill.cs`, `Session/Handler/InGameUseItemHandler.cs`.
