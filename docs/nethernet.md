# NetherNet

`network.transport: nethernet` (or `both`, next to RakNet) lets Bedrock clients join over WebRTC. Clients 26.60+ only join third-party servers this way, so this is the transport that matters for current retail clients.

## Where it lives

| Layer | Project / folder | Knows about |
|---|---|---|
| Protocol library | `src/nethernet/Axolotl/` (`Zenith.NetherNet.Axolotl.*`) | HTTP + LAN signaling, SDP, identity assertions, WebRTC data channels, framing. Nothing of Zenith. |
| Server runner | `NetherNetServerRunner` (library) | Owns the tick thread; commands from other threads are queued. |
| Zenith adapter | `src/zenith/Network/NetherNet/` | `NetherNetTransport` implements `IGameTransport`; `FranchiseTokenVerifier` plugs Franchise auth into the library. |
| Transport-neutral layer | `src/zenith/Network/` | `IGameTransport`, `IGameTransportConnection`, `IGameTransportHost`, `GameSessionHost`, `GameTransportFactory`. |

RakNet is plugged in through exactly the same interfaces (`Network/RakNet/RakNetTransport.cs`). Sessions (`NetworkSession`) only see `IGameTransportConnection`; they never reference a transport. Adding a transport means implementing `IGameTransport` and adding it to `GameTransportFactory`.

The library is a 1:1 C# port of [`axolotl-nethernet`](https://github.com/axolotl-pm/NetherNet) (the PHP/PocketMine reference used by the Essential server), then adapted. See ADR §143 for the deviations.

## Wire contract

Follows Mojang's [NetherNet onboarding guide](https://github.com/Mojang/bedrock-protocol-docs/blob/main/additional_docs/NetherNetOnboardingGuide.md):

- `GET {join-url}/v1/join` returns server info (`name`, `protocol`, `version`, `level`, `players`, `maxPlayers`, `gameType`, …).
- `POST {join-url}/v1/join/{networkId}` takes an SDP offer and returns the SDP answer.
- LAN discovery (optional) answers Bedrock's UDP 7551 broadcast and carries trickle-ICE signals.
- The offer's `a=identity` assertion is verified; the answer carries the server's own assertion plus a detached ES384 signature over its DTLS fingerprints. Client `cpk` claims accept a P-384 JWK or Base64 DER SPKI string.
- The two client-created channels are `ReliableDataChannel` (ordered, fragmented) and `UnreliableDataChannel`.
- Game batches are sent without RakNet's `0xFE` identifier (`IncludesGamePacketIdentifier = false`).

## Configuration (`network.nether-net`)

| Key | Meaning |
|---|---|
| `join-url` | Address clients use; its host names the server identity. |
| `listen-url` | Signaling bind (`http://+:port` = all interfaces). Empty = `http://+:{server.port}`. |
| `tls-certificate`, `tls-private-key` | Serve signaling over HTTPS (PEM files). |
| `reverse-proxy-header`, `reverse-proxy-networks` | Trust a header such as `X-Forwarded-For` only from these proxy networks. |
| `lan-discovery` | Answer LAN discovery on UDP 7551. |
| `ice-udp-mux` | Serve every WebRTC peer from one UDP port (`ice-port-range-start`). Incompatible with TURN. |
| `ice-port-range-start/end` | UDP ports for ICE. `0/0` derives `server.port` … `server.port + max-players − 1` (starting at `server.port + 1` when RakNet also runs). Without mux the range needs one port per player. |
| `ice-bind-address`, `ice-servers` | Local NIC; optional `stun:host:port` / `turn:user:pass@host:port`. |
| `advertise-addresses` | Public IPs offered as ICE candidates (NAT, containers). |
| `mtu` | WebRTC MTU, default 1200. |
| `max-message-size` | Largest NetherNet message (≤ 262144). |

`maximum-fragment-size` was removed: fragmentation is fixed by the protocol.

Open the signaling TCP port and the ICE UDP port(s). A reverse proxy for signaling does not proxy the WebRTC UDP path. The operator key is `nethernet-operator-key.pk8` in the persistent data root; keep it to preserve the server identity and TOFU trust.

## Authentication

With `auth.accept` containing `xbox`, the SDP GameServerToken is verified against the Franchise key set (RS256, issuer, audience, timestamps). The keys are loaded at startup and refreshed hourly in the background, so verification never blocks on the network; until the first load every token fails. If self-signed/offline are also accepted, tokens that fail verification are let through but marked unauthenticated, and a FULL login still requires an authenticated token. The Bedrock login token must equal the token proven during signaling (`TransportIdentity`).

## Known limits

`DataChannelDotnet` exposes no RTT, byte counters or buffered amount, and channel reliability is inferred from the channel label. Not yet validated with a retail Bedrock client over this library (the previous implementation was only exercised with the smoke bot).

## Tests

```powershell
dotnet test src/nethernet.Tests/nethernet.Tests.csproj
dotnet test src/zenith.Tests/zenith.Tests.csproj --filter NetherNet
```
