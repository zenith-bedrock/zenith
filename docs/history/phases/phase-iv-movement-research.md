# Phase IV — Entity movement research and runtime opportunities

## Scope and method

Phase III terminou com o movimento concreto do Zombie caracterizado. Esta fase é uma auditoria,
não uma implementação. Foram comparados o runtime atual do Zenith com as referências disponíveis
em `D:\Development\bedrock`: PocketMine-MP, BetterAltay, Dragonfly, Basalt, Endstone e
PowerNukkitX. As referências foram usadas para identificar propriedades úteis, não para importar
hierarquias, frameworks ou APIs.

The preserved ownership is:

```text
Gameplay decides.
Movement applies the decision to authoritative state.
Protocol projects the decided result.
Packets serialize.
RakNet transports.
Diagnostics observes.
```

## Zenith today

### Player movement

`InGameAuthInputHandler` validates and submits the latest movement input. `MovementSystem`, owned
by the GameLoop, applies the accepted pose, fall damage and void transition. It keeps a dirty pose
set and uses `EntityProtocol.SendMoveAbsolutes` to batch peer updates. Respawn and explicit server
teleports use `MovePlayer` teleport projection. The current player path is therefore server-owned
at the gameplay boundary, but it accepts the client's authoritative position model rather than
re-simulating a full player physics step.

### Concrete actors

`ZombieSystem` now follows:

```text
target decision
  -> desired horizontal displacement
  -> bounded feet/head/support block probe
  -> apply PositionX/PositionZ
  -> ActorInterest reconciliation
  -> MoveActorAbsolute projection
```

The Zombie has no velocity, gravity, AABB movement, step height, slope, fluid or entity-to-entity
collision. Its local probe is intentionally enough for a flat full-block floor and the tested
wall. `SkeletonSystem` remains a ranged decision loop; `ProjectileSystem` has explicit velocity,
gravity, lifetime and point/radius hit checks. `GravitySystem` is a separate concrete falling-block
simulation with vertical velocity and landing resolution.

`World.GetBlock` is the current world query. `Geometry.Aabb` and `World.EntityHitboxes` already
exist for pure overlap/proximity and placement checks, but are not yet a general movement solver.
`EntityProtocol` transmits a decided actor pose and does not calculate movement.

### Strengths

- Single-writer GameLoop ownership is explicit.
- Behavior, movement validation and replication are visible in the concrete system.
- Player movement is batched and dirty-checked.
- Falling blocks and projectiles already demonstrate velocity/lifetime ownership without a shared
  entity framework.
- ActorInterest prevents replication to observers whose known chunks are irrelevant.

### Limitations

- Zombie movement is a cell occupancy rule, not a continuous swept-volume collision test.
- There is no general desired-versus-applied displacement result containing collision flags or
  `onGround` state for non-player actors.
- Step-up/down, partial block shapes, slopes, fluids and entity collision are unsupported.
- Zombie sends an absolute movement projection to known viewers after each tick; no actor-level
  movement dirty threshold is currently characterized.
- The player path and mob path use different synchronization assumptions, which is correct today
  but should remain explicit.

## Mature reference comparison

### Dragonfly

Relevant files: `dragonfly/server/entity/movement.go`, `entity/ent.go` and
`server/world/viewer.go`.

Dragonfly has the clearest separation of the audited projects:

1. a behavior chooses or changes velocity;
2. `MovementComputer` applies gravity and drag;
3. `CheckCollision` gathers block bounding boxes around the swept entity box;
4. collision is resolved axis-by-axis (Y, then X, then Z), zeroing the blocked velocity axis;
5. a `Movement` value holds the resulting position, velocity, rotation and ground state;
6. `Movement.Send` separately notifies viewers of movement and velocity, while viewers can
   interpolate movement.

The block model returns one or more collision boxes, so partial shapes are naturally supported.
The cost is a significantly richer world/entity contract and allocations/iteration around the
candidate block boxes. Dragonfly does not make pathfinding part of this movement computer.

### PocketMine-MP

Relevant files: `pocketmine/src/entity/Entity.php`, especially `updateMovement`, `tryChangeMovement`
and `move`.

PocketMine stores motion, gravity, drag, an AABB, `onGround` and a configurable `stepHeight` on
the entity. `move` first resolves requested Y, then X and Z against collision cubes, and can try a
second stepped candidate when horizontal movement is blocked. It updates collision flags, ground
state and zeroes blocked motion components. `updateMovement` uses position/rotation/motion
thresholds, sends `MoveActorAbsolute` only when movement changed, and sends actor motion separately.
Teleport is a distinct projection path.

This is a mature continuous collision model, but it couples a broad entity base to world and
network lifecycle. The step candidate and block-shape support are useful properties; the complete
base class is not a reason to create a Zenith `Entity` hierarchy.

### BetterAltay

BetterAltay is a PocketMine-derived Bedrock server. Its `src/pocketmine/entity/Entity.php` and
`Mob.php` retain the same broad pattern: motion/gravity, AABB collision, `onGround`, step height,
movement thresholds and separate teleport handling. The important independent finding is not a new
algorithm; it confirms that these concerns recur in practical Bedrock runtimes. BetterAltay should
not be counted as an independent design win over PocketMine.

### Basalt

Relevant files include `Basalt/Entities/Entity.cs`, `Basalt/Player/Player.cs` and
`Basalt/Player/Traits/PlayerChunkRenderingTrait.cs`.

Basalt keeps entity velocity as runtime/persisted state, resets velocity during player teleport,
and records teleport tick state for input handling. Its player chunk rendering trait maintains a
visible actor set: actors outside the configured radius are hidden and actors entering the radius
are spawned. This is a useful lifecycle/visibility observation, but the checked code does not
provide a complete reusable mob collision solver comparable to Dragonfly or PocketMine.

### Endstone

Relevant files include `endstone/src/bedrock/entity/components/movement_interpolator_component.h`,
`block_source.h` and the replay/movement correction components.

Endstone exposes native Bedrock concepts rather than implementing an independent server physics
engine. The native movement interpolator has bounded lerp steps (maximum three), while the block
source exposes collision-shape queries and the replay-state policy decides when server correction
is required. This demonstrates that client synchronization has distinct interpolation/correction
state, but those internals are not portable primitives for Zenith's server runtime.

### PowerNukkitX

`PowerNukkitX/src/main/java/org/powernukkitx/utils/Utils.java` contains collision checks over
`AxisAlignedBB`, including a cached-block variant and compact collision-direction information.
This is evidence that broad collision queries and cached local block access can matter in a Java
runtime. It does not justify a Zenith cache before a query benchmark demonstrates repeated lookup
pressure.

## Comparison by concern

| Concern | Zenith | Mature pattern | Decision for Zenith now |
|---|---|---|---|
| Desired movement | Zombie computes a bounded target displacement | Behavior/AI chooses velocity or displacement | Keep concrete per behavior |
| Applied movement | Cell passability probe | Swept AABB with axis resolution | No general solver without a second real use case |
| Acceleration | Projectiles/falling blocks only | Motion + drag + gravity fields | Preserve per-actor concrete state |
| Gravity | FallingBlock and Projectile | Shared force step in entity runtimes | Do not add Zombie gravity until a falling mob requires it |
| Collision | Full-cell feet/head/support | AABB against block collision shapes, often step retry | Smallest future primitive is a bounded displacement result, not a framework |
| Step height/slopes/fluids | Unsupported | Entity-specific collision parameters and block shapes | Defer; no current workload requires them |
| Entity collision | Point/radius checks for projectile | Usually separate broad-phase/entity overlap query | Defer until gameplay requires actor-vs-actor blocking |
| Replication | ActorInterest + absolute actor moves | Viewers plus movement/velocity/teleport channels | Measure dirty/threshold policy separately |
| Interpolation | Client receives absolute moves | Viewer receives movement and sometimes velocity; client interpolates | Do not simulate interpolation server-side |

## Phase III evidence revisited

The existing 10-observer benchmark remains the relevant runtime evidence:

| Workload | 1,000 actors | Avg tick | Movement fan-out | Egress |
|---|---:|---:|---:|---:|
| Zombie idle | 1,000 | 2.909 ms | 0 | 140 datagrams / 93,692 B |
| Zombie direct | 1,000 | 4.351 ms | 23,800 | 884 datagrams / 1,041,385 B |
| Zombie obstacle | 1,000 | 4.378 ms | 25,815 | 923 datagrams / 1,105,951 B |

The obstacle probes increased this sample's allocations by roughly 4% and movement fan-out by
roughly 8.5% relative to direct movement. That is characterization, not proof that a generalized
collision solver would be faster. The visible scale cost is still actor projection/fan-out, while
the simulation itself remains below the Phase III pressure threshold.

## Possible improvements

### Easy — measure and reduce unnecessary movement projection

Add no framework. First characterize whether a concrete Zombie (and later Projectile) sends a move
when its applied pose is unchanged or below a small protocol-safe threshold. If confirmed, retain a
last-projected pose in the owning concrete system and skip redundant `MoveActorAbsolute` calls.
This directly targets the observed replication cost and preserves the Gameplay → Protocol boundary.
It must be benchmarked before and after; it must not suppress an enter-interest spawn or a required
teleport/correction.

### Easy — keep collision queries explicit and countable

If future terrain tests show repeated `GetBlock` cost, measure the number of queried cells and the
time spent per concrete workload first. A local runtime-id-to-shape lookup or a small per-tick
scratch buffer may then be justified. Do not introduce a global collision cache speculatively.

### Medium — a concrete applied-movement result

If a second actor needs continuous movement, introduce the smallest internal value that reports
`applied displacement`, `horizontal collision`, `vertical collision` and `onGround` for that
specific path. It can sit beside the concrete system initially. It should be promoted to a shared
primitive only after Zombie, Projectile or a falling actor demonstrate the same invariant.

### Large — general collision/physics/navigation

Swept AABB against arbitrary block shapes, step-up/down, slopes, fluids, actor broad-phase and
navigation are all legitimate future work, but each is a separate pressure. They would require
new correctness tests and workload evidence. They are not a reason to create `MobBase`,
`NavigationSystem`, `PhysicsEngine`, `EntityWorld` or an ECS.

## Audit answers

1. **Is current movement adequate?** Yes for the characterized flat-world Zombie, Projectile,
   FallingBlock and current player slice. It is intentionally incomplete for general terrain.
2. **Next real bottleneck?** Replication fan-out and movement projection, not the representation of
   simulation state. The Phase III benchmark is the evidence.
3. **Smallest high-value primitive?** First, a measured concrete movement dirty/threshold decision
   for actor projection. If simulation pressure appears later, an internal applied-displacement
   result with collision flags is the next candidate.
4. **What stays concrete?** Target selection, desired movement, attack/range rules, gravity policy,
   actor lifecycle and the decision to spawn/remove/replicate.
5. **What must not be abstracted without pressure?** Mob hierarchy, AI/navigation framework,
   physics engine, broad collision cache, interpolation framework, scheduler/jobs, ECS and
   networking/visibility abstractions.

## Closure

Mature Bedrock runtimes converge on a few properties—velocity where motion is continuous, AABB or
shape-based collision when terrain becomes non-trivial, explicit ground/collision results, and
separate movement/teleport/velocity projection—but they do not imply one universal architecture.

For Zenith, the smallest evolution is to measure and, if justified, suppress redundant concrete
actor movement projections. The current cell probe remains appropriate for the tested Zombie. A
continuous applied-movement primitive should be added only when another real actor requires the same
collision contract. Phase IV therefore closes with knowledge and a prioritized pressure list, not
runtime code.
