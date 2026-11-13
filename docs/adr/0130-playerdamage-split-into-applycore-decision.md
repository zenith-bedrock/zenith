# ADR §130 — `PlayerDamage` split into `ApplyCore` (decision) + `PlayerDamageResult.Conclude` (peer-list-dependent tail)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §130`.

**Context:** The follow-up to ADR §129's deferred item — the user explicitly asked for the larger
"separate gameplay decision from replication" refactor despite the real risk flagged there (death
handling has several coupled side effects; naively splitting across 9 call sites risks desync).
Investigated the review's three concrete examples first: `BlockDigSystem.ApplyDig` (a private,
same-file helper — not a cross-boundary leak, and its 4 branches each pair one mutation with its own
specific notification, no clean split exists) and `FloorDropSystem` (already correctly structured —
`Inventory.TryAddUpTo` never took `online`) don't hold up as real decision/replication splits on
close reading; only `PlayerDamage.Apply` does.

**Choice, and the design correction found mid-implementation:** Rejected two independent static
calls (`ApplyCore(...)` then a separate `Replicate(...)` the caller must remember to pair — a
footgun, not a fix) in favor of `ApplyCore` returning a `PlayerDamageResult` whose own
`.Conclude(online)` the caller chains in the same expression — one statement per call site,
structurally hard to skip. All 9 callers (`CreeperSystem`, `EndermanSystem`, `GolemSystem` ×2,
`PlayerMeleeSystem`, `ProjectileSystem`, `SpiderSystem`, `ZombieSystem`, `EffectSystem`,
`HungerSystem`, `MovementSystem`) migrated.

While implementing, found the initial name — `Replicate` — oversold what the method does. Tracing
`ChestLidFanout.ReleaseOpener`/`FloorDropFanout.TryDropDeathLoot` (both called from the Died branch)
showed they aren't pure notification: both bundle a real state mutation (closing the chest in
`world.Chests`, moving inventory into `world.FloorDrops`) atomically with their packet send, by
existing design elsewhere in this codebase (`TryDepositBatch`'s own doc comment: "keeps an inventory
transaction... atomic"), and both need `online` for that same commit-and-publish. A death is not
actually finished — loot isn't dropped, an open chest isn't released — until this step runs; it is
required for that outcome, not an optional/skippable notification. Renamed to `Conclude` and
corrected the doc comments to say this plainly, rather than ship a name that implies "safe to skip"
for a method that, for one of its two outcomes, is load-bearing. `BlockDigSystem`/`FloorDropSystem`
got extract-method passes only (named private methods, mutation+notification kept paired) — no
decision/replication split, since none cleanly exists in either.

**Status (ago 2026):** Shipped — `PlayerDamage.cs` and its 9 callers; `BlockDigSystem.cs`
(`ApplyActivityPing`/`ApplyAbort`/`ApplyNewBreak`); `FloorDropSystem.cs`
(`NotifyDespawns`/`TryPickup`/`NotifyPickup`). New test:
`PlayerDamageGameModeTests.Creative_player_takes_no_melee_damage`/
`Creative_player_takes_no_projectile_or_generic_damage` now call `ApplyCore(...).Applied` with no
`Conclude`/`online` involved at all — the actual claim ("apply damage to Steve doesn't need to know
about every other online player") made directly verifiable rather than just asserted.
`MovementSystem`'s own movement-fanout batching (`_dirtyPose` → `foreach peer in online`) remains a
separate, larger, not-yet-scoped follow-up — not touched here.
