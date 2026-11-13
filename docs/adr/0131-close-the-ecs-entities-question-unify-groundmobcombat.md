# ADR §131 — Close the ECS/entities question: unify `GroundMobCombat`/`DamageableActorCombat`'s duplicated logic, keep the storage split

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §131`.

> **Superseded in part by §149:** `IDamageableActor`, `GroundMobCombat`, `DamageableActorCombat` and the per-species stores described here no longer exist; every species is an ECS actor on one replication path.

**Context:** External review asked whether the Zenith's ECS-for-6-species/OOP-for-6-species split
was "genuinely solving a measurable problem or becoming a second entity architecture." `docs/ecs.md`
already had a detailed, twice-evaluated answer to this — Phase XXI's initial migration and Phase
XXII's explicit "Outcome A: keep both helpers" revisit, backed by a per-species table of exactly
what's non-mechanical about each of the 6 unmigrated species (Creeper's area-damage-on-death,
Enderman's damage-triggered aggro, Bat's 3D flight, Villager's trade inventory, Golem's boss phases,
Fish's swim validity). That analysis is correct and stays — forcing a migration to hit a roster-parity
number was already explicitly rejected once, for good reason, and re-litigating it without new
evidence would be worse, not better.

What the review's underlying question actually deserved was verification, not another round of
agreement or disagreement in the abstract: is the storage split actually *free*, or is it quietly
costing something the project's own "no maintenance-cost signal has appeared" claim doesn't capture?
Read `GroundMobCombat.TryApplyDamage<TMob>` and `DamageableActorCombat.TryApplyDamage` side by side —
they were byte-for-byte identical in sequence (loot-capacity guard → apply damage → replicate
health/hurt → early-return if alive → deposit loot or throw → award XP → replicate death/removal →
destroy) since Phase XXI, differing only in how a mob's state was read: `IDamageableActor` object
properties on one side, three `ComponentStore<T>.TryGet` calls on the other. `HealthState` being a
class (not a struct) meant this was mechanically closeable without any write-back complexity — both
backends already hold a reference to the same authoritative instance.

**Choice:** Extracted the shared sequence into `DamageableActorCombatCore` (new file), parameterized
by a `DamageableActorView` — a small readonly struct either backend builds cheaply (object property
reads on one side, three `TryGet` calls on the other) — plus a `destroy` callback for the one step
that's genuinely different per backend (`mob.Remove(); removeFromStore(mob);` vs.
`EntityRuntime.DestroyActor`). `GroundMobCombat` and `DamageableActorCombat` are now thin adapters:
build a view, call the shared core, done. **Every public signature on both classes is byte-for-byte
unchanged** — none of the 12 species' call sites (`ZombieSystem`, `MinecartSystem`, `CowSystem`,
`SkeletonSystem`, `SpiderSystem`, `ProjectileSystem` on the ECS side; `CreeperSystem`,
`EndermanSystem`, `BatSystem`, `VillagerSystem`, `GolemSystem`, `FishSystem` on the OOP side) needed
any change at all.

This closes the actual risk the retirement gate was watching for — "no bug fixed in one helper and
missed in the other, no feature written twice" — not by hoping it keeps not happening, but by making
it structurally impossible: there is now exactly one place this sequence exists. The storage split
stays exactly as documented and justified; only the duplicated *behavior* on top of it is gone.

**Status (ago 2026):** Shipped — `DamageableActorCombatCore.cs` (new), `GroundMobCombat.cs`,
`DamageableActorCombat.cs`. No call sites changed, no new tests needed — the existing 1049-case
suite (which already exercises combat for all 12 species) stayed green with zero changes, exactly
the signal expected for a pure logic-deduplication refactor with unchanged public contracts.
`docs/ecs.md`'s retirement gate section updated to point here.
