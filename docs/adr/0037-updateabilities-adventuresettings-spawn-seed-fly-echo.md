# ADR §37 — UpdateAbilities + AdventureSettings (spawn seed + fly echo)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §37`.

**Choice:** After §34 `SetActorData`/`UpdateAttributes`, send `UpdateAbilities` (`0xBB`) then `UpdateAdventureSettings` (`0xBC`) before PreSpawn. Shared `AbilityData` writer (SSOT) also used by `AddPlayer` via wire `GameMode` int. Survival mask = pre-refactor golden bits; Creative = Survival + `MayFly` + `InstantBuild` + `Flying` (join already flying — intentional). `Invulnerable` off. Runtime id = unique id. Inbound `RequestAbility` (`0xB8`) for `FLYING` only: Creative echoes `SendLocalAbilities` with packet bool (stateless); Survival ignore (no kick). Adventure LAN defaults: ShowNameTags + AutoJump.

**Why:** StartGame gamemode without abilities is cosmetic; clients need ability layers for fly/instant-build feel. RequestAbility must be consumed or Warning-spams. Seed pattern matches §34 — no `Player.MayFly` / AbilitySystem.

**Deferred / next honesty:** hunger/food/fall damage. (`/gamemode` → §52; Death/Respawn → §40 adendo; drop-entity → §26.)
