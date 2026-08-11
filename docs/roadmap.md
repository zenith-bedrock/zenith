# Roadmap (lean)

**Purpose:** shared order of work for humans and agents. **Not** PocketMine feature parity and **not** a second copy of every Deferred line in [`decisions.md`](decisions.md).

Update this file when a horizon closes (e.g. alpha tagged) or an ADR changes “yes-next”. Day-to-day constraints stay in [`ARCHITECTURE.md`](../ARCHITECTURE.md).

---

## Horizon 0 — Closed: `v0.0.1-alpha`

Tagged **v0.0.1-alpha** (Jul 2026). `main` and `develop` both at the release tip. Notes: [`release-notes-template.md`](release-notes-template.md) / [GitHub Release](https://github.com/zenith-bedrock/zenith/releases/tag/v0.0.1-alpha). Gate: [`alpha-gate.md`](alpha-gate.md).

**Already shipped on that tag:** LAN spine (login → flat InMemory **or** LevelDB world → place/break → inventory/chests/craft → Creative/Survival → persist → dig crack + void rescue → graceful disconnect). Chest facing (§46), MOTD/session hygiene (§47), ordered fragment reassembly (raknet), folder layout (§48).

**Do not slide into post-tag polish as Horizon 0:** sand/gravel gravity, drop-entity wire, death/respawn, tools, double-chest, `/` commands, plugins, biomes — those are Horizon 1+ or explicit non-goals.

---

## Horizon 1 — Honesty gaps — **closed** (Zenith leaf)

Do these **as separate ADRs + PRs**. Earlier items unblock later ones. **All rows below are shipped on `develop`** (Jul 2026). Mojang import (§61) is **not** part of this horizon’s Zenith-native leaf — see [Yes-next](#yes-next-after-h1).

| Order | Leaf | Notes |
|------:|------|--------|
| 1 | **Drop-entity wire** (minimal) | **Shipped:** `AddItemActor` / `TakeItemActor`; FloorDropStore holds entity id (§26 adendo). No ECS/physics/despawn. |
| 2 | **Death / Respawn** | **Shipped:** void → DeathInfo + Respawn handshake (§40); Survival death loot → floor (§73). Soft-rescue retired. No full damage pipeline. |
| 2b | **Block/item registry honesty** | **Shipped:** palette reverse `runtimeId→name` + `Blocks.IsPlaceable` allowlist (§12 adendo). |
| 3 | **`/gamemode` minimal** | **Shipped:** CommandRequest/chat → intent → `GameModeSystem` + remint CreativeContent (§52). Not a command framework. |
| 4 | **Tool dig speed / efficiency** | **Shipped:** DF `BreakDuration` + curated tools + 3602 on speed delta (§27 adendo). No enchants. |
| 5 | **Double-chest** (sneak-place + 54 UI) | **Shipped** in `v0.0.2-alpha` (ADR §56) — pair + 54 UI + 2×`ct:`. |
| 6 | **Block gravity** (sand/gravel) | **Shipped:** `GravitySystem` + `GravityPendingStore` (ADR §57); real falling_block actor added in ADR §95 (visible per-tick fall, no more instant column teleport). |
| 7 | **Playerdata reconnect** (`pd:`) | **Shipped:** pose + GameMode in world LevelDB (ADR §60). No `players/` volume. |
| 8 | **World beyond flat** | **Shipped (Zenith leaf):** §62–§67 provider/noise/biomes/caves/ore; §70 join terrain; §71 Dimension seam; §72 FastNoiseLite + anti-pillar height. **§61 Mojang seam = later** (not blocking H1 close). |

If two contributors pick work, prefer **different** [Yes-next](#yes-next-after-h1) rows — not both building command/plugin infrastructure.

**Platform health (not H1 product):** see [`robustness-dx-debt.md`](robustness-dx-debt.md) and ADR §54. **Delivery risks (hard):** dirty&gt;remote, SoftCap honesty, leaf CI vs missing Bedrock E2E.

**Foundation:** StackId + DigProfiles (ADR §55) — shipped. **Join wire fidelity** (ADR §59) — shipped in `v0.0.2-alpha`.

**Release tag `v0.0.2-alpha`:** closed H1 rows 1–5 + §55/§56/§59. Post-tag on `develop`: gravity (§57), playerdata (§60), world seams (§62–§72), survival/dig honesty (§73–§74), cave-gen perf (§75), packet wire codegen (§76). Dual-storage **direction** is §61 (not shipped).

**Protocol smoke bot (ADR §58):** [`zenith-bedrock/zenith-smoke-bot`](https://github.com/zenith-bedrock/zenith-smoke-bot) — Bun + bedrock-protocol, **separate repo**. Human Gate A remains the product smoke authority for tags.

---

## Shipped since H1 close (platform work, not new horizon rows)

Not gameplay leaves — recorded here so this file stays the accurate "what actually landed" list instead of drifting behind `git log`.

| ADR | Leaf | Notes |
|----:|------|-------|
| §73 | **Floor-drop honesty** | Q-throw / ISR Drop, Survival death loot to floor, `TryAddOrMerge` no silent clobber. No gravity/despawn TTL/XP orbs. |
| §74 | **Dig/chest honesty** | Wrong-tool no-drop, Creative chest content dump, death SoftCap keeps slot on refuse. No loot tables/durability. |
| §75 | **Cave-gen alloc cut** | `OverworldCaveContext` disposable + ThreadStatic scratch + `ArrayPool` — cut §69 cave-context GC pressure. No carve-semantics change. |
| §76 | **Packet wire codegen** | Roslyn incremental source generator (`[GamePacket]` + `[Wire*]` attributes) mechanizes `Encode`/`Decode` boilerplate. 31/64 packet types migrated (opt-in, Tier B packets stay hand-written by design — audited aug 2026, `TextPacket` added to Tier B, **no plain migrations remain**). `tools/protocol-import` (Mojang + Endstone schema sources) scaffolds new migrations and diffs protocol drift. |
| §77 | **Floor-drop despawn TTL** | `FloorDropStore.AgeTicks` (merge-safe) + `TickDespawn` + `RemoveActor` fan-out. 6000-tick (5 min) vanilla-parity default. Closes the last §73 non-goal. |
| §78 | **EventBus first domain consumers** | `PlayerPresenceAnnouncer` (join/leave system chat) on the existing `PlayerLoginEvent`/`PlayerQuitEvent` — no new event types. `EventBusTests` closes a coverage gap the infra shipped without. |
| §79 | **Protocol bump 1001→2169 / 1.26.33→1.26.50** | `ServerIdentity` bumped now; **15 shipped packets left pre-Cereal shaped as accepted debt** — see "Cereal migration debt" below. Deliberate LAN/private-use tradeoff, not a silent gap (boot `Warning` + ADR §79). Packet naming audited: 0 mismatches against `r/26_u4` by ID. |

---

## Cereal migration debt (tracked from ADR §79)

Mojang moved ~23 packets to "Cereal" serialization at protocol 2169 — tagged variants + presence-byte optionals, not a field reorder. Of those, 15 were packets Zenith already ships, still encoding in the pre-Cereal (1001-era) shape at the time this table was opened. **Closed (ago 2026, ADR §93):** every packet below is now shipped.

**Methodology note (ADR §81/§82):** the community trackers cloned first (gophertunnel, `bedrock-v/protocol`, Endstone) were all at **2168**, one version behind — for anything Cereal-listed, their code shows the *legacy* shape being replaced, not the new one (caught this once, see §81). `EndstoneMC/bedrock-protocol` (added §82, cloned to `D:\Development\bedrock\endstone-bedrock-protocol`) fixed that: it versions its schema explicitly (`@type(until=2168)`/`@type(since=2168)` pairs, modelling 975/1001/2168/2181 side by side) instead of describing only current HEAD, so a diff is readable directly instead of inferred from one snapshot. **Use it first** for any packet below — check `protocol.py`'s per-file `@packet`/`@type` version gates before reaching for the Mojang JSON schema alone.

| Packet | Notes |
|--------|-------|
| ~~`StartGamePacket`~~ | **Shipped (ADR §93):** last item on this table. Turned out to be **mostly already correct** — cross-checked field-by-field against `endstone-bedrock-protocol` and gophertunnel (byte-for-byte agreement), only 3 narrow bugs found: a phantom `IsLoggingChat` field removed at 2168 that Zenith still wrote, `PlayerPermissions` zigzag-varint'd instead of a raw byte (harmless single-byte-either-way, but wrong value), `EducationEditionOffer` signed instead of unsigned (always 0, no observed effect). Live-verified via `smoke:join` reaching `event: spawn`. |
| ~~`LevelChunkPacket`~~ | **Shipped (ADR §88):** subchunk-count sentinel replaced by varint count + genuine Cereal optional client-request-limit; `CacheMetadata` now always written as an (empty) list. Verified live — unblocks `smoke:join` reaching `spawn`. |
| ~~`MovePlayerPacket`~~ | **Shipped (ADR §88):** teleport data now carries its own presence bool (Cereal optional) instead of being implicit from `Mode == Teleport`. Verified live. |
| ~~`PlayerAuthInputPacket`~~ | **Shipped (ADR §89):** `input_data` moved from packed bitset to a Cereal list of set-flag ordinals; `transaction`/`item_stack_request`/`block_action`/`vehicle_rotation`/`predicted_vehicle` became independent optionals with a "double presence bool" quirk (decorative `_presence` field + the option's own real bool). Verified live — unblocks `smoke:break`. Embedded `item_stack_request` content still delegates to the still-pre-Cereal `SkipEmbeddedRequest` below. |
| ~~`CraftingDataPacket`~~ | **Shipped (ADR §86):** rewritten to separate per-recipe-type arrays + tagged-descriptor ingredients, cross-checked against `endstone-bedrock-protocol` and gophertunnel (gophertunnel is *at* 2168, and this restructuring happened at/before that boundary, so it's trustworthy ground truth here unlike the §81 case). Verified live. |
| ~~`CreativeContentPacket`~~ | **Shipped (ADR §85):** see priority 7 below. |
| ~~`ItemStackRequestPacket` / `ItemStackResponsePacket`~~ | **Shipped (ADR §90):** turned out to be action-numbering + field-width bugs, not the Cereal double-optional pattern the grouping was opened to investigate — Zenith had invented two action types (`PlaceInContainer`/`TakeOutContainer`) that don't exist, shifting every action after `create` by two; every action was also missing its `legacy_type_id` byte; several fields (`stack_id`, `mine_block.network_id`, `craft_grindstone_request.recipe_network_id`) were varint where the wire is fixed `li32`. `ItemStackResponsePacket`'s `containers`/`item_stack_id` were missing their optional presence markers entirely. Verified via wire-shape cross-reference + unit tests only — **not yet live-validated** (smoke-bot doesn't exercise this packet; real-client validation is next). |
| ~~`AddItemActorPacket` / `AddPlayerPacket` / `SetActorDataPacket`~~ | **Shipped (ADR §82):** entity metadata now double-writes the type (`EntityMetadataWriter.WriteEntryType`) and `AddPlayerPacket.HeldItem`/`AddItemActorPacket.Item` use the new `NetworkItemStack.WriteSerializedNetworkItemStackDescriptor` (fixed i16 id, no air early-out). §12's old "don't use the fixed-id shape" finding reversed for 2169 specifically — see §82. |
| ~~`PlayerListPacket`~~ | **Shipped (ADR §92):** per-entry tagged union (`RemoveEntry`/`AddEntry`), no more packet-level `Type` byte + parallel arrays, trailing trusted-skin bool array gone (flag now lives inside the skin write, same as §91's `PlayerSkinPacket` fix). Wire union index turned out to be **inverted** relative to Zenith's own domain constants — caught live, not by schema-reading (see ADR). Live-verified via a real decoded join. |
| ~~`PlayerSkinPacket`~~ | **Shipped (ADR §91):** trusted-skin flag was a phantom trailing bool that doesn't exist on the wire — the real fields (`trusted_skin_flag` name-coded enum + `profile_hash` string) live inside the `skin` sub-structure, fixed locally in this packet (not the shared `SerializedSkin` type, which `PlayerListPacket` also uses and needs a different fix). No live coverage yet (smoke bot doesn't exercise skin change). |
| ~~`ResourcePackClientResponsePacket`~~ | **Shipped (ADR §91):** was missing `response_status_name` (always present) and the conditional `resourcepackids` array (`send_packs` only); converted off `[GamePacket]` codegen to hand-written (generator has no status-conditional-array vocabulary). Live-verified — `smoke:first10`'s join-path packets still pass. |
| ~~`ResourcePacksInfoPacket`~~ | **Shipped (ADR §91):** `texture_packs` was a fixed `int16(0)` instead of a varint-count array — one byte-width fix, not a missing field. Live-verified via `smoke:first10`. |

**Suggested grouping** (shared encoding helpers, do together): ~~(1) `PlayerAuthInputPacket` + `ItemStackRequestPacket` + `ItemStackResponsePacket`~~ — closed (§89/§90); (2) everything else standalone.

**Not on this list, not Cereal, don't touch here:** the §76 structural-drift trio (`UpdateBlockPacket`, `ContainerOpenPacket`, `UpdateAdventureSettingsPacket`) — separate, smaller, already tracked under §76.

---

## Yes-next (after H1)

Pick **one** axis. Do **not** open plugins/JS, Spectator, custom biomes, FormSystem, hunger/damage completo, or Serenity traits/ECS without ADR + demonstrated need (see Horizon 2).

| Priority | Leaf | Notes |
|------:|------|--------|
| 0 | **Human smoke — fresh noise world (§72)** | Delete/`c:`-clear world; Gate A look for pillar columns. Still owed — code shipped and later gen work (§75) landed on top of it, but the human confirmation pass itself isn't recorded as done. Do this before opening another *terrain* ADR (perf/infra leaves like §75/§76 are not gated by this). |
| ~~1~~ | ~~Packet codegen migration, remaining Tier A~~ | **Closed (aug 2026, no ADR needed):** audited all 50 unmigrated packets — 40 are outbound-only no-op `Decode` (already "left as-is" per §76), the other 10 are all Tier B (`TextPacket` added to that list — discard-on-decode category byte + 3-way variant field switch). **No plain migration candidates remain**; 31/64 is the ceiling for the current attribute set. |
| 2 | **Smoke-bot `first10` green** | **Shipped (ADR §94):** `smoke:respawn` was the last blocker — root cause was Zenith's own `RakNetSession.HandleNack` corrupting `OrderIndex`/`MessageIndex` on every retransmission (routed resends through the same code path as a brand-new send), not a wire/Cereal bug. Fixed (`HandleNack` now replays the frame verbatim via `QueueFrameLocked`) + a real RakNet order-channel split (bulk `LevelChunkPacket` streaming no longer shares a channel with gameplay packets). `join`/`place`/`break`/`inv-hotbar`/`respawn`/`chest-open` all pass in sequence now; `double-chest` has an unrelated pre-existing place-cell-collision flake. Also fixed a `zenith-smoke-bot` bug: `client.ts`'s post-spawn error listener was silently no-op'ing, masking transport errors during the investigation. |
| ~~3~~ | ~~Floor-drop despawn TTL~~ | **Shipped (ADR §77):** `FloorDropStore.TickDespawn` + `AgeTicks` (merge-safe) + `RemoveActor` fan-out, 6000-tick vanilla-parity default. |
| 4 | **§61 Mojang spike** | Only when the goal is open/import PM/BDS worlds. Seam + offline converter — never replace ZLDB default. |
| 5 | **AuthInput × client FPS** | Measure-only ([`robustness-dx-debt.md`](robustness-dx-debt.md)); coalesce only after numbers. |
| ~~6~~ | ~~EventBus second consumer~~ | **Shipped (ADR §78):** `PlayerPresenceAnnouncer` (join/leave system chat) on the existing `PlayerLoginEvent`/`PlayerQuitEvent` — no new event types (§21 still holds). `EventBusTests` closes the coverage gap the infra shipped with. Not a plugin API — see "Competitive read" below for why this was the right-sized next step. |
| ~~7~~ | ~~`ItemRegistryPacket` decode failure~~ | **Corrected + shipped (ADR §85):** the original note misattributed the failure — `item_registry` decodes cleanly; `CreativeContentPacket`'s `category` field was a fixed `int32` instead of the wire's `u8`, misaligning everything from the second group onward. Fixed, verified live via `zenith-smoke-bot`'s new `debug:capture` tool. |
| ~~8~~ | ~~`CraftingDataPacket` structural rewrite vs 1.26.40+~~ | **Shipped (ADR §86):** see "Cereal migration debt" above. |
| 9 | **`NetworkItemStack.WriteNetworkItemStackDescriptor` fix rippled from §87** | Not a new todo — recorded so the fix is traceable: found via `debug:capture` right after §86 unblocked `inventory_content`, fixed at the shared method (§87), covers `InventoryContentPacket` + `MobEquipmentPacket` at once. Closed same session, no separate action needed. |

**Default stance:** priority **0 → 2** (confirm the terrain smoke gate, then smoke-bot). §61 is opt-in adoption work, not the default “next.”

### Competitive read (Aug 2026)

Cross-checked against local clones of comparable/newer projects (`dx.md` § "Bedrock references"): **Basalt** (C#/.NET, comparable age to Zenith) already ships plugin support, enchantments, hoppers, and forms — ahead of Zenith on breadth precisely because it didn't gate features behind an architecture-first bet. Dragonfly and Endstone, the two projects whose extension models are most mature, only got there because their event/handler seam was proven with real internal consumers *before* any plugin promise. Priority 6 is that proof step for Zenith — small, ADR-gated, reversible — not a decision to open plugins.

---

## Horizon 2 — Later (explicitly not “next”)

Pull only with ADR + demonstrated need:

- Full creative catalogue / `block_state_b64`
- 3×3 crafting table / Mojang recipe dump
- **Wire-palette refresh past 1.26.30** (`block_palette.nbt`/`item_palette.json`/`creative_items.json`) — checked ADR §84: no community source has published an extraction past 1.26.30 yet (PNX `GameData`, `minecraft-data`, PMMP `BedrockData` all stuck there); `Mojang/bedrock-samples` has newer raw data but not the runtime-ID-assigned wire format. Would mean building the extraction pipeline ourselves — not a data refresh, a new tool.
- Hunger / food / drowning / other damage sources (fall damage shipped, ADR §96 — the rest of the pipeline still doesn't exist)
- Armor / ender chest / hoppers
- Stairs/beds/doors generic block-state facing stack
- Multi-world load / Mojang world format (**direction:** §61 seam + converter — not ZLDB replacement)
- Plugin API / DI / VisibilitySystem / EventBus product surface beyond login/quit (§21) — **stepping stone shipped:** ADR §78 proved the `EventBus` seam with a real internal consumer (`PlayerPresenceAnnouncer`); this bullet is still the actual plugin-API boundary, unchanged
- `/` command **framework** + Bedrock autocomplete
- Automating protocol schema CI / full load harness
- Nether/End Dimensions; custom biome authoring; FormId product UI

---

## Where Zenith still lags (honest)

| Audience goal | Prefer instead | Zenith stance |
|---------------|----------------|---------------|
| Plugins tomorrow, ops mature | **PocketMine / Nukkit** | Intentional freeze until domains stable ([`comparison.md`](comparison.md)) |
| Vanilla fidelity, zero source | **BDS** | We own transport + tick; not Realms clone |
| “Works like PM for survival SMP” | Wait / other stack | Missing mobs, redstone, full damage/hunger, Mojang worlds, years of edge cases |
| Layered .NET platform, LAN spine, leaf libs | **Zenith** | Winning on structure/DX trajectory, not checklist length |

**Behind PM/Nukkit on:** extensibility economy, edge-case years, “just run a server,” gameplay completeness.  
**Ahead / deliberate on:** decide≠transmit≠serialize, owned Nbt/LevelDB leaves, documented freeze, overlay world + Dimension seam, sparse IO.

Do not measure success as “how many PM plugins we can host.” Measure success as **honest LAN multiplayer** → **honest extension points on Gameplay**.

---

## Contributor map

```text
Pick work          →  this file (Yes-next) + alpha-gate if pre-tag
Architecture       →  ARCHITECTURE.md + AGENTS.md (folders)
Why / Deferred     →  docs/decisions.md (ADR before new layer)
How to open PR     →  CONTRIBUTING.md + issue/PR templates
```

Anti-patterns for drive-by PRs: recreate `Network/`; add Scheduler/ECS/plugin bus “for cleanliness”; start `/` autocomplete or §61 before a Yes-next survival/smoke leaf is chosen.

---

## Maintenance

When tagging alpha or merging a Yes-next leaf: tick/adjust rows here and link the ADR. Prefer deleting stale “next” bullets over accumulating a second Deferred encyclopedia.
