# Zenith × Dragonfly — o que falta no Zenith (2026-11-15)

**Método.** Dois levantamentos feitos lendo o código, não a documentação: `dragonfly-reference/server` (Go, commit `0f9a3a57` de 2026-11-07, uma árvore customizada e mais nova que o Dragonfly público: NetherNet, portal do End, engine de redstone, API de HUD) e `zenith/src` (develop, `63ae4d8`). Contagens de blocos, itens, partículas e sons do Dragonfly vêm de grep e contagem de arquivos, então são aproximadas. Nada foi executado.

**Aviso sobre a documentação do próprio Zenith.** `docs/vanilla-behavior.md` está desatualizado (diz que não há mobs; há 11 espécies) e `data/manifest.json` ainda diz protocolo 2193 / 1.26.52 enquanto o código mira 2225 / 1.26.62. Esta análise foi feita pelo código.

**Legenda:** ✅ equivalente · ⚠️ parcial · ❌ ausente · ➕ só no Zenith · — não se aplica

**Como ler a comparação.** Dragonfly é uma **biblioteca** para quem escreve o servidor (API de eventos, `Tx`, blocos/itens customizados). Zenith é um **servidor** com plugin API congelada. Onde a diferença é de modelo (API de extensão) e não de funcionalidade de jogo, a linha está marcada como tal.

---

## 1. Resumo em números

| | Dragonfly | Zenith |
|---|---|---|
| Handlers de pacote serverbound reais | 24 (+14 explicitamente ignorados) | 16 (+9 no-op) no jogo; mais os de login/pré-spawn |
| Pacotes clientbound emitidos | ~75 tipos | ~100 classes de pacote, das quais ao menos 6 nunca são enviadas (ver §3) |
| Dimensões | Overworld, Nether, End (+ custom) | só Overworld |
| Geração de terreno | só `flat` (interface `Generator` para custom) | ➕ `flat` e `noise` (5 biomas, cavernas, minérios, árvores, ruínas) |
| Blocos registrados/usáveis | ~225 tipos com comportamento | 1477 no palette, ~97 colocáveis, ~114 quebráveis em survival |
| Itens com comportamento | ~141 + ~259 itens-bloco | 2076 no palette; comportamento só de comida, ferramentas, armadura, caixa de itens |
| Receitas | shaped, shapeless, smithing, poções, fornalha | 3819 (só shaped/shapeless; sem fornalha, smithing, stonecutter) |
| Encantamentos / efeitos / poções | 27 / 30 / 43 | 0 / 2 (Regeneration, Poison) / 0 |
| Entidades | 16 tipos (projéteis, TNT, orbes, itens…), **sem mobs** | 11 espécies de mob com IA simples, natural spawn e loot tables |
| Block entities | 25 tipos | 1 (baú) |
| Comandos | framework, **nenhum embutido** | 7 embutidos, sem modelo de permissão |
| Transportes | RakNet, NetherNet | RakNet, NetherNet |

---

## 2. Pacotes serverbound (handlers)

| Pacote | Dragonfly | Zenith | Falta no Zenith |
|---|---|---|---|
| PlayerAuthInput | ✅ movimento + flags (sprint, sneak, swim, glide, crawl, fly, jump) + ações de bloco + item stack request inline | ⚠️ movimento (só checa float finito), sneak/sprint, ações de bloco, item interaction, ISR inline | swim/crawl/glide/jump/fly; validação de movimento |
| PlayerAction | ✅ StartBreak/Abort/Predict/Crack, Start/StopSleeping, Respawn, DimensionChangeDone, StartItemUseOn, InteractWithBlock, MissedSwing | ⚠️ HandledTeleport, Respawn, CreativeDestroy/Predict; `StartItemUseOn/StopItemUseOn` só logam | sono, mudança de dimensão, uso contínuo de item |
| InventoryTransaction | ✅ Normal, Mismatch, UseItemOnEntity, UseItem (ClickBlock/ClickAir/BreakBlock), ReleaseItem | ⚠️ Normal só no formato "soltar do slot"; ItemUse (place/open/air/break/UseAsAttack); ItemUseOnActor (attack/interact); Release só cancela comer | release de arco/besta; Normal de offhand/inventário; usar item em entidade além de ataque/interação |
| ItemStackRequest | ✅ Take, Place, Swap, Destroy, Drop, Create, CraftCreative, MineBlock, CraftRecipe(+auto), Beacon, Anvil, Enchanting, Grindstone, Loom, Smithing, Stonecutter | ⚠️ Take/Place, Swap, Drop, Destroy, Create, MineBlock, CraftRecipe(+auto), CraftCreative; **recusa** Beacon, Loom, Grindstone, Anvil (CraftRecipeOptional), LabTable | Beacon, anvil, enchant, grindstone, loom, smithing, stonecutter; janelas de fornalha/bancada |
| CommandRequest | ✅ | ✅ | — |
| Text (chat) | ✅ | ✅ (com rate limit) | — |
| Interact | ⚠️ MouseOverEntity, OpenInventory | ⚠️ só OpenInventory | MouseOverEntity, montaria |
| MobEquipment | ✅ | ✅ | — |
| ContainerClose | ✅ | ✅ | — |
| RequestAbility | ✅ voo | ⚠️ só em creative, apenas ecoa | estado de voo no servidor |
| RequestChunkRadius | ✅ | ✅ (negociação própria) | — |
| Respawn | ✅ | ✅ | — |
| PlayerSkin / Emote | ✅ | ✅ | — |
| **BlockPickRequest** | ✅ | ❌ | pegar bloco (botão do meio) |
| **BlockActorData** | ✅ (edição de placa) | ❌ | placas |
| **BookEdit** | ✅ | ❌ | livros |
| **ModalFormResponse** | ✅ forms/diálogos | ❌ no-op (e o `ModalFormRequest` nunca é enviado) | formulários inteiros |
| **LecternUpdate** | ✅ | ❌ | púlpito |
| **NPCRequest** | ✅ | ❌ | diálogos de NPC |
| **SubChunkRequest** | ✅ | ❌ (só colunas completas) | protocolo de sub-chunk |
| **ClientCacheBlobStatus** | ✅ | ⚠️ reconhecido, sem blobs | cache de blobs de chunk |
| **ServerBoundDiagnostics / LoadingScreen** | ✅ | ⚠️ loading screen é no-op; diagnostics não verificado no levantamento | telemetria do cliente |

Pacotes que o Dragonfly ignora e o Zenith também (sem lacuna): `MovePlayer`, `Animate`, `EmoteList`, `AdventureSettings`, `SetPlayerInventoryOptions`.

---

## 3. Pacotes clientbound

| Grupo | Dragonfly emite | Zenith | Falta |
|---|---|---|---|
| Entrada/mundo | PlayStatus, Disconnect, **Transfer**, **ChangeDimension**, Respawn, SetTime, GameRulesChanged, SetSpawnPosition, BiomeDefinitionList, AvailableActorIdentifiers, CreativeContent, CraftingData, TrimData, PlayerList | ✅ quase tudo (identificadores e trim vêm dos blobs capturados do BDS) | Transfer, ChangeDimension |
| Chunks/blocos | LevelChunk, **SubChunk**, NetworkChunkPublisherUpdate, ChunkRadiusUpdated, ClientCacheMissResponse, UpdateBlock, BlockEvent, **BlockActorData**, **OpenSign** | ⚠️ LevelChunk, NetworkChunkPublisherUpdate, ChunkRadiusUpdated, UpdateBlock, BlockEvent | SubChunk, BlockActorData, OpenSign, cache de blobs |
| Entidades | AddActor/Player/ItemActor, RemoveActor, MoveActorAbsolute, MovePlayer, SetActorMotion, SetActorData, ActorEvent, Animate, **AnimateEntity**, TakeItemActor, MobEffect, MobEquipment, MobArmourEquipment, UpdateAttributes, PlayerSkin, Emote | ✅ equivalente (+ SetActorLink para minecart) | AnimateEntity |
| Inventário | InventoryContent, **InventorySlot**, ContainerOpen/Close, **ContainerSetData**, ItemStackResponse, **PlayerEnchantOptions**, **ClientStartItemCooldown** | ⚠️ InventoryContent, ContainerOpen/Close, ItemStackResponse, PlayerHotbar | InventorySlot (incremental), ContainerSetData (fornalha/bancada), opções de encantamento, cooldown de item |
| Estado do jogador | UpdateAbilities, UpdatePlayerGameType, SetPlayerGameType, **UpdateClientInputLocks**, **SetHud**, ToastRequest | ⚠️ UpdateAbilities, SetPlayerGameType, ToastRequest, UpdateAdventureSettings | input locks, HUD |
| UI | Text, **SetTitle**, **ModalFormRequest**, **ClientBoundCloseForm**, **NPCDialogue**, **BossEvent**, **SetDisplayObjective/SetScore/RemoveObjective**, **PrimitiveShapes** | ⚠️ Text, ToastRequest. As classes de **título, form e close-form existem mas nunca são enviadas** | forms, títulos/actionbar, boss bar, scoreboard, diálogo, formas de debug |
| Comandos | AvailableCommands, **CommandOutput**, **UpdateSoftEnum** | ⚠️ AvailableCommands; feedback sai como toast | CommandOutput (resposta nativa), soft enums |
| Efeitos/áudio | **LevelEvent** (partículas, clima…), **LevelEventGeneric**, LevelSoundEvent, **PlaySound**, **StopSound** | ⚠️ LevelEvent só para rachadura de bloco; LevelSoundEvent; PlaySound/StopSound existem sem uso | partículas, clima, sons por servidor |

---

## 4. Eventos que o Dragonfly expõe a plugins (e o Zenith não)

O Zenith tem plugin API **congelada de propósito**. Esta tabela serve de mapa dos pontos de extensão que uma API futura precisaria cobrir, e mostra quais já têm um ponto natural no código.

| Dragonfly `Handle*` (player) | Ponto equivalente no Zenith |
|---|---|
| Move, Jump, Teleport, ChangeWorld, ToggleSprint/Sneak | `MovementSystem` (sem jump/world) |
| Chat, CommandExecution | `ChatSystem`, `CommandRuntime` |
| Hurt, Heal, FoodLoss, Death, Respawn, SetOnFire, FireExtinguish | `HealthState`, `HungerSystem`, `PlayerDamage` (sem fogo) |
| StartBreak, BlockBreak, BlockPlace, BlockPick | `BlockDigSystem`, `BlockEditSystem` (sem pick) |
| ItemUse, ItemUseOnBlock, ItemUseOnEntity, ItemRelease, ItemConsume, ItemDamage, ItemPickup, ItemDrop, HeldSlotChange | `InGameUseItemHandler`, `InventorySystem`, `FloorDropSystem` |
| AttackEntity, PunchAir, ExperienceGain | `PlayerMeleeSystem`, `MobKillReward` |
| SkinChange, SignEdit, Sleep, LecternPageTurn, Transfer, Quit, Diagnostics | skin ✅; placa, sono, púlpito, transfer ❌ |

| Dragonfly `Handle*` (world) | Zenith |
|---|---|
| LiquidFlow / Decay / Harden | ❌ água estática |
| FireSpread, BlockBurn | ❌ |
| CropTrample, LeavesDecay | ❌ |
| PortalCreate, PortalActivate | ❌ |
| EntitySpawn, EntityDespawn | ✅ internamente (`MobSpawner`, `DespawnLifecycle`) |
| Explosion | ⚠️ só o creeper |
| RedstoneUpdate | ❌ |
| Sound, Close | ⚠️ |

Diferença estrutural: o Dragonfly entrega **um `Handler` por jogador/mundo com `Context` cancelável**. O Zenith não tem esse contrato; hoje o equivalente seria um conjunto de portas nos sistemas do `GameLoop`.

---

## 5. Funcionalidades de jogador

| Funcionalidade | Dragonfly | Zenith |
|---|---|---|
| Fome / saciedade / exaustão / inanição | ✅ | ⚠️ sem exaustão de andar/pular |
| Dano de queda | ✅ | ✅ |
| Void | ✅ | ✅ |
| Afogamento / ar | ✅ | ❌ |
| Fogo, lava, dano ambiental | ✅ | ❌ (não há lava; `DamageCause` só tem 7 causas) |
| Natação, rastejar, planar (elytra) | ✅ | ❌ |
| Sono / cama / ponto de spawn | ✅ | ❌ (sempre renasce na origem) |
| Efeitos de poção | 30 | 2 (Regeneration, Poison) |
| Totem, escudo | totem ✅, escudo ❌ | ❌ ambos |
| Encantamentos | 27 | ❌ |
| Armadura: redução + desgaste | ✅ | ⚠️ redução sim, **sem desgaste**; armas também não desgastam |
| Experiência | níveis, progresso, orbes | ⚠️ níveis e progresso, **sem orbes** (XP vai direto ao jogador) |
| Modos de jogo | survival, creative, adventure, spectator | survival, creative |
| Forms (custom/menu/modal) | ✅ | ❌ (pacotes existem, sem uso) |
| Título, actionbar, toast | ✅ | só toast |
| Scoreboard, boss bar, HUD, input locks | ✅ | ❌ |
| Diálogo de NPC | ✅ | ❌ |
| Persistência de jogador | LevelDB, JSON (inventário, efeitos…) | ⚠️ inventário, armadura, offhand, posição, modo, XP, vida, fome. **Não** persiste efeitos, slot selecionado nem spawn |
| Baú de Ender | ✅ | ❌ |
| Validação de movimento (anti-cheat) | cliente autoritativo, com checagens básicas | ❌ cliente autoritativo, só checa float finito |

---

## 6. Mundo

| Funcionalidade | Dragonfly | Zenith |
|---|---|---|
| Overworld | ✅ (só flat) | ✅ flat + ➕ noise com biomas, cavernas, minérios, árvores |
| Nether / End | ✅ mundos ligados, portais | ❌ (ids de dimensão são só constantes de protocolo) |
| Provider | LevelDB no formato Bedrock (mcdb) | ⚠️ LevelDB com **esquema próprio** (edições + baús; terreno regenerado da seed) |
| Importar mundo Bedrock | ✅ | ❌ |
| Clima (chuva, tempestade, raio) | ✅ | ❌ |
| Ciclo dia/noite | ✅ + dormir avança o dia | ⚠️ derivado do tick, não persistido, sem `/time` |
| Líquidos fluindo, baldes | ✅ | ❌ |
| Fogo e propagação | ✅ | ❌ |
| Crescimento (plantações, mudas, bambu, cactos…) | ✅ | ❌ |
| Queda de folhas, espalhar grama | ✅ | ❌ |
| Random ticks, scheduled ticks, neighbour updates | ✅ | ❌ (nenhum random tick) |
| Explosões | TNT, cristal do End | ⚠️ só creeper (r=3, sem drops) |
| Redstone | engine dedicada (fio, tocha, bloco, alavanca, minério, nota, TNT) | ❌ |
| Gravidade de blocos | areia, cascalho, concreto em pó, bigorna | ⚠️ só areia e cascalho |
| Estruturas | API programática (`BuildStructure`) | ⚠️ "ruínas" pequenas; nenhuma estrutura vanilla |
| Biomas | 89 definições (não geram terreno) | 5 gerados + blob de definições |
| Partículas / sons | ~20 / ~91 tipos | ❌ / pouco (só o evento de som do cliente) |
| Block entities | 25 (baú, fornalha, placa, banner, púlpito, farol, bancada de poções, funil…) | 1 (baú) |
| Resource packs | servidos + auto-build para blocos custom | ❌ (info e stack enviados vazios) |
| Raio de chunks | até 32 | até 32, com budgets por tick |

---

## 7. Blocos e itens

| Item | Dragonfly | Zenith |
|---|---|---|
| Blocos com comportamento | ~225 tipos; ~38 ativáveis, ~32 com random tick, ~70 com neighbour update | 1477 no palette; **~97 colocáveis**, ~114 com perfil de quebra. O resto não pode ser colocado nem quebrado em survival |
| Famílias que faltam | — | escadas, lajes, portas, alçapões, camas, escadas de mão, vidros/painéis, plantas, colheitas, tochas de parede, líquidos |
| Drops de bloco | tabela por bloco, fortuna, toque suave | ⚠️ 8 minérios especiais; resto dropa o próprio bloco (pedra dropa pedra, minério de ferro dropa o bloco) |
| Ferramentas | tiers incluindo cobre e netherite; enxada, pá, machado com ações | ⚠️ dano e velocidade por tier; sem ações de uso (arar, descascar, acender) |
| Armadura | 7 tiers + elytra + trims | ⚠️ 24 peças, sem trims, sem desgaste |
| Comida | ~30 | ✅ 40 |
| Arco, besta, projéteis | ✅ (arco, besta, flecha, bola de neve, ovo, pérola) | ❌ como itens (o projétil sai de `UseAsAttack` sem checar o item na mão) |
| Poções, frascos, cauldron | ✅ 43 poções | ❌ |
| Baldes, flint & steel, tesoura, osso | ✅ | ❌ |
| Livros e quill | ✅ | ❌ |
| Fogos de artifício | ✅ | ❌ |
| Receitas | shaped, shapeless, multi, smithing (transform/trim), poção, receita dinâmica; fornalha via `SmeltInfo` | ⚠️ 3819 shaped/shapeless; **sem fornalha, smithing, stonecutter, poção** |
| Inventário criativo | ✅ | ✅ 1980 itens em 124 grupos |
| API de blocos/itens customizados | ✅ + resource pack automático | ❌ |
| Tamanho de pilha | por item | ✅ `item_stack_sizes.json` |

---

## 8. Entidades

| Entidade | Dragonfly | Zenith |
|---|---|---|
| **Mobs com IA** | ❌ nenhum | ➕ 11 espécies (zumbi, vaca, creeper, aranha, esqueleto, golem, enderman, peixe, minecart, morcego, aldeão), spawn natural e loot tables |
| Item no chão | ✅ merge, despawn, coleta | ✅ física, merge, delay de coleta, despawn |
| Bloco em queda | ✅ | ✅ areia e cascalho |
| Projéteis | flecha, bola de neve, ovo, pérola, poção, fogos | ⚠️ um projétil genérico (esqueleto, `UseAsAttack`) |
| TNT | ✅ | ❌ |
| Orbe de XP | ✅ | ❌ |
| Raio | ✅ | ❌ |
| Cristal do End, nuvem de efeito | ✅ | ❌ |
| Texto flutuante | ✅ | ❌ |
| Minecart, barco | ❌ ambos | ➕ minecart (sem lógica de trilho) |
| Pathfinding | — | ⚠️ perseguição em linha reta com step-up |
| Persistência de entidades | por mundo | ❌ não persiste |
| API de entidade custom | ✅ `EntityType`/`TickerEntity` | ❌ |

---

## 9. Comandos, operação e infraestrutura

| Item | Dragonfly | Zenith |
|---|---|---|
| Framework de comandos | ✅ completo (structs com tags, enums, subcomandos, alvos, vec3) | ⚠️ 7 comandos, tipos Enum/Integer/String/Player |
| Comandos embutidos | nenhum | gamemode, help, sample, diagnostics, effect, gamerule, summon |
| Comandos que faltam | — | `/tp`, `/give`, `/time`, `/weather`, `/kill`, `/say`, `/kick`, `/ban`, `/op`, `/whitelist`, `/stop`, `/list`, `/clear`, `/xp`, `/difficulty`, `/setworldspawn`, `/fill`, `/setblock` |
| **Permissões** | via `Allower` e `Source` | ❌ **todo jogador executa como Operator** (inclusive `/gamerule` e `/summon`) |
| Console | via código do host | ❌ sem stdin |
| Ban / allowlist / ops | `Allower` (interface) | ❌ |
| Autenticação | Xbox (gophertunnel) ou desligada | ✅ Xbox, self-signed, offline, configurável |
| Transportes | RakNet, NetherNet | ✅ RakNet, NetherNet |
| Status / MOTD | provider substituível | ✅ por config |
| Configuração | `Config` Go + `UserConfig` TOML | ✅ YAML + overrides por env/CLI, Docker, Pterodactyl |
| Plugin API | `Handler` + `Context` | ❌ congelada de propósito |
| Telemetria | `ServerBoundDiagnostics` | ➕ `libs/diagnostics` com janelas, percentis e incidentes, `/diagnostics` |
| Tick | 20 TPS por mundo | ✅ 20 TPS, thread dedicada, ECS esparso, pacing medido |
| Query / RCON | — | ❌ |

---

## 10. Lacunas priorizadas

Critério: o que impede um servidor **survival vanilla jogável** primeiro, e só depois o que é extensão. A coluna "Referência" aponta onde o Dragonfly resolve o mesmo problema.

### P0 — quebram a promessa de vanilla ou são riscos de operação

| # | Lacuna | Por quê | Referência |
|---|---|---|---|
| 1 | **Permissões**: todo jogador é Operator | qualquer um muda game rules e invoca mobs | `cmd.Source`, `Allower` |
| 2 | Comandos de administração e console (`/stop`, `/kick`, `/op`, `/tp`, `/give`, `/time`) | não há como operar um servidor | `cmd` |
| 3 | Validação de movimento | cliente autoritativo sem checagem de velocidade, voo ou colisão | `session/handler_player_auth_input.go` |
| 4 | Blocos colocáveis: ~97 de 1477 | o mundo de survival não é construível; escadas, lajes, portas, vidro, plantas faltam | `block/register.go`, `block/*` |
| 5 | Random ticks e scheduled ticks | base de crescimento, folhas, fogo, líquidos | `world/tick.go`, `world/block.go` |

### P1 — loop de survival

| # | Lacuna | Referência |
|---|---|---|
| 6 | Líquidos (fluxo, baldes, lava) | `block/water.go`, `block/lava.go`, `block/liquid.go` |
| 7 | Fornalha + `ContainerSetData` + receitas de fundição | `block/furnace.go`, `item/smelting_info.go` |
| 8 | Dano de queda já existe; falta **afogamento, fogo, lava** | `player/player.go` |
| 9 | Drops por bloco (pedra→pedregulho, minério→item cru, folhas, grama) e `BlockLoot` data-driven como o loot de mobs | `block/break_info.go` |
| 10 | Desgaste de armadura e armas | `item/armour.go`, `item/durability.go` |
| 11 | Cama, sono e ponto de spawn | `block/bed.go`, `world/sleep.go` |
| 12 | Arco, besta e itens arremessáveis | `item/bow.go`, `entity/arrow.go` |
| 13 | Orbes de XP | `entity/experience_orb.go` |
| 14 | Efeitos e poções (além dos 2 atuais) | `entity/effect/*`, `item/potion*.go` |
| 15 | Modos adventure e spectator | `world/game_mode.go` |
| 16 | Clima | `world/weather.go` |
| 17 | Forms, título, actionbar, boss bar, scoreboard (os pacotes já existem) | `player/form`, `title`, `bossbar`, `scoreboard` |

### P2 — profundidade

| # | Lacuna | Referência |
|---|---|---|
| 18 | Nether e End: dimensões, geradores, portais, `ChangeDimension` | `world/portal`, `server.go` |
| 19 | Encantamentos + mesa + bigorna + grindstone | `item/enchantment/*`, `session/handler_enchanting.go` |
| 20 | `SubChunkRequest` e cache de blobs | `session/handler_sub_chunk_request.go` |
| 21 | Mais block entities: placa, fornalha, funil, shulker, púlpito | `block/*` com `NBTer` |
| 22 | Persistência de entidades e de efeitos do jogador | `player/playerdb` |
| 23 | Redstone | `world/redstone.go` (o Dragonfly também tem só o básico) |
| 24 | Resource packs e blocos/itens customizados | `block/customblock`, `internal/packbuilder` |

### Onde o Zenith já está à frente (não copiar)

- Terreno procedural com biomas, cavernas, minérios, árvores (o Dragonfly só tem flat).
- 11 mobs com IA, spawn natural, knockback, animação de morte e loot tables data-driven (o Dragonfly não tem mobs).
- Pipeline de chunks assíncrono com orçamento por tick e residência.
- Diagnóstico com percentis por janela.
- Modelo de thread único para o estado de jogo, com intents e testes de contrato.

### Armadilhas que a comparação revelou no próprio Zenith

1. **Pacotes que existem mas ninguém envia** (`ModalFormRequest`, `SetTitle`, `PlaySound`, `StopSound`, `ClientboundCloseForm`): a cobertura de pacotes nos CSVs de `plans/` conta classes, não uso. Vale o CSV passar a medir "emitido por gameplay".
2. **`UseAsAttack` dispara projétil com qualquer item**: precisa ser amarrado ao arco antes de existir munição.
3. **Respawn sempre na origem** e **tempo derivado do tick** (reinicia com o processo): ambos são estado de mundo que deveria ser persistido.
4. **Metadados desatualizados**: `data/manifest.json` (2193/1.26.52) e `docs/vanilla-behavior.md`.

---

## 11. Limites desta análise

- O Dragonfly analisado é uma árvore customizada; features como NetherNet e HUD podem não existir na versão pública.
- Dois trechos de `registerHandlers` (linhas 617–618) não foram inspecionados.
- Contagens de blocos, itens, partículas e sons do Dragonfly são aproximadas (grep e contagem de arquivos). O total de 1477 blocos e 2076 itens do Zenith vem de `manifest.json`, não de uma leitura do NBT.
- Nada foi executado: "emitido" e "tratado" significam presença de código com chamador, verificada por busca, não observação em runtime.
