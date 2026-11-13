# ADR §107 — Phase XXII expands the ECS roster and replaces the two-species damage dispatch chain

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §107`.

> **Superseded in part by §149:** `IDamageableActor`, `GroundMobCombat`, `DamageableActorCombat` and the per-species stores described here no longer exist; every species is an ECS actor on one replication path.

**Choice:** migrate three more actors — Cow, Skeleton, Spider — onto the ECS runtime established by
§106, chosen to pressure passive/breeding behavior, ECS-to-ECS composition (Skeleton spawning an
ECS Projectile), and a feature-specific post-hit consequence (Spider's poison) respectively.
Replace `ProjectileSystem`'s hardcoded two-branch `if (_zombies.Owns) else if (_minecarts.Owns)`
damage-target chain with `DamageDispatch` (`src/zenith/Gameplay/DamageDispatch.cs`) — a small, fixed
list of `(Owns, TryApplyDamage)` pairs registered once per ECS-damageable species at
composition-root time. Re-evaluate, and deliberately not act on, the `GroundMobCombat` retirement
gate §106 left open. Full detail:
[`docs/phase-xxii-ecs-roster-consolidation-findings.md`](../history/phases/phase-xxii-ecs-roster-consolidation-findings.md),
[`docs/ecs.md`](../ecs.md), [`docs/entities.md`](../entities.md) §12.

**Why now:** §106's migrated slice (Zombie, Minecart, Projectile) proved the ECS pattern but was
deliberately small — three categories sharing mostly the same shape (ground mob, vehicle,
projectile). Phase XXII's job was to find out whether that DX held up across enough *different*
gameplay shapes, and whether the two-consumer damage dispatch chain (explicitly flagged in §106 as
temporary) would need a real design once a third ECS-damageable category arrived. Both questions
needed real migrations to answer, not more design work in the abstract.

**Why `DamageDispatch`, not a generic event bus:** the trigger condition written into `docs/ecs.md`
during Phase XXI was explicit — a third ECS-damageable category is the signal to design a real seam,
not to keep growing the `if`/`else if` chain. Cow/Skeleton/Spider's migration produced five
ECS-damageable species besides Projectile, well past that trigger. `DamageDispatch` is the smallest
design that removes species-name growth from `ProjectileSystem`: a fixed list built once at startup,
each entry a one-line registration, no runtime pub/sub, no reflection. Finding *candidate* targets
remains capability-based (`Query.With(Health, Position)`, unchanged); `DamageDispatch` only answers
"which system's `TryApplyDamage` do I call," never "what happens when I call it" — death
consequences stay entirely feature-owned, exactly as before.

**Why `GroundMobCombat`/`IDamageableActor` were not retired despite reaching roster parity:** the
retirement trigger was never "the migrated count matches the legacy count" — it was "migrating the
stragglers costs less than maintaining two combat helpers." Evaluated honestly against the six
remaining species' actual behavior (Creeper's fuse+explosion, Enderman's damage-triggered
aggro+teleport, Bat's 3D flight, Villager's trade interaction, Golem's boss phases, Fish's swim
predicate), none of it is mechanical port work, and no maintenance-cost signal (a bug fixed in one
combat helper and missed in the other) has appeared. Forcing migration to hit a round number would
have been exactly the "migrate to fill a table" anti-pattern this project has avoided since Phase
XVII. See `docs/ecs.md`'s retirement-gate section for the full per-species accounting and the
updated trigger going forward (migrate a remaining species when gameplay work already needs to
touch it, not on a schedule).

**What was measured:** ECS-core benchmarks (entity lifecycle, component-store query iteration,
runtime-id lookup) re-run after the roster expansion show the same zero-allocation-at-steady-state
result as Phase XXI, at the same 100/1,000/10,000 entity scales — confirming the additional
component stores (`CowState`/`SkeletonState`/`SpiderState`) introduced no regression. A full
mixed-species runtime-load benchmark (Zombie+Cow+Skeleton+Spider+Minecart+Projectile together under
`GameLoop`) was not built this phase — the existing per-species harnesses and the full test suite
(817 tests passing) were judged sufficient evidence for a scope this size; building a new harness
extension for a benchmark whose purpose is compositional validation, not a capacity claim, was
deprioritized. This is recorded honestly, not hidden.

**Boundary, preserved from §106 and unchanged by this ADR:** Gameplay decides → Protocol transmits →
Packets serialize → RakNet transports. `GameLoop` remains single-writer, single-threaded. No
inheritance hierarchy exists anywhere in the ECS or its consumers. `ViewerReconciliation.Sync`
remains the one replication seam for every category. Archetypes, a job scheduler, and reflection-
driven components remain deferred — nothing in this phase's evidence justifies them.

**Non-goals, restated for this specific decision:** this ADR does not extend ECS to the six
remaining legacy species, Player, sessions, inventory, world storage, packets, or transport. It does
not retire `GroundMobCombat`/`IDamageableActor` — see above. It does not build a generic damage/
event framework — `DamageDispatch` is deliberately narrower than that, and the findings doc records
what was explicitly considered and rejected.
