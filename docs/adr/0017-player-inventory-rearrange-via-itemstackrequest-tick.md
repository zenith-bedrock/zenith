# ADR §17 — Player inventory rearrange via ItemStackRequest (tick authority)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §17`.

**Choice:** Enable `ServerAuthoritativeInventory` in StartGame. Client moves use `ItemStackRequest` (take/place/swap) → handler maps containers 12/28/29/cursor → flat → FIFO `InventoryStackIntent` → `InventorySystem` applies `TryTransfer`/`TrySwap` (all-or-nothing snapshot) → same-session `ItemStackResponse`. Stack network IDs live in `InventoryProtocol` only. Place/break stay hotbar IT UseItem. Open inventory: Interact → `ContainerOpen` window 0.

**Why:** After §16, drag in the client UI desynced on the next `SendInventoryContent` because moves never reached domain authority. Adding ISR without chests keeps decide≠transmit: domain owns slots/cursor; Protocol owns net IDs and wire. `InventorySystem` exists because there is now intent + tick + transmit (unlike §16’s TryAdd-only expansion).

**Deferred:** creative craft, drop entity, destroy-void, AuthInput-embedded ISR, Normal IT slot moves. (Held peer → §18. Chests → §19 sketch.)
