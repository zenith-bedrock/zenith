# Plan 004 — Vanilla parity audit of what already exists

- Status: AUDIT, being worked through. Done: items 1, 2, 3, 4, 5, 6, 7 (partly), 9, 10, 13, 14, 19 (see "Done" at the end). The rest are findings with evidence and a rough size.
- Date: 2026-11-16. Method: the code of each implemented system read against (a) the BDS 1.26 behavior pack in
  `bds-reference/bds/behavior_packs/vanilla` (entities and spawn rules; authoritative, extracted from the `.brarchive` files),
  (b) PocketMine (`essential-reference`) and Dragonfly (`dragonfly-reference`) sources, (c) known vanilla rules where neither
  reference states a number. Confidence is marked: **BDS** = read from the pack, **REF** = read from PM/Dragonfly,
  **KNOWN** = vanilla rule from memory, not re-read here (check before fixing).
- Not audited: crafting/recipes, containers beyond what the tests cover, commands, world generation, the protocol layer.

## P0 — wrong numbers that change how the game plays

| # | What Zenith does | Vanilla | Evidence | Size |
|---|---|---|---|---|
| 1 | Fall damage `floor(distance - 3)` (`MovementSystem.ApplyFallDamage`) | `ceil(distance - 3)`; a 5-block fall hurts 3, not 2 | REF PocketMine `Living::calculateFallDamage` | XS |
| 2 | Sprint exhaustion 0.1 **per tick** while sprinting (`HungerSystem`) | 0.1 **per metre** (about 0.03 per tick at running speed): hunger drains about 3.5x too fast | REF Dragonfly (`Exhaust(0.1 * distance)`), KNOWN | S (needs distance moved) |
| 3 | Creeper explosion: damage 10 at point blank, linear to 0 at 3 blocks, players only | Damage radius is 2 x power (6), up to about 43 at point blank on Normal, scaled by exposure and blast-reduced by armor; hurts and knocks back every entity | BDS `creeper`/`tnt` `minecraft:explode` power 3 (4 for tnt), KNOWN formula | M |
| 4 | Explosion carves a sphere and destroys everything except bedrock: obsidian, water and lava included, no drops | Ray-cast with block blast resistance (obsidian, water and lava survive), about 1/power of destroyed blocks drop, creeper blast does not break most of these | KNOWN | M |
| 5 | Zombie melee 4, spider melee 2, golem melee fixed 6, enderman health 20 | The pack gives zombie 3, spider 3, golem 7 to 21 random, enderman health 40 (damage depends on difficulty, which Zenith has no concept of) | BDS `minecraft:attack`, `minecraft:health` | XS (data) |
| 6 | `killExperience`: cow 1, golem 25, bat 1, minecart 1, fish 1 | cow 1 to 3 random; iron golem, bat, minecart none; cod and salmon 1 to 3; zombie and skeleton 5 + equipment count x (1 to 3) | BDS `minecraft:experience_reward` | XS (data) + random amounts |
| 7 | Breaking speed ignores water and air | In water or not on the ground the break takes 5 times longer; Haste and Mining Fatigue change it | `BreakDuration` header comment says so; KNOWN | S |
| 8 | Fire block never hurts anything; no burning state, no flint and steel | Standing in fire or lava sets entities alight (damage over time, extinguished by water), flint and steel lights fire | grep: no "on fire" state anywhere; lava only hits for 4 per damage window | M |

## P1 — behaviour that is visibly different

| # | Finding | Evidence | Size |
|---|---|---|---|
| 9 | Hunger: no fast regeneration at full hunger with saturation (heals every half second using saturation) | REF Dragonfly `canQuicklyRegenerate` | S |
| 10 | Hunger exhaustion missing: jump 0.05, sprint-jump 0.2, swim, attack 0.1 (Zenith has mining 0.005 and damage only); and damage exhaustion is added for **every** cause, vanilla gives none for fall, starvation, drowning or void | REF Dragonfly lines for `Jump` and `Attack`; KNOWN for the cause list | S |
| 11 | Melee: no critical hit (1.5x when falling), no Strength/Weakness, no weapon durability loss when hitting, no armor durability loss when hit | REF Dragonfly `Attack` | M |
| 12 | Armor reduction skips Fall/Void/Starve/Magic/Drown but not by a per-source flag; fire/lava/explosion/projectile reductions and enchantments are not distinguished | KNOWN | S |
| 13 | Block drops are fixed (`drop.count`): leaves drop nothing (no saplings, sticks, apples), gravel never gives flint, tall grass no seeds, ores no multi-count (redstone, lapis), no Silk Touch or Fortune | `data/block_definitions.json` ("saplings and apples are not modelled yet" in `LeavesDecayBehavior`) | M |
| 14 | Dropped item from a break sits still at the cell centre; vanilla pops it with a small random velocity and offset | `FloorDropFanout.TryDepositBatch` publishes zero velocity | S |
| 15 | Dropped items: one per cell key (custody handle), no merging of nearby piles after they settle, no water buoyancy, no lava/fire/cactus destruction, no void despawn, collision only against full cubes | `FloorDropStore.CanMergeAt`, `FloorDropPhysics` | M |
| 16 | Orbs: no merging of nearby orbs (hundreds of kills make hundreds of entities), only mob kills give experience, no persistence, the orb value rolls for cow/cod are not random | `ExperienceOrbSystem` (plan 003 "left out") | M |
| 17 | Natural spawn: weights/groups differ from the pack: skeleton weight 100 (pack 80), skeleton group 1-3 (1-2), zombie group 1-4 (2-4), enderman group 1 (1-2), cow group 2-4 (2-3), spider has no group; 5% of zombies should be villager zombies; monsters spawn by **light level 0-7** (also underground, also by day in the dark) not by "night"; cows need grass and light 7 or more; density limits per species | BDS `spawn_rules` | M (needs a light model for full fidelity) |
| 18 | Skeleton arrow damage fixed 6, speed 0.45; the pack has arrow damage 1 to 4 scaled by speed with power 1.6 and gravity 0.05 | BDS `arrow` | S |
| 19 | Fish species: one "Fish" with 3 health; cod and salmon have 6 | BDS `cod` | XS |
| 20 | Starvation stops at 1 health regardless of difficulty (vanilla: Easy stops at 10, Normal at 1, Hard kills) | KNOWN; Zenith has no difficulty | S once difficulty exists |

## P2 — environment simulation, narrower than vanilla

| # | Finding | Size |
|---|---|---|
| 21 | Leaves decay only by random tick, and a broken log does not wake nearby leaves; leaf distance rules and Bedrock's update bit are approximated | S |
| 22 | Sapling grows oak only, in one random-tick step shape, without the light requirement; no other species, no bone meal | M |
| 23 | Grass spreads without a light check and never decays under blocks (no light model) | M (light) |
| 24 | Fire does not burn out in rain (no weather), has no per-block burn rates beyond the families table check, no netherrack/soul fire | M |
| 25 | Fluids: no water pushing entities or items, no bubble columns, no sponge or cauldron interaction; lava-water reaction handles source and flowing but not the vertical case separately | M |
| 26 | No weather, no light, no difficulty, so P1 items 17, 20, 23, 24 stay approximate until those exist | L |

## Verified correct (no action)

Fall-damage threshold 3, drowning (300 air, -20, 2 damage, +4 refill), water flow delay 5 and lava delay 30 with source/flow reach, fire tick delay 30-39, hit invulnerability 10 ticks with delta damage, sword/axe/pickaxe/shovel damage tiers, tool tiers' mining speeds 2/4/6/8, harvest vs speed rule (30 vs 100), item pickup delay 10 (thrown 40), item despawn 6000, orb despawn 6000, orb sizes, creeper fuse 30 ticks, enderman XP 5 and creeper/spider/skeleton XP 5, zombie/skeleton/spider/creeper/golem/bat/villager/cow health, falling blocks (they land from exactly the floor height since plan 003).

## Suggested order

1. P0 data fixes (5, 6, 19) and fall damage (1): small, safe, immediately visible.
2. Hunger set (2, 9, 10): one coherent change in `HungerSystem` plus distance tracking in `MovementSystem`.
3. Explosions (3, 4): one shared explosion routine (rays, blast resistance, exposure damage, knockback, drops) used by creepers and later TNT.
4. Mining modifiers (7) and block drop tables with random counts (13), then the item pop (14) and merging (15).
5. Burning state (8) with its sources (lava, fire, flint and steel).
6. Melee set (11) and spawn rules (17, 18) once the light model question is decided.

## Done

- **1 Fall damage** now `ceil(distance - 3)` (`MovementSystem`); test `A_started_block_beyond_the_safe_distance_counts_as_a_whole_one` fails with `floor`.
- **5 Mob data** from the BDS pack: zombie 3, spider 3, golem 7 to 21 (new `damageMax`, rolled per hit by `MeleeProfile.Roll`), enderman health 40. The golem's area slam (8) is a separate constant and was left alone.
- **6 Experience** from the pack: cow and fish 1 to 3 (new `killExperienceMax`, rolled per kill), iron golem, bat, minecart and villager 0. Zombie and skeleton equipment bonus is not modelled (Zenith mobs carry no equipment).
- **19 Fish** health 6 (cod).
- Tests that pinned the old numbers now pin the pack's (zombie/spider damage, golem and enderman kills, XP ranges, minecart/bat/villager 0).
- **2, 9, 10 Hunger.** `HungerSystem.Exhaust` is the one door for exhaustion (Creative never tires). Sprinting costs 0.1 per metre walked and swimming 0.01 per metre (`MovementSystem.AccrueMovementExhaustion`; steps of 5 blocks or more are corrections and cost nothing), a jump 0.05 and a sprinting jump 0.2 (a rise from the ground, since the input has no jump bit), a landed attack on a mob or player 0.1, mining as before. Damage exhausts only for melee, projectile and lava, not for a fall, starvation, drowning or the void. Full hunger with saturation left heals every 10 ticks for min(saturation, 6) / 6 and pays min(saturation, 6) exhaustion (the KNOWN vanilla rule; Dragonfly has the same shape on a 20-tick cadence). Not done: a player at 6 hunger or less is not stopped from sprinting by the server (the client enforces it from the attribute).
- **3, 4 Explosions.** One routine, `Explosion` (`Gameplay/WorldInteraction`), used by the creeper (power 3 from the pack) and ready for TNT. Blocks: the 16 x 16 x 16 ray grid with strength `power * (0.7 to 1.3)`, 0.225 lost per 0.3 step plus `(blast resistance + 0.3) * 0.3` per block (resistance now read from `block_properties.json` through `Blocks.BlastResistanceOf`), so obsidian, water, lava and bedrock stop a ray where dirt does not and a wall shades what is behind it; each destroyed block drops with chance 1 / power (and not at all with `doTileDrops` off); every cell still asks the environment policies (cause Explosion). Bodies: everything with health within 2 x power takes `floor((i^2 + i) / 2 * 7 * 2 * power + 1)` with `i = (1 - distance / (2 * power)) * exposure`, exposure being the share of a grid over the body whose line to the centre crosses no solid block, so a creeper at point blank deals 43 and a wall protects; players are pushed by `i` (client knockback), mobs through the ECS knockback component. New cause `DamageCause.Explosion` (armor applies, 0.1 exhaustion). Creepers hurt other species through the damage dispatch (`Explosion.Mobs`, wired in `EntityModule`); the creeper itself is excluded from its own blast. Limits: mobs use a person-sized box for exposure (the ECS has no per-species size), mob knockback ignores species resistance (a golem is pushed), no blast protection enchantment, no chain reaction with TNT blocks (there is no TNT block yet).
- Test hygiene found on the way: the fire-and-gravel test depended on unseeded fire randomness and failed one run in many; `BlockUpdateSystem` now takes an optional `Random` and the test seeds it.
- **7, 13, 14 Mining and drops.** Digging takes 5 times longer with the head in water and 5 times longer off the ground, and the two stack (`DigPenalty`, `BreakDuration` speed divisor); a dig timed in the air still completes at the faster of its start and current time once the player lands (`IsBreakAuthorized`). Drops are rolled (`BlockLoot.Roll`): `block_definitions.json` drop entries take `countMax` (range), `instead` (first chance that comes up replaces the main drop) and `bonus` (each rolls on top). Done: iron/gold/copper ore (raw items, copper 2 to 5), redstone 4 to 5, lapis 4 to 9, emerald, nether gold nugget 2 to 6, quartz, glowstone dust 2 to 4, clay 4 balls, snow 4 snowballs, melon 3 to 7 slices, sea lantern 2 to 3 crystals, bookshelf 3 books, gravel flint 10% instead, short grass seeds 12.5%, every leaves block sapling 5% (jungle 2.5%), stick 2% (1 to 2), apple 0.5% (oak, dark oak). A block-break drop now leaves the cell with a hop (0.2 up, up to 0.1 aside). Not done: Haste and Mining Fatigue (no such effects exist yet), Fortune, Silk Touch, the random offset inside the cell (floor drops are cell-anchored), shears/tool-dependent leaves and grass drops, aqua affinity.
