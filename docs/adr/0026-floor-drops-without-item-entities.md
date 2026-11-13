# ADR §26 — Floor drops without item entities

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §26`.

**Choice:** When break `TryAdd` fails, still break the block and deposit `(runtimeId,count)` in `World.FloorDrops` (sparse cell map). `BlockSystem` picks up within 1.5 blocks on tick via `TryAdd`. No Bedrock item entities, no physics.

**Why:** Full hotbar must not soft-lock survival break; entity actors stay frozen.

**Deferred:** Merge across cells, despawn timers, gravity/physics, LevelDB persist of drops. (Wire shipped — adendo below.)

**Known debt (updated §36):** SoftCap refuse on new cells (`2048`); merge into existing cells still allowed. Warn once at refuse.

**Adendo (jul 2026 — drop-entity wire MVP):** Each floor cell also holds `EntityRuntimeId` (from `PlayerManager.AllocateRuntimeId`). Spawn → `AddItemActor` (0x0f) to peers with `Chunks.Knows`; pickup → `TakeItemActor` (0x11). Merge count change → `RemoveActor` + `AddItemActor`. Late join / column stream catch-up emits Add for drops in known columns. Position = cell center, velocity zero (floating OK). Still **no** WorldEntity/ECS, gravity tick, despawn TTL, or LevelDB persist of drops (§32). **Q-throw + death loot → §73.**

**Adendo (jul 2026 — AddItemActor ItemStackWrapper):** Protocol 1001 encodes AddItemActor item as legacy **ItemStackWrapper** (same as AddPlayer held), not NetworkItemStackDescriptor. Wrong shape crashed the Bedrock client after full-inv floor drop. `NetworkItemStack` writers use Mojang wire names (`WriteItemStackWrapper` / `WriteNetworkItemStackDescriptor` / `WriteItemStack`); packet Encode picks; skip Add when `DescribeStack` is air.

**Adendo (jul 2026 — partial pickup):** `PickupFloorDrops` uses `TryAddUpTo` + `FloorDropStore.TryTakeUpTo`. Partial fit (e.g. stack 63 + floor count 5) takes what fits, `TakeItemActor`, then Remove+Add republish for remainder. `TryAdd` (break/chest dump) stays all-or-nothing with `CaptureSnapshot`/`RestoreSnapshot` so a failed full add never sticky-fills 63→64.

**Adendo (jul 2026 — pickup reach + break partial):** Pickup reach uses item-entity Y (`+0.125`, same as `AddItemActor`) and player torso (`feet + 0.75`), radius **2** blocks — old cell-center `+0.5` / 1.5 from feet failed rim-of-hole smoke. Break/chest dump uses `TryAddUpTo` + floor surplus (not all-or-nothing before drop). `Blocks.SameMergeItem` / `NormalizeMergeRuntimeId` stack chest facings and same-name palette rids. Roll back inventory if `TryTakeUpTo` fails after `TryAddUpTo`.

**Adendo (jul 2026 — AuthInput eye→feet):** StartGame / AuthInput wire Y is eye-space. Domain `Player.PositionY` is feet (`FlatSpawnY`, block reach `+ PlayerEyeHeight`). `InGameSessionHandler` submits via `MovementInputState.FromClientAuthInput` (`eyeY - PlayerEyeHeight`).

**Adendo (jul 2026 — Absolute network offset):** Bedrock player `MoveActorAbsolute` / local `MovePlayer` need wire Y = feet **+ 1.621** (`PlayerEyeHeight` + 0.001 so Absolute does not clip into the block top). `AddPlayer` stays at bare feet. Before AuthInput→feet, raw eye Y on Absolute accidentally looked correct; after feet domain, bare Absolute sank peers into the floor. `EntityProtocol` applies `EntityHitboxes.AbsoluteWireY`.

**Adendo (jul 2026 — AABB pickup + delay):** Sphere/`torso+0.75` retired. Pickup = player standing AABB (`0.6×1.8` feet) **expanded** `(1, 0.5, 1)` ∩ item AABB `0.25³` at cell (expand player side, not item). Pure math in `Geometry/Aabb`; Bedrock sizes in `World/EntityHitboxes`. `FloorDropStore` default **pickup delay 10** ticks (new cell); merge delay = `max(existing, incoming)`; partial `TryTakeUpTo` / republish **does not** reset delay. `TickPickupDelays` once per `BlockSystem` tick before reach checks — same-tick deposit becomes 10→9, not vacuum. Wire still shows item during delay. Expand `+0.5` Y does **not** reach an item one full block below feet (rim-of-hole) — step into the cell / same Y (vanilla-shaped, not the old sphere). **Known debt:** fixed standing BB (no sneak/swim); no off-hand preference; block interact reach stays Euclidean eyes (§15). Proximity gameplay going forward prefers AABB (orientation, not a mandate to rewrite §15 this PR).
