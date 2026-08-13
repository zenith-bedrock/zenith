# Phase XV — Entity Behavior Expansion & Abstraction Validation

## Pre-coding audit

Re-read `docs/phase-xiv-gameplay-expansion-findings.md` and
`docs/phase-xiv-ground-mob-combat-consolidation.md`, then re-inspected `IGroundMob`,
`GroundMobCombat`, `ZombieSystem`, `SkeletonSystem`, `CowSystem` before writing a line of new code.

**1. Does Creeper fit `IGroundMob`?** Yes, without modification. `IGroundMob` asks for
`EntityId`/`RuntimeId`/`Position`/`Health`/`IsActive`/`ApplyDamage`/`Remove` — all seven are exactly
what a Creeper needs regardless of how it dies. The interface says nothing about *how* a Creeper
takes damage or what happens after, so "explodes instead of just falling over" was never a fit
question for the interface at all.

**2. Does Enderman fit `IGroundMob`?** Yes, same reasoning. Teleportation is a way of changing
`PositionX/Y/Z` — already plain mutable properties on the concrete type — not a new capability the
interface would need to expose.

**3. Which parts are shared lifecycle?** Exactly what Phase XIV.1 already extracted: a player's
melee hit landing, health replication, death detection, loot deposit, XP credit, death replication,
store removal. Confirmed unchanged by this phase — no new parameter, no new hook, no new overload
was needed on `GroundMobCombat` to support either mob.

**4. Which parts must remain completely custom?** Target acquisition, fuse timing, explosion,
teleportation, aggro triggering and decay, retaliation. All of it stayed on `Creeper`/`CreeperSystem`
and `Enderman`/`EndermanSystem` respectively — see below for what, if anything, turned out to
overlap with an existing mob after all.

---

## Creeper vertical slice

```text
Player weapon hit (unrelated to fuse)
        |
        v
GroundMobCombat.TryApplyDamage  (shared — ordinary kill, loot, XP)

Player enters detection range
        |
        v
CreeperSystem: acquire target -> approach (GroundMobMovement.CanStandAt) -> ignite range -> fuse
        |
        v
Fuse completes
        |
        +--> GroundMobCombat.TryApplyDamage(self, lethal, DamageSource.Generic)   [shared: loot, no XP — no attacker]
        +--> PlayerDamage.Apply(nearby players, area falloff damage)              [NOT shared — Creeper-only]
        +--> explosion particle + sound broadcast                                 [NOT shared — Creeper-only]
```

The self-explosion death **reuses `GroundMobCombat.TryApplyDamage` unmodified** — a lethal
self-inflicted hit is still exactly "apply damage, detect death, deposit loot, credit XP if
attributable, replicate removal, remove from store." `DamageSource.Generic` carries no
`OwnerRuntimeId`, so `MobKillReward.AwardExperience` — also untouched — correctly credits nobody.
This was not special-cased; it fell out of the existing contract for free (confirmed by
`Explosion_does_not_credit_experience_since_no_player_caused_it`... folded into the main explosion
test in the final version, same assertion).

Area damage to nearby players is genuinely new and was **not** forced into `GroundMobCombat` — it
mutates *player* health via the ordinary `PlayerDamage.Apply` path, which `GroundMobCombat` has
never touched and still doesn't.

**Damage → Death** and **Fuse → Explosion → Area damage** were confirmed to be separate concepts,
exactly as the brief predicted — but not disjoint: the *end* of the explosion chain rejoins the
death chain at the self-inflicted-damage step, rather than needing its own parallel removal/loot/XP
logic.

---

## Enderman vertical slice

```text
Player weapon hit lands
        |
        v
GroundMobCombat.TryApplyDamage (shared)  ->  provoke: set AggroTarget + AggroTicksRemaining

Not aggro'd: periodic passive teleport (random nearby valid landing spot)
Aggro'd:     periodic teleport toward attacker, melee retaliation on cooldown once in reach
        |
        v
AggroTicksRemaining reaches 0 -> target cleared, back to passive
```

No `TeleportComponent`, `MovementComponent`, or `AbilitySystem` was created. Teleportation is one
private method (`TryTeleport`) that reuses `GroundMobMovement.CanStandAt` for the *destination*
only — there is no path, no step, no incremental movement at all, confirming
`GroundMobMovement`'s scope (position validity) is independent of *how* a mob arrives at a position.
Aggro is two `int`/`long?` fields on the concrete `Enderman` type and a countdown check in
`EndermanSystem.Tick` — not a state machine type, not a generic "aggro system."

**A real bug was caught by the test suite here**, worth recording precisely because it's the kind of
thing this phase was meant to surface: the first version of the aggro countdown decremented
`AggroTicksRemaining` and then unconditionally ran the aggro tick, so a target only actually cleared
one tick *after* reaching zero. `Aggro_expires_and_the_target_is_cleared_once_it_counts_down_to_zero`
failed against the real implementation (not a placeholder), was fixed in `EndermanSystem.Tick`, and
re-verified. This is evidence the tests exercised real logic, not just structure.

---

## Architectural validation

### `IGroundMob` — still represents only shared state

No method was added. `Creeper`'s `TargetPlayerRuntimeId`/`IsFusing`/`FuseStartedTick` and
`Enderman`'s `AggroTargetRuntimeId`/`AggroTicksRemaining`/`NextPassiveTeleportTick`/`NextAttackTick`
are all `internal` members on the concrete types, invisible to and unused by `IGroundMob` or
`GroundMobCombat`. Nothing resembling `Attack()`/`Move()`/`Teleport()`/`Explode()`/`Think()` exists
anywhere in the interface.

### `GroundMobCombat` — still only damage/health/death/loot/XP/removal/replication

Zero new parameters, zero new overloads, zero new methods were added to `GroundMobCombat` by this
phase. Both new mobs call the exact same two methods (`ApplyPlayerMeleeAttacks`, `TryApplyDamage`)
that `Zombie`/`Skeleton`/`Cow` already called. It did not grow into a `MobBehaviorSystem` or
`CombatFramework` — it could not, structurally, since it has no branch on mob type or "kind of
death."

### New repeated behavior found: `GroundMobMovement`, now with three real consumers

Phase XIV.1 predicted the exact trigger for this: *"a fourth mob also needs movement... the point at
which this same document's own reasoning says to extract it."* Creeper needed to walk toward a
target to reach ignite range — the same bounded local step-validity check `Zombie` and `Cow` already
had, now genuinely a third instance. `GroundMobMovement.CanStandAt` was extracted (position
validity only — no stepping, no heading, no assignment) and `ZombieSystem`/`CowSystem`'s own
`TryMove` were both updated to call it, so the claim of "three real consumers" is backed by all
three, not just the newest one. Enderman then confirmed the extraction was scoped correctly by
reusing the *same* function for an entirely different purpose (teleport-destination validation, not
step validation) — a coincidence-resistant sign that "is this position safe to occupy" was really
the shared concept, not "how a mob decides to move."

**Targeting (`FindOrAcquireTarget`-shaped nearest-player search with a retained target) was
considered and explicitly not extracted.** Zombie and Creeper both have it now — a second instance,
not a third — and Skeleton's targeting is structurally different (one-shot LINQ scan, no retained
target, plus a minimum-range check Zombie/Creeper don't need). Two similar-looking implementations
that aren't actually identical is exactly the "don't force it" case Phase XII's fan-out review
already established a precedent for.

### Stores — still `List<T>` + `TryAdd`/`Remove`/`Active`, still not worth a shared type

`CreeperStore` and `EndermanStore` are two more ~10-line copies of the same wrapper
(`ZombieStore`/`SkeletonStore`/`CowStore` already had it). Now **five** instances of an
identical, trivial shape. This was noticed, not ignored: a generic `ActiveActorStore<T>` would save
perhaps 40 lines total across five files. It was not extracted in this phase because (a) the brief
for this phase scoped the validation to `IGroundMob`/`GroundMobCombat` specifically, and (b) unlike
the combat bookkeeping or movement validity, the store wrapper has never been the source of a bug,
a hard-to-read method, or a cross-cutting change — it is not costing anything today beyond raw line
count. Flagged here as the next candidate if a sixth mob makes the count six, not acted on now.

### Performance

No new diagnostics metric was added — the existing fixed-per-system `Timing` pattern already
answers "tick cost" the moment a system is registered (`tick.system.creeper`,
`tick.system.enderman` now exist alongside the other seven). No allocation profile change was
introduced beyond what Phase XIII already reviewed and accepted for `Zombie`/`Skeleton`/`Cow`:
`Creeper`/`Enderman` use the same `.Active.ToArray()` per-tick snapshot and the same small
`online.FirstOrDefault`/nearest-player scan LINQ calls Zombie already had — structurally identical
allocation shape, not a new pattern. No benchmark run was added for the two new mobs specifically;
Phase XIII's 1000-actor Zombie benchmark (1–2.5ms avg tick, 3–5 Gen0 collections per 200 ticks) is
the closest existing evidence that this general shape of mob tick is not a measured problem, and
nothing about Creeper's fuse timer or Enderman's teleport cooldown changes that shape (both are
`ulong`/`int` comparisons, no new allocations per tick). Per the brief, this was not re-measured
because there was no reason to suspect a regression — optimizing without evidence would have been
the actual violation here.

---

## Validation

`dotnet build zenith.sln` — clean. `dotnet test src/zenith.Tests/zenith.Tests.csproj` —
**716/716 passing** (702 before this phase + 6 `CreeperSystemTests` + 8 `EndermanSystemTests`).
`ZombieSystemTests`, `SkeletonSystemTests`, `CowSystemTests`, `ProjectileSystemTests`,
`ExperienceTests`, `DeathLootAtomicityTests` all pass unchanged — regression confirmed for every
existing mob, including through the `GroundMobMovement` rewiring of `Zombie`/`Cow`'s `TryMove`.

Creeper coverage: bootstrap spawn, target acquisition + fuse start, full fuse duration → explosion
(loot + area damage + no XP), defuse on the player leaving ignite range, early melee kill (loot + XP,
zero explosions), late-join replication.

Enderman coverage: bootstrap spawn, passive teleport with no player ever in range, a landed hit
provoking aggro, aggro'd teleport-and-retaliate against the attacker, aggro decay/clearing (the bug
described above), melee kill (loot + XP), out-of-reach no-op, late-join replication.

---

## Final questions

**1. Did Creeper fit the current lifecycle model?**
Yes, completely, for the "taking damage and dying" half. Its distinguishing behavior (fuse,
explosion, area damage) is additive on top of the lifecycle model, not a modification of it — and
the self-explosion path actively *reused* the model rather than working around it.

**2. Did Enderman fit the current lifecycle model?**
Yes, same answer. Its distinguishing behavior (teleport movement, damage-triggered aggro,
retaliation) never touches `IGroundMob` or `GroundMobCombat` at all.

**3. Did `GroundMobCombat` remain small?**
Yes — unchanged, zero lines added, zero new members, still exactly the two methods from Phase XIV.1.

**4. Did any new repeated behavior appear?**
Yes, one: the movement-validity check, now with three genuine consumers instead of two, extracted
as `GroundMobMovement.CanStandAt` and backed by all three real callers (`Zombie`, `Cow`, `Creeper`)
plus a fourth, differently-shaped consumer (`Enderman`'s teleport-destination check) that confirms
the extraction generalized on the right axis. Targeting and store wrapping were both *also* found
repeated, and both were explicitly left alone — repetition alone wasn't the bar, "identical logic
with a clear extraction shape that reduces real complexity" was, and only movement validity cleared
it this time.

**5. Are we closer to needing more architecture, or did concrete systems continue scaling?**
Concrete systems continued scaling. Five ground mobs now exist; the shared surface between them
(`IGroundMob`, `GroundMobCombat`, `GroundMobMovement`) is 120 + 22 = 142 lines total, none of it
mob-specific, none of it AI, and it did not need to grow by a single member to accommodate two mobs
with genuinely novel death and movement behavior. The store-wrapper duplication is the one place
mild pressure is visibly building (5 instances of the same 10 lines) — noted as the next thing to
watch, explicitly not acted on, per the brief's own instruction not to extract ahead of a measured
need.
