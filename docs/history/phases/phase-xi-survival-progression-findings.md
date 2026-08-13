# Phase XI — Survival & progression gameplay findings

## XI.1 — Food / Hunger (closed)

### What landed

```text
UseClickAir (held stack is food)
  -> Player eat intent (bounded, mirrors attack/projectile intent)
  -> HungerSystem.Tick: consume item, raise Hunger
  -> Inventory replication + persistence (existing paths)
  -> SendDefaultAttributes (existing wire call)
```

Concrete additions, no new abstractions:

| Concern | Implementation |
|---|---|
| Food identity | `Gameplay/FoodItems.cs` — a small name→nutrition dictionary, not an item-component system |
| Player state | `Player.Exhaustion` (float), alongside the existing `Player.Hunger` |
| Eat intent | `Player.SubmitEatIntent`/`TryConsumeEatIntent`, same shape as the existing attack/projectile intents |
| Simulation | `Gameplay/Systems/HungerSystem.cs`, one more concrete `IGameSystem` registered after `InventorySystem` (mutates the held stack) and before `EquipmentSystem` (replicates the final held state) |
| Damage cause | `DamageCause.Starve` added to the existing cause enum; routes through the existing `PlayerDamage.Apply` |
| Healing | `HealthState.Heal` — the first authoritative increase path, mirroring `Apply`'s guard rules |

### Rules implemented (vanilla-parity, intentionally minimal)

- Eating consumes exactly 1 of the held stack (Survival) and raises Hunger by the item's nutrition, capped at 20; no-op above 20 Hunger.
- Creative eats without consuming inventory (matches vanilla) and skips exhaustion/starvation/regen entirely.
- Sprinting accrues `Exhaustion` (+0.1/tick); crossing 4.0 drops Hunger by 1 and resets the accumulator.
- Hunger at 0 deals 1 starvation damage every 80 ticks (4s) through the existing damage/death pipeline — a starved player can die and loots/respawns exactly like any other death.
- Hunger ≥ 18 and health below max heals 1 HP every 80 ticks.
- Respawn resets Hunger to 20 and Exhaustion to 0 (`Player.CompleteRespawn`).

### Explicitly not built

- No saturation buffer (vanilla's extra layer between eating and hunger depletion) — the task brief named only `Player.Hunger`/`Player.Exhaustion`, and no gameplay consumer needs the extra state yet.
- No eat-duration/animation window — consumption is immediate on intent, same simplification level as the existing block-place and projectile slices.
- No generic `AttributeSystem` or item-component pipeline — food identity is one small static table, following the same pattern as `RecipeRegistry`/`CreativeCatalog` data tables already in the codebase.
- Exhaustion is currently driven only by sprinting (the single existing authoritative signal, `Player.IsSprinting`). Jump/mining/damage exhaustion costs are deferred — no test or vertical slice currently depends on them.

### Diagnostics

Registered the fixed `tick.system.hunger` timing metric (`ServerRuntimeDiagnostics`), same shape as every other fixed per-system timing. No new dynamic/per-player metrics — consistent with Phase X's decision to avoid per-entity diagnostics without a concrete question to answer.

### Validation

9 new focused unit tests (`HungerSystemTests.cs`): food consumption + inventory decrement, full-hunger no-op, creative no-consume, sprint exhaustion threshold, starvation damage cadence, creative starvation immunity, well-fed regen cadence, low-hunger no-regen, respawn reset. Full suite: 642/642 passing.

### Pattern repeating from Phase X

The intent → tick → replicate → persist shape used by block-place, attack and projectile is now used a fourth time by eating, with no new plumbing required — this is the strongest signal so far that the bounded-intent pattern is a stable primitive, not something to prematurely name or abstract.

---

## XI.2 — Armor (closed)

### What landed

```text
ItemStackRequest (drag helmet into armor slot)
  -> InventorySystem ISR pipeline (Transfer/Swap, unchanged machinery)
  -> InventorySlotArea.Armor / PlayerInventory 4-slot area
  -> ArmorMitigation.Apply (the one new step between DamageSource and HealthState)
  -> Protocol replication (InventoryContent window 120 + MobArmorEquipment) + World persistence (ar: key)
```

Armor equip/unequip reuses the *existing* generalized ISR pipeline (`InventorySlotReference` /
`InventorySlotResolver` / `InventoryContainerMap` / `InventorySystem.Apply`) instead of a parallel
path — the same machinery that already serves hotbar, bag, cursor, craft grid and chest slots. Armor
is simply a sixth `InventorySlotArea`. This is the concrete evidence the resolver-based slot design
from Phase IX/X was already general enough; no new indirection was added to accommodate it.

Concrete additions:

| Concern | Implementation |
|---|---|
| Armor identity + protection | `Gameplay/ArmorItems.cs` — name→(slot, points) table, same shape as `FoodItems` |
| Damage integration | `Gameplay/ArmorMitigation.cs` — one pure function, single call site in `PlayerDamage.Apply` |
| Slot storage | `PlayerInventory` gains a 4-slot `_armor` array + Get/TrySet/Snapshot/Restore/Pack/Load, mirroring the existing bag methods exactly |
| Slot-type guard | `InventorySystem.CanPlaceInArmorSlot` — a helmet can only land in the helmet slot; every other area is untouched |
| Peer visibility | `Gameplay/ArmorFanout.cs` (mirrors `ChestLidFanout`/`FloorDropFanout`) + new `MobArmorEquipmentPacket` (manual, Tier B — `NetworkItemStackDescriptor` fields aren't scaffoldable, same as the existing `MobEquipmentPacket`) |
| Own-client UI | `InventoryProtocol.SendArmorContent` — window 120, same shape as the existing window-0/window-124 sends |
| Persistence | `IChunkStorage.PutArmorAsync/GetArmorAsync` + `ar:{uuid}` key, `World.PersistArmor/TryLoadArmor` — a third instance of the *already-established* "one uuid-keyed store per player-state concern" pattern (inventory, playerdata, now armor) |
| Death/respawn | `PlayerInventory.Clear()` now also clears armor; `FloorDropFanout.TryDropDeathLoot` deposits equipped pieces before clearing; `MovementSystem`'s respawn resync sends the (now-empty) armor window |

### Architectural review (requested before closing)

**1. Stayed a concrete extension, not a framework.** No `AttributeSystem`, `ModifierFramework`, or
stats engine exists anywhere in this change. `ArmorMitigation.Apply` is one function with one
formula (4%/point, capped 20 points) and one caller (`PlayerDamage.Apply`); it is not a pipeline,
has no registration, and nothing else in the codebase calls it. `ArmorItems` is a flat data table,
not a component system — identical shape to `FoodItems` (Phase XI.1) and `RecipeRegistry` (pre-existing).

**2. Persistence boundary re-examined, kept where established.** Armor is player state, and it now
lives behind `IChunkStorage` under a `uuid`-keyed key (`ar:`), exactly like `inv:` (inventory) and
`pd:` (playerdata) already do. This is *not* a new boundary introduced to accommodate armor — it is
the third use of a boundary the codebase already chose twice. The alternative (folding armor into the
existing inventory blob) was considered and rejected: it would have required changing `SlotBlob`'s
fixed-length format or `PlayerInventory`'s 36-slot contract, both load-bearing in several existing
tests and call sites — a bigger, riskier diff than adding one more key of the same shape.

**3. `ArmorFanout` reviewed for prematurity — justified by real duplication, not by anticipation.**
It has exactly one method and two call sites (`InventorySystem.Apply` after an armor-touching ISR,
`FloorDropFanout.TryDropDeathLoot` after death clears armor). Both call sites need the identical
"describe 4 armor slots, push to every other online peer" sequence; factoring it out once avoids
duplicating that sequence, it does not anticipate a third consumer. No shared base class or dispatch
mechanism was added — it is not the start of a fanout framework, just one more concrete helper next
to `ChestLidFanout`/`FloorDropFanout`/`PlayerVisibility`.

### Gaps found and disposition

- No item-durability or armor-damage-on-hit modeling — out of scope for this slice; nothing currently reads it.
- Body slot (5th `MobArmorEquipmentPacket` field, used by mobs like llamas) is wired but always empty for players — correct for the current player-only scope.
- Fall/void/starvation bypass rules are asserted by test but not exhaustively vanilla-parity-checked against every damage cause; only the causes the codebase currently produces (`Melee`, `Projectile`, `Generic`, `Fall`, `Void`, `Starve`) were considered.

### Diagnostics

No new fixed metric was added. Armor mitigation runs inline inside the already-instrumented
`tick.system.inventory` window (ISR apply) and the already-instrumented damage call sites; it added
no new hot-path allocation pattern distinct from what those systems already do. No question currently
requires a dedicated "damage mitigated" gauge — deferred until one does.

### Validation

13 new focused unit tests (`ArmorTests.cs`): equip via transfer, unequip, wrong-slot rejection,
swap-into-armor rejection, full-diamond-set mitigation math, unarmored full damage, void bypass,
death drop + clear, respawn resync, armor blob round-trip, reconnect through `World` storage, and
two wire-level late-join tests (decoded `MobArmorEquipmentPacket` bytes, both join directions) using
the same RakNet-frame-decode technique as `HealthReplicationTests`. Full suite: 655/655 passing.

### Pattern repeating from Phase XI.1 / Phase X

The **resolver-based slot area** (`InventorySlotArea` + `InventorySlotResolver` + `InventoryContainerMap`)
turned out to be the load-bearing abstraction already in place — adding armor required exactly one
new enum case plus mechanical switch arms, not a redesign. The **uuid-keyed `IChunkStorage` entry**
(inventory → playerdata → armor) and the **small named fan-out helper**
(`ChestLidFanout`/`FloorDropFanout`/`ArmorFanout`) are now each used three times; if a fourth
player-state blob or a fourth peer-visibility push shows the same shape, that repetition — not
anticipation — would be the trigger to name the pattern explicitly. Not yet: three is still
"the same small idea, copied," not evidence of a missing shared abstraction.

## XI.3 — Effects (closed)

### What landed

```text
/effect command (or a future item-use path)
  -> Player.SubmitEffect(EffectIntent)
  -> EffectSystem.Tick: apply/refresh/clear, expire, Poison -> PlayerDamage.Apply, Regeneration -> HealthState.Heal
  -> MobEffect packet (self) + Protocol/World replication already built for health
```

Effects reuse the same intent → GameLoop-system → concrete mutation shape as Food (XI.1) and the
same "damage source → concrete step → HealthState" shape as Armor (XI.2). The trigger for this first
slice is a `/effect` command (mirroring vanilla's own `/effect give`/`/effect clear` and the existing
`gamemode` command's structure exactly) rather than a potion item — no potion item, brewing, or
splash-projectile pipeline exists yet, and inventing one only to exercise Effects would have been
scope creep beyond "validate the model."

| Concern | Implementation |
|---|---|
| Effect identity | `Gameplay/PlayerEffect.cs` — `EffectType` enum (2 members), `ActiveEffect` record struct, `EffectIntent` record struct |
| Storage | `Player._effects` — a plain `Dictionary<EffectType, ActiveEffect>`, GameLoop-owned like other Player collections (no lock; single tick-thread writer) |
| Intent handoff | `Player.SubmitEffect`/`TryConsumeEffectIntent` — overwrite-latest single slot, identical shape to `SubmitGameMode`/`TryConsumeGameMode` |
| Trigger | `/effect give <type> [duration] [amplifier] [target]` / `/effect clear [target]`, registered in the existing `CommandCatalog`/`CommandRuntime` — no new command infrastructure |
| Tick | `Gameplay/Systems/EffectSystem.cs` — one more concrete `IGameSystem`; Poison and Regeneration are two explicit `switch` branches, not a dispatch table |
| Damage integration | Poison ticks through the same `PlayerDamage.Apply` chokepoint as everything else, using a new `DamageSource.Magic` (bypasses armor, vanilla parity) |
| Healing | Regeneration calls the `HealthState.Heal` path added in XI.2 |
| Replication | New `MobEffectPacket` (0x1c, Tier A — fully attribute-scaffolded, no `NetworkItemStackDescriptor` complication) sent only to the owning player; peer-visible particles were not required by anything currently in scope |
| Death | `PlayerDamage.Apply`'s death branch sends one `Remove` per active effect and clears them — vanilla parity, and keeps the client HUD honest |

### Requested boundary review — EffectType vs. Bedrock wire ids

`EffectType` is declared with its members' underlying values set directly to the Bedrock wire effect
ids (`Regeneration = 10`, `Poison = 19`). This is a deliberate shortcut, not an accident:

- **Why acceptable now:** there are exactly two members, and every place that needs the wire id casts
  the enum directly (`(int)intent.Type`). A separate domain↔wire lookup table would be pure ceremony
  over two cases with no ambiguity between them.
- **What is preserved conceptually:** `EffectType` still means *domain/gameplay effect identity*; the
  numbers happen to be borrowed from the wire vocabulary for convenience, not because the domain is
  defined in terms of the protocol. `ActiveEffect`, `EffectIntent`, `EffectSystem` and `PlayerDamage`
  never depend on the numeric value meaning anything beyond "this case" — only `EntityProtocol.SendMobEffect`
  reads it as a wire id.
- **When this stops being acceptable:** if effect count grows enough that some domain effects have no
  1:1 Bedrock counterpart (a custom effect, or one Zenith wants to represent differently from how the
  client renders it), or if the same domain effect ever needs a different wire id depending on protocol
  version, the direct-value shortcut breaks and an explicit `EffectType -> wire id` mapping (same shape
  as `ItemPalette` or `ArmorItems`) becomes the correct next step. That is a mapping table, not a
  behavior framework — still consistent with "no abstraction before real pressure."

### Requested boundary review — ActiveEffect stayed data, not behavior

`ActiveEffect` is a `readonly record struct` with three fields and one derived predicate
(`HasExpired`). It has no `Apply`/`Tick`/`Remove` methods, is not a base class, and nothing implements
or extends it. All effect *behavior* (what Poison actually does, what Regeneration actually does)
lives as two concrete `case` branches inside `EffectSystem.TickActiveEffects` — exactly the
`ActiveEffect -> effect processing -> concrete gameplay mutation` shape asked for, not
`ActiveEffect -> IEffect.Tick() -> polymorphic dispatch`.

### Abstractions considered and rejected

- **`IEffect`/`EffectBase` with virtual `Apply`/`Tick`/`Remove`** — rejected. Two concrete branches in
  one `switch` are shorter, more obviously correct, and easier to test than a two-implementation
  interface hierarchy would be.
- **Generic `EffectType -> wire id` mapper** — considered, rejected for now (see above); the two-member
  enum's direct numeric values already serve that purpose without ceremony.
- **A modifier/attribute system feeding Speed into `MovementSystem`** — explicitly out of scope per the
  brief; `MovementSystem` has no server-side speed concept to modify (movement is client-authoritative
  AuthInput, validated but not simulated), so adding Speed now would mean building a movement-speed
  consumer first. Deferred until a real product requirement forces that consumer to exist.
- **Peer-visible effect particles (broadcasting `MobEffect` to observers, not just self)** — deferred;
  nothing currently in Zenith's vertical slices needs another player to see someone else is poisoned.
  Adding it is one more `SendMobEffect` loop over `online`, trivial to add the day a concrete
  requirement appears — not worth guessing at now.
- **A new persistence layer for effects** — rejected outright per the brief's instruction. See below.

### Persistence decision (asked explicitly, not assumed)

**Effects do not persist across reconnect or restart.** Reasoning:

- Zenith's own `Player.Hunger` — the closest existing "vital" precedent from this same phase — is
  *also* not persisted today (no storage key, resets to the default on every new session). Effects
  are the same shape of transient vital state; making them persist while Hunger does not would be an
  inconsistency, not a feature.
- Vanilla Bedrock/Java *do* persist potion effects in the player's saved NBT, but Zenith's persistence
  boundary (Phase XI.2's review) is "player state goes through `IChunkStorage` under a uuid key" —
  extending that to a third/fourth concern (Hunger, then Effects) is a reasonable near-term direction,
  but doing it for Effects alone, ahead of Hunger, would be solving the problem in the wrong order and
  duplicating effort once Hunger's persistence lands.
- If/when Hunger gets a persistence key, Effects should reuse the exact same `ar:`/`inv:`/`pd:`-shaped
  mechanism at that point — not invent a separate one now.

Death already clears effects and notifies the client, so the only observable transience is
disconnect/reconnect and process restart, both intentionally accepted for this slice.

### Diagnostics

Registered the fixed `tick.system.effect` timing metric, same shape as every other fixed per-system
timing. No dynamic/per-player/per-effect metric was added — "active effect count" or "tick cost per
effect type" don't currently answer a question anyone is asking; they can be added the moment they do.

### Validation

10 new focused unit tests (`EffectTests.cs`): apply, refresh (duration + amplifier), natural expiry,
clear, Poison periodic magic damage that bypasses armor, Poison's floor-at-1-HP rule, Regeneration
periodic healing capped at max health, death clearing every active effect, a dead player rejecting new
effect intents, and the transient/no-reconnect-persistence decision. Full suite: 665/665 passing
(one unrelated pre-existing flake was observed twice in `Tools`/`CreativeCatalog`'s static load under
parallel test execution — reproduces on both `MovementOverwriteTests` and `PlayerVisibilityJoinTests`
runs unmodified by this phase, passes in isolation and on rerun; out of scope for XI.3, flagged for a
separate fix).

### Pattern repeating from XI.1 / XI.2

The **bounded intent → concrete `IGameSystem` → existing damage/heal/replication chokepoint** shape
has now been used a fifth time (food, block-place, attack, projectile, and now effects), and the
**command → overwrite-latest intent** shape used by `gamemode` was reused verbatim for `effect`. No
new plumbing was needed for either. This is now three phases running the same two shapes without
needing to name them as a framework — still "the same small idea, applied again," not evidence of a
missing shared abstraction.

### Closing question

**Did Effects reveal a real missing abstraction, or are concrete implementations still sufficient?**

Concrete implementations are still sufficient. Effects added exactly one new wire packet, one new
`IGameSystem`, one new intent, and two `switch` branches — every one of them is a direct reuse of a
shape the codebase already had (intent handoff, `IGameSystem`, `PlayerDamage.Apply`, `HealthState.Heal`,
command registration). Nothing about implementing Poison and Regeneration required guessing at a third
effect's shape in advance. The two watch points going forward are the ones already named above (the
`EffectType`↔wire-id shortcut, and Hunger/Effects persistence ordering) — both are documented decisions
with a stated trigger for revisiting them, not open risks.

## XI.4 — Experience (closed)

### What landed

```text
Player-caused mob kill (melee via ApplyPlayerAttacks, or projectile via ProjectileSystem)
  -> ZombieSystem/SkeletonSystem.TryApplyDamage detects death + DamageSource.OwnerRuntimeId
  -> Player.AddExperience (PlayerExperience level-up math)
  -> SendDefaultAttributes (minecraft:player.level / minecraft:player.experience — already-existing wire fields, previously frozen at 0)
  -> World.PersistPlayerData (pd: blob, now v2)
```

Unlike Food (a new item-use path) and Effects (no existing trigger, so a `/effect` command was added),
Experience's natural trigger already existed in full: Zenith's mob combat (Phase III/IX) already
computes `DamageSource` with an attacker's `OwnerRuntimeId` for the projectile path, and already
detects death via `result.CausedDeath` inside `ZombieSystem`/`SkeletonSystem.TryApplyDamage`. Awarding
XP is one call at the exact point loot is already dropped — no new gameplay entry point was needed.
The one gap closed to make this uniform: `ApplyPlayerAttacks`'s melee path was sending plain
`DamageSource.Melee` (no attacker identity) where the projectile path already sent
`DamageSource.Projectile(ownerRuntimeId)`; switching melee to the existing `DamageSource.MeleeFrom(id)`
factory (unused until now) closed that gap and made both kill paths equally attributable.

| Concern | Implementation |
|---|---|
| Level math | `Gameplay/PlayerExperience.cs` — three pure static functions (`PointsToNextLevel`, `AddPoints`, `Progress`); vanilla-parity thresholds, no state |
| Player state | `Player.ExperienceLevel`/`ExperiencePoints`, mutated only via `AddExperience` (gain) or `SetExperience` (login hydrate) |
| Gain trigger | `ZombieSystem`/`SkeletonSystem.AwardKillExperience` — reads `DamageSource.OwnerRuntimeId`, credits whichever online player matches; a flat `KillExperience = 5` constant per mob, not a loot-table |
| Replication | `UpdateAttributesPacket.CreateDefaults` / `EntityProtocol.SendDefaultAttributes` extended with `experienceLevel`/`experienceProgress` params (optional, default 0) — filled in the two already-wired wire attributes that were previously hardcoded to 0 at every one of Health/Hunger's existing 6 send sites |
| Persistence | `PlayerDataBlob` gains a `Version2` (adds 2×i32 after the existing v1 layout); `World.PersistPlayerData`/`TryLoadPlayerData` thread the two new fields; old v1 blobs still load correctly with level/points defaulting to 0 |

### Scope cuts (explicit, not oversights)

- **No XP orb entity.** Vanilla drops pickupable orbs on kill and on death; Zenith's mobs currently
  loot-drop directly into the floor-drop store with no "orb" concept, and building a new pickup-able
  actor type for this alone would be exactly the kind of premature system the phase brief warns
  against. XP is credited directly to the killer, no pickup step.
  - Consequently: **death does not reduce or drop experience.** Vanilla resets level and drops orbs on
    death; that mechanic depends on the orb entity above. Points/level simply survive death unchanged
    — a deliberate, documented simplification, not a bug.
- **No mining/smelting/trading/breeding XP sources.** Only the mob-kill path was wired, matching "start
  with the minimum needed to validate the model." Every one of those other vanilla XP sources already
  has (or will have) its own concrete gameplay path in Zenith; each can award XP the same one-line way
  the day it exists.

### Diagnostics

No new fixed metric. XP math runs inline inside the already-instrumented `tick.system.zombie`/
`tick.system.skeleton` windows on the one tick a kill happens — no steady-state cost was added.

### Validation

17 new focused unit tests (`ExperienceTests.cs`): level-threshold table, single-level and multi-level
rollover math, progress fraction, `Player.AddExperience` wiring, melee kill awarding XP (both Zombie
and Skeleton), projectile kill awarding XP, an unattributed kill (`DamageSource.Void`) granting nothing
without throwing, persistence round-trip through `World`, reconnect through the same storage boundary,
and death leaving experience untouched. `PlayerDataBlobTests.cs` gained a `Version2` round-trip case
and every existing call site was updated for the two new out-params. Full suite: 683/683 passing.

### Pattern repeating from XI.1 / XI.2 / XI.3

The **"read attacker identity off `DamageSource`, act at the existing death-detection point"** shape
required zero new plumbing — it is the same chokepoint Armor's mitigation and Effects' Poison already
use (`PlayerDamage`/`TryApplyDamage`), just read from the other side (the killer, not the victim). The
**focused two-field extension to an already-versioned blob** (`PlayerDataBlob` v1→v2) is the same shape
`SlotBlob` used for its own v1→v2 migration — this is the second time a "add a version, keep old data
readable" blob extension has been the right move in this codebase, not the first.

## Phase XI closing summary

**What repeated naturally as gameplay grew (four sub-phases, four different feature shapes):**

1. **Bounded network/command intent → one `IGameSystem` → concrete mutation → replication → (sometimes) persistence.** Used by food, armor equip/unequip, effect apply/clear, and — in spirit — the mob-kill XP path (intent-free, but the same "one system, one mutation, one replicate" shape). This is now Zenith's default answer to "how does a new player action reach the world," five/six times over across two phases (Phase IX/X had it first).
2. **`PlayerDamage.Apply` as the single funnel for anything that changes Player health**, with each phase adding exactly one more step to the pipeline (Armor: mitigation; Effects: another damage source; Experience: reading the *source* for attribution) without ever turning it into a generic pipeline/middleware chain.
3. **Small, flat, named data tables for domain-to-wire identity** (`FoodItems`, `ArmorItems`) or **pure math with no state** (`ArmorMitigation`, `PlayerExperience`) — never a component/attribute system, even as the number of these tables grew to four.
4. **Uuid-keyed `IChunkStorage` entries, one per player-state concern**, extended a second time in this phase alone (armor's new `ar:` key, then experience folded into the *existing* `pd:` key via a versioned blob) — proof the boundary generalizes without needing a generic "player state store."
5. **Small named fan-out helpers** (`ArmorFanout`) next to the pre-existing `ChestLidFanout`/`FloorDropFanout` — still one-off concrete helpers, still exactly two call sites each, never a shared base.

**Abstractions considered and rejected across all of XI:** `AttributeSystem`, `ModifierSystem`/stats
engine, `EffectFramework`/`IEffect`/`EffectBase`, generic `EffectType`↔wire mapper, `EquipmentFramework`,
item-component system, damage/mitigation pipeline, XP orb entity/pickup system, a new player-state
persistence layer. Every one of these was evaluated against a concrete need in front of it and rejected
because two or three concrete cases were cheaper, clearer, and fully testable without it.

**What stayed deliberately simple:** two-effect enum with wire ids as literal values; flat kill-XP
constant instead of a loot table; no death-XP-loss mechanic; no peer-visible effect particles; Hunger
still unpersisted (a gap inherited from XI.1, not solved incidentally by XI.4's persistence work — it
would have been solving the wrong phase's problem).

**Is there a missing shared abstraction after four sub-phases?** No single repeated shape has hit a
third case that would justify a name change: the "uuid-keyed store" boundary has now been *used* a
fourth time (inventory, playerdata, armor, and armor's-into-playerdata-extension) but each use is still
one field addition or one new key, never a rewrite of the boundary itself. If a fifth or sixth piece of
player state needs the same treatment, that is the point to write an ADR naming the pattern explicitly
— not before.
