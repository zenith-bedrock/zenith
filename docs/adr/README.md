# Architecture decision records

One file per decision. Code, tests and docs cite a decision as **`ADR §N`**; the record for §N is the file `docs/adr/NNNN-*.md` (four digits, zero padded; a letter suffix such as `104a` marks a follow-up filed under the same number). The log reads in number order, which is also chronological order.

- **Front door and non-goals:** [`../decisions.md`](../decisions.md) (the timeline, the milestones and the list of things we deliberately do not build).
- **Writing a new one:** copy [`TEMPLATE.md`](TEMPLATE.md), take the next number (the highest below plus one), add a row here. Write the ADR *before* a new layer or abstraction (see `AGENTS.md`).
- **Superseding:** never rewrite an old record's decision. Add a short `**Superseded by §N**` note at its top and say in the new record what it replaces.
- **Companion design documents** (the long-form designs behind §97, §111 and §114) keep their own files, listed next to their records below.

| § | Decision |
|---|----------|
| 1 | [Own the stack from UDP up — don't wrap a PHP/Java runtime](0001-own-the-stack-from-udp-up-dont-wrap-a-php-java-runtime.md) |
| 2 | [`BinaryStream` as `ref struct`](0002-binarystream-as-ref-struct.md) |
| 3 | [Separate Packets, Protocol, Handlers, Gameplay](0003-separate-packets-protocol-handlers-gameplay.md) |
| 4 | [GameLoop + pending input over Actor / ECS / Scheduler](0004-gameloop-pending-input-over-actor-ecs-scheduler.md) |
| 5 | [Overlay persistence instead of column copy-on-write](0005-overlay-persistence-instead-of-column-copy-on-write.md) |
| 6 | [Own NBT and LevelDB as leaf libraries](0006-own-nbt-and-leveldb-as-leaf-libraries.md) |
| 7 | [`UseBlockNetworkIdHashes = true` by default while using FNV palette IDs](0007-useblocknetworkidhashes-true-by-default-while-using-fnv.md) |
| 8 | [Config beside the executable](0008-config-beside-the-executable.md) |
| 9 | [Auth accept modes (config visual)](0009-auth-accept-modes-config-visual.md) |
| 10 | [LGPL-3.0](0010-lgpl-3-0.md) |
| 11 | [Zenith.LevelDB = snapshot + WAL in RAM, not a full LSM](0011-zenith-leveldb-snapshot-wal-in-ram-not-a-full-lsm.md) |
| 12 | [ItemPalette via ServerContext (not Items.Load static)](0012-itempalette-via-servercontext-not-items-load-static.md) |
| 13 | [GameLoop never waits on LevelDB overlay Put](0013-gameloop-never-waits-on-leveldb-overlay-put.md) |
| 14 | [Flat chunk streaming + async PreSpawn](0014-flat-chunk-streaming-async-prespawn.md) |
| 15 | [Block authority harden + quiet Animate / LevelSoundEvent](0015-block-authority-harden-quiet-animate-levelsoundevent.md) |
| 16 | [Main inventory 36 slots; place stays hotbar-only](0016-main-inventory-36-slots-place-stays-hotbar-only.md) |
| 17 | [Player inventory rearrange via ItemStackRequest (tick authority)](0017-player-inventory-rearrange-via-itemstackrequest-tick.md) |
| 18 | [Held-item peer sync (MobEquipment fan-out)](0018-held-item-peer-sync-mobequipment-fan-out.md) |
| 19 | [Chests — deferred sketch (shipped as §28 MVP)](0019-chests-deferred-sketch-shipped-as-28-mvp.md) |
| 20 | [First release artifacts + on-disk layout (`v0.0.1-alpha`)](0020-first-release-artifacts-on-disk-layout-v0-0-1-alpha.md) |
| 21 | [Future extension surface form (not a schedule)](0021-future-extension-surface-form-not-a-schedule.md) |
| 22 | [Protocol churn process (human checklist)](0022-protocol-churn-process-human-checklist.md) |
| 23 | [Zenith.LevelDB robustness tests + flush fault hook](0023-zenith-leveldb-robustness-tests-flush-fault-hook.md) |
| 24 | [Measured zero-alloc (BenchmarkDotNet)](0024-measured-zero-alloc-benchmarkdotnet.md) |
| 25 | [Blocks façade stays static (known debt)](0025-blocks-fa-ade-stays-static-known-debt.md) |
| 26 | [Floor drops without item entities](0026-floor-drops-without-item-entities.md) |
| 27 | [Server-authoritative break timing](0027-server-authoritative-break-timing.md) |
| 28 | [Chests — RAM store + ISR container 7 (MVP)](0028-chests-ram-store-isr-container-7-mvp.md) |
| 29 | [Crafting 2×2 — RecipeRegistry MVP (wire completed in §35)](0029-crafting-2-2-reciperegistry-mvp-wire-completed-in-35.md) |
| 30 | [CreativeContent after this spine (conscious yes) — superseded by §31](0030-creativecontent-after-this-spine-conscious-yes.md) |
| 31 | [Creative mode v1 (config → join)](0031-creative-mode-v1-config-join.md) |
| 32 | [Entity coherence — defer WorldEntity](0032-entity-coherence-defer-worldentity.md) |
| 33 | [Store unbounded honesty + InventoryContent encode-shape](0033-store-unbounded-honesty-inventorycontent-encode-shape.md) |
| 34 | [HUD spawn honesty (seed, not authority)](0034-hud-spawn-honesty-seed-not-authority.md) |
| 35 | [CraftingDataPacket remint (RecipeRegistry SSOT)](0035-craftingdatapacket-remint-reciperegistry-ssot.md) |
| 36 | [Store honesty (warn + floor refuse-cap)](0036-store-honesty-warn-floor-refuse-cap.md) |
| 37 | [UpdateAbilities + AdventureSettings (spawn seed + fly echo)](0037-updateabilities-adventuresettings-spawn-seed-fly-echo.md) |
| 38 | [CraftCreative from CreativeCatalog SSOT](0038-craftcreative-from-creativecatalog-ssot.md) |
| 39 | [Inventory + chest LevelDB persist (`ct:` / `inv:`)](0039-inventory-chest-leveldb-persist-ct-inv.md) |
| 40 | [Vitals fields + void soft-rescue](0040-vitals-fields-void-soft-rescue.md) |
| 41 | [Void MovePlayer Teleport + shutdown persistence flush](0041-void-moveplayer-teleport-shutdown-persistence-flush.md) |
| 42 | [Dig crack LevelEvent peer fan-out](0042-dig-crack-levelevent-peer-fan-out.md) |
| 43 | [Protocol mismatch UX (+ EventBus negotiate seam)](0043-protocol-mismatch-ux-eventbus-negotiate-seam.md) |
| 44 | [Tick peer egress (dirty pose + Online snapshot + GamePacket batch)](0044-tick-peer-egress-dirty-pose-online-snapshot-gamepacket.md) |
| 45 | [Sparse flat columns (miss without Put)](0045-sparse-flat-columns-miss-without-put.md) |
| 46 | [Chest facing only (no double-chest)](0046-chest-facing-only-no-double-chest.md) |
| 47 | [MOTD online count + ghost session hygiene](0047-motd-online-count-ghost-session-hygiene.md) |
| 48 | [Folder layout = architecture roles (no Network/ junk drawer)](0048-folder-layout-architecture-roles-no-network-junk-drawer.md) |
| 49 | [PlayerSkinPacket wire + Session relay (no Player→Packets)](0049-playerskinpacket-wire-session-relay-no-player-packets.md) |
| 50 | [Split log levels (server vs RakNet)](0050-split-log-levels-server-vs-raknet.md) |
| 51 | [Collaborator UI/sound/emote packets — wire hygiene (not product feature)](0051-collaborator-ui-sound-emote-packets-wire-hygiene.md) |
| 52 | [`/gamemode` mínimo (sem command framework)](0052-gamemode-m-nimo-sem-command-framework.md) |
| 53 | [Pose flags + emote relay + arm swing (peer interaction feel)](0053-pose-flags-emote-relay-arm-swing-peer-interaction-feel.md) |
| 54 | [Platform health (Robustness / DX) — separate from Horizon‑1 product](0054-platform-health-robustness-dx-separate-from-horizon-1.md) |
| 55 | [Block/item foundation — StackId + sparse capability profiles](0055-block-item-foundation-stackid-sparse-capability.md) |
| 56 | [Double-chest — pair model + 54 UI (no BlockActor)](0056-double-chest-pair-model-54-ui-no-blockactor.md) |
| 57 | [Block gravity — sand/gravel cell tick (no Tile / FallingBlock framework)](0057-block-gravity-sand-gravel-cell-tick-no-tile.md) |
| 58 | [Protocol smoke bot — separate Bun repo (not inside Zenith C#)](0058-protocol-smoke-bot-separate-bun-repo-not-inside-zenith.md) |
| 59 | [Join wire fidelity (ClientProfile + gamemode peers + LevelSound)](0059-join-wire-fidelity-clientprofile-gamemode-peers.md) |
| 60 | [Reconnect playerdata (`pd:{uuid}` pose + GameMode)](0060-reconnect-playerdata-pd-uuid-pose-gamemode.md) |
| 61 | [Dual storage — ZLDB default + Mojang-compat seam (H1#8 direction)](0061-dual-storage-zldb-default-mojang-compat-seam-h1-8.md) |
| 62 | [World domain map — Zenith-native seams (no BDS)](0062-world-domain-map-zenith-native-seams-no-bds.md) |
| 63 | [Noise heightmap terrain (H1#8 product)](0063-noise-heightmap-terrain-h1-8-product.md) |
| 64 | [Overworld band + surface features (trees, water, caves, ruins)](0064-overworld-band-surface-features-trees-water-caves-ruins.md) |
| 65 | [Worm cave carvers (H1#8 — caves v2)](0065-worm-cave-carvers-h1-8-caves-v2.md) |
| 66 | [Ore veins (H1#8 — underground resources)](0066-ore-veins-h1-8-underground-resources.md) |
| 67 | [Coarse overworld biomes (H1#8 — biome v1)](0067-coarse-overworld-biomes-h1-8-biome-v1.md) |
| 68 | [Pterodactyl / Wings hosting (egg + panel hooks)](0068-pterodactyl-wings-hosting-egg-panel-hooks.md) |
| 69 | [Noise column gen — spatial caves + single-pass payload (join perf)](0069-noise-column-gen-spatial-caves-single-pass-payload-join.md) |
| 70 | [Join terrain contract (client-ready)](0070-join-terrain-contract-client-ready.md) |
| 71 | [Dimension seam + Simplex overworld (PM-inspired)](0071-dimension-seam-simplex-overworld-pm-inspired.md) |
| 72 | [FastNoiseLite leaf + anti-pillar height (composition)](0072-fastnoiselite-leaf-anti-pillar-height-composition.md) |
| 73 | [Floor-drop honesty — Q-throw + death loot](0073-floor-drop-honesty-q-throw-death-loot.md) |
| 74 | [Dig/chest honesty — wrong-tool no-drop + Creative chest dump](0074-dig-chest-honesty-wrong-tool-no-drop-creative-chest.md) |
| 75 | [Cave context alloc — ThreadStatic scratch + ArrayPool](0075-cave-context-alloc-threadstatic-scratch-arraypool.md) |
| 76 | [Packet wire codegen — `[GamePacket]` source generator](0076-packet-wire-codegen-gamepacket-source-generator.md) |
| 77 | [Floor-drop despawn TTL](0077-floor-drop-despawn-ttl.md) |
| 78 | [EventBus first domain consumers — join/leave chat](0078-eventbus-first-domain-consumers-join-leave-chat.md) |
| 79 | [Protocol bump to 2169 / 1.26.50 — accepted Cereal debt](0079-protocol-bump-to-2169-1-26-50-accepted-cereal-debt.md) |
| 80 | [Architecture audit pass — findings and fixes](0080-architecture-audit-pass-findings-and-fixes.md) |
| 81 | [Cereal migration methodology — no live 2169 reference exists yet](0081-cereal-migration-methodology-no-live-2169-reference.md) |
| 82 | [Cereal item-stack shape for AddPlayer/AddItemActor — implemented](0082-cereal-item-stack-shape-for-addplayer-additemactor.md) |
| 83 | [ResourcePackClientResponse status was off-by-one — found by running the smoke bot](0083-resourcepackclientresponse-status-was-off-by-one-found.md) |
| 84 | [Registry data (`data/*.{nbt,json}`) already at the community frontier — no 1.26.40+ palette exists yet](0084-registry-data-data-nbt-json-already-at-the-community.md) |
| 85 | [`CreativeContentPacket.category` width bug — found via zenith-smoke-bot's `debug:capture`](0085-creativecontentpacket-category-width-bug-found-via.md) |
| 86 | [`CraftingDataPacket` structural rewrite for protocol 2168+](0086-craftingdatapacket-structural-rewrite-for-protocol-2168.md) |
| 87 | [`NetworkItemStack.WriteNetworkItemStackDescriptor` had the same class of bug — fixed at the root](0087-networkitemstack-writenetworkitemstackdescriptor-had.md) |
| 88 | [`LevelChunkPacket` / `MovePlayerPacket` Cereal migration — unblocks `smoke:join` reaching real `spawn`](0088-levelchunkpacket-moveplayerpacket-cereal-migration.md) |
| 89 | [`PlayerAuthInputPacket` Cereal rewrite — unblocks `smoke:break` (input-flags container change, not just added optionals)](0089-playerauthinputpacket-cereal-rewrite-unblocks-smoke.md) |
| 90 | [`ItemStackRequestPacket` / `ItemStackResponsePacket` — action-number and optional-field bugs, not just Cereal debt](0090-itemstackrequestpacket-itemstackresponsepacket-action.md) |
| 91 | [`PlayerSkinPacket` / `ResourcePackClientResponsePacket` / `ResourcePacksInfoPacket` — closing the Cereal debt table, minus `PlayerListPacket`/`StartGamePacket`](0091-playerskinpacket-resourcepackclientresponsepacket.md) |
| 92 | [`PlayerListPacket` rewritten for 2168+ — and a "double write" that isn't a repeat](0092-playerlistpacket-rewritten-for-2168-and-a-double-write.md) |
| 93 | [`StartGamePacket` — the last Cereal debt item, and it was mostly already right](0093-startgamepacket-the-last-cereal-debt-item-and-it-was.md) |
| 94 | [`smoke:respawn` root cause — NACK resend corrupted RakNet ordering, not a wire bug](0094-smoke-respawn-root-cause-nack-resend-corrupted-raknet.md) |
| 95 | [Real falling_block entity — replaces §57's instant column-teleport](0095-real-falling-block-entity-replaces-57s-instant-column.md) |
| 96 | [Fall damage — first real Health authority, closes the biggest Survival honesty gap](0096-fall-damage-first-real-health-authority-closes.md) |
| 97 | [Choose execution model by behavior, not by available abstraction](0097-choose-execution-model-by-behavior-not-by-available.md) — design: [`0097-runtime-execution-model.md`](0097-runtime-execution-model.md) |
| 98 | [Health is a small authoritative composition primitive](0098-health-is-a-small-authoritative-composition-primitive.md) |
| 99 | [ECS is an evidence-gated world-actor decision, not the next architecture step](0099-ecs-is-an-evidence-gated-world-actor-decision.md) |
| 100 | [Command surface may grow before plugins, without becoming gameplay authority](0100-command-surface-may-grow-before-plugins-without.md) |
| 101 | [Phase D records actor pressure before any entity-runtime choice](0101-phase-d-records-actor-pressure-before-any-entity.md) |
| 102 | [Defer world-actor ECS after the first isolated Phase-E comparison](0102-defer-world-actor-ecs-after-the-first-isolated-phase-e.md) |
| 103 | [Use confirmed chunk knowledge as the first actor-interest policy](0103-use-confirmed-chunk-knowledge-as-the-first-actor.md) |
| 104 | [Runtime health is emitted as bounded server telemetry, not a metrics platform](0104-runtime-health-is-emitted-as-bounded-server-telemetry.md) |
| 104a | [Diagnostics is a fixed-layout leaf, not a server metrics platform](0104a-diagnostics-is-a-fixed-layout-leaf-not-a-server-metrics.md) |
| 104b | [Keep PlayerQuitEvent identity-bound; WasInGame is a gameplay-entry predicate](0104b-keep-playerquitevent-identity-bound-wasingame.md) |
| 105 | [Keep two concrete mob behaviors; extract only the Player death transition](0105-keep-two-concrete-mob-behaviors-extract-only-the-player.md) |
| 106 | [Phase XXI accepts a real ECS for a deliberately small, representative actor slice](0106-phase-xxi-accepts-a-real-ecs-for-a-deliberately-small.md) |
| 107 | [Phase XXII expands the ECS roster and replaces the two-species damage dispatch chain](0107-phase-xxii-expands-the-ecs-roster-and-replaces-the-two.md) |
| 108 | [World seed/generator mode is world identity, committed once and never silently overridden](0108-world-seed-generator-mode-is-world-identity-committed.md) |
| 109 | [Survival vitals close the reconnect/death honesty gap; no generic Attribute framework](0109-survival-vitals-close-the-reconnect-death-honesty-gap.md) |
| 110 | [Ore harvest gates on `ToolTier`, not just `ToolKind` — closing a live, not latent, gap](0110-ore-harvest-gates-on-tooltier-not-just-toolkind-closing.md) |
| 111 | [World generation becomes a bounded shared pipeline](0111-world-generation-becomes-a-bounded-shared-pipeline.md) — design: [`0111-world-generation-pipeline.md`](0111-world-generation-pipeline.md) |
| 112 | [`IBlockRegistry` introduced as an interface seam ahead of the static façade, not a migration](0112-iblockregistry-introduced-as-an-interface-seam-ahead.md) |
| 113 | [`InGameSessionHandler` dispatch becomes a registry map, scoped to this one handler](0113-ingamesessionhandler-dispatch-becomes-a-registry-map.md) |
| 114 | [Resident chunk state (overlay/chest/floor-drop hydrate + evict) — design only, not shipped](0114-resident-chunk-state-overlay-chest-floor-drop-hydrate.md) — design: [`0114-resident-chunk-state.md`](0114-resident-chunk-state.md) |
| 115 | [Zenith.LevelDB (ZLDB) robustness pass — checksums, typed I/O exceptions, reusable snapshots](0115-zenith-leveldb-zldb-robustness-pass-checksums-typed-i-o.md) |
| 116 | [`DB.Destroy`/`DB.Repair` — the two ops-facing capabilities §115 left out](0116-db-destroy-db-repair-the-two-ops-facing-capabilities.md) |
| 117 | [`LOCK` becomes a real OS-level exclusive lock, not a marker file](0117-lock-becomes-a-real-os-level-exclusive-lock.md) |
| 118 | [ZLDB read path: reader/writer lock, zero-alloc `TryGet`, span-based writes](0118-zldb-read-path-reader-writer-lock-zero-alloc-tryget.md) |
| 119 | [ZLDB read path becomes lock-free — §118's reader/writer lock didn't deliver, benchmarks proved it](0119-zldb-read-path-becomes-lock-free-118s-reader-writer.md) |
| 120 | [`Seek` becomes O(log n), `Destroy`/`Repair` refuse when locked, extensive edge-case coverage](0120-seek-becomes-o-log-n-destroy-repair-refuse-when-locked.md) |
| 121 | [Closing pass: final `mojang-leveldb` API diff, one more inconsistency fixed](0121-closing-pass-final-mojang-leveldb-api-diff-one-more.md) |
| 122 | [GameLoop failure diagnostics + inbound Snappy rejected instead of misparsed](0122-gameloop-failure-diagnostics-inbound-snappy-rejected.md) |
| 123 | [Resident chunk state implemented — supersedes ADR §114 (design-only)](0123-resident-chunk-state-implemented-supersedes-adr-114.md) |
| 124 | [Bounded write latency for LevelDbChunkStorage — closes the "hard kill loses recent inv:/ct:" gap](0124-bounded-write-latency-for-leveldbchunkstorage-closes.md) |
| 125 | [Per-session output backlog budget in RakNetSession](0125-per-session-output-backlog-budget-in-raknetsession.md) |
| 126 | [`Frame.Buffer` → `ReadOnlyMemory<byte>` — eliminates outbound fragmentation's per-fragment copy](0126-frame-buffer-readonlymemory-byte-eliminates-outbound.md) |
| 127 | [`RuntimeLoadHarness --soak` — periodic checkpointing for growth-over-time soak runs](0127-runtimeloadharness-soak-periodic-checkpointing.md) |
| 128 | [Two small cleanups from the same external-review pass: explicit socket buffer config, Stopwatch over DateTime.UtcNow](0128-two-small-cleanups-from-the-same-external-review-pass.md) |
| 129 | [`online.FirstOrDefault(p => p.RuntimeId == id)` → `PlayerManager.GetByRuntimeId` — finishes a half-done refactor](0129-online-firstordefault-p-p-runtimeid-id-playermanager.md) |
| 130 | [`PlayerDamage` split into `ApplyCore` (decision) + `PlayerDamageResult.Conclude` (peer-list-dependent tail)](0130-playerdamage-split-into-applycore-decision.md) |
| 131 | [Close the ECS/entities question: unify `GroundMobCombat`/`DamageableActorCombat`'s duplicated logic, keep the storage split](0131-close-the-ecs-entities-question-unify-groundmobcombat.md) |
| 132 | [Migrate Creeper, Enderman, Golem, Fish onto the ECS — re-derive "non-mechanical" from the actual code, not the per-species summary](0132-migrate-creeper-enderman-golem-fish-onto-the-ecs-re.md) |
| 133 | [`RecipeRegistry`/`CreativeCatalog`: compose → freeze → gameplay reads only](0133-reciperegistry-creativecatalog-compose-freeze-gameplay.md) |
| 134 | [`RakNetSession`'s UDP send stays inside `_sessionLock` — accepted, not fixed](0134-raknetsessions-udp-send-stays-inside-sessionlock.md) |
| 135 | [A fatal combat blow always lands; loot-capacity failure only drops the loot, never the kill](0135-a-fatal-combat-blow-always-lands-loot-capacity-failure.md) |
| 136 | [Vehicle dismount sends the rider an authoritative MovePlayer teleport — resolves Phase XX's open question](0136-vehicle-dismount-sends-the-rider-an-authoritative.md) |
| 137 | [Ground-cover vegetation (short grass/flowers): a per-column probability roll, not a cell grid — and a new "passable decoration" block class](0137-ground-cover-vegetation-short-grass-flowers-a-per.md) |
| 138 | [Recipe/item catalog expansion, scoped to what already has real gameplay behind it](0138-recipe-item-catalog-expansion-scoped-to-what-already.md) |
| 139 | [A real crafting table: a second, separate 3×3 grid, and 12 of 17 recipes actually need one](0139-a-real-crafting-table-a-second-separate-3-3-grid-and-12.md) |
| 140 | [NetherNet is a complete independent transport](0140-nethernet-is-a-complete-independent-transport.md) |
| 141 | [Stable Bedrock 1.26.52 targets protocol 2193](0141-stable-bedrock-1-26-52-targets-protocol-2193.md) |
| 142 | [Real Mojang game data, owned end-to-end: a first-party `.brarchive` reader, and the real vanilla creative catalog](0142-real-mojang-game-data-owned-end-to-end-a-first-party.md) |
| 143 | [Transport boundary: `Network/`, NetherNet rebuilt on the axolotl port (nov 2026)](0143-transport-boundary-network-nethernet-rebuilt.md) |
| 144 | [Protocol 2225 (Bedrock 1.26.62): wire first, data later (nov 2026)](0144-protocol-2225-bedrock-1-26-62-wire-first-data-later-nov.md) |
| 145 | [`BinaryStream` and `ILogger` leave the RakNet assembly (nov 2026)](0145-binarystream-and-ilogger-leave-the-raknet-assembly-nov.md) |
| 146 | [Crafting runs on the real vanilla recipe set, matched by shape, name and tag](0146-crafting-runs-on-the-real-vanilla-recipe-set-matched.md) |
| 147 | [Item stacks carry an aux (variant) value; recipes can require and produce it](0147-item-stacks-carry-an-aux-variant-value-recipes-can.md) |
| 148 | [Item damage is stack user data, not aux (evidence from a stock BDS 1.26.63.1)](0148-item-damage-is-stack-user-data-not-aux-evidence.md) |
| 149 | [Every actor species is an ECS actor; one replication path; the legacy combat backend is gone](0149-every-actor-species-is-an-ecs-actor-one-replication.md) |

Moved out of the log but still evidence for §99–§102: [first actor vertical slice findings](../history/first-actor-findings.md).
