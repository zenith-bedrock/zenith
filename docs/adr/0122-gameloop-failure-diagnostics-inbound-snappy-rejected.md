# ADR §122 — GameLoop failure diagnostics + inbound Snappy rejected instead of misparsed

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §122`.

**Choice:** Two small, independent fixes from the ADR §80 non-goals line ("resource-pack / snappy
TODOs... left for their own pass") — investigation split that single line into two differently-sized
items; only the Snappy one turned out to be small (the resource-pack half is a currently-nonexistent
feature — empty pack list by deliberate design, `ResourcePackStackPacket` still has `TODO: behavior
packs`/`texture packs`/`experiments` — and is deferred as its own product-scope decision, not touched
here).

1. `GameLoop.TickOnce`'s fatal-system-failure log line only ever said which system threw, with no
   sense of *when* in the server's life that was. Enriched the existing `_logger.Error` call with
   `tick=`/`onlinePlayers=`/`elapsedInTick=` alongside the system name — same `catch`/`throw;`
   shape, same exception identity and stack, purely additive context. Entity/position context was
   considered and dropped: `GameLoop` only ever sees `(GameClock, IReadOnlyList<Player>)`, never a
   specific entity/position, so adding that would mean instrumenting every `IGameSystem`
   individually — a much larger, differently-scoped change than "enrich the diagnostics that already
   exist at this layer."
2. `NetworkSession.HandleGamePacket`'s decompression switch recognized the Snappy prefix byte
   (`PacketCompression.SNAPPY = 0x01`) well enough to consume it off the stream, then fell through
   the `// TODO: snappy compression` gap straight into subpacket parsing — silently treating
   still-compressed bytes as plaintext packet data. `LoginSessionHandler` only ever advertises ZLIB
   in `NetworkSettingsPacket`, so a compliant client never sends this prefix; a malicious/buggy one
   trivially could. Rather than implement a decompressor for an algorithm the server never
   negotiates, the gap now throws `NotSupportedException` explicitly the moment the prefix is seen —
   fail-fast and legible, instead of misparsing.

**Status (ago 2026):** Shipped — `GameLoop.cs`, `NetworkSession.cs`. New/updated tests:
`GameLoopTests.System_failure_stops_the_authoritative_tick_loop` (asserts the four new fields);
`GamePacketDispatchTests.Snappy_compressed_batch_is_rejected_instead_of_misparsed`.
