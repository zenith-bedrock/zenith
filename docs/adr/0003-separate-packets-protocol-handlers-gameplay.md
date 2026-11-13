# ADR §3 — Separate Packets, Protocol, Handlers, Gameplay

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §3`.

**Choice:** Explicit layering (`548b289` and `ARCHITECTURE.md`).

**Why:** PocketMine-style codebases often blur “decode packet” / “mutate world” / “send reply” in one listener. Zenith's rule set forces:

- Handlers write **intent**
- Systems mutate **domain**
- Protocols **send**

That makes tick order and backpressure mental models simpler and keeps wire formats testable without a full server boot.
