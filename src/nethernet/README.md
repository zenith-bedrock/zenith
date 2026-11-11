# Zenith.NetherNet

Independent .NET library for the Bedrock NetherNet transport: HTTP and LAN signaling, SDP negotiation, identity assertions and WebRTC data channels. It has no reference to Zenith gameplay, packets or RakNet.

It is a 1:1 port of [`axolotl-nethernet`](https://github.com/axolotl-pm/NetherNet) under `Axolotl/`, built on `DataChannelDotnet` (managed binding to libdatachannel), which provides the reliable and `maxRetransmits=0` channel modes the protocol needs. `NetherNetServer` is single-threaded and tick-driven; `NetherNetServerRunner` runs it on its own thread and accepts commands from any thread.

Zenith plugs it in through `src/zenith/Network/NetherNet/`. See [`docs/nethernet.md`](../../docs/nethernet.md).

```powershell
dotnet test src/nethernet.Tests/nethernet.Tests.csproj
```
