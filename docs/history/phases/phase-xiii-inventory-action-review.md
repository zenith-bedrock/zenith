# Phase XIII — Inventory & Action Pipeline Architecture Review

**Status: analysis only. No code was changed as part of this document.**

## Question

> O modelo atual continua adequado para a próxima expansão de gameplay ou alguma fronteira precisa
> evoluir agora?

**Short answer: the model is adequate. One boundary (`InventoryStackAction`) is doing exactly the
job it should; nothing needs to evolve yet. One smaller boundary (stack-net-id replay validation)
is worth watching, not fixing. Two future features (trading, enchantments) would each cost one
small, well-precedented addition, not a rework.**

---

## Current architecture

```text
Bedrock ItemStackRequestPacket
        |
        v
InGameInventoryHandler.HandleItemStackRequest   (Session/Handler — network thread)
        | per action: TryMapAction (Phase XIII.3 extraction)
        | container/slot id -> InventorySlotReference   [Protocol boundary: wire well-formed?]
        v
InventoryStackAction[]                           (Player — domain, wire-agnostic)
        |
        v
InventoryStackIntent                             (Player — bounded mailbox, PendingMailbox<T>)
        |  (GameLoop tick, single consumer)
        v
InventorySystem.Apply                            (Gameplay/Systems — GameLoop thread)
        | per action: TryTransfer / TrySwap / TryRemoveForDrop / craft / CanPlaceInArmorSlot
        | slot resolution: InventorySlotResolver.GetSlot/TrySetSlot (dispatches by InventorySlotArea)
        | [Gameplay boundary: does the player actually have this, is this slot allowed, is the
        |  container reachable/open, does armor/craft/creative eligibility hold?]
        v
Inventory mutation (PlayerInventory / ChestStore / PlayerCraftUi)
        |
        +--> Protocol.SendItemStackResponseOk/Error + SendInventoryContent/SendArmorContent/SendChestContent
        +--> World.PersistInventory / PersistArmor / PersistChest
```

### Ownership map

| State | Owner type | Mutator | Persisted via |
|---|---|---|---|
| Hotbar + bag (36 slots) | `PlayerInventory._slots` | `InventorySystem` (ISR), `BlockEditSystem` (place consume), `HungerSystem` (eat consume) | `SlotBlob` under `inv:{uuid}` |
| Cursor (drag ghost) | `PlayerInventory._cursor` | `InventorySystem` only | not persisted (ephemeral UI state) |
| Armor (4 slots) | `PlayerInventory._armor` | `InventorySystem` (ISR, via `CanPlaceInArmorSlot` guard) | `SlotBlob` (4-length) under `ar:{uuid}` |
| Craft grid + result | `PlayerCraftUi` | `InventorySystem` only | not persisted (ADR §39, ephemeral) |
| Chest contents | `ChestStore` (position-keyed) | `InventorySystem` (ISR while a chest session is open) | `SlotBlob` under `ct:{x}:{y}:{z}` |
| Which container a player currently sees | `Player.OpenContainer` (`OpenContainerSession`) | `InventorySystem.ApplyWindow` | not persisted (reconnect reopens nothing, correct — Bedrock has no "resume open UI" concept) |

Every slot-array state (bag, armor, chest) is owned by exactly one type and mutated through exactly
one path (`InventorySystem`, with two narrow, already-justified exceptions: `BlockEditSystem`
consumes one hotbar slot on placement, `HungerSystem` consumes one hotbar slot on eating — both use
`PlayerInventory.TryConsume`/`TryConsumeOne`, the same mutation primitives `InventorySystem` itself
uses, not a parallel path). No state is owned by two types. No mutation path bypasses the domain
methods to poke array indices directly.

---

## 1. Is `InventoryStackAction` sufficient, or has it started mixing concerns?

**Still sufficient — and it already cleanly separates the three things the brief asked about, even
though nothing forced that separation to be named explicitly:**

| Brief's concept | What actually plays that role today |
|---|---|
| **Intent** ("client wants to move an item") | `InventoryStackIntent` (`RequestId`, `Actions[]`, `ExpectedOpenContainerGeneration`) — exactly one per ISR packet request, submitted through `PendingMailbox<InventoryStackIntent>` |
| **Command** ("server accepted this to execute") | `InventoryStackAction[]` — produced by `TryMapAction`, which validates *wire shape* (container/slot ids resolve to a real domain location) but not *feasibility* (whether the player actually has the item, whether the destination has room). A "command" here means "this is a well-formed action against a real domain location," not "this will definitely succeed." |
| **Result** ("what happened") | **Implicit**, not reified. `InventorySystem.Apply` decides success/failure via a local `ok` bool and either calls `SendItemStackResponseOk` + persists, or restores every snapshot and calls `SendItemStackResponseError`. |

The Intent/Command split is real and already enforced by different types in different files
(`InventoryStackIntent` in `Player/`, `InventoryStackAction` in `Player/`, but *built* by
`TryMapAction` in `Session/Handler/` and *consumed* by `InventorySystem` in `Gameplay/Systems/`).
This is not accidental — it is the same "decode on the network thread, mutate on the GameLoop
thread" split every other intent in the codebase uses (Phase XII's `PendingMailbox<T>` review).

**The Result is the only piece not reified as a type, and that is fine, not a gap:** nothing outside
`InventorySystem.Apply` currently needs to *consume* a result — the two things a result produces
(protocol response, persistence) both happen inline, once, in the one place that computes success.
Making `InventoryActionResult` a real type would be justified the moment a second consumer appears
(an audit log, a "last action" diagnostic, a batched multi-request commit) — not before. This is the
same reasoning Phase XII applied to `PlayerVitals`: don't reify a concept into an object until
something other than its one current call site needs to read it.

---

## 2. Validation boundary — protocol vs. gameplay

| Layer | What it rejects | Where |
|---|---|---|
| **Protocol** (wire well-formedness) | `request.AllSupported == false`, empty action list, unresolvable container/slot id (`InventoryContainerMap.TryMap` failure) | `InGameInventoryHandler.HandleItemStackRequest` + `TryMapAction` |
| **Gameplay** (business rules) | Insufficient stack count, mismatched stack id on a transfer target, armor piece in the wrong slot (`CanPlaceInArmorSlot`), unreachable/closed chest (`IsOpenChestAccessible`), invalid recipe/creative id, stale open-container generation, dead player | `InventorySystem.Apply` and its private `TryTransfer`/`TrySwap`/`TryRemoveForDrop`/`IsValidReference` helpers |

No duplicated validation was found between the two layers — each rule is checked in exactly one
place. The two layers also fail differently on purpose: a protocol-layer failure means "this request
can't even be interpreted" (reject the whole batch, no snapshot needed since nothing was touched
yet); a gameplay-layer failure means "this was interpretable but not allowed" (roll back whatever
snapshot was captured, then reject).

**One spot is worth naming, not fixing:** `ValidateClientStackNetIds` (soft match against
`InventoryProtocol`'s last-advertised stack net id) lives inside `InventorySystem.Apply`
(Gameplay), but the thing it validates — "does the client's view of this slot's wire identity still
match what we told it" — is arguably protocol/replication-state, not a gameplay rule. It is not
duplicated, and moving it would require passing session-scoped protocol state into a place that
otherwise only touches domain state, which is its own cost. Left as-is; flagged here as the single
least-crisp point in an otherwise clean split, worth revisiting only if more protocol-state-aware
checks accumulate at this layer.

---

## 3. Persistence — one model or three ad hoc ones?

**Already one model, reused three times, not discovered by this review so much as confirmed by it:**

- Every *slot-array* state (bag, armor, chest cells) is packed with the same `SlotBlob.Pack`/
  `TryUnpack` format — a generic `(kind, value, count)` triple per slot, already versioned (v1→v2
  happened once, pre-Phase-XI, for the tool/block migration). Bag, armor, and chest are three
  *consumers* of one format, not three formats.
- Scalar player state (pose, GameMode, and — as of Phase XI.4 — experience level/points) uses the
  separate `PlayerDataBlob` format, which is the *correct* separate model: those fields are
  heterogeneous scalars with a different write cadence than a slot array, not another instance of
  the same shape (Phase XII's persistence review already reasoned through why merging these would
  be wrong — write-cadence mismatch).

This is exactly the outcome Phase XII's persistence section predicted and did not need to build:
"the versioned-blob pattern is already proven and cheap to extend." It has now been reused a third
time (chest, transitively, was already using it before this review; armor was the second explicit
use in Phase XI.2) without anyone having to design a new format.

**Verdict: no persistence change needed.** There is no "ad hoc, each evolved separately" problem —
the model was already consistent; this review just made that explicit.

---

## 4. Comparison with references (behavior only, not architecture)

- **Basalt** (`Network/Handlers/ItemStackRequest.cs`) executes every ISR action **inline on the
  network thread**, with `[ThreadStatic]` mutable fields (`_pendingCreativeStackId`,
  `_pendingCraftResult`) carrying state between actions within one request. There is no
  intent-queue/GameLoop split at all — mutation and network decode are the same step. Zenith's
  bounded-intent handoff (decode on network thread → `PendingMailbox` → mutate on GameLoop thread)
  is a stricter, more deliberate single-writer discipline than this reference uses. Not something to
  copy *toward* — evidence Zenith's existing choice is already more rigorous.
- **Dragonfly** (`item/inventory/armour.go`, 249 lines) gives armor its own dedicated `Armour` type
  with slot-specific accessors (`Helmet()`, `Chestplate()`, ...), separate from the general
  `Inventory` type. Zenith's armor (4 slots inside `PlayerInventory`, gated by `ArmorItems`/
  `CanPlaceInArmorSlot`) is simpler because Zenith's armor has no per-piece behavior yet (no
  enchantments, no durability, no set bonuses) — Dragonfly's split becomes necessary once a piece
  needs its own logic beyond "occupies a slot." This is this review's one concrete **forward-pressure
  signal**: if/when armor gains per-piece behavior, a dedicated small type (not a framework) is the
  natural next step, matching a reference implementation that already needed it for the same reason.
- **PocketMine/BetterAltay** separate inventory *windows* (PlayerInventory, ArmorInventory,
  CraftingGrid, EnderChestInventory, ...) as independent objects implementing a shared interface,
  each broadcasting its own contents. Zenith's `InventorySlotArea` enum + `InventorySlotResolver`
  achieves the same practical effect (one dispatch point per area) without an interface hierarchy —
  confirmed still the right call: Zenith has 6 areas (`PlayerInventory`, `Cursor`, `OpenContainer`,
  `CraftGrid`, `CraftResult`, `Armor`), each a `case` in one `switch`, not 6 classes implementing a
  contract nothing else needs.

None of these references' architectures were adopted. They confirm Zenith's specific choices
(bounded intent over inline mutation; enum+switch over an interface hierarchy) are already at least
as disciplined as mature alternatives, and they name the one place (per-piece armor behavior) where
a reference needed to split further than Zenith currently does.

---

## 5. Future pressure — what would avoid real rework later?

| Future feature | What it needs | Does today's model block it? |
|---|---|---|
| **Trading / villagers** | A new `InventorySlotArea` (trade input/output) + `InventoryContainerMap` wire ids + `InventorySlotResolver` cases. Same shape as adding Armor was in Phase XI.2. | No — this is the existing extension point, already exercised once. |
| **Advanced crafting** (3×3 grid, shaped recipes) | `PlayerCraftUi.GridSize` grows, `RecipeRegistry` grows. No new area, no new pipeline stage. | No — orthogonal to the action pipeline; it's a grid-size and recipe-matching change, not a boundary change. |
| **Enchantments** | Per-item metadata beyond `(StackId, Count)` — this is the one place today's `InventorySlot`/`StackId` shape would need to grow (an item identity currently carries no NBT-equivalent payload). | **Partially** — not a pipeline problem, a data-shape problem. `InventorySlot` would need an optional payload; every layer that already threads `InventorySlot` through unchanged (persistence, wire description, resolver) would need to carry it too. This is the one item on this list that touches more than one file by necessity — but it is a data-shape extension, not an architecture rework: `SlotBlob`'s versioned format and `NetworkItemStack`'s existing extra-data-length field (currently always written as 0) were both already built with room for exactly this. |
| **Quests / plugins** | Out of scope for this review — neither has a concrete design yet in Zenith, and neither obviously touches the inventory pipeline specifically (a quest system would consume events like `MobKillReward` does, not inventory actions directly). | N/A |

**What would avoid real rework today, if anything?** Nothing needs to change *now*. The one item
worth a one-line mental note for whoever eventually builds enchantments: `InventorySlot`/`StackId`
currently assume an item's full identity is `(kind, network id, count)` — adding a fourth,
optional field (durability/enchantment payload) later is additive, not a redesign, provided nobody
in the meantime starts assuming `InventorySlot` is exactly 3 fields wide in a way that resists a
fourth. No code change is proposed to pre-empt this; the data shape already has an obvious, minimal
extension point (`SlotBlob` versioning, `NetworkItemStack`'s unused extra-data length) and building
it before there's a real enchantment feature to drive its exact shape would be guessing.

---

## Result: Caso A

**Manter a arquitetura atual.** The complexity is real (six ownership areas, three validation
layers' worth of rules, three persistence blob types) but every piece of it is already localized to
exactly one file/type, none of it is duplicated, and the one boundary the brief specifically asked
about (`InventoryStackAction`) already does its one job cleanly. Phase XIII.3's `TryMapAction`
extraction (done, not proposed) was the one concrete improvement this area needed, and it has
already landed.

**Not proposed, and explicitly rejected per the brief's own boundaries:** `InventoryFramework`,
`ItemComponentSystem`, a generic `TransactionEngine`, an `InventoryActionResult` type (no second
consumer exists yet), a dedicated `Armor` type (no per-piece behavior exists yet). Each has a named,
concrete trigger above for when it would stop being premature.

## Risks of doing nothing

None identified beyond the two forward-pressure notes above (armor-per-piece-behavior,
enchantment-data-shape), both of which are additive when their time comes, not blocked by today's
structure. The main risk this review checked for — validation duplicated between protocol and
gameplay layers, silently drifting apart — was not found.

## Implementation plan

None required for this document. If the enchantment or trading features above become real work,
each gets its own short design note at that time, following the same "audit → evidence → smallest
justified step" process this phase and Phase XII both used.
