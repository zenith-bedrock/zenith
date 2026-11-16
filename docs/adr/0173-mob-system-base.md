# ADR §173 — One life cycle for every ECS mob species

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §173`.

**Context:** Nine species systems (zombie, spider, cow, fish, bat, villager, creeper, golem, enderman) each carried the same 60 to 100 lines: the same fields, the same constructor wiring, a hand-written `Owns`, five replication counters, a `Tick` that despawned, reconciled viewers, applied player attacks, pruned and replicated moves, and a `TryApplyDamage` that forwarded to `MobCore`. A new species meant copying one of them and registering `(Owns, TryApplyDamage)` by hand. The shared work already lived in `MobCore`; what was duplicated was the glue around it.

**Choice:**
- `MobSystem<TState>` (`Gameplay/Entities/MobSystem.cs`) is the base of every species whose pass fits one shape. It owns the `MobCore`, the replication, the state `ComponentStore<TState>`, `Owns`, the counters, the stable per-tick copy of the entity list, spawning through `SpawnWith`, and the template `Tick`: begin replication, (with a player online) for each live entity despawn, reconcile viewers, apply player attacks, call `Think`, reconcile again if still alive; then `AfterThinking`, prune, replicate moves.
- A species writes only what is its own: constants, its `Think`, its spawn alias, and overrides where it differs: `ReconcileViewers` (creeper tells a newcomer about a lit fuse), `ApplyPlayerAttacks` (enderman is provoked by a hit), `TryApplyDamage` (golem retaliates, villager records the attack), `AfterThinking` (cow breeding), `BeginTick`, `ReplicatesMoves` (enderman teleports) and `DespawnRadius`.
- `IDamageableSpecies` (`Owns`, `TryApplyDamage`) is what `DamageDispatch` registers: `damage.Register(zombie)`.
- Inheritance rather than a pipeline of delegates: the life cycle is fixed and the variation is a handful of named hooks; a species cannot reorder or skip the steps by accident. The base stays small and holds no behaviour of its own beyond that order.
- Not migrated: skeleton (its pass runs without a player online and shoots after reconciling), minecart (a vehicle, not a mob) and projectile. They stay as they were until their shape fits.

**Why:** The nine files shrink by roughly a third each, the order of the life cycle is written once, and a new species is a state struct, a `Think` and one registration line.

**Invariants:** Existing species tests pass unchanged (every species' despawn, viewer, attack, damage and dispatch behaviour); the system order in `GameplayCompositionTests` is unchanged.

**Non-goals / Deferred:** Migrating skeleton and minecart, and moving species registration (spawner, dispatch, system list) into one per-species declaration.

**Status (November 2026):** Shipped. `Gameplay/Entities/{MobSystem,DamageDispatch}.cs` and the nine species systems.
