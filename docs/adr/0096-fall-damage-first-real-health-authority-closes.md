# ADR §96 — Fall damage — first real Health authority, closes the biggest Survival honesty gap

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §96`.

**Superseded in part by §98:** fall-distance policy remains here; authoritative health mutation, death idempotence, peer replication, and atomic death loot now follow §98.

**Choice:** Give `Player.Health` (§40's spawn-attribute field, documented since as "damage pipeline Deferred") its first real write path: fall damage on landing. Before this, the *only* way a player's Health ever changed was void death setting it straight to 0 — there was no damage pipeline of any kind, no `VitalsSystem`, nothing tracking a fall in progress.

1. **`Player.FallPeakY`** — the highest feet Y reached since the player was last on-ground (reset to current Y on every landing and on respawn). Tracked in `MovementSystem.Tick`, using the same `AuthInput.OnGround` (`VerticalCollision`) field the pose-dirty logic already reads — no new wire dependency.
2. **On the `wasOnGround=false → IsOnGround=true` landing transition**, `fallDistance = FallPeakY - PositionY`. Below the vanilla-parity 3-block safe distance, no damage. Past it, `damage = floor(fallDistance - 3)` — one HP per block beyond the safe distance, matching the constant PowerNukkitX/vanilla use.
3. **Lethal fall reuses the exact death handshake void death already has** — extracted `MovementSystem.BeginVoidDeath`'s body into a shared `Kill(player, online, cause)` taking the cause string ("generic" for void, "fall" for this), so `DeathInfoPacket`, Survival death-loot dump, and the Respawn searching/ready sequence are identical regardless of cause. No new death machinery.
4. **Non-lethal damage sends `UpdateAttributesPacket`** via the existing `SendDefaultAttributes` (misleadingly named — it's just "current attributes snapshot", already reused for death) so the client's health bar reflects the hit immediately.
5. **Creative is immune** — matches vanilla's creative fall immunity and Zenith's existing Creative-keeps-inventory convention on death; checked before computing damage, not after.

**Why this is the "biggest gap" closed, not the whole pipeline:** hunger, drowning/air supply, and any damage source other than a fall or the void still do not exist — `Player.Health`'s doc comment is updated to say so explicitly rather than silently going quiet about it. This ADR is deliberately the smallest real slice of the Deferred "VitalsSystem + Player vitals authority" line, not an attempt to close it in one PR.

**Non-goals:** Hunger tick / food; drowning / `AirSupply`; any damage source besides fall and void; fall damage from `MoveActorAbsolute`-driven entities (falling blocks, §95) landing on a player — that's the falling-block side's own `damager` interface (Dragonfly has it for anvils), not implemented; a dedicated `VitalsSystem` GameLoop registration — this lives inline in `MovementSystem` because it only reacts to a transition that system already observes every tick, and a second system re-deriving the same `wasOnGround`/`IsOnGround` comparison would be duplicated state, not real separation.

**Verification:** New `FallDamageTests.cs` (5 tests): short fall no damage, damage scales at 1 HP/block past the safe distance, a lethal fall kills and produces `DeathCause == "fall"`, Creative immunity, and respawn resets `FallPeakY` so the next fall is measured fresh (not against a stale pre-death peak). 530/530 zenith.Tests green, 642/642 full suite. **Not yet live-verified** — no `zenith-smoke-bot` script exercises a real fall yet (unlike gravity/respawn, which already had one); flagged for whoever next touches this path to add one, same honesty stance §90 took for `ItemStackRequestPacket`.

**Status (ago 2026):** Shipped — `Player.FallPeakY`, `MovementSystem.Kill`/`ApplyFallDamage`, 5 new tests. Unit-verified only.
