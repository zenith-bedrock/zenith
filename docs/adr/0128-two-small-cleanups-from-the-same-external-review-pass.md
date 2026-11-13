# ADR §128 — Two small cleanups from the same external-review pass: explicit socket buffer config, Stopwatch over DateTime.UtcNow

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §128`.

**Choice:** Two independent, low-risk items from the same pass as ADR §124-127.

1. `RakNetServer.CreateListener` hardcoded `ReceiveBufferSize`/`SendBufferSize` to `int.MaxValue` with
   no documented reasoning. The literal value itself wasn't necessarily wrong (the OS clamps a request
   this large to its own real ceiling regardless), but it was unmeasured and un-tunable without a code
   change. Made it an explicit constructor parameter (`RakNetServer(int port, ulong? serverGuid,
   int socketBufferBytes = int.MaxValue)`) wired through a new `ServerConfig.Network.SocketBufferBytes`
   knob — default preserves current behavior exactly (no silent regression), but an operator can now
   tune it without a rebuild. Deliberately did **not** invent a new default number to replace
   `int.MaxValue` — the original review's own recommendation was to measure packet loss under burst
   across several candidate values against real network conditions before picking one, which isn't
   something a chat session can do; making it configurable is the correctly-scoped move until that
   measurement exists.
2. `RakNetServer.TickAsync`'s pacing loop used `DateTime.UtcNow` for elapsed-time measurement;
   `GameLoop`'s own tick loop already uses `Stopwatch.GetTimestamp()`/`GetElapsedTime()` for the same
   purpose. Matched it — `Stopwatch` isn't subject to system-clock adjustments (NTP sync, manual clock
   changes) the way `DateTime.UtcNow` is, and the two hot loops now measure elapsed time the same way.

**Status (ago 2026):** Shipped — `RakNetServer.cs`, `ServerConfig.cs`, `ZenithServer.cs`. New tests:
`Validate_rejects_non_positive_socket_buffer_size`, `Validate_accepts_an_explicit_smaller_socket_buffer_size`.
