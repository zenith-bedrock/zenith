# ADR §109 — Survival vitals close the reconnect/death honesty gap; no generic Attribute framework

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §109`.

**Choice:** `Player.Saturation` becomes real state (default 5, vanilla-parity buffer that depletes
before `Hunger` on each exhaustion-threshold crossing); `HungerSystem`'s exhaustion accumulator gains
mining and damage as sources alongside sprinting; death resets XP to zero
(`PlayerDamage.Apply` → `player.SetExperience(0, 0)`); and `PlayerDataBlob` gains a v3 layout (v2 + 4
floats: health/hunger/saturation/exhaustion) so a reconnect restores the same vitals a player left
with instead of resetting three of four to spawn defaults while only XP quietly survived. Each closes
a specific gap already named in the Phase XXIII-B cross-reference audit (`docs/entity-fidelity.md`) —
this is not a new survival subsystem, it's finishing four documented half-implementations.

**Why version the blob instead of a parallel `pd2:` key or a migration pass:** `PlayerDataBlob`
already had a version byte and an established pattern for it — `TryUnpack` reads each version band
with `>=`, so a v3 blob still gets its v2 XP fields, and an old v1/v2 blob simply has no vitals band
and loads at fresh-spawn defaults, exactly how a v1 blob already behaved for XP before this phase. No
migration step exists or is needed; adding a fourth storage key for four floats would be more
machinery than the problem calls for.

**Why not a generic `Attribute` framework:** Minecraft's own attribute model is broad (health, hunger,
movement speed, attack damage, …), and it would be easy to justify "unifying" hunger/saturation/
exhaustion/health under one generic mutable-attribute type now that both exist side by side on
`Player`. Rejected — each already has its own owner, its own mutation rules, and its own wire
projection (`HealthState` vs. plain `Player` fields vs. `UpdateAttributes`), and nothing about this
phase's four fixes needed them to share a spine. Building one now would be designing for hypothetical
future attributes, not solving today's four gaps.

**Non-goals for this ADR:** no per-item stack-size table, no fix for `BreakDuration`'s tool-tier-
mismatch formula (still a latent no-op — no block gates on `ToolTier`, only `ToolKind`), no movement
validation, no walking-exhaustion source (needs distance tracking `MovementSystem` doesn't have), no
per-food saturation-modifier table (one constant ratio applies to every food), no effects persistence
across reconnect. All are pre-existing, separately documented gaps this phase deliberately left alone
— see the findings doc for the full list.

See `docs/history/phases/phase-xxv-survival-foundation-findings.md` for the full record.

**Adendo (ago 2026 — two more HungerSystem gaps closed by the same reference-parity audit):**

1. **Starvation had no health floor.** `HungerSystem.Tick` applied `DamageSource.Starve` at 0 hunger
   with no check against `player.Health` — it could kill the player outright. Reference servers gate
   starvation below Hard difficulty (Dragonfly: `p.Health() > StarvationHealthLimit()`; PowerNukkitX:
   `(difficulty==1 && health>10) || (difficulty==2 && health>1) || difficulty==3`) so it is never
   lethal on its own below Hard. Zenith has no difficulty concept to gate this on, so it now defaults
   to that common case: `player.Health > 1f` guards the starvation-damage branch, matching the same
   floor `EffectSystem.TickPoison` already enforced for Poison.
2. **Natural well-fed regen was free.** `HungerSystem`'s regen branch called `player.Heal(1f)` with no
   `Exhaustion` cost, so a well-fed player healed indefinitely at no food cost. Reference servers pair
   every natural heal with an exhaustion cost (Dragonfly `regenerate`: `p.Heal(1, ...); if exhaust {
   p.Exhaust(6) }`; PowerNukkitX `PlayerFood.tick()`: `heal(...); exhaust(6)`). `HungerSystem` now adds
   the same 6-exhaustion cost on every regen tick. Not fixed in the same pass: reference servers also
   have a faster 10-tick regen cadence when food is full and saturated — Zenith keeps its single
   80-tick cadence; this is a pacing difference, not a free-resource bug, and is left as a further
   pre-existing gap rather than folded into this fix.
