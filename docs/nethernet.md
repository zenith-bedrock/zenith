# NetherNet

`network.transport: nethernet` selects a complete Bedrock-over-WebRTC transport. The implementation lives in the independent `Zenith.NetherNet` project; Zenith adapts its sessions in `src/zenith/Session/`. It does not emit a new Bedrock game packet format: it transports the existing Bedrock game batches after removing RakNet's message identifier and split-packet envelope.

## Mojang wire contract

The implementation follows Mojang's [NetherNet onboarding guide](https://github.com/Mojang/bedrock-protocol-docs/blob/main/additional_docs/NetherNetOnboardingGuide.md):

- `GET {join-url}/v1/join` returns `name`, `protocol`, `version`, `level`, `players`, `maxPlayers`, and `gameType` as JSON.
- `POST {join-url}/v1/join/{networkId}` accepts and returns raw `application/sdp`. Both SDP descriptions contain gathered ICE candidates; there is no trickle signaling.
- The client offer's `a=identity` assertion is validated before its identity line is removed for WebRTC parsing. Strict Xbox mode validates the GameServerToken against the Franchise key set and verifies its signed DTLS fingerprints against `cpk`. Client `cpk` claims accept either a P-384 JWK object or a Base64 DER SubjectPublicKeyInfo string, matching the two formats handled by [go-nethernet](https://github.com/df-mc/go-nethernet/blob/main/identity.go). Server assertions emit a JWK object as required by Mojang's current guide. The login token must match the token from signaling.
- Fingerprint signature verification preserves the algorithm and digest strings exactly as carried in SDP; canonical JSON changes key ordering and whitespace, not the signed string values.
- Every server answer includes a persistent operator identity assertion and a detached ES384 signature over the answer's DTLS fingerprints. The private key is stored as `nethernet-operator-key.pk8` in Zenith's persistent data root. Keep that file to preserve the operator identity and HTTP TOFU trust.
- The host accepts the two client-created channels, `ReliableDataChannel` and `UnreliableDataChannel`. Reliable traffic is ordered and fragmented; unreliable traffic is unfragmented. The one-byte remaining-fragment counter and the Bedrock batch body follow the Mojang framing guide and `df-mc/go-nethernet`.

The broad server boundary also follows [`axolotl-pm/NetherNet`](https://github.com/axolotl-pm/NetherNet): the transport owns WebRTC and signaling, then reports session data and closure to the server. Zenith keeps that adapter out of `Gameplay`, `Protocol`, and `Packets`.

## Configuration and network

`network.nether-net.join-url` is the address clients use. `listen-url` is the local HTTP signaling listener; it defaults to `http://+:{server.port}` and can sit behind an HTTPS reverse proxy. The listener may use a different path from `/` if `join-url` has the matching base path.

Each active peer binds a distinct UDP ICE port. If the ICE range is left at `0`, Zenith derives the inclusive range `server.port` through `server.port + server.max-players - 1`. A configured range must have at least one port per allowed player. Open or forward that inclusive range over UDP, and expose the signaling `join-url` over TCP. The signaling listener uses TCP; ICE and game data use UDP. Do not enable UDP multiplexing for these per-peer sockets.

`ice-bind-address` optionally selects the local NIC address. `ice-servers` is normally empty, consistent with Mojang's full-ICE latency guidance. Configure a STUN server when the server is behind NAT and needs a server-reflexive public candidate; TURN is only useful when a relay is required. The server must return a reachable candidate in its SDP answer. A reverse proxy for HTTP signaling does not proxy the direct WebRTC UDP path.

Use HTTPS for public signaling when available. HTTP is supported by Bedrock's TOFU flow; preserve the operator key across restarts and backups. Linux/container deployments should publish the signaling TCP port and the ICE UDP range.

On Windows, `HttpListener` uses HTTP.sys. A non-administrator server account needs a [URL reservation](https://learn.microsoft.com/en-us/windows/win32/http/add-urlacl) for its listener prefix; without it, startup fails with `HttpListenerException (5): Access is denied`. The error includes the effective prefix and a command for the current server account. Run that command once in an administrator PowerShell terminal, then run Zenith normally. For the default port and root path, replace `MACHINE\user` with the account that runs Zenith:

```powershell
netsh http add urlacl url="http://+:19132/" user="MACHINE\user" listen=yes delegate=no
```

Keep the trailing `/`. If `join-url` includes a base path such as `/signaling`, reserve `http://+:19132/signaling/` instead. A configured `listen-url` with a different host or port needs its corresponding prefix. The `join-url` host does not restrict the local listener: its default remains `http://+:{server.port}`. Reserve the prefix for the server account rather than granting every user access.

## Authentication

For a public server, set `auth.accept: [xbox]`. This verifies the signaling GameServerToken against the Franchise authorization service before the player starts the Bedrock login. The server also requires that the login packet present the exact same token that was bound to the DTLS certificate during signaling. Self-signed/offline modes retain their existing server-config policy; they do not bypass FULL token verification.

The issuer publishes a standard RSA JWKS at `https://authorization.franchise.minecraft-services.net/.well-known/keys`: a `keys` array with Base64URL `n` (modulus) and `e` (exponent). It does not return `authenticationKeys` or DER `publicKey` entries. Zenith verifies RS256, the fixed issuer, the multiplayer audience, and token timestamps before accepting FULL login. If validation fails, `NetherNet GameServerToken rejected: ...` records the reason without logging the token. Signing-key fetch failures remain authentication failures; they do not enable offline FULL login.

## Implementation and verification

`src/nethernet/NetherNetHost.cs` uses `DataChannelDotnet` over native libdatachannel to provide the reliable and unreliable SCTP channel modes required by Bedrock. SIPSorcery's current API does not implement the partial-reliability controls NetherNet needs. `src/nethernet.Tests/` covers identity assertions, frame validation/reassembly, signaling endpoints, and full local ICE negotiation with both channels and bidirectional frame delivery.

Run the leaf tests with:

```powershell
dotnet test src/nethernet.Tests/nethernet.Tests.csproj -c Release
```

The leaf integration test proves HTTP/SDP/full-ICE negotiation, both channel modes, and bidirectional frame delivery between libdatachannel peers. I also connected the smoke bot's installed `bedrock-protocol`/`nethernet` client to a temporary Zenith NetherNet server through an HTTP signaling adapter. With protocol 2193, the client received `network_settings`, `login_success`, `resource_packs_info`, `start_game`, chunks, and `spawn`. That client smoke used offline auth and checked that the server answer carried `a=identity`; it did not verify the retail Bedrock client's graphical TOFU prompt or authenticated Xbox identity flow. No new Bedrock packet was introduced for this transport.
