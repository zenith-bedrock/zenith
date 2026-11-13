# ADR §132 — Migrate Creeper, Enderman, Golem, Fish onto the ECS — re-derive "non-mechanical" from the actual code, not the per-species summary

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §132`.

> **Superseded in part by §149:** `IDamageableActor`, `GroundMobCombat`, `DamageableActorCombat` and the per-species stores described here no longer exist; every species is an ECS actor on one replication path.

**Context:** §131's table (inherited from Phase XXII) listed all 6 unmigrated species as
non-mechanical to migrate, each for a one-line reason: Creeper's area-damage-on-death, Enderman's
damage-triggered aggro, Golem's boss phases, Fish's swim validity, plus Bat's 3D flight and
Villager's trade inventory. That table was accurate as a summary of *what's distinct* about each
species, but re-reading the actual `Tick`/combat/movement logic (not just the summary) for
Creeper/Enderman/Golem/Fish showed the summary had been doing more work than it should: "has
distinct per-tick behavior" was being treated as equivalent to "requires new ECS infrastructure,"
and for 4 of the 6 it isn't the same claim at all.

**What the code actually showed:**

- `CreeperSystem.Explode` and `GolemSystem.TrySlam` (area damage) already looped
  `PlayerDamage.ApplyCore` over `online` players using world-space coordinates read from the mob's
  own position — neither touched the mob's storage for anything else. An area-damage ability was
  never a missing ECS primitive; it was an ordinary loop that would work identically whether the
  mob's own state lived in a C# object or a `ComponentStore<Position>`.
- `EndermanSystem`'s aggro/teleport state (target, aggro window, teleport/attack cooldowns) and
  `GolemSystem`'s enrage/target-retention state are structurally identical to what `SpiderState`/
  `ZombieState` already solved for Phase XXII: a handful of `long?`/`int`/`ulong`/`bool` fields with
  no cross-species query need. A feature-specific `ComponentStore<T>` is the designed-for case for
  exactly this shape, not an edge case requiring new design.
- `FishSystem.TrySwim` is a movement-validity predicate, structurally the same role as
  `GroundMobMovement.TryMoveHorizontal` — it reads/writes a position and returns whether the move is
  legal; it never cared whether that position lived on a C# object or in `ComponentStore<Position>`.

Bat (no pitch field on `Position`) and Villager (`Wares: List<StackId>`, no ECS inventory analog)
are different in kind: each needs an actual schema addition the migrated species never needed. That
is the real dividing line — not species count, not "how much custom behavior," but "does closing
this require a new component field/type Zenith's ECS doesn't have a slot for yet."

**Choice:** Migrated all 4 onto `EntityRuntime`, following `docs/ecs.md`'s "Adding a new ECS actor"
template — the same shape `SpiderSystem` already used. One feature-specific component per species
(`CreeperState`, `EndermanState`, `GolemState`, `FishState` — all in `src/zenith/Ecs/`), each a
plain struct holding exactly the fields the OOP class used to mark `internal` beyond
Position/Health/Yaw. `VerticalFallSpeed` (Creeper, Golem) was not duplicated — both now reuse
`Velocity.Y` as gravity fall-speed, the Phase XXIX convention every other migrated ground mob
already follows. Combat calls moved from `GroundMobCombat` to `DamageableActorCombat` (both thin
adapters over the same `DamageableActorCombatCore` since §131 — no behavior change, only which
adapter). Species-local logic (fuse/explode, aggro/teleport, enrage/slam, swim) was translated 1:1,
reading/writing through `ComponentStore<T>.GetRef`/`TryGet` instead of object fields — no logic was
redesigned. Each of the 4 was migrated and test-verified independently (Fish, then Creeper, then
Enderman, then Golem — increasing state/ability complexity), not landed as one large change.

Each species also registered into the shared `DamageDispatch` for the first time. This closes a
real, previously undocumented gap: `ProjectileSystem.FindHitDamageableActor` only ever queried ECS
component stores, so arrows could not hit Creeper, Enderman, Golem, or Fish under the old
`IDamageableActor`/`*Store` pattern — only melee worked. One test per species now confirms a
projectile lands damage through the shared dispatch, which was not previously expressible as a test
at all (there was nothing to assert — the hit path didn't exist).

**Status (ago 2026):** Shipped — `CreeperState.cs`, `EndermanState.cs`, `GolemState.cs`,
`FishState.cs` (new); `CreeperSystem.cs`, `EndermanSystem.cs`, `GolemSystem.cs`, `FishSystem.cs`
rewritten to `EntityId`/`EntityRuntime`; `Creeper.cs`, `Enderman.cs`, `Golem.cs`, `Fish.cs` (the OOP
`IDamageableActor` classes and their `*Store` types) deleted; `ZenithServer.cs`'s composition root
updated to construct and register all 4 alongside the rest of the ECS roster. Legacy roster is now
2 species (Bat, Villager), both retained for a genuine schema blocker, not migration backlog. Test
suite: 1049 → 1053 (4 new arrow-hit-dispatch tests, one per species); existing per-species tests
converted from `*Store`/object-field assertions to `ComponentStore<T>` reads with unchanged
observable behavior. `docs/ecs.md`'s Scope, ground-locomotion, and retirement-gate sections updated
to match.
