# ADR §143 — Transport boundary: `Network/`, NetherNet rebuilt on the axolotl port (nov 2026)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §143`.

**Problem:** the session layer leaked RakNet (`RakNetSession.Priority`, `NetworkSession.RakSession`, a RakNet-typed constructor); `ZenithSessionListener` was a RakNet listener with NetherNet bolted on through parallel dictionaries; telemetry took `RakNetServer`; `ZenithServer` chose one transport with `if/else` at start, run and shutdown; the client identity type lived in the NetherNet library. NetherNet itself was a bespoke host that had only been exercised with the smoke bot.

**Choice:**
1. `src/nethernet/Axolotl/` is a 1:1 C# port of `axolotl-nethernet` (the reference used by the Essential server), then adapted: HTTP + LAN signaling, SDP negotiation, identity, WebRTC (`DataChannelDotnet`), single-UDP ICE mux, MTU, candidate advertising, connection budgets. Deviations are DataChannelDotnet limits (no RTT/byte counters/buffered amount; reliability inferred from channel label; SCTP heartbeat validated but not applied). The old `NetherNetHost`/frame codec/signaling server and their tests were deleted.
2. `src/zenith/Network/` (narrowly reverses §48: a **transport boundary**, not the old junk drawer): `IGameTransport` (run/stop-accepting/shutdown/statistics), `IGameTransportConnection` (what a Bedrock session may ask of the wire), `IGameTransportHost` (what a transport may tell the server), `GameSessionHost` (the only code that knows both sides), `GameTransportFactory` (the only reader of transport config). `RakNetTransport` and `NetherNetTransport` are symmetric adapters. `network.transport: both` runs them together.
3. Sessions take `GameTransportPriority`/`GameTransportCloseReason`/`TransportIdentity` (transport-neutral); telemetry takes `TransportStatistics`. The Login handler binds the Bedrock token to `TransportIdentity` for any transport that requires it.
4. `NetherNetServer` is single-threaded; `NetherNetServerRunner` owns its thread and queues commands from other threads.
5. Franchise verification is split into pure sync validation (`FranchiseTokenValidator.Validate`) plus a background-refreshed key cache (`FranchiseTokenVerifier : ITokenVerifier`), so it never does I/O on the negotiation path. The operator key file (`nethernet-operator-key.pk8`) is read unchanged, preserving TOFU trust.

**Config (breaking, pre-release):** `network.transport` gains `both`; `maximum-fragment-size` removed; `max-message-size` capped at 262144; new `tls-certificate`, `tls-private-key`, `reverse-proxy-header/-networks`, `lan-discovery`, `ice-udp-mux`, `advertise-addresses`, `mtu`. ICE range defaults start at `server.port + 1` when RakNet also runs.

**Open:** retail-client validation over the new library; protocol bump 2193 → 2225 for 26.60+ clients; `BinaryStream` still lives in the RakNet assembly and is used by `Packets/` (candidate for a neutral shared library).
