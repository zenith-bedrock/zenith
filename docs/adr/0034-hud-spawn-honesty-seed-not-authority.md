# ADR §34 — HUD spawn honesty (seed, not authority)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §34`.

**Choice:** After empty `BiomeDefinitionList`, send local `SetActorData` (FLAGS include `Breathing`) then `UpdateAttributes` with **frozen** defaults (health/hunger/saturation/exhaustion/movement/level/xp). Call site is spawn orchestration only — same pattern as CreativeContent. **No** `Player.Health`/`Hunger` fields, **no** `VitalsSystem`, **no** `UpdateAbilities`/`UpdateAdventureSettings` in this leva.

**Why:** Survival clients expect local metadata/attributes; omission leaves air/hunger HUD broken. Smallest honest surface (Rule 7); food/effects remain Deferred (§14–16). Domain vitals without tick authority would mirror pre-§17 inventory lie.

**Deferred:** VitalsSystem + Player vitals authority; water/`AirSupply`; hunger tick; food; damage. (UpdateAbilities/Adventure → §37.)
