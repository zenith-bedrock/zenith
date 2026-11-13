# ADR §133 — `RecipeRegistry`/`CreativeCatalog`: compose → freeze → gameplay reads only

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §133`.

**Context:** A broad plugin-readiness proposal (entity registry, config-driven mob disable, an
AI-less "generic actor" primitive, new cancellable `EventBus` events, an `IServerExtension`
registration seam) was scoped down hard across three rounds of review before any of it shipped.
Nearly all of it was withdrawn — no real current consumer existed for any of it, and building it
anyway would have been exactly the "wrong extension surface early becomes forever" trap `docs/dx.md`
already names (see the Hytale-research-informed audit that produced this correction: Hypixel
Studios' own decompiled server shows first-party gameplay dogfoods extensible foundations, but every
one of its modules was built from a real, shipping feature's real requirements — never a hypothetical
one). What survived, on its own merits, was much narrower: `RecipeRegistry.Register`
(`src/zenith/Gameplay/Inventory/RecipeRegistry.cs`) and `CreativeCatalog.RegisterBlock`/
`RegisterTool` (`src/zenith/Gameplay/Inventory/CreativeCatalog.cs`) both silently overwrote on a
duplicate net id (`_byNetId[id] = value`, a bare dictionary indexer with no surrounding commentary) —
a real, live correctness gap in already-shipping code, independent of any plugin question. A
sibling registry, `DigProfiles` (`src/zenith/World/DigProfiles.cs`), was audited alongside these two
and found to have the *opposite* property: its own doc comment ("Contributor / boot registration...
Prefer `OverrideForTests` in unit tests so rows do not leak across cases") plus a separate,
purpose-built `OverrideForTests` sibling are real evidence its overwrite-on-register behavior is
deliberate, not debt. `BlockLoot` (`src/zenith/World/BlockLoot.cs`) was also audited and found to have
no evidence either way — no doc comment, no `OverrideForTests`-shaped sibling — so no change was made
to it either; absence of evidence is not evidence for a decision one way or the other.

**Choice:** `RecipeRegistry`/`CreativeCatalog` now enforce, at runtime, the invariant their lifecycle
was already informally following: **compose → freeze → gameplay reads only.** A duplicate net id
passed to (the still-`private`) `Register`/`RegisterBlock`/`RegisterTool` throws
`InvalidOperationException` immediately, matching `ServerConfig.Validate()`'s existing "invalid state
at boot → throw" convention (e.g. `world.spawn-chunk-radius` bounds checking) rather than silently
degrading. A new `internal void Freeze()` on each class — called once by `ZenithServer`'s constructor
right after `CreateDefault()`, before `GameLoop` starts ticking — makes any further registration call
throw too, so the boot/gameplay boundary is a real, enforced line, not just a convention. A second,
subtler gap closed alongside this: `RecipeRegistry.SnapshotRecipes()` and `TryGet()` both used to hand
callers the *same backing array reference* stored inside a `Recipe`'s `Inputs` field — so `Freeze()`
alone would have stopped new entries without making existing ones actually immutable. Both methods now
return `recipe.Inputs.ToArray()` (a defensive copy), symmetric with the write-side copy `Register`
already takes on the way in — "frozen" now means the stored data genuinely cannot change from outside,
not just that no new keys can be added.

This is **not** claimed as a universal shape for every registry in the codebase. `DigProfiles`/
`BlockLoot`'s static, lazy-loaded lifecycle is different from `RecipeRegistry`/`CreativeCatalog`'s
fresh-per-instance-object-at-boot lifecycle, and stays different — `DigProfiles` keeps its
intentional overwrite semantics untouched, `BlockLoot` is untouched entirely. The shared *contract*,
where it actually applies, is semantic (register during composition, freeze, read during gameplay) —
not a shared base class, not a shared interface, not forced uniformity for its own sake.

`Register`/`RegisterBlock`/`RegisterTool` stay `private` — **not** promoted to `internal` — because no
code outside these two classes' own `CreateDefault()` registers a recipe or creative entry today.
Promoting them now, before a real second caller exists, would itself have been the "extension surface
before a consumer" mistake this whole review was catching. **When the first real first-party
composition consumer needs to add content from outside these classes, promote the narrow registration
method to `internal` at that time — not before.** `internal` is assembly-local and narrow, so this
isn't a large future risk either way — but there is no cost to waiting, since the promotion itself is
a one-line, fully backward-compatible change when that day comes.

**Non-goals:** a JSON/data-file content format for recipes/loot (every registry here stays plain C#
`Register` calls at boot, matching the existing pattern — a data-file format is a separate, larger,
currently-unjustified decision). An id allocator for externally-minted net ids (no real consumer mints
ids from a pool yet; the new duplicate-throw is what makes a future manually-assigned id safe). Any
`RegisterExtensionX`-shaped naming (there is no extension/plugin API yet). Any change to
`BlockLoot`/`DigProfiles`. A generic `EntityRegistry`, `IServerExtension`, spawn-policy config, or new
`EventBus` event types — all separately proposed and separately withdrawn this same review, for lack
of a real current consumer; see the review's own "Deferred" notes if any of that work resumes later.

**Verification:** `RecipeRegistryTests`/`CreativeCatalogTests` — `CreateDefault()`'s curated content
unchanged; `TryGet`/`SnapshotRecipes` callers mutating the array they received do not affect what a
later call returns (proves the read-side clone actually closes the freeze escape). Full suite:
1,053 → 1,058.

**Status (ago 2026):** Shipped — `RecipeRegistry.cs`, `CreativeCatalog.cs`, `ZenithServer.cs`
(explicit `.Freeze()` calls), `RecipeRegistryTests.cs`, `CreativeCatalogTests.cs` (new).
