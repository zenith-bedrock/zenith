# ADR §113 — `InGameSessionHandler` dispatch becomes a registry map, scoped to this one handler

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §113`.

**Choice:** Replace `InGameSessionHandler.HandleDataPacket`'s `switch (header.Id)` (15 named cases, 1 bare
no-op case, 7 grouped no-op cases, `default`) with a `private static readonly FrozenDictionary<int,
PacketHandler> Handlers` (`System.Collections.Frozen`), built once via `BuildHandlers()`, where
`PacketHandler` is a named delegate (`void(NetworkSession, ref BinaryStream)` — `BinaryStream` is passed by
`ref`, so `Action<>` cannot represent it). `FrozenDictionary` over a plain `Dictionary`: this table is built
exactly once and read on every inbound in-game packet — the read-heavy, build-once shape `FrozenDictionary` is
designed for, trading its slower one-time construction for faster lookup than `Dictionary` on every
subsequent read. `HandleDataPacket` becomes a two-line lookup-and-invoke. No handler method body changed;
every existing `private static void HandleXxx(NetworkSession, ref BinaryStream)` across the four
`InGameSessionHandler` partials is reused unchanged as a dictionary value. The 8 cases that previously just
`return true` with no action are grouped into a `NoOpPacketIds` array mapped to one shared `HandleNoOp` in a
loop inside `BuildHandlers()` — preserving the original switch's compact "these N ids, one behavior" shape
instead of repeating `HandleNoOp` on 8 separate dictionary-literal lines. Registration uses `.Add(key, value)`
(via `{ key, value }` collection-initializer syntax and `map.Add(...)` in the loop), not indexer assignment
(`[key] = value`) — a duplicate key throws `ArgumentException` at static init instead of silently overwriting
one handler with another at runtime, preserving the compile-time duplicate-`case`-label safety the switch
had (caught in review before this shipped: the first draft used indexer assignment, which built and passed
tests but would have accepted a duplicate `ProtocolInfo` key silently). Scoped to `InGameSessionHandler`
only — `LoginSessionHandler` (2 cases), `ResourcePacksSessionHandler` (different shape, dispatches on a
status enum not a packet ID) and `PreSpawnSessionHandler`/`SpawnResponseSessionHandler` (smaller switches)
are unchanged.

**Why now, revisiting §76 and the project non-goals list:** both explicitly left a centralized packet-dispatch
table as *out of scope, not rejected* — §76 only ever covered per-packet-class `Encode`/`Decode`/`Id` codegen
and said any dispatch-table proposal "needs its own ADR"; the non-goals list echoed the same deferral, citing
the risk of the freeze-listed `Dispatcher` abstraction (`ARCHITECTURE.md`) without proven need. The honest
justification here is **not** a new functional bug or perf pressure — none exists. It is that `CommandCatalog`
(`src/zenith/Gameplay/Commands/CommandCatalog.cs`) already uses this exact shape
(`Dictionary<string, CommandDefinition>`, built once, looked up per dispatch) for a sibling domain (command
routing by name), and that precedent already cleared the freeze-list bar for "map key to handler, built once
at startup, hand-registered not reflected/generated." Applying the same already-accepted shape to a second
domain (packet-ID routing) is not introducing a new abstraction category — it is closing a consistency gap
the wire-codegen ADR (§76) explicitly declined to rule on because dispatch was a different architectural
layer than it was scoped to touch.

**What this is not:** not codegen — the dictionary is hand-written, not generated from `[GamePacket]`
metadata (§76's generator still stops at `Encode`/`Decode`/`Id`, unrelated to this). Not a project-wide
rewrite — the other three session handlers keep their plain `switch` because none of them are large enough
to have motivated this in the first place. The `FrozenDictionary` choice is a genuine (if minor) lookup-speed
improvement over a `switch` on this many cases, but that is a secondary benefit of the data structure picked,
not the reason this change was made — the primary point is call-site/testability consistency with
`CommandCatalog`.

**Amends:** the non-goals list entry "Generated/centralized packet-dispatch table replacing the per-handler
`switch` statements" is narrowed — a *generated* dispatch table (derived from `[GamePacket]` or any other
codegen source) remains a non-goal; a *hand-written* registry map, scoped to one handler with a demonstrated
same-codebase precedent, is not.

**Status (ago 2026):** Shipped — `InGameSessionHandler.cs`'s `Handlers` frozen dictionary, `NoOpPacketIds`
group, and two-line `HandleDataPacket`. 1019/1019 `zenith.Tests` green, no behavioral change (every case in
the original switch has a matching dictionary entry, checked 1:1 against the original as an implementation
checklist).
