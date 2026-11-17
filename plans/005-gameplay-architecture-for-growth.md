# Plan 005 — Gameplay architecture for growth (what the parity batches showed)

- Status: PROPOSAL. Nothing here is built yet. Written after the plan 004 batches `5e65cae` to `e9cebcb` (mining, fire, melee gear, spawn, items, light, difficulty, weather, effects).
- Why now: every batch left a "not done" list. Almost every item on those lists is not missing a feature, it is missing a **shared piece of structure** that the feature would sit on. Building the features one by one on today's structure would repeat the same hand wiring each time, so the pieces below come first.

## What the nine commits cost (evidence)

Counting files per commit (`git log --name-only 1030533..HEAD`) and reading the diffs:

1. **Adding one world-level setting touches about twelve places.** Difficulty and weather each needed: a state class, `World` property, load at boot (`ZenithServer`), persist, a version counter, a sync system or extra branch in one, a port method on `IWorldChannel`, its `WorldProtocol` body, the hand-written `RecordingClientChannel` stub, the join sequence, a command, a permission node, the `CommandRuntime` constructor, `ServerContext`. Game rules, difficulty and weather are three copies of the same idea (versioned, persisted, replicated, commanded).
2. **Adding one `IGameSystem` touches four lists** that must agree: `WorldModule`/composition, `ServerRuntimeDiagnostics` timing registry, `GameplayCompositionTests` order, and the architecture owner table. A forgotten one fails a test, which is good, but none of it is derived from the registration.
3. **`PlayerDamage.ApplyCore` is a growing `if` chain** (creative, mob scaling by difficulty, fire immunity, armor, resistance, exhaustion, gear wear, knockback, death). It changed in 4 of 9 commits. Mobs take damage through a different path (`DamageableActorCombat`, `MobCore`), so every rule is written twice or only for players.
4. **Players and mobs are two worlds.** Burning exists twice (`BurnSystem` over `Player.BurnTicks`, `MobHazardSystem` over a private dictionary). Effects exist only on `Player.Effects`. `EffectRules` is a set of static functions that each consumer must remember to call (dig, melee, damage, air, hunger, burn): the next effect means finding every consumer again.
5. **Environment questions are scattered.** Light is a static `LightModel`, weather a `World` property read inside it, sun exposure is `SeesSky` called from three systems, climate does not exist. Each new rule re-decides how to ask.
6. **One species is one system plus registrations** (36 files in `Gameplay/Entities`). A short-lived actor such as a lightning bolt has no home.
7. **Block behaviour has one hook** (random tick, data trait). Farmland, ice and snow need more than that.
8. **The client port is edited by hand in two places per packet** (`IWorldChannel` plus `RecordingClientChannel`), and recorder drift is a silent risk.

## What each deferred item needs underneath

| Deferred item | Missing structure |
|---|---|
| Effects on mobs | a body model shared by players and mobs (effects, burning, health) |
| Absorption, Health Boost | per-body attributes with modifiers (max health, extra absorption pool) |
| Server checks Speed-boosted movement | attributes (movement speed) plus a movement allowance the validator reads |
| Helmet vs sun | an equipment query on any body (`HeadItem`) |
| Lightning | a transient actor kind, a strike rule (fire, damage, light), thunder as environment state |
| Snow and ice by biome | climate data (temperature per biome and height), block traits reacting to precipitation |
| Farmland wetted by rain | block state with a behaviour hook that can ask the environment (rain, water nearby) |
| Potions as items, brewing, splash, tipped arrows | item user data used as a behaviour key, a block entity for the brewing stand, a thrown-projectile kind |

## Proposed structure

### A. Registration that derives itself (DX, no behaviour change)
- A system declares itself once (`name`, phase, owner of the state it writes). Diagnostics timing, the order test and the owner table read that declaration instead of being separate lists.
- `RecordingClientChannel` stops being hand-written: a source generator (or a recording proxy) from the port interfaces, so a new port method needs one edit.
- Done when: adding a system or a port method is one edit plus its own test; the four lists above are gone or generated.

### B. `WorldState<T>`: versioned, persisted, replicated, commandable
- One small contract: current value, version, codec to bytes, optional wire projection (`Send(player)`), optional command description. A generic sync system serves every registered state.
- Game rules, difficulty and weather move onto it first (their tests are the safety net), then it is the way to add time rules, world spawn settings, and so on.
- Done when: a new world setting is one class and one registration; `GameRuleSyncSystem` and the weather and difficulty branches are one system.

### C. `Body`: one living-thing model for players and mobs
- Components every living thing has: `Health`, `Effects`, `Burning`, `Attributes`, equipment view. Players and ECS mobs both expose them through the same read/write interface; the single-writer rule stays (the owning system writes).
- `Attributes` carries modifier stacks (effects, sprint, items) for max health, movement speed, attack damage, knockback resistance, absorption. Strength becomes "a modifier on attack damage", Speed "a modifier on movement speed", Health Boost "a modifier on max health", Absorption "an absorption amount that damage drains first". `EffectRules` collapses to a table from effect to modifiers plus the few periodic ones (poison, wither, regeneration).
- One `DamagePipeline` of ordered stages (gate, difficulty, immunity, armor, enchant, resistance, absorption, apply, exhaustion, wear, knockback, death) used by players and mobs; each stage is a small function with its own tests, and a feature adds a stage instead of editing a chain.
- Burning, drowning and the sun become body rules applied by one system over all bodies.
- Done when: an effect given to a zombie works; helmet-in-sun, Health Boost and Absorption are small additions; `PlayerDamage.ApplyCore` is the pipeline's entry, not a hand-grown chain.

### D. `Environment` facade
- One read-only API: `Light(x,y,z)`, `SkyExposed`, `Precipitation(x,y,z)` (none, rain, snow by climate), `Temperature(x,y,z)`, `IsRainingAt`. `LightModel` and weather sit behind it; biome climate (temperature, rainfall) comes from data next to `spawns.json`.
- Done when: consumers (spawn, fire, burn, sun, random ticks) ask the facade; snow and ice are data traits plus a precipitation hook.

### E. Block behaviour beyond random ticks
- Extend the data-trait approach already used for `RandomTick`: traits for `Hydration` (farmland, with a moisture state), `Freezes`/`Melts` (water, ice, snow layer by temperature), `Trampled` (farmland back to dirt). Behaviours receive the `Environment` facade and write through the same policy-checked context.
- Done when: farmland exists with moisture and trample, rain and water nearby wet it, ice forms and melts by climate.

### F. Transient actors and projectiles as kinds
- A small `ActorKind` descriptor (identifier, spawn packet shape, lifetime, despawn) and a generic system for actors that need no AI: lightning bolts, thrown potions, falling blocks reuse it. Species with AI keep their systems but share the registration helper.
- Lightning: a strike rule (random per loaded area while thundering), the actor, fire, damage 5 in a radius, light, sound.
- Done when: a lightning strike and a splash potion are each one descriptor plus one rule.

### G. Items that carry behaviour, block entities, brewing
- `ItemBehavior` registry keyed by item and `ItemUserData` (the potion type and duration live in the stack's user data), shared by drink, throw, arrow tip and brewing output.
- `BlockEntityStore<T>` generalising the chest store for the brewing stand (and later furnace, hopper).
- Done when: a potion item can be brewed, drunk, thrown and put on an arrow.

### H. Movement allowance
- With `Attributes` from C, the movement system computes the allowed speed per tick (base, sprint, Speed and Slowness modifiers, blocks such as soul sand and ice) and corrects the client when it is exceeded.
- Done when: a cheating client is rubber-banded and a Speed II player is not.

## Order and why

1. **A and B first** (registration, `WorldState`): pure structure, no behaviour change, protected by the existing 2000+ tests, and they remove the largest recurring cost.
2. **C next** (`Body`, `Attributes`, `DamagePipeline`): the keystone. It closes effects on mobs, Absorption, Health Boost, helmet vs sun and prepares H.
3. **D and E** (environment, block behaviours): snow, ice, farmland, rain wetting.
4. **F** (transient actors): lightning, then thrown things.
5. **G and H** (item behaviour, block entities, movement allowance): potions end to end and movement validation.

Each phase ends the way plan 004 batches do: full suite green, Benchmarks build, plan updated, local commit, no push. Behaviour-changing steps carry tests that show the new behaviour; pure-structure steps carry tests that show nothing changed.

## Risks

- C touches the damage path and the player/mob split that single-writer ownership depends on; it needs the architecture tests (owner table) updated deliberately, not worked around.
- B must keep the game rule wire order and the stored format readable by older worlds (rules blob and module-state bytes stay as they are).
- A's generated recorder must keep the `Calls`/`Of` surface the tests use.
