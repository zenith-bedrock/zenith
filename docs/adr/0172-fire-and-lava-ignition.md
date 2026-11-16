# ADR §172 — Fire spreads on scheduled ticks; lava lights it

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §172`.

**Context:** Step 4 of `plans/world-dynamics-2026-11-15.md`, brought forward because lava (§171) had nothing to set alight. `block_properties.json` already carries flammability and flame encouragement per block, and the palette already has `minecraft:fire` with an `age` property.

**Choice:**
- Fire is the `minecraft:fire` family with a new `StateKeys.FireAge` (0..15). It is not placeable or breakable by players yet (`Unsupported` kind, no definition row), and it has no collision: `Blocks.IsFire` is excluded from `IsSolid`.
- Flammability and flame encouragement become fields of `BlockFamily`, filled for every block from `block_properties.json` when the catalog is built; `Blocks.FlammabilityOf` and `FlameEncouragementOf` read them. No block name appears in the rules.
- Fire runs on `ScheduledTicks`, not random ticks: a random tick would reach a given cell about once a minute, where vanilla fire acts every 30 to 39 ticks. `BlockUpdateSystem` schedules a fire cell whenever it, or something beside it, changes; `FireSpread.Tick` is the vanilla rule: water beside it puts it out; with nothing burnable beside it, it dies unless it stands on something solid and is young (age 3 or less); when fully aged over a floor that does not burn it may go out; it ages by 0 or 1; each neighbour may burn (weight 300 sideways, 250 vertically, against its flammability), becoming fire or nothing; it may jump to nearby air (one cell around, four up) that touches something with encouragement, less often the higher the cell. It reschedules itself while it stays lit.
- Water enters a fire cell like any open cell (`FluidFlow.CanHold`).
- Lava is a random-tick block (`RandomTick` trait on `minecraft:lava`, `LavaIgnitionBehavior`): it lights open air up to two cells out and one up that touches something burnable, or air above a burnable block beside it; a solid block in the way stops the search.

**Why:** Same bounds as §171: no new system, the scheduled-tick cap and the `Schedule` de-duplication keep the cost proportional to the fires burning, and a world with no fire costs nothing.

**Invariants:** Fire with nothing to burn goes out; fire beside planks spreads and consumes them; water beside fire puts it out; lava lights the air over a burnable neighbour; flammability is data, not a name (`FireTests`).

**Non-goals / Deferred:** Flint and steel and a fire charge, punching a fire out, fire blocks that stay lit forever (netherrack, soul fire), rain putting fire out (no weather), burning entities and fire damage to players and mobs, sounds and particles, TNT chaining, and any check of how a real client draws the fire block.

**Verification:** Unit tests (`FireTests`, 5). Fire in a chunk nobody has resident does not tick. Not run against a real client.

**Status (November 2026):** Shipped. `Gameplay/WorldInteraction/Fire/FireSpread.cs`, `RandomTicks/LavaIgnitionBehavior.cs`, `BlockUpdateSystem.cs`, `World/{Blocks,BlockCatalog}.cs`, `World/States/{BlockFamily,StateKeys}.cs`, `data/block_definitions.json`.
