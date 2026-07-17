---
name: zenith-unit-tests
description: >-
  Adds or extends Zenith leaf unit tests for the current changes when appropriate
  (intents, packet round-trips, palettes, raknet, nbt, leveldb). Use when the user
  asks for unit tests, test coverage for a diff, test the changes, or after
  implementing a feature that needs leaf tests instead of Bedrock client smoke.
---

# Zenith unit tests from changes

Write the **smallest leaf tests** that lock the behavior in the user’s diff. Prefer `dotnet test` over Bedrock client smoke.

## When to run

- User asks to add/extend unit tests for recent work
- After implementation, before or with post-impl audit
- Diff touches wire format, intents, systems, palettes, Geometry, or leaf libs

## When **not** to add tests

```text
❌ Docs-only / comment-only / pure rename with no behavior change
❌ “Make testing easier” via DI, plugin API, Actor, or new test frameworks
❌ Full integration server boot as a substitute for leaf tests
❌ Duplicating an existing IntentContract / round-trip test without a new assertion
❌ Testing private implementation details that will churn every PR (prefer public/internal contracts)
```

If none of the “when” rows apply, say **No tests warranted** and stop.

## Step 0 — Read the diff

```bash
git status
git diff
git diff --cached
```

Map changed paths → test project using [targets.md](targets.md).

## Step 1 — Choose project & style

| Change touches | Project | Style to copy |
|----------------|---------|----------------|
| `libs/nbt` | `nbt.Tests` | Existing endian/compound tests |
| `libs/leveldb` | `leveldb.Tests` | WAL / snapshot / API tests |
| `src/raknet` | `raknet.Tests` | Frame / reliability / fragment bounds |
| `src/zenith/Packets` | `zenith.Tests` | `CriticalPacketRoundTripTests`, `*PacketTests` Encode→Decode |
| Intents / Systems / World / Player / Geometry | `zenith.Tests` | `IntentContractTests`, `WorldTests`, `AabbTests`, `FloorDropStoreTests` |
| Config / ServerConfig | `zenith.Tests` | Config parse/validate tests |

`InternalsVisibleTo` already exposes internals to `zenith.Tests` — use it; do not make types public only for tests.

## Step 2 — Decide assertions (be rigorous)

Pick **invariants**, not UI:

1. **Contract** — queue overflow, FIFO drain, reject path leaves world unchanged
2. **Round-trip** — Encode buffer → Decode fields equal (Packets only; strip Id like peers)
3. **Geometry / reach** — AABB intersect true/false cases; no sphere regressions
4. **Merge / delay** — floor-drop delay default, `max` on merge, partial take preserves delay
5. **Layering-safe fixtures** — `IntentTestFixture` / recording transport; do not spin RakNet UDP in unit tests unless `raknet.Tests`

Name tests: `MethodOrType_scenario_expectedOutcome` (xUnit `[Fact]` / `[Theory]`).

## Step 3 — Implement

- Extend an existing test class when the scenario belongs there
- New file only for a new cohesive surface (`AabbTests`, `*PacketTests`)
- Match project usings, namespaces (`Zenith.Tests`), and fixture patterns
- No FluentAssertions unless already used in that project (xUnit + Assert)

## Step 4 — Prove green

```bash
dotnet test zenith.sln
```

Or filtered:

```bash
dotnet test src/zenith.Tests/zenith.Tests.csproj --filter "FullyQualifiedName~YourTestName"
```

Fix failures you introduced. Do not ignore flakes — if environmental, document and keep the assertion.

## Step 5 — Report

```markdown
# Unit tests

**Warranted:** yes | no (reason)
**Project:** …
**Added/extended:**
- `path/TestFile.cs` — what invariant

**Commands run:**
- `dotnet test …` → pass/fail

**Not tested (Deferred / needs client smoke):**
- …
```

## Architecture constraints while testing

- Tests must not teach the wrong layering (e.g. Gameplay constructing DataPackets in production code paths)
- Fixtures may use Protocol/Session test doubles already in the suite (`IntentTestFixture`, `RecordingRakNetServer`)
- Do not add NUnit/MSTest; stay on xUnit
- Do not add a DI container to “inject mocks”

## Packet Encode checklist (when testing Packets)

- [ ] Outbound Encode writes packet **Id** first (unsigned varint) unless the suite’s helper strips it consistently
- [ ] Item stacks: Wrapper vs Descriptor vs ItemStack — match production Encode path
- [ ] Prefer shape/round-trip over full world simulation
