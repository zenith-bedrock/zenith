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

| actors | mode | avg | p95 | alloc/tick | datagrams | bytes | move fan-out |
|---:|---|---:|---:|---:|---:|---:|---:|
| 100 | global | 3.638 ms | 5.696 ms | 1,149,850 B | 6,190 | 6,893,030 | 198,000 |
| 100 | interest | 0.778 ms | 0.966 ms | 136,171 B | 798 | 783,524 | 19,800 |
| 500 | global | 20.315 ms | 29.062 ms | 5,725,399 B | 26,870 | 34,431,750 | 990,000 |
| 500 | interest | 3.621 ms | 5.120 ms | 654,362 B | 2,866 | 3,537,396 | 99,000 |
| 1,000 | global | 49.998 ms | 69.468 ms | 11,447,525 B | 51,780 | 68,851,390 | 1,980,000 |
| 1,000 | interest | 7.876 ms | 10.876 ms | 1,302,316 B | 5,357 | 6,979,360 | 198,000 |

This is a Projectile churn baseline, not a Zombie/Skeleton AI-mix claim. It proves current
replication/interest cost remains dominant: global 1,000 actor p95 exceeds the 50-ms budget,
while one relevant observer remains within it. Do not interpret it as ECS evidence.

## Post-implementation audit

| Concern | Zombie | Skeleton | Projectile | Conclusion |
|---|---|---|---|---|
| Identity, transform and replication lifecycle | yes | yes | yes | repeated, but still concrete stores and projection paths |
| Health | yes | state only | no | not a common runtime requirement yet |
| Target selection | nearest player + chase | nearest player + range hold | none | gameplay-specific decision |
| Attack effect | direct melee damage | request a projectile | collision damage/lifetime | not interchangeable AI or combat behavior |

The evidence supports keeping repeated bookkeeping local while `ActorInterest` remains the small
policy seam already proven by Phase F. It does not justify a mob base class, actor hierarchy,
`CombatSystem`, AI framework, behavior tree, attribute system or ECS. The bottleneck measured at
scale is observer projection/wire fan-out; interest reduces it substantially and ECS would not
solve that cost.

## Deferred observations

Real repetition exists in identity/position/lifetime/projection bookkeeping, but Zombie melee and
Skeleton range still differ in decision state, target distance and attack effect. No generic mob,
AI, navigation, behavior tree, entity hierarchy or combat attributes are justified yet.
