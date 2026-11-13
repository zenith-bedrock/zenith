# ADR §110 — Ore harvest gates on `ToolTier`, not just `ToolKind` — closing a live, not latent, gap

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §110`.

**Choice:** `DigProfile` gains a `MinHarvestTier` field (default `ToolTier.None`, meaning "any tier of
the right kind" — every pre-existing profile keeps exactly its old behavior). Ore profiles now set it
to a vanilla-adjacent minimum: coal needs any pickaxe (Wood+), iron/copper/lapis need Stone+,
gold/diamond/redstone need Iron+. `BreakDuration.IsHarvestable` checks both `ToolKind` and `Tier`;
`BreakDuration.BreakTicks` no longer resets speed to 1.0× when the kind matches but the tier doesn't
— a wood pickaxe on diamond ore now digs at wood-pickaxe speed but drops nothing, matching vanilla's
"still faster than bare hands, no drop" rule instead of behaving identically to bare hands.

**Why this wasn't optional cleanup:** the Phase XXV findings doc inherited an assumption from the
Phase XXIII-B audit that this was a **latent** no-op — "no registered block has a tier gap yet." That
assumption was already false the moment Phase XXV's tool recipes shipped: `DigProfiles.cs` had ore
`RequiresCorrectToolForDrops=true` entries with `HarvestTool=Pickaxe` and no tier field at all, so
*any* pickaxe (including the wood ones those same recipes just made craftable) harvested diamond
identically to a diamond pickaxe. Phase XXVI's audit caught this as a live, exploitable gap, not a
hypothetical one — see the findings doc for the exact trace.

**Why gate at the `DigProfile`/`BreakDuration` layer instead of `RecipeRegistry` or the tools
themselves:** tier is a property of *what the block requires*, not of the tool — the existing
`HarvestTool`/`EffectiveTool` split already lives here for the same reason (kind is also a
block-side requirement). Keeping `MinHarvestTier` as a fourth field on the same struct, defaulting to
"no gate," extends the existing sparse-table pattern instead of introducing a parallel one.

**Non-goals:** no change to non-ore blocks (Stone/Cobblestone/Deepslate keep their old "any pickaxe"
behavior — vanilla's own minimum for them is Wood, which every registered tool already clears). No
Netherite/Gold tier modeling (neither is craftable yet — `ToolTier` still has no `Gold` member). No
generic "block hardness class" abstraction — four ore-tier bands is not enough repetition to
generalize past a per-block field.

See `docs/history/phases/phase-xxvi-survival-gameplay-findings.md` for the full audit this came out
of, alongside the Enderman yaw-orientation fix and the entity behavior matrix.
