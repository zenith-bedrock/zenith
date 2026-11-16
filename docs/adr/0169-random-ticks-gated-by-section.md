# ADR §169 — Random-tick only the sections that hold tickable blocks

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §169`.

**Context:** The plan in `plans/world-dynamics-2026-11-15.md` needs random ticks for grass, leaves, crops and saplings. Visiting every cell of every loaded column is out of the question, and sampling blindly wastes most samples on stone and air. §167 made sections real, so a section can say whether it has anything to tick.

**Choice:**
- Which blocks take random ticks is data: the `RandomTick` trait in `block_definitions.json`. What they do is code: an `IRandomTickBehavior` per block family in `RandomTickBehaviors`. `Validate()` runs when the system is built and fails, listing every problem, if a marked block has no behaviour or a behaviour sits on an unmarked block.
- A `Section` counts its tickable cells (`RandomTickableCount`), kept exact by `Set`, `Uniform` and `FromIds`. A `WorldColumn` keeps a bit per section that has any (`RandomTickMask`). A column with no bit is asleep: the system tests one integer and moves on. This is the Sleep half of Sleep/Continue; a section with tickable cells is Continue.
- `RandomTickSystem` visits only resident columns that have a viewer, samples `randomTickSpeed` cells (a game rule, Bedrock default 1) per tickable section, and runs the behaviour of the block it lands on. It reads with `TryGetBlock`, so a tick never loads a chunk. Changes are broadcast like any block update.
- First consumer: grass spreads to dirt within one block sideways and from three below to one above, when the dirt has open space over it.

**Why:** Hytale's chunk tick strategies and Minecraft's per-section random ticks both gate on section content; counting at write time makes the gate free. A per-cell Sleep/Continue result was considered and dropped: tracking it per cell costs more than the sampling it would save.

**Invariants:** The count equals the number of tickable cells after any sequence of writes (`SectionTickCountTests`). Unviewed columns and speed 0 tick nothing; a tick never loads a chunk (`RandomTickSystemTests`). Data and code agree on which blocks tick (checked at startup and in a test).

**Non-goals:** Grass decay and leaf decay (both depend on light; no light model exists yet). Scheduled ticks per block behaviour, liquids and fire (still the plan's later steps).

**Deferred:** Leaves, crops and saplings as consumers, each its own change. A light model for decay. Persisting nothing: ticks have no state to save.

**Verification:** 12 unit tests. Not measured: tick cost with many viewed columns full of grass (a flat world has a tickable section per column), and no wire smoke of grass actually spreading in front of a client.

**Status (November 2026):** Shipped. Carried by `World/{Section,WorldColumn,Blocks}.cs`, `Rulebook/GameRules.cs`, `Gameplay/WorldInteraction/RandomTickSystem.cs`, `RandomTicks/`.
