# ADR §34 — HUD spawn honesty (seed, not authority)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §34`.

**Choice:** After empty `BiomeDefinitionList`, send local `SetActorData` (FLAGS include `Breathing`) then `UpdateAttributes` with **frozen** defaults (health/hunger/saturation/exhaustion/movement/level/xp). Call site is spawn orchestration only — same pattern as CreativeContent. **No** `Player.Health`/`Hunger` fields, **no** `VitalsSystem`, **no** `UpdateAbilities`/`UpdateAdventureSettings` in this leva.

**Why:** Survival clients expect local metadata/attributes; omission leaves air/hunger HUD broken. Smallest honest surface (Rule 7); food/effects remain Deferred (§14–16). Domain vitals without tick authority would mirror pre-§17 inventory lie.

**Deferred:** VitalsSystem + Player vitals authority; water/`AirSupply`; hunger tick; food; damage. (UpdateAbilities/Adventure → §37.)

**Follow-up (2026-11-14):** Gameplay now owns live vitals. The historical frozen saturation/
exhaustion seed is replaced by the player's current values in SendPlayerAttributes, including
join and respawn. Protocol clamps only projected exhaustion to its wire range without changing
the domain accumulator. HungerSystem sends food/saturation changes immediately and refreshes
nonzero exhaustion once per second, including exhaustion accrued by other gameplay systems.
New peer actors also receive their current health after AddPlayer, as mob announcements already do.
