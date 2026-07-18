# Contributing to Zenith

Thanks for helping. Zenith is early alpha — small, reviewable PRs beat large “framework” dumps.

## Before you write code

1. Read [`ARCHITECTURE.md`](ARCHITECTURE.md) (constraints) and [`docs/architecture.md`](docs/architecture.md).
2. Skim [`AGENTS.md`](AGENTS.md) for folder layout (`Packets` / `Protocol` / `Session` — **do not recreate `Network/`**).
3. Check [`docs/roadmap.md`](docs/roadmap.md) for **which horizon** your idea belongs to (alpha gate vs after-alpha leaves).
4. Check [`docs/decisions.md`](docs/decisions.md). New layers or freeze-list items need an ADR **first**.
5. Prefer the leaf that owns the bug (`src/raknet`, `libs/nbt`, `libs/leveldb`) when possible.

Philosophy in one line: **who decides ≠ who transmits ≠ who serializes.**

## Hot-path conventions

- **Online-once:** `GameLoop` fills a shared online list per tick (`PlayerManager.FillOnline`); systems take `IReadOnlyList<Player> online` — do not call `PlayerManager.Online` inside nested peer loops ([`docs/dx.md`](docs/dx.md)).
- **Item wire:** `NetworkItemStack` has three writers — packet Encode picks; never invent a “default Write”. Inventory content / ISR OK: **`DescribeForWire` only**; soft ISR match via `MatchesAdvertisedStackNetId` ([`docs/dx.md`](docs/dx.md), ADR §54).
- **Measured / benchmarks:** optional ShortRun numbers in [`docs/dx.md`](docs/dx.md) Measured — not a CI gate.
- **Platform health** (not Horizon‑1 product): [`docs/robustness-dx-debt.md`](docs/robustness-dx-debt.md) + ADR §54.

## Deferred / frozen (do not sneak in)

Unless an ADR says otherwise: Scheduler, Actor/ECS, VisibilitySystem, DI container, plugin API, `/` command frameworks, Mojang LevelDB world format.

## Pull requests

- Use the PR template. One concern per PR when practical.
- Keep Gameplay free of `DataPacket` / `BinaryStream`; Packets free of `Player` / `World` / `Server`.
- Include tests for format/wire/behaviour when the change is testable without a Bedrock client.
- After implementation, contributors using agents should run:
  - skill **`zenith-post-impl-audit`** — audit `git status`/`diff` against architecture, freeze list, DX
  - skill **`zenith-unit-tests`** — add/extend leaf tests when the diff warrants them  
  Skills are mirrored for Cursor / Claude Code / OpenCode: [`.cursor/skills/`](.cursor/skills/), [`.claude/skills/`](.claude/skills/), [`.opencode/skills/`](.opencode/skills/).
- Run before requesting review:

```bash
dotnet test zenith.sln
```

## Issues

- Use an issue template (bug or feature).
- Features that touch architecture must say whether an ADR is proposed.
- “Someone on Discord said…” is not enough for join bugs — attach server log lines and client version / platform when you can.

## Commit style

Short imperative summary (what/why). Match recent `git log` tone. Do not commit secrets, `worlds/` data, or build `bin/`/`obj/`.

## Questions

Open an issue or discuss on the PR. For agent/automation context, start from [`AGENTS.md`](AGENTS.md).
