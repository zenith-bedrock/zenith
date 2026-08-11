# Technical reference (SSOT)

Detailed pointers — protocol version, embedded assets, external study references, and what Zenith owns vs borrows. This is the deep-reference page; [`readme.md`](../readme.md) stays a short landing page and links here.

Comments and ADR tags (`ADR §N`, `§N`) appear in many files. **They are pointers, not a second spec.** When code and prose disagree, **`ServerIdentity` + `ServerConfig.Validate()` + `ARCHITECTURE.md`** win for runtime; **[`decisions.md`](decisions.md)** wins for intent and history.

## What this build speaks (SSOT)

| Field | Value | Defined in |
|-------|--------|------------|
| Bedrock protocol | **2169** | [`src/zenith/Server/ServerIdentity.cs`](../src/zenith/Server/ServerIdentity.cs) |
| Game version (wire) | **1.26.50** | same |
| Product / release | **0.0.2-alpha** | same (`ProductVersion` — Docker tags, logs; not on Bedrock wire) |

Bumped from 1001/1.26.33 to 2169/1.26.50 in ADR §79 (Aug 2026). **Known debt:** ~23 packets Mojang moved to Cereal serialization at 2169 (StartGame, LevelChunk, MovePlayer, PlayerAuthInput, CraftingData, CreativeContent, ItemStackRequest/Response, and more — full list in ADR §79) still encode/decode in this build's pre-Cereal shapes. A real 1.26.50 client will desync on those specific packets until each is migrated. New encode/decode work should match **2169** (branch `r/26_u4`) going forward — except when touching one of the debt packets, where matching the *old* pre-Cereal shape is still correct until that packet's dedicated migration lands. Bump workflow: [`protocol-churn.md`](protocol-churn.md).

## Authoritative docs inside this repo

| Document | Use for |
|----------|---------|
| [`decisions.md`](decisions.md) | **ADR §1–§96** — every `ADR §N` / `§N` in code should resolve here |
| [`../ARCHITECTURE.md`](../ARCHITECTURE.md) | Layer rules, smoke expectations, freeze list |
| [`../AGENTS.md`](../AGENTS.md) | Folder roles (`Gameplay/` decides, `Protocol/` transmits, …) |
| [`dx.md`](dx.md) | Contributor workflow, protocol-doc usage, local reference clones |
| [`roadmap.md`](roadmap.md) | H1 closed; **Yes-next** (not PM parity) |
| [`../schemas/zenith.schema.json`](../schemas/zenith.schema.json) | **IDE autocomplete only** — boot validation is `ServerConfig.Validate()` |
| [`../deploy/README.md`](../deploy/README.md) | Deploy contract — one image, `ZENITH_DATA`, platform adapters |
| [`../deploy/compose/dokploy.md`](../deploy/compose/dokploy.md) | Dokploy — Compose Path `./deploy/compose/docker-compose.dokploy.yml` + File Mount |
| [`../deploy/pterodactyl/README.md`](../deploy/pterodactyl/README.md) | Pterodactyl egg adapter (ADR §68) |

## Shipped assets (embedded, not fetched at runtime)

| Asset | Role |
|-------|------|
| [`src/zenith/data/block_palette.nbt`](../src/zenith/data/block_palette.nbt) | Block runtime IDs (`Blocks.Load`) |
| [`src/zenith/data/item_palette.json`](../src/zenith/data/item_palette.json) | Item registry wire + tools |
| [`src/zenith/data/creative_items.json`](../src/zenith/data/creative_items.json) | Reference dump — **not** fully parsed at boot (short curated list in code) |

Curated gameplay subsets (`Blocks`, `DigProfiles`, `CreativeCatalog`) are intentional — full registry fidelity is wire via palettes, not every block is placeable/diggable yet.

## External references (study only — **not** vendored, **not** runtime deps)

We read these for wire shapes, ops patterns, and gameplay math. Zenith does **not** fork or bundle them.

| Reference | Typical use in Zenith | Upstream |
|-----------|----------------------|----------|
| **Mojang bedrock-protocol-docs** | Packet field trees, changelogs | [github.com/Mojang/bedrock-protocol-docs](https://github.com/Mojang/bedrock-protocol-docs) branch **`r/26_u4`** |
| **PocketMine-MP / BedrockProtocol** | AuthInput bit indices, 1001-era cross-check | [github.com/pmmp/PocketMine-MP](https://github.com/pmmp/PocketMine-MP) |
| **Endstone** | BDS-aligned headers, Docker `/data` UX pattern | [github.com/EndstoneMC/endstone](https://github.com/EndstoneMC/endstone) |
| **Dragonfly (Go)** | `BreakDuration` / dig timing formulas | [github.com/df-mc/dragonfly](https://github.com/df-mc/dragonfly) — see `break_info.go`, Mojang `BlockBreakingOverview.md` (ADR §27) |
| **Prismarine bedrock-protocol** | Smoke bot only (separate repo) | [github.com/PrismarineJS/bedrock-protocol](https://github.com/PrismarineJS/bedrock-protocol) |
| **BDS / Mojang world format** | **Deferred** — ADR §61 seam + converter, not in-process decode | Official dedicated server layout |

Local clone layout (optional dev setup): [`dx.md`](dx.md) § "Bedrock references" — e.g. `~/Development/references/bedrock/`.

## Related Zenith repos

| Repo | Role |
|------|------|
| [zenith-bedrock/zenith](https://github.com/zenith-bedrock/zenith) | This server (C#) |
| [zenith-bedrock/zenith-smoke-bot](https://github.com/zenith-bedrock/zenith-smoke-bot) | Bun + bedrock-protocol join/place smoke (ADR §58) — **not** in this tree |

## What we copy vs what we own

| Own (LGPL in-repo) | Borrow concept / cross-check only |
|--------------------|-----------------------------------|
| `src/raknet`, `libs/nbt`, `libs/leveldb` | PocketMine-style auth modes (`self-signed`, `offline`) |
| Packet encode for protocol **1001** | Dragonfly dig duration math |
| ZLDB overlay world model (`c:`/`ov:`/`inv:`/`ct:`/`pd:`) | Endstone/PocketMine **single data directory** Docker pattern |
| Native terrain gen (§63–§67) | **Not** BDS noise/biome tables, **not** PM plugins |

**Explicit non-goals** (do not assume from comments elsewhere): plugin API, full Mojang `creative_items.json` / `block_state_b64`, in-process BDS world import, ECS/mob AI, `/` command framework — listed in [`decisions.md`](decisions.md) § "Explicit non-goals".

## Reading `ADR §N` in source

Example: `// ADR §57` → open [`decisions.md`](decisions.md) and search `### 57.` or `§57`. Shorthand `§N` in comments is the same index. New layers or abstractions need a **new ADR section before merge**, not only a code comment.
