# ADR §134 — `RakNetSession`'s UDP send stays inside `_sessionLock` — accepted, not fixed

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §134`.

**Context:** A cross-codebase audit (Session/Protocol/Packets/RakNet, done in parallel with two other
areas the same pass) flagged that `RakNetSession.SendQueueLocked` and `FlushAcknowledgeLocked`
(`src/raknet/RakNetSession.cs`) call `Server.Send(...)` — which does a synchronous
`_listener.Client.SendTo(...)` — while holding `_sessionLock`. `AGENTS.md` states, in absolute terms,
"Never hold a lock across await, I/O, protocol send, or external callback." This is a literal
instance of that: `SendTo` is I/O, done under lock.

**Why this isn't being changed:** the code already carries a deliberate, reasoned comment
(`RakNetSession.cs`, at the `SendQueueLocked` call site): *"Encode sob o lock; UDP I/O curto mantido
aqui para evitar reentrância frágil com QueueFrame → SendQueue aninhados"* — `QueueFrameLocked` can
itself call `SendQueueLocked` (when a batch would exceed MTU, or on `Priority.Immediate`), and `lock`
in C# is thread-reentrant, so keeping the whole chain under one lock is what makes that nested-call
shape safe without restructuring it into a snapshot-then-send or command-queue pattern. Moving the
`Send` call outside the lock would require exactly that restructuring — collecting encoded buffers
while locked, releasing, then sending — a real, non-trivial change to the hot path of the only
network transport this server has, undertaken here with no concrete failure or profiled contention
driving it. `SendTo` on a UDP socket is normally a fast, non-blocking-in-practice syscall (UDP has no
handshake/ACK to wait on); the realistic risk is a full OS send buffer briefly stalling this session's
lock for other callers (a concurrent `Tick()`, `HandleAck`, or gameplay-side `SendFrame` on the *same*
session) — not a deadlock, not data corruption, just serialization for the duration of one syscall.

**Choice:** Accept this as a deliberate, narrow exception to the stated rule, for `RakNetSession`
specifically — not a general license to hold locks across I/O elsewhere. Recorded here so the next
person auditing this file finds a reasoned decision instead of re-discovering the same tension and
either leaving it undocumented again or "fixing" a working, load-bearing transport path without
evidence it's actually costing anything.

**Revisit when:** real profiling (not a static-rule audit) shows session-lock contention correlated
with UDP send latency under load — at that point, the concrete fix is almost certainly a
snapshot-then-send pattern (build the encoded `FrameSet`/`AcknowledgePacket` bytes while holding the
lock, release, then call `Server.Send` outside it) rather than removing the lock's coverage of the
encode step, which still needs to happen atomically with the sequence-number/backlog-byte bookkeeping
it's interleaved with today.

**Non-goals:** restructuring `RakNetSession`'s locking model preemptively; any change to
`QueueFrameLocked`/`SendQueueLocked`'s reentrancy shape without a concrete driving case.

**Status (ago 2026):** Documented, no code change — the trade-off was already correct, it just wasn't
recorded anywhere besides one inline comment.
