# Phase XXV — Survival Foundation & Player Progression: findings

This document records what actually landed in this phase. It closes four specific, previously
documented gaps from the Phase XXIII-B cross-reference audit (`docs/entity-fidelity.md`, "Cross-
reference audit pass, round 2") rather than opening a new survival framework: saturation, exhaustion
sources beyond sprinting, XP not resetting on death, and vitals not surviving a reconnect. It also
adds the smallest tool-progression recipe chain the existing crafting model can express, and a
data-driven ore-drop table so breaking an ore yields its real item instead of the block.

## Baseline

- HEAD at start: `5ff9cdb` (feat: audit and harden overworld generation for exploration) — Phase
  XXIV, already committed to `develop`. This phase's work is entirely uncommitted on top of it.
- `dotnet build zenith.sln --no-restore`: succeeded, 0 warnings, 0 errors.
- `dotnet test zenith.sln --no-restore`: all suites green — 937 in `zenith.Tests`, plus 52/48/18/11/
  17/6 across `protocol-import.Tests`/`raknet.Tests`/`nbt.Tests`/`leveldb.Tests`/
  `zenith.PacketGenerator.Tests`/`diagnostics.Tests`. No flakes observed.
- Untracked `start.cmd` remains a local launch shortcut, unrelated to any phase's work — left
  untouched, same as Phase XXIV noted.

## Fixed / implemented

1. **No saturation system at all.** `Player.Saturation` (default 5, vanilla-parity) is now a real
   buffer: eating restores it at half a food's nutrition value, capped at the current hunger level
   (vanilla never lets a big meal "bank" more cushion than the hunger bar it's attached to); an
   exhaustion-threshold crossing depletes saturation first and only reaches `Hunger` once saturation
   is at 0 (`HungerSystem.AccrueExhaustion`).
2. **Exhaustion only came from sprinting.** Added two more sources through the same accumulator:
   mining (`BlockEditSystem`, one call per completed break, survival-only) and taking damage
   (`PlayerDamage.Apply`, survival-only — a Creative death only happens via the Void exception, which
   shouldn't feed the hunger cycle either). Walking's own smaller per-block cost is deliberately still
   deferred — it needs distance-moved tracking that doesn't exist yet in `MovementSystem`, unlike
   mining and damage which already have a single funnel each.
3. **XP was not reset on death.** `PlayerDamage.Apply` now calls `player.SetExperience(0, 0)` on the
   death transition, matching vanilla (XP resets on death regardless of gamemode; a Creative death
   only happens via the deliberate Void exception). `ExperienceTests.Death_resets_experience_to_zero`
   replaces the old test that asserted the opposite, undocumented behavior.
4. **Only XP + pose survived a reconnect; health/hunger/saturation/exhaustion silently reset.**
   `PlayerDataBlob` gains a v3 layout (v2 + 4 floats: health, hunger, saturation, exhaustion).
   `TryUnpack` reads each version band with `>=`, not `==`, so a v3 blob still gets its v2 XP fields —
   the original v2-only check (`== Version2`) would have silently skipped XP for any later version,
   the same class of stale-literal bug `ChunkPayloads.SubChunkVersion`'s own doc comment warns about.
   Old v1/v2 blobs simply have no saved vitals and load at fresh-spawn defaults, same treatment as
   missing XP on a v1 blob. `World.PersistPlayerData` persists `MaxHealth` instead of `0` for a
   player disconnected mid-death-screen (before `CompleteRespawn` ran) — a raw `0` would fail
   `TryUnpack`'s own corruption guard (`health <= 0f` rejected) on the next load, losing the entire
   blob including pose and XP to a validation failure that a live player's health can never actually
   trigger.
5. **Tool progression recipes.** Added the smallest chain the existing shapeless/2×2-grid recipe
   model can express: planks → stick, then wood/stone/diamond pickaxe/axe/shovel from
   directly-mined materials. Iron and gold are deliberately **not** wired to a recipe — their vanilla
   ingot form requires smelting, and Zenith has no furnace yet; a recipe that used their raw ore
   directly would simulate a feature that doesn't exist rather than reflect what a player can actually
   craft today. `CraftingDataBuilder` previously assumed every recipe input/output was a block
   (`StackId.IsBlock`) because the only two pre-existing recipes happened to both be block-to-block;
   it now resolves either a block name or an item-palette name, since tool outputs are items.
6. **Ore blocks dropped themselves instead of their real item.** `BlockLoot` is a small data lookup
   (not a loot-table framework) mapping the ores whose vanilla drop needs no smelling — coal,
   diamond, redstone, lapis, both stone and deepslate variants — to their dropped item. Iron, gold,
   and copper ore are deliberately excluded: their vanilla drop requires smelting a furnace doesn't
   yet provide, so they still drop the ore block itself (the correct smelting *input* for whenever
   that lands, not a silently wrong item today).

## Explicitly not touched this phase

Per the entity-fidelity audit's remaining survival gaps, and to keep this phase a coherent slice
rather than a full survival rewrite:

- **No per-item max-stack-size table** — every item still caps at 64 uniformly
  (`PlayerInventory.MaxStack`). Untouched; a real feature (data table + every merge/transfer/split
  call site), not something this phase's work happened to also need.
- **`BreakDuration`'s tool-tier-mismatch formula still resets to 1.0× speed** rather than vanilla's
  "still faster than bare hands, just no drop." Still a no-op in practice: `DigProfile` only checks
  `ToolKind` (pickaxe/axe/shovel), not `ToolTier` — no registered block gates on tier, so the new
  wood/stone/diamond tool recipes don't newly trigger this path. Remains exactly the latent gap the
  Phase XXIII-B audit described.
- **No movement speed/teleport-distance validation** — unrelated to survival vitals, untouched.
- **Walking exhaustion** — see point 2 above; needs `MovementSystem` distance tracking that doesn't
  exist.
- **No per-food saturation-modifier table** — vanilla varies the saturation-restore ratio per food;
  this phase uses one constant (`HungerSystem.SaturationRestoreRatio = 0.5`) applied to every food's
  nutrition value. A real per-food table is a bigger data-modeling change than this slice needed.
- **Effects are still not persisted across reconnect** — timed and expected to lapse naturally; a
  smaller, deliberately deferred gap than health/hunger/saturation/exhaustion, which don't have a
  "lapse naturally" story if silently dropped.

## Persistence compatibility

`PlayerDataBlob` v1 and v2 blobs both still load correctly under the new `TryUnpack` — verified by
existing round-trip tests plus new v3-specific ones (`PlayerDataBlobTests.Pack_unpack_round_trip`,
`..._with_vitals`, `TryUnpack_rejects_non_positive_persisted_health`). No migration step exists or is
needed: an old blob simply has no vitals band, and the reader already treats "band absent" as "use
spawn defaults," the same pattern already established for XP on a v1 blob.

## Validation

- `dotnet build zenith.sln --no-restore` — 0 errors.
- `dotnet test zenith.sln --no-restore` — all suites green (see Baseline for counts).
- No Bedrock client available in this environment. Logic, persistence round-trips, and wire-shape
  (`CraftingDataPacketTests`) are verified; real-client behavior (does the crafting table actually
  show these recipes, does eating visibly restore the hunger bar correctly) is **not** — see the
  manual validation checklist below.

## Manual validation checklist (needs a real client)

- Take fall/mob damage in Survival and confirm the hunger bar visibly ticks down faster than before.
- Mine for a while and confirm hunger still depletes (previously mining generated no exhaustion at
  all, so this was previously untestable by observation).
- Eat food below max hunger and confirm saturation (client-side "shine" on the hunger bar, if the
  client renders it) doesn't restore past the hunger level.
- Die with nonzero XP and confirm the XP bar reads zero on respawn.
- Disconnect mid-game with partial health/hunger, reconnect, and confirm both are restored instead of
  reset to full/20.
- Open the crafting table and confirm wood → stone → diamond pickaxe/axe/shovel recipes appear and
  produce a real, equippable tool.
- Mine coal/diamond/redstone/lapis ore and confirm the dropped item (not the ore block) lands in
  inventory or on the floor.
