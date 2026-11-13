# ADR §44 — Tick peer egress (dirty pose + Online snapshot + GamePacket batch)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §44`.

**Choice:** `MovementSystem` applies AuthInput then fans Absolute only when pose floats (XYZ+Pitch+Yaw+HeadYaw) **or on-ground** differ from `LastReplicated*` (exact equality; look-only must still fan — same AuthInput channel). `PlayerManager.Online` is a single allocating snapshot (`IReadOnlyList`); Tick / nested fan-out capture once (`var online = _players.Online`) and reuse. Dirty movers aggregated per peer via `EntityProtocol.SendMoveAbsolutes`; tick `UpdateBlock`s via `WorldProtocol.PublishUpdateBlocks` — both reuse `SendDataPacket(params)` (one envelope / peer). No VisibilitySystem.

**Why:** Idle AuthInput was O(n²) Absolute spam; repeated Online snapshots nested in loops allocated per mover; one Absolute/UpdateBlock per call wasted RakNet frames. Batch is frame reduction, not a §24 zero-alloc claim.

**Smoke:** Idle LAN — peers idle without Absolute flood; look-turn still updates peer yaw; multi-place same tick → one envelope per peer.

**Adendo (jul 2026 — join settle):** Dirty-check left late joiners with AddPlayer only until the subject moved — peer entities often floated until Absolute. `PlayerVisibility.SendAddPlayer` now follows with `MoveActorAbsolute` (`FLAG_ON_GROUND`, feet + network offset) for that recipient so new viewers settle without forcing idle Absolute spam.

**Adendo (jul 2026 — Absolute ON_GROUND):** Continuous Absolute used `flags=0`. Bedrock then applies local gravity to remote players; after they stop moving, peers look “frozen”/sunk while the stationary client still sees others fine. AuthInput **VerticalCollision** (bit 50) → `Player.IsOnGround` → `MoveActorAbsolute.FLAG_ON_GROUND`. On-ground changes dirty Absolute even when XYZ/look unchanged.
