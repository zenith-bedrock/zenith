# ADR §112 — `IBlockRegistry` introduced as an interface seam ahead of the static façade, not a migration

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §112`.

**Choice:** Add `IBlockRegistry`/`BlockRegistry` (`src/zenith/World/`) and expose `ServerContext.BlockRegistry`.
`BlockRegistry` is a thin wrapper delegating every member to the existing static `Blocks` façade — same
underlying palette state, zero behavioral change. None of `Blocks`' 234 production call sites or 923 test
call sites (47 test files call `Blocks.Load`/`EnsureLoaded`/`ResetForTests` directly for isolation) were
touched or need to be.

**Why:** §25 kept `Blocks` static and named its own migration trigger — a second palette/dimension, or the
block/inventory domain being touched again for a reason big enough to justify moving every call site. Neither
has fired. But the blast radius of a full migration (234 production + 923 test call sites, several in
per-tick hot paths — `GravitySystem`, `GroundMobMovement`, `ProjectileSystem`) is large enough that doing it
opportunistically, one call site at a time whenever a PR happens to touch a neighboring line, risks landing
half-migrated. Dragonfly's `world.BlockRegistry` is the applicable precedent: a static, `init()`-populated
registry kept behind an interface specifically so tests (and, here, future call sites) can depend on the seam
without forcing the underlying storage to change on the same day.

**What this is not:** not a reversal of §25, not a schedule for migrating the 234 call sites, not a claim
that `Blocks` becoming a static façade was a mistake. §25's original trigger is unchanged and still governs
when (if ever) existing call sites move off `Blocks.*` onto `Context.BlockRegistry`.

**Status (ago 2026):** Shipped — `IBlockRegistry.cs`, `BlockRegistry.cs`, `ServerContext.BlockRegistry`. New
code that wants an instance-injectable, test-swappable block classification seam should take `IBlockRegistry`
via `ServerContext` instead of calling the static façade directly; existing call sites are unchanged and stay
valid.
