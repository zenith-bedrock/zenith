# Phase H — gameplay vertical-slice findings

## Concrete primitives observed

Zombie now has target acquisition, chase, melee range, 20-tick cooldown and authoritative Player
damage. Skeleton is intentionally separate: it holds ranged distance, rotates to a target and
uses a 30-tick cooldown to ask `ProjectileSystem` to create a shot. `ProjectileSystem` remains
the owner of projectile identity, simulation, collision, replication and removal.

The only extracted primitive is `PlayerDamage`: one existing Player death transition reused by a
concrete mob. It owns health feedback, death finalization, loot and respawn handoff; it is not a
combat framework, attribute system or actor interface.

Projectile collision preserves the already-characterized player snowball → Zombie behavior. A
projectile whose owner is not an online Player (the concrete Skeleton case) can instead damage a
Player. This is an explicit Phase-H rule, not player-vs-player combat.

## Validation

- 13 focused `ZombieSystem`, `SkeletonSystem` and `ProjectileSystem` tests pass.
- External two-client Bedrock smokes pass for Zombie health/removal and late join, Projectile
  movement/removal, and reconnect pose.
- The Projectile smoke now identifies the player-fired snowball by origin and velocity, so it
  remains valid when Skeleton shots coexist in the same world.

## Runtime baseline

Production `GameLoop` benchmark, 200 ticks, 10 observers, 2026-08-11:

| actors | mode | avg | p95 | alloc/tick | bytes | move fan-out |
|---:|---|---:|---:|---:|---:|---:|
| 100 | global | 3.630 ms | 5.475 ms | 1,145,850 B | 6,893,030 | 198,000 |
| 100 | interest | 0.653 ms | 0.890 ms | 132,171 B | 783,524 | 19,800 |
| 500 | global | 19.550 ms | 27.122 ms | 5,705,411 B | 34,431,750 | 990,000 |
| 500 | interest | 4.005 ms | 6.458 ms | 634,362 B | 3,537,396 | 99,000 |
| 1,000 | global | 53.610 ms | 74.136 ms | 11,407,504 B | 68,851,390 | 1,980,000 |
| 1,000 | interest | 8.335 ms | 10.869 ms | 1,262,316 B | 6,979,360 | 198,000 |

This is a Projectile churn baseline, not a Zombie/Skeleton AI-mix claim. It proves current
replication/interest cost remains dominant: global 1,000 actor p95 exceeds the 50-ms budget,
while one relevant observer remains within it. Do not interpret it as ECS evidence.

## Deferred observations

Real repetition exists in identity/position/lifetime/projection bookkeeping, but Zombie melee and
Skeleton range still differ in decision state, target distance and attack effect. No generic mob,
AI, navigation, behavior tree, entity hierarchy or combat attributes are justified yet.
