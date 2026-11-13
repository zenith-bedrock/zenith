# ADR §43 — Protocol mismatch UX (+ EventBus negotiate seam)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §43`.

**Choice:** After `NetworkSettings`, `ProtocolGate.Evaluate(client, ServerIdentity.ProtocolVersion)` decides Accepted / FailClient / FailServer. Reject → `LoginProtocol.SendIncompatibleProtocol` (`PlayStatus` 1 or 2) + disconnect; no `Player`. Re-check on `LoginPacket.Protocol`. Before accept/reject, publish mutable `ProtocolNegotiateEvent` (Accepted / RejectPlayStatus) so a future listener can override the gate — **Accepted ≠ second codec** (document: forcing accept without encode support breaks on wire). Multi-codec / YAML supported-protocols / plugins: **Deferred**.

**Adendo (jul 2026 — PlayStatus flush):** Incompatible `PlayStatus` is sent **Immediate** + `NOT_PRESENT` compression, then `FlushOutgoing` before RakNet close. Prior Normal-priority PlayStatus was often lost when `Disconnect` Immediate flushed one random frame and closed — vanilla outdated UI never appeared (PlayStatus alone is enough; no custom Disconnect string required).

**Why:** Wrong-version clients previously hung or logged in without Bedrock’s classic incompatible UI. Gate stays pure; Protocol only transmits; Handler orchestrates. Cheap multiprotocol seam without inventing PluginAPI now (Rule 7).

**Smoke:** Connect with client protocol ≠ server → Bedrock outdated client/server UI + disconnect.
