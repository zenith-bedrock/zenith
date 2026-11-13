# ADR §136 — Vehicle dismount sends the rider an authoritative MovePlayer teleport — resolves Phase XX's open question

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §136`.

**Context:** Phase XX's runtime-relationships findings doc flagged, explicitly and by name, that
`FollowOccupant`'s per-tick `rider.PositionX/Y/Z = cart position` plus `SetActorLinkPacket` alone
("the assumption that a client auto-follows a linked vehicle without a separate teleport") was
**untested against a live Bedrock client** — derived from protocol documentation, not verified. A
live playtest report (not another audit) surfaced the real answer: a player who dismounts a Minecart
gets stuck — unable to attack or interact with anything afterward. `SetActorLinkPacket` visually
attaches/detaches the rider for peers; it does not resync the rider's *own* client's local
position/camera prediction. With no explicit correction, the departing rider's client silently
disagreed with the server about where it actually was for the entire ride, and every subsequent
server-side reach/interact check (attack range, block interaction) measured against that wrong
position — reading exactly as "stuck, can't do anything," matching the report.

**Choice:** `MinecartSystem.Dismount` (the single idempotent teardown already called from every exit
path — voluntary, disconnect, despawn) and the death-path's inline dismount in `TryApplyDamage` both
now call a shared `SendDismountTeleport` helper: `EntityProtocol.SendMovePlayerTeleport` to the
departing rider's own session, using the player's already-current position/pitch/yaw. This is the
same "local camera snap" mechanism `MovementSystem.ApplyRespawn` already established (ADR §41) for
exactly this class of problem — an authoritative reposition happening outside the normal
AuthInput-in, pose-out tick flow needs an explicit client-facing correction, not an implicit hope
that the client's own prediction lines up.

**A second, related bug found and fixed in the same pass:** `MovementSystem`'s riding branch
discarded the *entire* consumed `MovementInputState` while mounted, including Pitch/Yaw — not just
position. Bedrock does not lock a rider's camera; the player keeps looking around freely the whole
ride. Discarding Yaw meant `MinecartSystem.ApplyRiderSteering` (which reads `rider.Yaw` every tick to
steer) was actually steering by whatever direction the player faced at the *moment of mounting*, not
their current look direction, contrary to its own doc comment — and would have fed a stale Yaw into
this ADR's dismount teleport, causing a jarring camera snap on exit instead of a clean resync.
`MovementSystem` now applies Pitch/Yaw/HeadYaw from the consumed input while mounted and only skips
position (still vehicle-driven).

**Why this belongs in `Dismount`/`TryApplyDamage`, not `FollowOccupant`:** sending a teleport every
tick *while* mounted would fight the client's own vehicle-follow rendering (the exact "fighting
`SetActorLinkPacket` with server-pushed teleports was deliberately not attempted" non-goal Phase XX
already recorded) and spam a packet the client doesn't need until the ride actually ends. One
teleport, at the moment authoritative control hands back to the client, is the minimal correction.

**Non-goals:** no change to `FollowOccupant`'s per-tick position mirroring (still correct — it's the
*source of truth* this fix now correctly hands back at the end); no analog rider-driven steering (Phase
XX's non-goal stands); no boats — none exist in this codebase yet (`Boat` yields zero hits anywhere in
`src/zenith`; grep confirmed before any fix landed).

**Status (ago 2026):** Shipped — `MinecartSystem.cs`, `MovementSystem.cs`. Still logic-level tested
only, same caveat Phase XX already carried: no client-in-the-loop harness exists to verify the fix
against a real Bedrock client either, only that it now matches the same pattern (`SendMovePlayerTeleport`)
this codebase's one other confirmed-working case (respawn) already uses successfully.
