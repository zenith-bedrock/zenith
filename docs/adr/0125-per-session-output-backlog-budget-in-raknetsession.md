# ADR §125 — Per-session output backlog budget in RakNetSession

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §125`.

**Context:** Same external-review pass as ADR §124. This claim also held up on inspection: nothing
bounded `RakNetSession.OutputFrames` (pending send) + the sent-awaiting-ACK retransmission buffer —
a peer that stops draining (slow connection, or one that never ACKs at all, malicious or not) could
let both grow without limit, unbounded RAM per session. `OutputFrames` turned out to mostly self-limit
already (`QueueFrameLocked` flushes at every MTU boundary, not once per tick), and the highest-volume
producer (chunk streaming) already has its own bounded channel (`NetworkSession.WorldStreamQueueCapacity
= 64`) — so the actual unbounded structure is specifically the retransmission buffer, which only
shrinks on ACK or the 1500ms stale-resend cycle, not every tick. That field was previously named
`OutputBackup` — renamed to `UnacknowledgedFrameSets` in the same pass (raised independently, while
reviewing this fix): "backup" reads as "redundant copy kept for safety," but it's a retransmission
buffer, a materially different meaning. `ResendStaleBackupLocked` → `ResendStaleUnacknowledgedLocked`
for the same reason.

**Choice:** Same failure family as `MAX_CONCURRENT_FRAGMENTED_MESSAGES`/`MAX_FRAGMENT_COUNT_PER_MESSAGE`
(input-side fragment limits, already in place) — add the missing output-side counterpart. Added
`_unacknowledgedFrameSetsByteLength`, an incremental counter mirroring `_outputFramesByteLength`'s
existing pattern, maintained at all four `UnacknowledgedFrameSets` mutation points (`SendQueueLocked`
add; `ResendStaleUnacknowledgedLocked`/`HandleAck`/`HandleNack` remove). `SendFrameLocked` — the sole
entry point for *new* content (retransmission paths call `QueueFrameLocked` directly and never
increase the total, just cycle it) — now disconnects the session if
`_outputFramesByteLength + _unacknowledgedFrameSetsByteLength` exceeds `MaxOutputBacklogBytes` (8 MB,
generous against any real gameplay burst). New `DisconnectReason.OutputBacklogExceeded` distinguishes
this from a plain input timeout in logs. A `_overBudgetDisconnecting` guard flag prevents the
disconnect packet's own `SendFrame` call (which re-enters `SendFrameLocked` on the same thread —
reentrant-safe, `lock` is per-thread-recursive in .NET) from tripping the same check and blocking its
own exit frame.

**Status (ago 2026):** Shipped — `RakNetSession.cs`, `DisconnectReason.cs`. New tests:
`OutputBacklogBudgetTests` (a peer that never ACKs gets disconnected once the budget is exceeded; a
peer that ACKs promptly never approaches it, proven over the same total traffic volume).
