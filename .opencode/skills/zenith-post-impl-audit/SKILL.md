---
name: zenith-post-impl-audit
description: >-
  Audits uncommitted Zenith git status/diff against architecture, DX, freeze list,
  layering, and regression risk after an implementation. Use when the user asks to
  audit changes, review the diff, check architecture compliance, post-impl review,
  or run a quality gate before commit/PR.
---

# Zenith post-implementation audit

Run this **after** code changes and **before** commit/PR. Read-only unless the user asks you to fix findings.

## Mandatory inputs (do first)

```bash
git status
git diff
git diff --cached
git log -5 --oneline
```

If the working tree is clean, say so and stop (or audit `HEAD` vs base branch only if the user named a range).

Also open when relevant:

- [`AGENTS.md`](../../../AGENTS.md)
- [`ARCHITECTURE.md`](../../../ARCHITECTURE.md)
- [`.cursor/rules/zenith-architecture.mdc`](../../../.cursor/rules/zenith-architecture.mdc) (if present locally)
- [`docs/decisions.md`](../../../docs/decisions.md) — only for touched themes
- [`docs/dx.md`](../../../docs/dx.md) — DX / non-goals

Detailed rubric: [checklist.md](checklist.md).

## Workflow

1. **Scope the diff** — list paths by layer (`Packets/`, `Protocol/`, `Session/`, `Gameplay/`, `World/`, `Player/`, `Server/`, `Geometry/`, `libs/*`, `src/raknet`). Flag drive-by files unrelated to the stated task.
2. **Layering** — apply the decide ≠ transmit ≠ serialize rules (below). Any violation = **Blocker**.
3. **Freeze / anti-patterns** — new Scheduler, Actor/ECS, VisibilitySystem, DI, plugin API, `/` command framework, revived `Network/` = **Blocker** unless ADR already exists.
4. **DX** — smallest leaf? tests in the right project? no silent LevelDB→InMemory? no `ZENITH_*` env soup? no Gameplay→`DataPacket`/`BinaryStream`?
5. **Regression surface** — what existing smoke/contracts could break? Missing leaf tests for wire/intent changes?
6. **Noise** — do not commit `bin/`/`obj/`, `worlds/`, `tmp-*`, secrets, large binaries.
7. **Verdict** — emit the report template. Do **not** auto-commit.

## Hard rules (quick)

```text
Gameplay → Protocol → Packets → RakNet
Handler: validate + Submit* intent only
System (GameLoop): mutate domain
Protocol: transmit decided outcomes
Packets: serialize only — no Player/World/Server
```

```text
❌ Handler mutates World / Inventory / final pose
❌ Handler fans out Send* to other sessions (chat/join/blocks)
❌ GameLoop waits on disk (GetResult / .Wait on LevelDB Put)
❌ Packets know Player/World; Gameplay knows DataPacket/BinaryStream
❌ New Network/ catch-all folder
❌ Overwrite-latest for discrete actions (place/break/chat) — FIFO
```

```text
✅ Movement = overwrite-latest; place/break/chat = bounded FIFO
✅ Cross-session sends from GameLoop (or locked RakNet send path)
✅ SetBlock = RAM sync + fire-and-forget persist
```

Folders = roles: see AGENTS.md. Packets stay flat by policy (types may nest later; do not invent `Network/`).

## Report template (always use)

```markdown
# Post-impl audit

**Scope:** [branch / dirty paths summary]
**Verdict:** Pass | Pass with nits | Block merge

## Blockers
- …

## Nits (non-blocking)
- …

## Layer map
| Path | Role | OK? |
|------|------|-----|
| … | Packets/Protocol/Session/Gameplay/… | ✅/❌ |

## Freeze / anti-pattern scan
- [ ] No new frozen abstraction without ADR
- [ ] No Network/ revival
- [ ] Handler/thread rules OK
- [ ] Intent queue policy OK (FIFO vs overwrite)

## DX / tests
- Leaf tests: [present / missing / N/A]
- Suggested test project: [nbt.Tests | leveldb.Tests | raknet.Tests | zenith.Tests | none]
- `dotnet test` recommendation: [full sln | filtered]

## Should not be committed
- …

## Suggested follow-ups
1. …
```

## Severity

| Level | Meaning |
|-------|---------|
| **Blocker** | Architecture/freeze break, wrong layer, unsafe thread mutation, secrets in tree |
| **Nit** | Style, missing ADR note for behavior change, test gap that is not a layering violation |
| **Note** | Optional improvement; do not block |

When unsure between Blocker and Nit, prefer **Blocker** for layering/freeze; prefer **Nit** for taste.

## Out of scope

- Rewriting the feature
- Running Bedrock client smoke (suggest only)
- Amending git history / force-push
- Approving freeze-list items without an ADR
