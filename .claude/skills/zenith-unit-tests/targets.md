# Zenith unit test targets

Companion to [SKILL.md](SKILL.md).

## Project map

| Tree | Test assembly | Command |
|------|---------------|---------|
| `libs/nbt` | `nbt.Tests` | `dotnet test libs/nbt.Tests/nbt.Tests.csproj` |
| `libs/leveldb` | `leveldb.Tests` | `dotnet test libs/leveldb.Tests/leveldb.Tests.csproj` |
| `src/raknet` | `raknet.Tests` | `dotnet test src/raknet.Tests/raknet.Tests.csproj` |
| `src/zenith` (+ Geometry, Packets, …) | `zenith.Tests` | `dotnet test src/zenith.Tests/zenith.Tests.csproj` |
| Whole solution | all of the above | `dotnet test zenith.sln` |

## Good examples in-repo (copy shape, not paste blindly)

| Area | File |
|------|------|
| Intent FIFO / systems | `src/zenith.Tests/IntentContractTests.cs` |
| Packet round-trip | `src/zenith.Tests/CriticalPacketRoundTripTests.cs` |
| UI/sound encode | `src/zenith.Tests/UiSoundEmotePacketTests.cs` |
| Floor drops / SoftCap | `src/zenith.Tests/FloorDropStoreTests.cs` |
| AABB / hitboxes | `src/zenith.Tests/AabbTests.cs` |
| World / inventory bag | `src/zenith.Tests/WorldTests.cs` |
| Break timing | `src/zenith.Tests/BreakTimingTests.cs` |

## Decision tree

```text
Changed Encode/Decode of a DataPacket?
  → zenith.Tests round-trip (fields + Id policy)

Changed Submit*/TryConsume* or a GameSystem?
  → IntentContractTests-style fixture; assert domain + optional captured datagrams

Changed FloorDropStore / Aabb / EntityHitboxes?
  → store + geometry leaf tests; delay/merge/partial cases

Changed BinaryStream / NBT / LevelDB / RakNet frame logic?
  → corresponding leaf.*Tests — never only zenith.Tests

Changed docs or AGENTS only?
  → no tests
```

## Smoke vs unit

| Kind | Role |
|------|------|
| Unit / leaf | Required for merge when behavior is deterministic |
| Manual Bedrock smoke | Listed in ARCHITECTURE.md; never replaces leaf tests |

## Anti-patterns in new tests

- Sleeping / real-time waits for GameLoop (use `GameClock.AdvanceBy` / direct `Tick`)
- Binding a real UDP port unless existing raknet tests do
- Asserting log strings as the only signal
- Giant setup that recreates full `ZenithServer` for a palette id check
