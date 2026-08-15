# Entity behavior matrix

Gameplay-behavior catalog (target/movement/attack/special), as opposed to
[`docs/entity-fidelity.md`](entity-fidelity.md)'s wire/animation-fidelity matrix — that document
answers "does the client see the right packets," this one answers "what does the mob actually do."
Compiled for Phase XXVI (`docs/history/phases/phase-xxvi-survival-gameplay-findings.md`) from the
live source, not from naming or memory — every cell that names a mechanic has file:line evidence
behind it. Purpose per the phase brief: identify missing behavior and guide future ECS/component
decisions, not to justify a generic AI framework.

Runtime split: **Zombie, Skeleton, Spider, Cow** are ECS-based (own `ComponentStore<T>` under
`src/zenith/Ecs/`, ticked by a matching `Gameplay/Systems/*System.cs`). **Creeper, Enderman, Golem,
Bat, Villager** remain legacy hand-written classes in `src/zenith/Gameplay/` with their own store
class — outside ECS, per ADR §99/§106/§107's evidence-gated migration (only six of the eleven
species were ever migrated; the rest stay put until a concrete capability need, not "completeness").
Fish is passive/no-combat and omitted below.

| Entity | Target | Movement | Attack | Knockback | Special |
|---|---|---|---|---|---|
| Zombie | Nearest player in aggro range; retarget each tick (`ZombieSystem.cs`) | Ground chase, bounded per-tick turn toward target | Melee, `AttackDistance=2.25`, `AttackDamage=4`, `AttackCooldownTicks=20` (`ZombieSystem.cs:23-26`), no windup | `KnockbackImpulse=0.3f`, `KnockbackDecayPerTick=0.5f` (`ZombieSystem.cs:33-34`) | None — no door-breaking, no burn-in-daylight |
| Skeleton | Nearest player in range | Ground chase with ranged-kiting positioning (retreat/approach thresholds unmeasured against vanilla — LOW confidence) | Ranged only, `ProjectileSystem.TrySpawnFromActor` with ballistic arc (`SkeletonSystem.cs:189-214`); `AttackDistance=2.25`/`AttackDamage=4` shared constants exist but arrows are the actual damage path | Not applicable (no melee contact) | Shot cadence not vanilla-verified this phase |
| Spider | Nearest player in range | Ground chase, bounded turn | Melee, `AttackDistance=2.25`, `AttackDamage=2` (weaker per-hit than Zombie "to keep overall threat comparable to a faster mob" — `SpiderSystem.cs:29`), `AttackCooldownTicks=20` | Shared `GroundMobCombat`/ECS combat knockback path | No poison (correct — regular Spiders don't poison in either Java or Bedrock; only Cave Spiders do, which Zenith doesn't have), no wall-climbing |
| Creeper | Nearest player in range | Approaches to fuse range, no retreat-after-ignite behavior | No melee/projectile — AoE explosion only: `FuseDurationTicks=30`, `ExplosionRadius=3f`, `ExplosionDamage=10f` (`CreeperSystem.cs:34-36`) | N/A (explosion applies its own separate impulse, not the shared melee knockback) | Fuse ignite/defuse hysteresis (ignite ≤3 blocks, defuse ≥7 — fixed in Phase XXIII to stop toggle-flicker at the boundary) |
| Enderman | Last attacker only (damage-triggered aggro, `AggroDurationTicks` window) — no stare-triggered aggro | Instant teleport, not a walked chase: passive random short-hop (`PassiveTeleportRadius=8`, every 60-140 ticks) or aggro teleport-toward-target (`AggroTeleportRadius=2`, `AggroTeleportCooldownTicks=20`) | Melee only when in range post-teleport, `AttackDistance=2.25`, `AttackDamage=7` (`EndermanSystem.cs:18-20`) | Shared `GroundMobCombat` path | Teleport (`TryTeleport`, `EndermanSystem.cs:209-230`); **Yaw now set on every aggro tick as of Phase XXVI** (`EndermanSystem.cs` `TickAggro`) — previously never oriented at all, a confirmed entity-fidelity gap. Still missing vanilla's stare-triggered aggro and water/rain avoidance (`docs/entity-fidelity.md:240-241`) |
| Golem | Player who last attacked a Villager (`Player.LastVillagerAttack` provocation, not direct aggro) | Ground chase toward provoker | Melee, `AttackDistance=2.5`, `AttackDamage=6`, `AttackCooldownTicks=20` (`GolemSystem.cs:27-30`) | Shared `GroundMobCombat` path | Provoke-by-proxy (attacking a Villager redirects Golem aggro) |
| Cow | None | Passive wander only | N/A — takes damage via `DamageableActorCombat.ApplyPlayerMeleeAttacks`, never deals it | N/A | No breeding, no feeding (grepped project-wide for `Breed` — zero hits) |
| Villager | None | Passive wander only | N/A | N/A | No flee-on-hit (`docs/entity-fidelity.md:242-243` — vanilla villagers run from their attacker; Zenith's only records the hit for Golem-provoke purposes), no breeding/trading |
| Bat | None | Passive flight/wander | N/A — takes player damage (`BatSystem.cs:63,120-123`), never attacks | N/A | Passive only |

## Reading this alongside the fidelity matrix

`docs/entity-fidelity.md`'s species table already tracks look/turn, hurt/death feedback, and attack
*timing* fidelity at the wire level — this document doesn't repeat that axis. The two together answer
different questions: "is the packet right" (fidelity matrix) vs. "is the *decision* the mob makes
right" (this one). Both are needed to judge combat feel — a mob can send perfectly correct packets
for a targeting/attack decision that's still wrong (e.g. Enderman's missing stare-aggro).

## What this rules out, not just in

- **No shared "AttackDistance/AttackDamage/AttackCooldownTicks" table exists, and none is proposed
  here.** Each species' constants are already independently tunable per file — `GroundMobCombat`
  (legacy) and `DamageableActorCombat` (ECS) only provide the shared *mechanics* (reach-checked
  intent consumption, one authoritative damage/health/death/loot/XP funnel), never the per-species
  numbers. Collapsing those constants into one data table was considered and rejected: nothing here
  shows repeated *inconsistency* between species, only repeated *values* (many share `2.25`/`20`),
  which is a coincidence of vanilla-adjacent tuning, not evidence of a missing abstraction.
- **No AI/behavior-tree framework.** Every species' decision loop (target → move → attack) is
  ~30-80 lines of a plain `Tick` method. None of them show the kind of branching complexity that
  would make a generic framework pay for itself, and the brief's own instruction is not to build one
  from this document.
