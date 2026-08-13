# Phase X — Player gameplay completeness findings

## Initial state audit

The Player already crosses the first complete gameplay loop through concrete existing paths:

```text
network input
  -> bounded Player intent
  -> GameLoop gameplay decision
  -> Health/Inventory/World mutation
  -> Protocol projection
  -> Packets/RakNet
```

Verified current authorities:

| Concern | Concrete implementation |
|---|---|
| Player state | `Player` owns position, pose, `HealthState`, hunger HUD value, inventory, selected hotbar and lifecycle flags |
| Player damage | `PlayerDamage` plus `MovementSystem`/mob systems apply authoritative damage and relay health |
| Player attack | attack intent is tick-consumed by concrete `ZombieSystem` and `SkeletonSystem` reach checks |
| Item use | block placement, chest interaction and the projectile vertical slice consume distinct bounded use paths |
| Death | `PlayerDamage` finalizes death, releases chest state, decides inventory drops and sends death/respawn protocol |
| Respawn | `MovementSystem` consumes the one-shot request, restores health, resets pose/fall state and resynchronizes inventory |
| Persistence | player pose/game mode, inventory and chest contents use the existing storage boundary |

The tested invariant is concrete: a lethal transition must not partially commit its decided loot;
inventory and floor-drop destination planning happens before authoritative removal. Respawn is
one-shot and health is restored only by the GameLoop owner.

## Reference comparison

Local implementations under `D:\Development\bedrock` were inspected for behavior only:

- PocketMine and BetterAltay separate health/death/respawn from inventory windows and expose broad
  item, armor, effect and event surfaces. Those extension surfaces are not evidence for a Zenith
  framework.
- Dragonfly keeps player combat and item use as concrete session/gameplay paths while item
  entities and inventory remain separate concerns. Its behavior matches Zenith's existing
  intent/tick boundary more closely than a generic combat abstraction would.
- Basalt and Endstone expose richer player state, armor and effect APIs. They confirm possible
  future content, not a current need for an AttributeSystem or public event bus.

## Gaps found and disposition

The audit found no missing primitive required to close the first Player cycle. The following are
explicitly deferred because they lack a concrete product requirement in the current runtime:

- hunger simulation: `Hunger` is currently a fixed HUD value, with no exhaustion/regeneration rule;
- armor, effects and experience: no authoritative gameplay consumer currently needs them;
- food consumption: no item-use protocol/gameplay path is currently required by the vertical slices;
- player-versus-player attack: current combat proof is Player → concrete Zombie/Skeleton;
- reconnecting a dead player: session lifecycle and death/respawn handshake are covered separately,
  but no new reconnect state machine is justified.

These are gaps in content, not evidence for a generic framework. They should be implemented as
concrete slices when a real gameplay requirement appears.

## Diagnostics and performance

Existing fixed diagnostics cover tick, movement, inventory, mob and floor-drop work, while runtime
health records allocations, GC, actor counts, datagrams and bytes. No per-player labels or dynamic
metrics were added. The relevant benchmarks and smoke flows already exercise inventory/container
work, combat actors, death loot, respawn packets and persistence.

## Decision

**Close Phase X with the current concrete Player model.**

The first gameplay cycle is complete for the features currently present: Player state, concrete
combat against mobs, item use through blocks/projectiles, authoritative death/loot, respawn and
persistence. Do not create `PlayerFramework`, `CombatSystem`, `AttributeSystem`, `EffectSystem`,
public event bus, ECS, entity hierarchy or data-driven item pipeline.

The next implementation should be selected by a concrete missing player feature (for example food,
armor or experience), and should add only the smallest state and ownership path that feature proves
necessary.

## Validation

The existing focused contracts cover health transitions, fall damage, death loot atomicity, respawn
packet shape, player persistence, inventory conservation, player attack against Zombie/Skeleton,
Projectile damage, chest lifecycle and reconnect-related session boundaries. Full build/test
validation was rerun for this closure.
