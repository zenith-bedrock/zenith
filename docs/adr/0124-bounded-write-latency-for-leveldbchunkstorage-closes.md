# ADR §124 — Bounded write latency for LevelDbChunkStorage — closes the "hard kill loses recent inv:/ct:" gap

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §124`.

**Context:** External review (two independent ChatGPT passes over the repo, cross-checked against
current code before acting on anything — several of their other claims were stale or simply wrong,
e.g. reported ~2,800-line `RakNetSession.cs`/~1,500-line `RakNetServer.cs` against an actual 717/365,
and a "GameLoop swallows exceptions" claim that was already false). One claim held up: `alpha-gate.md`
already flagged, from a real manual test, that recent `inv:`/`ct:` writes aren't guaranteed to survive
a hard kill (`kill -9`/`taskkill /F`), unlike overlays.

**Root cause, found by tracing the write path, not by re-running the manual test:** `PutInventoryAsync`
/ `PutArmorAsync` / `PutPlayerDataAsync` / `PutChestAsync` (and `PutWorldMetadataAsync`) each spun up
their own `Task.Run` per call, tracked only for `FlushAsync` to await at graceful shutdown. Overlays
were never affected — they already had a dedicated worker thread draining a queue every 50ms. The gap
was specifically the `Task.Run` scheduling delay: between "gameplay called Persist" and "the queued
`Task.Run` actually got a thread-pool thread and executed `_db.Put`," a hard kill loses the write —
and that window had no documented (or even measured) bound, since it depends on thread-pool queue
depth at the moment, not a fixed interval. This is a distinct issue from ZLDB's own
`Journal.Flush(sync: false)` (which reaches the OS on every write regardless — durable against a
process-level kill already, just not against power loss, which `kill -9` testing doesn't exercise).

**Choice:** Generalize the overlay-only write queue into one shared queue+worker covering every
`LevelDbChunkStorage` Put/Delete (overlay, chest, inventory, armor, playerdata, world metadata).
`EnqueueWrite` is now the single choke point; the worker polls every 50ms
(`LevelDbChunkStorage.WriteWorkerPollMs`), same interval the overlay worker already used. This turns
an unbounded, load-dependent loss window into a concrete, documented, testable one: a mutation can
only be lost to a hard kill if it lands in the ≤50ms gap before the next drain. `FlushAsync` and
`Dispose` still force-drain everything unconditionally, unchanged in guarantee. The read-after-write
race guard (S39b/§60 — fast quit→rejoin must see the just-written blob) moved from "await the
tracked `Task.Run`s" to "force-drain the queue inline," which is simpler (one mechanism, not two) and
removes the last use of the old `_pendingDiskTasks`/`Track()` machinery entirely.

**Status (ago 2026):** Shipped — `LevelDbChunkStorage.cs`. New tests:
`LevelDbChunkStorageDurabilityTests` (proves the periodic worker alone, independent of `FlushAsync`
or a read-triggered drain, lands chest/inventory/armor/playerdata writes; proves `Dispose` without an
explicit flush still drains everything queued). `docs/alpha-gate.md`'s crash-soft-check line updated
to point at this concrete bound instead of "not guaranteed."
