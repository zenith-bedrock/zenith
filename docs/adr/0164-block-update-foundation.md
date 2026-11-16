# ADR §164 — Block update foundation

World changes need consequences: a plant whose soil was dug, a door without ground, a torch without its wall.
Until now only plants one level above a *player* break had a special case inside `BlockEditSystem`; gravity has its own
pending store. Liquids, fire and growth all need the same two things — "a cell changed, look around" and "look at this
cell again in N ticks" — so they share one foundation instead of each inventing a queue.

**Decision.** `World.TrySetBlock` feeds `BlockUpdateQueue` (bounded 8192, deduplicated, FIFO) on every successful write,
O(1). `BlockUpdateSystem` runs after block edits and gravity: per tick it drains at most 256 changed cells, evaluates
each cell and its six neighbours with `BlockShapes.CanExist` and `BlockShapes.UpdateShape` (pure, per-kind shapes — see §165), and breaks and drops the
unsupported ones; breaks are world writes, so cascades (door upper half, plant on a plant) continue through the same
queue and spill to the next tick when over budget. `ScheduledTicks` (min-heap by due tick, one entry per cell, earliest
wins, bounded 8192) is drained in the same system and re-evaluates the cell; it has no other consumer until liquids.

**Not done on purpose.** No behaviour registry or `OnRandomTick` yet — the only rule is support, answered by a kind
switch; the registry arrives with liquid flow, the first second consumer. Gravity keeps `GravityPendingStore`: its
two-per-tick staggering and falling-block entities are behaviour of their own, and moving them gains nothing now.
Queue and scheduled ticks are RAM only: a reaction lost to a restart is recomputed by the next neighbouring edit.

**Refusal at the cap** loses only the reaction, never the block; the worst case is a floating plant that breaks on the
next nearby edit. Cost, debug build, 256 changed cells/tick (1792 neighbour reads): p50 ≈ 0.21 ms, p99 ≈ 0.54 ms.

Tests: queue order/dedup/cap, scheduled-tick due/dedup, plant/door/torch reactions, cascade across ticks
(`BlockUpdateTests`); wire: `zenith-smoke-bot` `smoke:blocks` digs the soil under a dandelion and expects both cells to
go to air. Torch support follows Zenith's own placement convention (face clicked → `torch_facing_direction`), not yet
checked in a rendered client.
