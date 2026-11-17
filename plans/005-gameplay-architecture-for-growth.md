# Plan 005 — Gameplay architecture for growth, shaped by BDS

- Status: PROPOSAL (second version). Nothing here is built. Written after plan 004 batches `5e65cae` to `e9cebcb`, then refined against Dragonfly (`dragonfly-reference/server`), PocketMine (`essential-reference/src`) and the BDS 1.26 pack and protocol docs (`bds-reference`, `mojang-protocol-docs`).
- Premise: Zenith is "vanilla first". Where BDS has a vocabulary (components, filters, attributes, damage causes, effect names, difficulty values) Zenith adopts **BDS's names and shapes**, so data and behaviour can be compared with the real server line by line. Where BDS keeps something inside the engine (effects, difficulty scaling, weather, blocks) Zenith writes its own code, but as **data tables and small pure functions** that tests can drive.

## 1. What the three references teach

### BDS (the authority for names and for what is data)
Found by extracting the packs (96 entities, 43 spawn rules, 71 biomes, 821 recipes) and by strings in the executable:
- **Entities are `components` + `component_groups` + `events` + `filters`.** Common to nearly every mob: `minecraft:health`, `minecraft:movement`, `minecraft:breathable`, `minecraft:type_family`, `minecraft:despawn`, `minecraft:loot`, `minecraft:hurt_on_condition`, `minecraft:experience_reward`. Behaviour switches are events that add or remove groups (`environment_sensor` fires an event when a filter holds; `randomize` and `sequence` pick groups).
- **The filter language is the core.** One recursive evaluator `(test, subject, operator, value)` with `all_of/any_of/none_of`. Tests seen: `is_daytime`, `hourly_clock_time`, `light_level`, `is_brightness`, `is_weather`, `in_water_or_rain`, `is_difficulty`, `is_family`, `has_mob_effect`, `has_equipment`, `on_fire`, `in_lava`, `is_underwater`, `is_temperature_value`, `is_snow_covered`, `is_biome`, `has_biome_tag`, `random_chance`, `is_game_rule`, ... Spawn rules, `damage_sensor`, `environment_sensor`, `despawn`, `burns_in_daylight` all use it.
- **Damage immunity and reactions are data:** `minecraft:damage_sensor` (27 mobs) with `deals_damage: yes|no`, causes (`fall`, `entity_attack`, `projectile`, `magic`, `lightning`, `contact`, `all`) and `on_damage` filters. `minecraft:fire_immune {}` (11 mobs), `knockback_resistance`. The engine's cause list (`ActorDamageCause`): Contact, EntityAttack, Projectile, Fall, Fire, FireTick, Lava, Drowning, Magic, Wither, Lightning, Temperature, Freezing, ...
- **Lightning is only a family tag.** `lightning_bolt.json` is `type_family: ["lightning"]` and nothing else. Its effects come from other mobs reacting to the family or cause `lightning` (creeper becomes charged, villager becomes a witch, skeleton is immune). The engine decides when it strikes and what it burns.
- **Attack effects are data:** `minecraft:attack {damage, effect_name, effect_duration}` (wither skeleton: wither 10 s; cave spider: poison picked by `is_difficulty` events, 0 / 7 / 15 s).
- **Engine-only:** attributes (`minecraft:health`, `absorption`, `movement`, `underwater_movement`, `lava_movement`, `attack_damage`, `follow_range`, `knockback_resistance`, `player.hunger/saturation/exhaustion/level/experience`), effects (only names), difficulty scaling of attack damage, weather, light, blocks, potions as items. The wire has `UpdateAttributes` with **`Modifiers` per attribute**, `MobEffect` add/update/remove, `SetDifficulty`, `GameRule`.
- **Data that exists and we can read:** brewing (`recipe_brewing_mix` 61, `recipe_brewing_container` 2), biome climate (`minecraft:climate {temperature, downfall, snow_accumulation}` and tags), splash and lingering potions as projectile entities (`minecraft:projectile {power, gravity, on_hit: thrown_potion_effect | spawn_aoe_cloud}`), arrow `impact_damage` with a `hard_arrow` group (1 to 5 instead of 1 to 4), food `effects`.

### PocketMine
- **One `Living` base** for players and mobs, each with an `AttributeMap` (string ids like BDS) and an `EffectManager`: effects work on mobs for free.
- **The damage event is the best idea:** a base amount plus **named modifiers** (armor, resistance, absorption, strength, weakness, previous-damage cooldown, enchantments), computed in one fixed order from the running total, cancellable, with the final amount the sum. Poison and wither go through the same pipeline.
- Weaknesses: effects mutate attributes in place (speed multiplied then divided), modifier ids are integer constants in one class, registries are global statics, weather and game rules are not implemented, no difficulty scaling of damage.
- Block hooks: `onRandomTick` for a whitelist of states, `onScheduledUpdate`, `onNearbyBlockChange`, a cancellable `FarmlandHydrationChangeEvent`, tiles separate from blocks.

### Dragonfly
- **Only the player is `Living`.** There are no mobs. Burning is a separate small interface (`Flammable`), health and effects are reusable components (`HealthManager`, `EffectManager`).
- **`world.Settings` plus `Viewer`:** a plain struct for difficulty, time, rain and thunder timers, weather cycle; the world's one-second tick pushes `ViewTime` and `ViewWeather` to every viewer. Persisted to `level.dat`.
- **Weather questions are positional:** `RainingAt(pos)` = rain flag AND biome has rainfall AND temperature above 0.15 (falling with height) AND above the highest obstructing block; `SnowingAt` is the cold twin. Lightning: 1 in 100000 per chunk per tick while thundering, biased to exposed living things; the bolt damages in a radius and sets fire.
- **Projectiles are a copyable config plus a callback** (`ProjectileBehaviourConfig`: gravity, drag, damage, potion, `Hit` function), the most testable part. Splash potions find living things in an 8.25 x 4.25 x 8.25 box and scale by distance. Spawning is by factory functions so blocks and items never import entities.
- Weaknesses: the damage logic is one monolithic `Player.Hurt`; effects undo by division; no attribute system, no typed game rules, no change events, no movement validation, nothing forms snow or ice.
- Tick order worth copying: weather, lightning, entities, scheduled updates, random ticks, neighbour updates. `AdvanceTick` for tests (Zenith already has `GameClock.AdvanceBy`).

### Movement
Neither reference checks player speed; both trust the client. BDS offers server-authoritative movement modes in `PlayerMovementSettings`. Zenith has nothing to copy here, so this part is ours (section 3H).

## 2. Corrections to what plan 004 already shipped (found by reading the pack)

These are bugs or approximations in my earlier batches, to fix inside the phases below:
1. **Arrows must not be scaled by difficulty.** I scale `Projectile` damage in `PlayerDamage.ApplyCore`. In BDS the arrow's difficulty effect is *data* (`hard_arrow` group, damage 1 to 5). Fix: projectile damage comes from the projectile definition by difficulty; the generic scaling applies to melee and explosions only.
2. **Weather and light:** spawn rules carry `brightness_filter {min, max, adjust_for_weather}`. Zenith dims the sky by weather for every consumer. It should only do so where the rule says `adjust_for_weather`, and the grass/sapling rules should use the engine's own choice. Needs a check of the data per rule.
3. **Poison from a cave spider** is 0 / 7 / 15 seconds by difficulty (Easy gives none), as events on the entity. `SpiderSystem` has a fixed duration.
4. **`burns_in_daylight` is `{}` in the pack** and the helmet check is engine-side; so "helmet shields the undead" is a rule of the sun-burn system (`has_equipment head`), not data.
5. **`brightness_filter` for monsters,** `difficulty_filter {min, max}`, `density_limit`, `spawns_on_block_filter`, `herd`: spawn data keys we should read by their BDS names instead of our own spellings in `spawns.json`.

## 3. Proposed architecture

Each piece says what it copies, what it improves, and when it is done.

### A. Registration that derives itself (DX, no behaviour change)
- A system declares itself once (name, phase, the state it owns). Diagnostics timing, the composition-order test and the single-writer owner table read that declaration (today four hand lists).
- The recording client channel is generated from the port interfaces (source generator), so a new port method is one edit.
- **Registries are instances, not global statics** (both references use statics; tests here run in parallel).
- Done when: adding a system or a port method is one edit plus its own test.

### B. `WorldState<T>`
- Copies Dragonfly's `Settings` + `Viewer` pull model, adds what it lacks: **typed values, a version, change events** (`WorldDifficultyChangeEvent` in PocketMine is the model), a codec, optional wire projection and command description.
- Game rules (BDS names and order stay), difficulty, weather and time rules move onto it; one generic sync system.
- Done when: a new world setting is one class and one registration, the stored bytes of existing worlds still load, `GameRuleSyncSystem` and the weather and difficulty branches are one system.

### C. Filters (the BDS core), then data-driven rules
- A `Filter` type and one evaluator with the BDS grammar and the BDS test names, over a small `FilterContext` (subject entity or block, other, world environment). Start with the tests Zenith already needs: `is_daytime`, `hourly_clock_time`, `light_level`, `is_brightness`, `is_weather`, `in_water_or_rain`, `is_difficulty`, `is_family`, `on_fire`, `in_lava`, `is_underwater`, `has_mob_effect`, `has_equipment`, `is_biome`, `has_biome_tag`, `is_temperature_value`, `random_chance`, `is_game_rule`.
- Users: spawn rules (`spawns.json` reads BDS keys), sun burning, `damage_sensor`, `environment_sensor`, despawn.
- Done when: `NaturalSpawnSystem`, the sun rule and the lightning immunity use filters, and a new test name is one function.

### D. Body, attributes and the damage pipeline (the keystone)
- **One `Body` view** for players and mobs: health, effects, burning, attributes, equipment. (PocketMine's `Living`, but as components in the existing ECS and on `Player`, not an inheritance tree, so single-writer ownership stays.)
- **Attributes with modifier stacks**, named as BDS (`minecraft:health`, `minecraft:movement`, `minecraft:absorption`, `minecraft:attack_damage`, `minecraft:knockback_resistance`, `minecraft:lava_movement`, `minecraft:underwater_movement`). A modifier has an id (the source: an effect, sprint, an item) so removing it is exact. This is what both references lack and what the wire's `AttributeData.Modifiers` already expects. Strength is a modifier on attack damage, Speed one on movement, Health Boost one on max health; Absorption is the attribute `minecraft:absorption` that damage drains first.
- **Effects as a data table** (`data/effects.json`): BDS effect name and id, category, the attribute modifiers it grants per level, its periodic rule (poison, wither, regeneration, hunger, saturation) or instant rule. `EffectRules` collapses to this plus a few periodic handlers. Effects work on mobs because the effect manager is a component.
- **Damage pipeline as ordered stages with a named breakdown** (PocketMine's event, made typed): gate (creative, dead) -> damage_sensor immunities (filters, `deals_damage`) -> difficulty (melee and explosion only) -> fire immunity -> previous-damage window -> armor -> enchantments -> resistance -> absorption -> apply -> post (exhaustion, wear, thorns, knockback, death). Result carries the list of modifiers so tests assert each stage and events can observe it. Damage causes follow BDS `ActorDamageCause` (add Wither, Lightning, Freezing, FireTick, Contact). Dragonfly's source flags (`ReducedByArmour`, `ReducedByResistance`, `Fire`) become properties of the cause.
- Done when: an effect given to a zombie works; Absorption, Health Boost and the helmet-in-sun rule are small additions; `PlayerDamage.ApplyCore` is the pipeline entry; the arrow scaling bug is fixed through projectile data.

### E. Environment facade with climate
- One read-only API in BDS terms: `Light`, `SkyExposed`, `Temperature(x,y,z)`, `Rainfall`, `PrecipitationAt(x,y,z)` (none, rain, snow), `IsRainingAt`. Dragonfly's rule (flag AND biome rainfall AND temperature AND above the highest obstructing block, temperature falling with height) with the biome numbers from BDS `minecraft:climate` (temperature, downfall, snow accumulation).
- `LightModel` and `WeatherState` sit behind it; sun burning, fire, spawn, random ticks ask the facade.
- Done when: snow layers and ice form and melt by climate and rain wets farmland, using only the facade.

### F. Block behaviour traits
- Keep the data-trait approach already used for `RandomTick`. Add traits for hydration (farmland, with a moisture state), freezing and melting (water, ice, snow layer), trampling. Behaviour hooks follow Dragonfly's interface split (random, scheduled, neighbour) and the tick order **weather, lightning, entities, scheduled, random, neighbour**; the PocketMine whitelist idea (only registered states are random-ticked) is what Zenith already does. Cancellable environment policies (plan 002) cover the "event" side.
- Block entities: a generic `BlockEntityStore<T>` generalising the chest store (brewing stand first).
- Done when: farmland (moisture, trample, rain), ice and snow work with the checks from E.

### G. Transient actors and projectiles
- A projectile is **a copyable config plus callback** (Dragonfly), with BDS's field names (`power`, `gravity`, `on_hit`: thrown potion effect or area cloud, arrow `impact_damage` range with difficulty groups). Arrows, splash and lingering potions, thrown things share it.
- Actor kinds with no AI (lightning bolt, area cloud, falling block) are small descriptors (identifier, family tag, lifetime) behind one generic system. Spawning goes through a port (`MobSpawner` today is by name) so blocks and items never depend on species systems.
- Lightning: strike rule (rate while thundering, biased to exposed things, from the environment facade), then reactions as data: a family `lightning` and a cause `lightning` that `damage_sensor` and the creeper, villager and skeleton definitions use.
- Done when: a bolt strikes, burns, hurts in a radius, charges a creeper, and a splash potion is one config.

### H. Items that carry behaviour, and brewing
- Item behaviours are small capabilities (Dragonfly's `Consumable`, `Throwable`, `Usable`) with a use-context out-parameter so items request mutations and the caller applies them (keeps the inventory transaction in one owner).
- Potions: the potion type lives in the stack's `ItemUserData`; the type -> effects table is **our** data (`data/potions.json`, since BDS keeps it in the engine); **brewing reads BDS's own `recipe_brewing_mix` and `recipe_brewing_container`** from the recipe files we already extract. Food effects read the item `effects` list.
- Done when: a potion can be brewed, drunk, thrown and put on an arrow.

### I. Movement allowance
- With attributes from D the movement system computes the allowed speed per tick (base, sprint, Speed/Slowness modifiers, soul sand, ice, fluids) and rubber-bands the client when the packet exceeds it, using the server-authoritative settings the protocol offers. Opt-in, tolerant, logged first before it corrects.
- Done when: a cheating client is corrected and a Speed II player is not.

## 4. Order

1. **A and B** (registration, `WorldState`): structure only, behaviour unchanged, protected by the 2000+ tests.
2. **C** (filters), starting with spawns and the sun rule, since D and G use it.
3. **D** (body, attributes, effects table, damage pipeline), with the corrections of section 2.
4. **E and F** (environment facade, block traits): snow, ice, farmland.
5. **G** (projectiles, transient actors, lightning).
6. **H and I** (item behaviour, potions, brewing, movement allowance).

Each phase ends as plan 004 batches do: full suite green, Benchmarks build, plan updated, local commit, no push; structural steps carry tests that prove nothing changed, behavioural steps carry tests that show the new behaviour.

## 5. Risks

- D touches the damage path and the player/mob split that single-writer ownership depends on: the architecture owner table must change deliberately.
- B must keep the game rule wire order and the stored bytes readable by existing worlds.
- C must stay a subset: full component groups and events are not adopted at once; the evaluator and data shapes are chosen so they can be added later without rewriting rules.
- A's generated recorder must keep the `Calls`/`Of` surface the tests use.
- BDS data here is a trimmed server pack (blocks, effects, potions, weather are engine-only); for those the values come from vanilla behaviour and the references, and are marked as ours in the data files.
