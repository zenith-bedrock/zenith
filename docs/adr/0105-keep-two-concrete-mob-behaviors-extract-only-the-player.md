# ADR §105 — Keep two concrete mob behaviors; extract only the Player death transition

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §105`.

**Choice:** retain `ZombieSystem` and `SkeletonSystem` as separate, tick-owned gameplay systems.
Zombie owns target/chase/melee/cooldown decisions; Skeleton owns ranged distance/aim/cooldown
decisions and requests a concrete Projectile from `ProjectileSystem`, which remains the sole
projectile lifecycle owner. `PlayerDamage` reuses the existing Player health, feedback, death-loot
and respawn transition for concrete non-player damage sources.

**Why:** the two behaviors repeat identity, position, target lookup, cooldown and projection
bookkeeping, but their decision state and attack effects remain materially different. The only
duplicated behavior already shared with movement was Player damage/death finalization; extracting
that path prevents a second copy without claiming an entity or combat framework. The Phase-H
benchmark still identifies observer fan-out as the dominant scale cost, not mob simulation.

**Boundary:** Gameplay decides target, damage and lifecycle. `EntityProtocol` transmits add/move/
health/remove outcomes. Packets serialize; RakNet transports. `PlayerDamage` contains no packet
serialization, target selection, actor storage or replication policy.

**Non-goals:** `LivingEntity`/generic actor hierarchy, generic AI/navigation/behavior trees,
combat attributes/effects/armor, ECS, VisibilitySystem, public actor/plugin API or scheduler.
