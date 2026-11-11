# Zenith.NetherNet

`Zenith.NetherNet` is the independent .NET library for Zenith's Bedrock NetherNet transport. It owns HTTP full-ICE signaling, WebRTC peer connections, operator/client identity assertions, and NetherNet data-channel framing. It has no reference to Zenith gameplay, packets, or RakNet.

The library uses `DataChannelDotnet`, a managed binding to native libdatachannel, because the transport requires both fully reliable ordered channels and `maxRetransmits=0` unordered channels. Package/runtime support follows the upstream NuGet package's included native assets. See [`docs/nethernet.md`](../../docs/nethernet.md) for the wire contract, configuration, authentication, and network setup.

Build and run the transport tests from the repository root:

```powershell
dotnet test src/nethernet.Tests/nethernet.Tests.csproj -c Release
```
