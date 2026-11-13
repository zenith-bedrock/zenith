# ADR §28 — Chests — RAM store + ISR container 7 (MVP)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §28`.

**Choice:** Ship the §19 sketch minimally:

1. `ChestStore` dict `(x,y,z) → InventorySlot[27]`; `Ensure` on place; `RemoveAndDump` on break (contents → `TryAdd` else floor drops). No LevelDB `ct:` keys yet.
2. `Player.OpenChest` nullable; set on empty-hand UseClickBlock on chest; clear on `ContainerClose`. Wire: `ContainerOpen` type **0**, window id **2** (≠ `0xff`); `InventoryContent` for chest + player.
3. Flat ISR slots `100+` map from container **7**; `PlayerInventory` does not own chest. `InventorySystem` dual-store transfer/swap with snapshot rollback of player + chest.
4. `PlaceInContainer` / `TakeOutContainer` decode as supported (same shape as take/place).

**Why:** Inventory rearrange (§17) without chests still left Survival “storage furniture” incomplete; dual-store keeps decide≠transmit without stuffing chest into `PlayerInventory`.

**Deferred:** LevelDB hydrate/Put; BlockActor; hopper; full creative item list / `block_state_b64`; window-scoped net-id isolation beyond protocol arrays. **Sneak-place + double-chest → §56.**

**Known debt (updated §36):** Warn once when Ensure count crosses `10_000` — still no refuse/eviction (needs LevelDB redesign).

**Adendo (jul 2026 — chest lid BlockEvent):** Lid animation is wire-only — `BlockEvent` (0x1a) `ChangeChestState` (type **1**), `EventData` **1** open / **0** close. No BlockActor NBT. `ChestStore` keeps opener refcount per cell (player runtime id set): animate open on **0→1**, close on **1→0**. `WorldProtocol.SendBlockEvent` is session-scoped; `ChestLidFanout` sends to opener + `IsInGame` peers with `Chunks.Knows` of the chest column (same Knows pattern as UpdateBlock joiners — not VisibilitySystem). Hooks: `InventorySystem` ApplyWindow OpenChest / Close; break clears openers + fans close if needed; disconnect / void-death release opener. Optional LevelSound later — not required for lid honesty.
