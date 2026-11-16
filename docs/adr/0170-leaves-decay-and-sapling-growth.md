# ADR §170 — Leaves decay and oak saplings grow, as random-tick consumers

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §170`.

**Context:** §169 gave random ticks a home; the next consumers named in `plans/world-dynamics-2026-11-15.md` are leaves and plants.

**Choice:**
- Leaves are their own `BlockKind.Leaves` with a `LeavesShape` that sets `persistent_bit` on placement. A leaf a player places never decays; a leaf the world generator grew (default state, bit clear) can. All fourteen leaf rows carry the `RandomTick` trait.
- `LeavesDecayBehavior`: a non-persistent leaf looks for a trunk by breadth-first search through leaves, at most four steps. None found: it becomes air (leaves drop nothing, per data). A neighbour in a chunk that is not resident counts as unknown, and unknown never decays a leaf.
- Trunks are marked in data with the new `Log` trait (logs and woods, not nether stems), read through `Blocks.IsLog`.
- `SaplingGrowthBehavior`, on the oak sapling only: one tick in seven advances a stage. Stage one sets `age_bit`; stage two plans the tree (trunk 4–6, canopy radius 2 over the top three trunk cells, cap radius 1: the shape world-gen plants) and places it only if every cell is air, leaves or passable decoration and every chunk involved is resident. The grown leaves are non-persistent, so a tree that loses its trunk decays.

**Why:** Persistence has to live in the block state, as Bedrock has it, or a player's leaf hedge would rot. Gating on "unknown" instead of "missing" keeps a tick from destroying leaves at a chunk border just because the neighbour is asleep.

**Invariants:** Player-placed leaves never decay; a leaf within four steps of a trunk stays and one at five goes; a sapling with no room does not grow (`RandomTickSystemTests`). Data and code still agree on which blocks tick.

**Non-goals / Deferred:** Other species' saplings and trees (they need their own log and leaf shapes); sapling and apple drops; bone meal; light requirement for growth; crops — the block set has no wheat, farmland or stems yet, so crop growth waits for those blocks. Decay is not batched into "update" waves as vanilla does.

**Verification:** Unit tests only; nothing was watched in front of a client. The decay search allocates nothing after warm-up but its cost under many leaves per section has not been measured.

**Status (November 2026):** Shipped. `World/Shapes/SimpleShapes.cs` (`LeavesShape`), `RandomTicks/{LeavesDecayBehavior,SaplingGrowthBehavior}.cs`.
