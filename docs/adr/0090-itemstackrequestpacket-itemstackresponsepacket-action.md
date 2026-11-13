# ADR §90 — `ItemStackRequestPacket` / `ItemStackResponsePacket` — action-number and optional-field bugs, not just Cereal debt

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §90`.

**Choice:** Close the last row of the §89 "do together" grouping. Cross-checked both packets against `zenith-smoke-bot`'s own `bedrock-protocol` dependency's bundled `minecraft-data` `protocol.json` (1.26.40) — the same source that byte-verified §88/§89 — and found the actual bugs were **not** the Cereal double-optional pattern this grouping was opened to investigate, but two independent, more basic correctness bugs plus one genuine optional-field miss on the response side:

1. **`ItemStackRequestPacket`'s action-type numbering was simply wrong**, unrelated to any protocol-version boundary: Zenith's constants included `ActionPlaceInContainer = 7` and `ActionTakeOutContainer = 8`, two action kinds that **do not exist** in the real `ItemStackRequestActionType` mapper (`take=0, place=1, swap=2, drop=3, destroy=4, consume=5, create=6, lab_table_combine=7, beacon_payment=8, mine_block=9, craft_recipe=10, craft_recipe_auto=11, craft_creative=12, optional=13, craft_grindstone_request=14, craft_loom_request=15, non_implemented=16, results_deprecated=17`). Every action from `lab_table_combine` onward was consequently read as the wrong case — e.g. a real `lab_table_combine` (7) action byte was decoded as `ActionPlaceInContainer`, triggering a `count + 2×StackRequestSlotInfo` read that doesn't exist on the wire for that action, corrupting every action after it in the same request. Renumbered to match; the two invented actions are gone.
2. **Every action entry carries a `legacy_type_id: u8` immediately after `type_id`** that Zenith never read — a permanent one-byte misalignment on every single action, present since the packet was first written (not a 2168 regression).
3. **`StackRequestSlotInfo.stack_id` is a fixed `li32` (4 bytes), not a varint** — Zenith read it with `ReadVarInt()`, silently correct only when the id happened to fit and zigzag-decode to something that consumed the same byte count by coincidence (usually did not).
4. **Three other field-width mismatches found while re-deriving every action's shape from the same schema:** `mine_block`'s `network_id` is `li32` (was read as a varint), `craft_grindstone_request`'s `recipe_network_id` is `li32` (was read as a varint), and `craft_recipe_auto` had one extra phantom `u8` read before its ingredient count that doesn't exist on the wire at all (source of the byte drift for that action specifically).
5. **`craft_recipe_auto`'s ingredient skip (`RecipeIngredient2`) and `results_deprecated`'s entry skip (`ItemStackRequestInstanceDescriptor`) were both modeled on the wrong descriptor shape** (`ItemDescriptorType`'s 6-case CraftingData-era variant) — rewrote each against its own real schema (4-case mapper + `legacy_type` byte + type-specific fields + a differently-typed trailing count per descriptor).
6. **`ItemStackResponsePacket` was missing a real optional entirely, not just a Cereal-era addition:** `containers` is an unconditional `option<array<...>>` in the struct (present regardless of `status`) preceded by a decorative `containers_presence` bool — Zenith wrote a bare count only on `StatusOk` and nothing at all on error, omitting both presence markers always. Same pattern for each slot's `item_stack_id`, which the real schema also gates behind a decorative presence bool + the option's own bool — Zenith wrote a bare unconditional varint.

**Non-goals:** `ItemStackRequestPacket.ReadAction`'s remaining unsupported cases (`lab_table_combine`, `craft_recipe_auto`, `craft_recipe_optional`, `craft_grindstone_request`) stay wire-correct-but-domain-unsupported — Zenith doesn't implement grindstone/loom/auto-craft server logic, only decodes them accurately enough not to desync the stream. `PlayerListPacket`, `StartGamePacket` — untouched this ADR, see §91.

**Verification:** `dotnet test zenith.sln -c Release`: fixed 5 existing tests that encoded the old (wrong) action numbers/widths, added `ItemStackResponsePacketTests` (byte-level, `IsEndOfFile`-asserted, covers error/with-stack-id/without-stack-id), 518/518 green. Not yet re-verified against a live client — `smoke:break`/`smoke:place` don't exercise `ItemStackRequestPacket` (they use `inventory_transaction`, a different packet), so this fix is validated by wire-shape cross-reference and unit tests only; real-client validation (crafting, shift-click, container drag) is the next confirmation step, planned separately (not smoke-bot — real Bedrock client).

**Status (ago 2026):** Shipped — `ItemStackRequestPacket`/`ItemStackResponsePacket` rewritten, 5 tests updated + 3 new tests, 518/518 green. Live validation pending (real client, not smoke-bot).

**Correction (ago 2026, protocol 2168 reconciliation):** The first item above described the two
discriminants of `StackRequestAction` incorrectly. The Cereal **variant** intentionally omits
`PlaceInContainer`/`TakeOutContainer` (7/8), but each action is followed by the historical
`legacy_type_id` byte, which still retains them. Consequently action types at and after the gap
have different values in the two fields: `MineBlock`, for example, is Cereal variant **9** followed
by legacy id **11**. Zenith must read both fields and map the variant through the gap before
dispatching by legacy id; it must not replace the historical ids with a compact 0–17 enum. This
was verified directly against local Gophertunnel 1.26.40 / protocol 2168 reader and writer, and is
characterized by a byte-level Zenith test. The original prose in item 1 conflated the compact
variant list with the legacy id, even though item 2 correctly identified that the latter is present
on wire.
