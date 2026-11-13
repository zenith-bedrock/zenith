# ADR §145 — `BinaryStream` and `ILogger` leave the RakNet assembly (nov 2026)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §145`.

**Problem:** `Packets/` (67 files) and the server logger depended on `Zenith.Raknet` only for the wire primitive `BinaryStream` and for `ILogger`/`LogLevel`, so the "transport-neutral" core still compiled against a transport library (ADR §143).

**Choice:** two leaf libraries, no dependency on anything else: `libs/binary` (`Zenith.Binary.BinaryStream`, with its tests) and `libs/logging` (`Zenith.Logging.ILogger`, `LogLevel`). `src/raknet` and `src/zenith` reference them; `src/nethernet` is unaffected. RakNet-only helpers that lived on the stream (`ReadMagic`/`WriteMagic`) are now extension methods in `src/raknet`. `GamePacket.GameId` is a literal `0xFE`; the "game batches are reliable-ordered" contract moved from `NetworkSession` to `RakNetGameConnection.GameBatchReliability`.

**Residual:** `ChatProtocol` still uses `TokenBucketRateLimiter` from `Zenith.Raknet.Extension` (a generic utility that deserves a neutral home); `NetworkSession` comments still mention RakNet channel semantics as history.
