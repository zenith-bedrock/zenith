# ADR §160 — Item stack capacity

ItemStackLimits owns immutable item-name capacities from an embedded, attributed
minecraft-data Bedrock 1.26.60 table. Only exceptions to 64 are stored; runtime item
IDs resolve through the loaded item palette, block stacks through Blocks' full
reverse palette. Boot prepares the item table beside Tools/BlockLoot. This is a
domain fact shared by inventories, crafting and floor drops, not a registry API.

New additions, creative results, transfers, swaps and recipe batches obey capacity
per item, including non-stackable block items. Existing oversized saves remain
readable: same-identity reductions are allowed, but cannot grow or transfer their
oversize into a new slot. Snapshot rollback preserves existing contents exactly.
Floor batches split oversized legacy stacks into legal entities before planning;
capacity refusal rolls back the whole operation. No source stack is clamped away.

Test 1/16/64 capacities, cursor/chest/grid transfers, creative, batch crafting,
legacy reductions, floor merges/splitting and rejection without partial state.
