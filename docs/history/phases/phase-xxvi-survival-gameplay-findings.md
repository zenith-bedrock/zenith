# Phase XXVI — Survival Gameplay Loop & World Interaction Polish: findings

Per the phase brief, this is an audit-first phase: connect already-built systems into a coherent
survival loop by fixing what's actually broken, not by building new architecture. This document
records the audit, what was found, what was fixed, and what was deliberately left alone.

## Baseline

- HEAD at start: `9eeef74` (feat: land survival foundation — vitals, death XP reset, tool recipes,
  ore drops) — Phase XXV, already committed to `develop`.
- `dotnet build zenith.sln --no-restore`: succeeded, 0 warnings, 0 errors (before and after this
  phase's changes).
- `dotnet test zenith.sln --no-restore`: 945 in `zenith.Tests` after this phase's additions (937
  inherited + 8 new: 4 `BreakDurationTests` + 4 `MiningProgressionTests`), plus one more in
  `EndermanSystemTests`. No flakes observed.

## Audit method

Reviewed `src/zenith/Gameplay`, `src/zenith/Player`, `src/zenith/World`, `src/zenith/Protocol`,
`src/zenith/Ecs`, and existing docs (`docs/entity-fidelity.md`, `docs/decisions.md`, prior phase
findings) before writing any code, per the brief's own instruction not to re-implement what already
exists. The single most important finding came directly out of that audit, not from guessing.

## XXVI.1 — Mining & progression: the real finding

**A wood pickaxe could mine and drop diamond ore exactly as well as a diamond pickaxe.** The
Phase XXIII-B cross-reference audit had flagged `BreakDuration`'s tool-tier-mismatch formula as
diverging from vanilla, but framed it as **latent**: "Currently a no-op (no registered block has a
tier gap yet), becomes a real bug the moment one is added." That framing was already wrong by the
time it was written — `DigProfiles.cs` already registered every ore (coal through diamond) with
`RequiresCorrectToolForDrops=true` and `HarvestTool=ToolKind.Pickaxe`, and `DigProfile` had no tier
field at all. Since `BreakDuration.IsHarvestable` only ever compared `ToolKind`, *any* pickaxe —
including the wood ones Phase XXV's own recipes made craftable — satisfied the harvest check for
every ore, with only a break-*speed* difference by tier. This wasn't a hypothetical edge case; it
was live and exploitable the moment a player could craft any pickaxe and reach any ore, which Phase
XXV's tool recipes directly enabled.

**Fixed:** `DigProfile` gains `MinHarvestTier` (default `ToolTier.None`, i.e. no gate — every
non-ore profile is unaffected). Ore profiles now require: coal → any pickaxe (Wood+, vanilla's own
floor); iron/copper/lapis → Stone+; gold/diamond/redstone → Iron+. `BreakDuration.IsHarvestable`
checks tier alongside kind; `BreakDuration.BreakTicks` no longer resets speed to 1.0× when kind
matches but tier doesn't, so a wood pickaxe on diamond ore now digs at wood-pickaxe speed (still
faster than bare hands) but drops nothing — matching vanilla's actual rule instead of either "full
access" (the bug) or "hand speed" (the old, never-actually-triggered mismatch branch). Full rationale
in `docs/decisions.md` §110.

**Validated** end-to-end through the real `BlockEditSystem` break path (not just unit-level
`BreakDuration` calls), in `MiningProgressionTests.cs`:
- Wood pickaxe breaks diamond ore → block clears, nothing lands in inventory.
- Iron pickaxe breaks diamond ore → the diamond item lands in inventory (via `BlockLoot`).
- Stone pickaxe harvests iron ore → drops the ore block itself (no furnace yet — unchanged from
  Phase XXV, correct).
- Wood pickaxe harvests coal ore → gets the coal item.

**Everything else in the mining pipeline was already coherent** and needed no changes: `RecipeRegistry`
already has the full wood→stone→diamond chain (iron/gold correctly excluded pending a furnace, per
Phase XXV); `BlockLoot` already covers every no-smelt ore (coal/diamond/redstone/lapis); the
kind-vs-tier distinction the brief called out ("how fast can this tool break this block?" vs. "does
this block drop its resource with this tool?") was already a real, separate concept in the code
(`IsEffective` vs. `IsHarvestable`) — this phase closed the one place they'd drifted apart from
vanilla, it didn't invent the distinction.

## XXVI.2 — Survival state: audited, not changed

Full re-read of `HungerSystem.cs`/`PlayerDamage.cs`/`Player.cs`'s vitals fields confirmed the Phase
XXV loop is internally coherent: eating restores saturation (capped at hunger) → exhaustion depletes
saturation before hunger → hunger below the starvation threshold deals periodic damage → hunger above
the regen threshold heals. Exhaustion sources are exactly three (sprint, mining, damage — confirmed
by grep, no undocumented fourth source exists). No changes made here; the loop doesn't show a
concrete defect, only the two gaps Phase XXV already named as deliberately deferred (walking
exhaustion needs `MovementSystem` distance tracking that doesn't exist; per-food saturation
modifiers are a bigger data-modeling change than one constant ratio). Adding either now would be
exactly the "system without evidence the current model needs it" the brief warns against — nothing
in this phase's audit surfaced a case where the current model behaves wrong.

## XXVI.3 — Combat feel: one real fix, rest confirmed already correct

Audited targeting, movement, attack, and feedback across every combat-capable species (Zombie,
Skeleton, Spider, Creeper, Enderman, Golem — Cow/Villager/Bat are non-combat and confirmed as such).
Full per-species behavior is now in `docs/entity-behavior-matrix.md` rather than duplicated here.

**Fixed: Enderman never set `Yaw` at all** — a confirmed gap already named in
`docs/entity-fidelity.md`'s species matrix ("never sets yaw at all — teleport doesn't orient the
actor toward anything"). `EndermanSystem.TickAggro` now sets `enderman.Yaw` toward its aggro target
via the existing `LookMath.YawTowards` helper (already used by Zombie's chase turning — no new
formula introduced) on every aggro tick; since Enderman moves by instant teleport rather than a
walked chase, snap-facing matches its own movement model rather than borrowing
`GroundMobMovement`'s gradual per-tick turn, which is built for walking mobs. Covered by
`EndermanSystemTests.Aggro_tick_orients_the_enderman_toward_its_target`.

**Confirmed already correct, no change needed:**
- Hit-invulnerability frames exist and apply uniformly (`HealthState.cs`, `InvulnerabilityTicks=10`,
  a Phase XXIII-B fix — before it, neither players nor mobs had any i-frame concept at all).
- Hurt-animation (`ActorEvent`/`SendHurt`) and death feedback fire correctly for every species
  (confirmed PARITY across the entity-fidelity species matrix).
- `GroundMobCombat`/`DamageableActorCombat`'s shared melee funnel (reach-checked intent consumption,
  one authoritative damage/health/death/loot/XP path) already takes attack distance and damage as
  **per-call-site parameters**, not hardcoded shared constants — species are already independently
  tunable through this shared mechanic, exactly the kind of "mechanics shared, numbers per-species"
  split the brief's example table implies without saying outright.
- Knockback exists on every melee-capable species via the shared combat path; Creeper's explosion
  correctly uses its own separate impulse rather than the melee knockback formula (different physical
  event, not a bug).

**Confirmed real but out of scope for this phase** (already documented, not rediscovered as new):
Enderman's stare-triggered aggro and water/rain avoidance are still missing (vanilla's two signature
Enderman mechanics); Villager has no flee-on-hit behavior; Skeleton's retreat/approach kiting
thresholds are unmeasured against vanilla (LOW confidence, not HIGH-confidence bugs). None of these
showed up as "the result feels wrong" in the audit the way the tool-tier gap and Enderman orientation
did — they're pre-existing, already-tracked gaps, not new findings, and fixing them wasn't evidenced
as urgent by anything this phase's audit turned up.

## XXVI.4 — Entity behavior matrix

New: [`docs/entity-behavior-matrix.md`](../../entity-behavior-matrix.md). Catalogs target/movement/
attack/knockback/special per species with file:line evidence, distinct from `entity-fidelity.md`'s
wire-fidelity matrix (that one asks "is the packet right"; this one asks "is the decision right").
Explicitly documents what it *rules out* — no shared attack-constants table, no AI/behavior-tree
framework — since both were plausible-sounding generalizations the audit data didn't support.

## Rejected abstractions

- **A shared `AttackDistance`/`AttackDamage`/`AttackCooldownTicks` table across species.** Several
  species happen to share the same numbers (`2.25`/`20` appear repeatedly), but that's vanilla-
  adjacent tuning converging, not evidence of duplicated logic — each constant is already
  independently overridable per file, and `GroundMobCombat`/`DamageableActorCombat` already own the
  actual shared mechanics.
- **A generic block-hardness/tier-requirement framework** beyond the one new `DigProfile` field.
  Four ore tier-bands is not enough repetition to generalize past a per-block field; `MinHarvestTier`
  extends the existing sparse-table pattern (`HarvestTool`/`EffectiveTool` already worked this way)
  rather than introducing a parallel one.
- **A generic Attribute framework** (health/hunger/movement/attack unified). Not newly proposed this
  phase, but re-confirmed rejected per ADR §109 — nothing in this phase's survival-state audit
  surfaced pressure to revisit that.
- **AI/behavior-tree framework.** Every species' decision loop is small (~30-80 lines) and none show
  branching complexity that would pay for a generic framework — see the entity behavior matrix's own
  closing section for the explicit "what this rules out" note.

## New pressure points (evidence-based, not proposals)

- **`ToolTier` has no `Gold` member and no gold/iron tool recipes exist** (Phase XXV's deliberate
  furnace gap, unchanged). If a furnace ever lands, `RecipeRegistry`/`Tools.cs` both already have the
  seams to extend (curated tool names for gold/iron already exist in `Tools.CuratedNames`, just
  unregistered as tier constants and unreciped) — noted here so a future phase doesn't have to
  rediscover where those seams are.
- **Walking generates no exhaustion** because `MovementSystem` doesn't track per-tick distance moved.
  If a future phase adds distance tracking for *any* other reason (anti-cheat movement validation is
  already a separately-documented gap in `entity-fidelity.md`), walking exhaustion becomes a small
  addition on top of it rather than its own feature — worth doing together, not sequentially.
- **Enderman's stare-aggro/water-avoidance gap is now the only entity-fidelity item in the "signature
  mechanic entirely missing" category** for the six combat species — everything else audited this
  phase is either PARITY or a documented SIMPLIFIED timing/threshold gap, not a missing mechanic.

## Manual validation checklist

Not run against a real Bedrock client in this environment — logic and persistence are unit-tested,
real-client feel is not. Separate from automated validation per the brief's own instruction.

**Mining:**
- Confirm a wood pickaxe visibly fails to yield diamond/redstone/gold when mining those ores (block
  still breaks, no item appears), while an iron+ pickaxe does.
- Confirm coal still drops for a wood pickaxe (should be unaffected by this phase).
- Confirm break-speed still visibly scales by tool tier even on ore the tool can't harvest (wood
  pickaxe should still crack diamond ore faster than bare hands, just yield nothing).

**Combat:**
- Aggro an Enderman (attack it) and confirm it visibly turns to face the player after each teleport,
  rather than facing a fixed/stale direction.
- Confirm hit feedback (red flash / hurt animation), invulnerability window, and knockback all still
  feel correct across Zombie/Skeleton/Spider/Creeper/Golem — no regression expected, but this phase
  didn't touch their code paths and hasn't been client-verified this phase either.

**Regression coverage from prior phases** (re-verify still intact, not new to this phase): hunger/
saturation/exhaustion loop, death/XP reset, reconnect vitals persistence — see the Phase XXV findings
doc's own checklist; nothing in this phase should have changed their behavior, and the full automated
suite (945 tests) stays green.

## Definition of done — status

- Survival progression feels coherent: mining now enforces a real tier gate; the rest of the loop was
  already coherent per the XXVI.2 audit. ✅
- Mining/tool progression works end-to-end: verified via `MiningProgressionTests` through the real
  break path, not just unit-level `BreakDuration` calls. ✅
- Survival state behaves consistently: audited, no defect found, no change needed. ✅
- Combat interactions feel less placeholder-like: Enderman orientation fixed; everything else audited
  and confirmed already correct or already tracked as a known, non-urgent gap. ✅
- Entity behaviors documented: `docs/entity-behavior-matrix.md`. ✅
- Client validation checklist exists: above. ✅
- No unnecessary framework introduced: one new struct field (`MinHarvestTier`), one new field
  mutation (Enderman `Yaw`), zero new abstractions. ✅
