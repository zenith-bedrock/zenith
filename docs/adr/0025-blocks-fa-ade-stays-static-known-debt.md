# ADR §25 — Blocks façade stays static (known debt)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §25`.

**Choice:** Keep `Blocks` as a static Load/EnsureLoaded façade over `block_palette.nbt` network ids, even after dirt/planks/log/sand/chest variety. Do **not** migrate to `ServerContext` in the same leva as chest/craft.

**Why:** Every hot path (`PlayerInventory` defaults, `BreakTicks`, wire name lookup) already calls the façade; injecting palette refs everywhere is a refactor without product gain while recipes/chest stabilize.

**Deferred:** Inject runtime-id table via `ServerContext` when a second palette / dimension forces it.
