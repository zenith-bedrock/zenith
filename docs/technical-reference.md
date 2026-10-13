# Technical reference (SSOT)

Detailed pointers — protocol version, embedded assets, external study references, and what Zenith owns vs borrows. This is the deep-reference page; [`readme.md`](../readme.md) stays a short landing page and links here.

Comments and ADR tags (`ADR §N`, `§N`) appear in many files. **They are pointers, not a second spec.** When code and prose disagree, **`ServerIdentity` + `ServerConfig.Validate()` + `ARCHITECTURE.md`** win for runtime; **[`decisions.md`](decisions.md)** wins for intent and history.

## What this build speaks (SSOT)

| Field | Value | Defined in |
|-------|--------|------------|
| Bedrock protocol | **2193** | [`src/zenith/Server/ServerIdentity.cs`](../src/zenith/Server/ServerIdentity.cs) |
| Game version (wire) | **1.26.52** | same |
| Product / release | **0.0.2-alpha** | same (`ProductVersion` — Docker tags, logs; not on Bedrock wire) |

Targets Bedrock **1.26.52** (released as a hotfix after 1.26.51) with protocol **2193**; the Mojang schema tag for 1.26.51 also uses 2193. The 2193 packet changes applied here include ItemUse's `Hand`, AuthInput's required Input Data array, removal of obsolete Cereal accessor-presence bytes, ItemStackResponse optionals, and the PlaySound loop-count/bypass/optional fields. Live-client validation remains outstanding; a matching protocol number does not imply complete support for every packet. See [`protocol-churn.md`](protocol-churn.md) for the migration checklist and verification status.

## Authoritative docs inside this repo

| Document | Use for |
|----------|---------|
| [`decisions.md`](decisions.md) | **ADR §1 onward** — every `ADR §N` / `§N` in code should resolve here |
| [`../ARCHITECTURE.md`](../ARCHITECTURE.md) | Layer and folder rules, smoke expectations, freeze list |
| [`dx.md`](dx.md) | Contributor workflow, protocol-doc usage, local reference clones |
| [`roadmap.md`](roadmap.md) | Current dependency graph, maturity gates, NOW/NEXT/LATER |
| [`protocol-import.md`](protocol-import.md) | Deterministic schema cache, coverage report, and Tier A/B codegen boundary |
| [`audit/ZENITH_MATURITY_AND_ECS_READINESS_AUDIT.md`](history/audits/ZENITH_MATURITY_AND_ECS_READINESS_AUDIT.md) | Capability maturity assessment and ECS decision evidence |
| [`2026-10-12-bedrock-data-nethernet.md`](history/audits/2026-10-12-bedrock-data-nethernet.md) | Protocol 2193 data refresh, NetherNet implementation, and verification results |
| [`../schemas/zenith.schema.json`](../schemas/zenith.schema.json) | **IDE autocomplete only** — boot validation is `ServerConfig.Validate()` |
| [`../deploy/README.md`](../deploy/README.md) | Deploy contract — one image, `ZENITH_DATA`, platform adapters |
| [`../deploy/compose/dokploy.md`](../deploy/compose/dokploy.md) | Dokploy — Compose Path `./deploy/compose/docker-compose.dokploy.yml` + File Mount |
| [`../deploy/pterodactyl/README.md`](../deploy/pterodactyl/README.md) | Pterodactyl egg adapter (ADR §68) |
| [`nethernet.md`](nethernet.md) | NetherNet WebRTC/signaling wire contract, identity, ICE networking, and leaf verification |

## Shipped assets (embedded, not fetched at runtime)

| Asset | Role |
|-------|------|
| [`src/zenith/data/block_palette.nbt`](../src/zenith/data/block_palette.nbt) | Block runtime IDs (`Blocks.Load`) |
| [`src/zenith/data/item_palette.json`](../src/zenith/data/item_palette.json) | Item registry wire + tools |
| [`src/zenith/data/creative_items.json`](../src/zenith/data/creative_items.json) | Reference dump — **not** fully parsed at boot (short curated list in code) |
| [`src/zenith/data/biome_definitions.bin`](../src/zenith/data/biome_definitions.bin) | Exact BDS 2193 BiomeDefinitionList packet body |
| [`src/zenith/data/manifest.json`](../src/zenith/data/manifest.json) | Source versions, counts and SHA-256 hashes; embedded asset regression test |

The 2026-10-12 refresh was extracted from official BDS **1.26.52.3**, with Endstone **0.11.13**: **22,091 block states / 1,477 types, 2,076 items, 78 non-empty item component definitions, 124 creative groups / 1,980 entries, and 89 biomes**. Every exported block state and runtime ID was queried from BDS. The block NBT stores only the fields consumed by Zenith (`network_id`, `name`, `states`). Item definitions are decoded into typed NBT in the catalog, mapped by `InventoryProtocol`, and serialized by the existing `ItemRegistryPacket`; previously it wrote empty NBT for every entry. ItemRegistry and biome captures were checked by exact codec roundtrip; creative JSON is a decoded reference, not an exact re-encoding of its capture. See [`tools/bedrock-data`](../tools/bedrock-data/README.md) for the extraction/import contract.

This rollout assumes a **fresh world and player stores**, as selected by the operator. Persisted numeric item IDs and block states can change identity across palettes; no migration of old saves is implemented by this update. The smoke test uses a separate temporary world and does not delete operator data.

Curated gameplay subsets (`Blocks`, `DigProfiles`, `CreativeCatalog`) are intentional — full registry fidelity is wire via palettes, not every block is placeable/diggable yet.

## External references (study only — **not** vendored, **not** runtime deps)

We read these for wire shapes, ops patterns, and gameplay math. Zenith does **not** fork or bundle them.

| Reference | Typical use in Zenith | Upstream |
|-----------|----------------------|----------|
| **Mojang bedrock-protocol-docs** | Packet field trees, changelogs | [github.com/Mojang/bedrock-protocol-docs](https://github.com/Mojang/bedrock-protocol-docs); stable schema tag **1.26.51 / 2193**. The local clone supplied for this task is preview **1.26.60-beta.30 / 2225**. |
| **Mojang NetherNet onboarding guide** | Full-ICE HTTP signaling, identity assertions, WebRTC channel framing | [Official guide](https://github.com/Mojang/bedrock-protocol-docs/blob/main/additional_docs/NetherNetOnboardingGuide.md); implementation notes in [`nethernet.md`](nethernet.md). |
| **NetherNet references** | Cross-check transport/session boundary and wire framing | [axolotl-pm/NetherNet](https://github.com/axolotl-pm/NetherNet), [df-mc/go-nethernet](https://github.com/df-mc/go-nethernet); study-only, no source copied or runtime dependency. |
| **Endstone protocol-docs** | Independent BDS-extracted stable packet reference | [github.com/EndstoneMC/protocol-docs](https://github.com/EndstoneMC/protocol-docs), r26_u5 / **1.26.52.3 / 2193** |
| **Endstone bedrock-protocol** | Version-aware packet schema cross-check, including 2193 and preview 2211 | [github.com/EndstoneMC/bedrock-protocol](https://github.com/EndstoneMC/bedrock-protocol) |
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
| `src/raknet`, `src/nethernet`, `libs/nbt`, `libs/leveldb` | PocketMine-style auth modes (`self-signed`, `offline`) |
| Packet encode for protocol **2193** (implemented packet subset) | Dragonfly dig duration math |
| ZLDB overlay world model (`c:`/`ov:`/`inv:`/`ct:`/`pd:`) | Endstone/PocketMine **single data directory** Docker pattern |
| Native terrain gen (§63–§67) | **Not** BDS noise/biome tables, **not** PM plugins |

**Explicit non-goals** (do not assume from comments elsewhere): plugin API, full Mojang `creative_items.json` / `block_state_b64`, in-process BDS world import, ECS, broad mob AI and plugin-facing command registration — listed in [`decisions.md`](decisions.md) § "Explicit non-goals".

## Reading `ADR §N` in source

Example: `// ADR §57` → open [`decisions.md`](decisions.md) and search `### 57.` or `§57`. Shorthand `§N` in comments is the same index. New layers or abstractions need a **new ADR section before merge**, not only a code comment.
