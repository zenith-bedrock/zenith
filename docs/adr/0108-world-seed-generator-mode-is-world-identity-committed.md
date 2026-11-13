# ADR §108 — World seed/generator mode is world identity, committed once and never silently overridden

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §108`.

**Choice:** the first time a world is ever opened, its configured `world.terrain`/`world.seed` is
committed to storage (`WorldMetadata`, a new fixed `wm:` LevelDB key) as that world's permanent
identity. On every later boot, the *committed* identity wins over whatever `zenith.yml` currently
says — a mismatch is logged loudly as a misconfiguration to fix, not silently applied.
`WorldIdentity.Reconcile` (`src/zenith/World/WorldIdentity.cs`) is the one place this happens; it
returns the resolved identity explicitly rather than mutating the caller's config object.

**Why this needed a decision, not just a bug fix (Phase XXIV):** `World.GetOrCreateColumnAsync` never
persists generated base terrain on an ordinary miss (ADR §45 predates this and is unchanged) —
every chunk a player hasn't overlaid regenerates from the live provider on every load. Before this
ADR, the seed powering that provider was a plain config value re-read fresh every restart, with no
persistence and no restart contract at all. A changed `world.seed` (typo, copy-paste from another
server's config, etc.) would silently regenerate every un-visited chunk as a different world sitting
directly next to already-explored, still-persisted-overlay terrain from the old one — a real,
evidenced data-integrity risk, not a hypothetical one, and exactly the kind of "silent obvious
corruption" this project's own conventions exist to prevent.

**Why reject-and-warn instead of auto-migrate or auto-adopt:** an already-generated world's overlays
(player edits) are only meaningful in the context of the base terrain they were placed against. Auto-
adopting a new seed would corrupt that context for every un-visited chunk without the operator ever
choosing to. Auto-migrating (regenerating/re-committing everything under the new seed) is exactly the
"full world-version migration" the brief scoped out of this phase — a real feature, not a one-line
fix. Reject-and-warn is the smallest change that closes the actual risk: the world keeps behaving
exactly as it always has, and the operator gets a clear, actionable log line instead of silent drift.

**Non-goals for this ADR:** no generator-version field exists yet — a future *algorithm* change (not
just seed/mode) to the noise pipeline itself would still silently regenerate un-visited terrain
differently. Recorded as a known future gate in `docs/world-generation.md`, not solved here; add a
`generatorVersion` field to `WorldMetadata` if/when that risk becomes real. This ADR also does not
migrate flat-world saves that predate it — such a world simply commits its current `flat`/default
seed the first time it boots under this code, same as any other "brand-new" world from
`WorldIdentity`'s point of view.

See `docs/history/phases/phase-xxiv-overworld-generation-findings.md` for the full audit this
decision came out of, and `docs/world-generation.md` for the living contract this ADR backs.
