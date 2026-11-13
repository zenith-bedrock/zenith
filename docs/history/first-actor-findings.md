# First actor vertical slice (Zombie) — findings

> Moved out of the decision log: evidence collected before ADR §99–§102 (the ECS gate). See [ADR §99](../adr/0099-ecs-is-an-evidence-gated-world-actor-decision.md).


### FIRST_ACTOR_FINDINGS

The first living actor is intentionally a concrete `Zombie` plus a small `ZombieStore` while we
collect evidence for a future actor-runtime decision. The store owns only active authoritative
state; `ZombieSystem` owns spawn, targeting, movement, damage and lifecycle decisions; `HealthState`
owns health transitions; `EntityProtocol` owns Bedrock projection. The store is scaffolding, not a
template for a permanent `SkeletonStore`/`CowStore` family.

| Concern | FallingBlock | FloorDrop | Zombie | Classification |
|---|---|---|---|---|
| Runtime identity | entity/runtime id | item actor id | entity/runtime id | shared lifecycle candidate |
| Position | continuous fall position | cell position | continuous XZ + yaw | similar, not yet generalized |
| Velocity | gravity-specific | none | chase step only | actor-specific |
| Health | none | none | composed `HealthState` | actor-specific |
| Lifetime | short-lived settle/void | delay/age/despawn | active/dead/remove once | similar |
| Active tick | `GravitySystem` | `FloorDropSystem` | `ZombieSystem` | shared execution shape |
| Spawn/remove | gameplay + `EntityProtocol` | gameplay + `EntityProtocol` | gameplay + `EntityProtocol` | repeated plumbing |
| Replication | add/move/remove | add/remove/take | add/health/move/remove | similar, wire-specific |
| Late join | chunk emission | `ColumnSend` | tick-owned viewer catch-up | similar |
| Visibility | known chunk / online | known chunk / online | online viewer set | unknown future boundary |
| World interaction | block support | pickup reach | simple player proximity | actor-specific |

The repeated fields/operations are recorded as evidence only. No `EntityStore`, registry, hierarchy,
ECS, or generic visibility abstraction is introduced until a materially different second actor shows
which parts are actually common. The current vertical slice also records the DX cost explicitly:
one concrete state file, one tick system, one composition-root registration, one attack handoff in
`Player`/handlers, and one actor projection in `EntityProtocol`/`AddActorPacket`.

**Lifecycle invariant:** the bootstrap slice creates at most one actor; the gameplay owner is
the only writer; an accepted melee swing is reach-validated; lethal health removes the actor once;
late ticks cannot resurrect it. This is a testable behavior slice, not a claim that all future actors
share this storage shape.

**Real-client observation:** two Bedrock clients independently received `add_entity` for
`minecraft:zombie` with the same runtime/unique id, then observed actor movement and a health
attribute update. A protocol-valid `InventoryTransaction` `item_use`/`click_air` probe against a
real client then produced the authoritative health sequence `20 → 16 → 12 → 8 → 4 → 0`, followed
by `remove_entity`. The earlier `missed_swing`-only probe was a harness encoding limitation, not a
server-side damage failure. The two-client probe remains useful for replication observation; the
single-client click-air probe closes the real-client damage/death path.
