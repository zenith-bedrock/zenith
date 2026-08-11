# Zenith Architecture Audit

Escopo: `Gameplay/Runtime`, `Gameplay/Systems`, os fluxos inbound completos (network → domínio → replicação), o modelo de gravidade/chunk streaming, concorrência, performance e boundaries de arquitetura. Auditoria puramente investigativa — nenhuma mudança de código foi feita. Todas as citações usam `arquivo:linha` do estado atual do repositório (branch `develop`).

Gatilho: dúvida levantada sobre se `IGameSystem`/`GameLoop` são a unidade de execução correta para responsabilidades discretas (Chat, GameMode, Equipment) versus simulação contínua (Movement, Block, Gravity), e se o modelo de gravidade/chunk streaming escala para mobs/AI/combate/redstone.

---

## Executive Summary

O runtime atual (`GameLoop` de 20 TPS + 9 `IGameSystem` registrados em ordem fixa) está **fundamentalmente correto** e não precisa de uma reescrita. A separação decide/transmite/serializa é respeitada na quase totalidade do código: nenhuma violação foi encontrada nas regras 3 e 4 do `ARCHITECTURE.md` (Packets não conhecem domínio; Gameplay não conhece `DataPacket`/`BinaryStream`). O padrão "handler enfileira intenção, system decide no tick" está implementado de forma consistente em 7 dos 9 fluxos auditados.

A hipótese de que Chat/GameMode/Equipment "não deveriam ser System" **não se confirma como problema real**: o custo desses três systems é uma verificação O(players) barata com early-continue por jogador, sem overhead mensurável às 20 Hz atuais. Substituí-los por uma abstração de Commands/Events não eliminaria trabalho nem risco — apenas renomearia o mesmo padrão consumidor-de-fila-pendente que já existe. Nenhuma das quatro implementações maduras pesquisadas (PocketMine, Minestom, Dragonfly, Cuberite) usa uma separação Systems/Commands/Events distinta para esse tipo de transição discreta; todas aplicam a mudança inline no tick (ou equivalente).

Os problemas reais encontrados são de outra natureza:

1. **Um bug de concorrência real** — campos de estado de "dig lock" em `Player` são mutados sem lock tanto pela thread de rede quanto pela thread de tick (§Concorrência, ZAR-001).
2. **Duas fugas confirmadas do modelo "GameLoop single-threaded"** — continuations assíncronas de `ChunkStreamSystem` e `PreSpawnSessionHandler` tocam estado de `Player` fora do tick, sem passar por fila (ZAR-002).
3. **Uma lacuna de validação** — abertura de baú não tem verificação de reach/distância, diferente de place/break (ZAR-003).
4. **Débito estrutural real em `BlockSystem`** — 627 linhas misturando três disparadores semanticamente distintos (dig lifecycle, resolução de edit, pickup de floor drop) que já são fases sequenciais separadas no código, mas não são tipos separados (ZAR-010).
5. **Zero testes de arquitetura** — nenhuma regra de layering é verificada automaticamente; dependem só de convenção/review (ZAR-005).
6. **Um overload de teste morto em produção** replicado em 8 dos 9 systems, custando um campo `_onlineScratch` redundante por system (ZAR-008).

Nenhuma dessas correções exige ECS, Actor Model, Command Bus, Event Sourcing, ou um framework de replicação genérico. Todas são fixáveis com código explícito, incremental, sem trocar o modelo de execução.

---

## Current Runtime Model

```
ZenithServer (composition root)
    │
    ├─ RakNetServer.StartAsync()      ─┐  thread de rede (RakNet receive loop)
    │                                   │  Handlers em Session/Handler/*
    │                                   │  escrevem em filas/slots pendentes por-Player
    │                                   ▼
    └─ GameLoop.RunAsync()  20 TPS      Player (pending intent fields, cada um com seu lock)
            │
            ├─ clock.Advance()
            ├─ players.FillOnline(_onlineScratch)   — 1 snapshot compartilhado, sem alocação
            └─ foreach system in ordem-de-registro:
                  try { system.Tick(clock, online) } catch { log, continua }

Ordem real (ZenithServer.cs:62-101):
  1. TimeSyncSystem     6. BlockSystem
  2. MovementSystem     7. GravitySystem
  3. EquipmentSystem    8. InventorySystem
  4. ChatSystem         9. ChunkStreamSystem
  5. GameModeSystem
```

`IGameSystem` (`Gameplay/Runtime/IGameSystem.cs:11`) é um contrato de 1 método: `Tick(GameClock, IReadOnlyList<Player>)`. O `GameLoop` não conhece a lógica interna de nenhum system — só chama, cronometra e isola exceções por system (`GameLoop.cs:40-50`).

---

## Dependency Graph

Confirmado por grep exaustivo (agente de boundary):

```
Packets/     → (nada de Gameplay/Player/World/Server)         ✅ regra 4 respeitada, 0 violações
Gameplay/    → não referencia DataPacket/BinaryStream          ✅ regra 3 respeitada, 0 violações
Session/     → Player (via pending-intent fields, majoritariamente)
             → 1 violação real (ZAR-001) + 1 exceção documentada (SelectedHotbarSlot, ADR §80)
             → várias exceções não-documentadas de baixo risco (ZAR-014/ZAR-015)
World/       → Gameplay/Systems consomem sincronamente (GetBlock/TrySetBlock/CanAcceptBlockWrite)
             → I/O de persistência é sempre fire-and-forget (`_ = _storage.PutOverlayAsync(...)`)
Protocol/    → chamado diretamente de dentro de Systems (Tick) — é o próprio contrato de "transmit"
```

Nenhuma dependência circular conceitual foi encontrada. O grafo é unidirecional como documentado.

---

## GameLoop Analysis

`Gameplay/Runtime/GameLoop.cs` (69 linhas) e `GameClock.cs` (47 linhas), citados na íntegra.

| Aspecto | Achado |
|---|---|
| Frequência | `GameClock.TicksPerSecond = 20` (const), `DayLengthTicks = 24000` |
| Scheduling | Fixed-timestep com âncora (`loopStart + interval * tickIndex`) — drift-corrected, não `Task.Delay` naive por iteração. Se um tick atrasa, o próximo delay encolhe (ou zera) em vez de acumular drift. |
| Ordem | Ordem de `Register()` = ordem de execução. Único comentário de ordering explícito na composition root: Movement antes de Block/Inventory por causa de `IsSneaking` (`ZenithServer.cs:63`). |
| Snapshot de players | `_players.FillOnline(_onlineScratch)` — **uma vez por tick**, lista reutilizada, sem alocação (`GameLoop.cs:39`), passada por referência a todos os systems. |
| Alocações no loop | Nenhuma no próprio `GameLoop`. Ver achados por-system para alocações internas. |
| Locks | Nenhum lock no `GameLoop` em si — a sincronização cross-thread vive inteiramente nos campos pendentes de `Player`. |
| Awaits/async | Só o `Task.Delay` de pacing entre ticks. Nenhum system é `await`ado dentro do loop de systems — mas ver ZAR-002: um system (`ChunkStreamSystem`) dispara trabalho assíncrono fire-and-forget de dentro do seu `Tick` síncrono. |
| Exceções | Cada `system.Tick(...)` tem seu próprio `try/catch`; uma exceção só loga (`_logger.Error`) e o loop continua para o próximo system **na mesma tick** — sem rollback, sem circuit breaker, sem distinguir falha transiente de fatal (ZAR-007). |
| Shutdown | `GameLoop` não tem shutdown próprio — só sai do `while` no `OperationCanceledException`. Todo o trabalho de settle/flush (gravidade, persistência) é feito por `ZenithServer.ShutdownAsync` **depois** de cancelar o token, sem esperar confirmação de que o loop de fato parou (`ZenithServer.cs:275-311`). |
| Tick excede budget | Não há detecção/log explícito de tick overrun — o único sinal indireto é `GameClock.MeasuredTps` ficar abaixo de 20, mas nada consome essa métrica hoje (nenhum alerta, nenhum log). |

---

## System-by-System Analysis

Tabela síntese (evidência completa por system no relatório de research; aqui os fatos relevantes para decisão):

### MovementSystem (295 linhas)
- **Trigger real:** input de `PlayerAuthInputPacket`, um slot overwrite-latest (não fila).
- **Estado lido/alterado:** Position/Pitch/Yaw/IsOnGround/IsSneaking/IsSprinting, Health, FallPeakY, OpenChest, GameMode (leitura).
- **Frequência necessária:** 20 TPS — é literalmente a simulação de física/posição. Nenhuma dúvida aqui.
- **Classificação:** Simulation. **Manter como System, sem mudanças de modelo.**

### BlockSystem (627 linhas)
- **Trigger real:** três disparadores distintos coexistindo na mesma classe: (a) `DigIntent` (crack/predict/continue/abort, com timing-state-machine), (b) `BlockEditIntent` (place/break, resolução de mundo), (c) nenhum — `PickupFloorDrops` roda incondicionalmente todo tick, é manutenção contínua não ligada a nenhuma intent.
- **Complexidade:** alta — reach check, Creative vs Survival, chest pairing, obstruction check, dig-auth timing anti-speedhack, floor drop overflow, persistência de inventário.
- **Classificação:** Simulation + Input consumption + World maintenance, misturados na mesma classe.
- **Achado:** as três fases já são sequenciais e semanticamente separadas no próprio código (`BlockSystem.cs:39-45` dig, `:47-58` edit, `:87` pickup incondicional) — a costura para separação já existe, só não foi extraída (ZAR-010).

### GravitySystem (288 linhas)
- **Trigger real:** `World.GravityPending` (fila com dedup via `HashSet`), alimentada exclusivamente por `BlockSystem.NotifyGravityAfterEdit` (único call site no repo).
- **Modelo:** `BeginNewFalls` (máx. 2 novas quedas/tick, `MaxStepsPerTick`) + `AdvanceActiveFalls` (recomputa landing a cada tick, sem cache — corrige bug documentado do §95 anterior) + cascata via fila `_deferred` promovida no próximo tick (evita colapso de torre inteira em 1 tick).
- **Shutdown:** `SettleAllPending()` drena tudo e resolve sem animação antes do flush de persistência — chamado só por `ZenithServer.ShutdownAsync`, prova que representa estado real do mundo.
- **Achado de escala:** `AdvanceActiveFalls` escaneia toda a lista `FallingBlocks.Active` (até 512, `SoftCap`) **sem batching**, ao contrário de `BeginNewFalls`/`ChunkStreamSystem` que limitam trabalho iniciado por tick (ZAR-011 — risco de escala, não bug atual).
- **Classificação:** Simulation (scheduled block update, especializado para sand/gravel). **Manter — validado externamente** (ver comparação).

### InventorySystem (398 linhas)
- **Trigger real:** `InventoryWindowIntent` (open/close) + `InventoryStackIntent` (ISR), ambos filas.
- **Modelo transacional:** snapshot→apply→rollback-on-any-failure (all-or-nothing por request) — corretude real, precisa ser determinístico e sequencial. Precisa do tick.
- **Classificação:** Simulation / Command processing. **Manter.**

### ChunkStreamSystem (113 linhas)
- **Trigger real:** recomputa chunk atual e diffa contra known chunks **todo tick, para todo jogador**, não é gated por flag de "cruzou boundary". O gate real de custo é `MaxStartsPerTick = 8` (novas streams iniciadas) — o resto do trabalho por tick é O(1) checks (`PublisherCenterChanged`, `TryBegin`).
- **I/O:** `StreamAsync` é fire-and-forget, `await`s LevelDB fora do tick, e a continuation **toca `Player.IsInGame`/`IsSpawning`/`Player.Chunks` diretamente**, sem passar por fila — validado como a real fuga do modelo single-threaded (ZAR-002).
- **Classificação:** Streaming / Replication. **Manter no tick** (validado externamente: PocketMine, Minestom e Dragonfly também fazem streaming tick-polled). **Corrigir apenas o mecanismo da continuation assíncrona.**

### EquipmentSystem (66 linhas, transcrito na íntegra)
- **Trigger real:** diff de 3 campos (`SelectedHotbarSlot`, `HeldStackId`, `HeldCount`) contra o último estado replicado — dirty-check por jogador, com early-continue.
- **Custo:** desprezível — `if (online.Count < 2) continue;` já evita fan-out para partidas solo.
- **Achado de ordering:** registrado **antes** de `BlockSystem`/`InventorySystem` (`ZenithServer.cs:64` vs `:99,101`) — uma mudança de stack causada por break/place/craft só é vista pela replicação de equipamento **no tick seguinte** (defasagem de até 50ms). Não é bug de corretude, é uma defasagem de latência evitável.
- **Por que não extrair como Command/Event:** não existe hoje nenhum evento "ItemConsumed"/"StackChanged" emitido pelos ~15 call sites que mutam `Inventory` em `BlockSystem`/`InventorySystem`. Introduzir isso agora exigiria instrumentar todos esses call sites para ganhar exatamente o mesmo resultado que o diff já dá de forma simples. Passa no teste da seção 11 do brief (só 1 caso real, não 2-3 — código explícito continua superior a um framework de dirty-tracking).
- **Classificação:** Replication (dirty-diff). **Manter como System — apenas reordenar para depois de Block/Inventory.**

### GameModeSystem (59 linhas, transcrito na íntegra)
- **Trigger real:** slot overwrite-latest (`_pendingGameMode`), alimentado por `/gamemode` via chat OU `CommandRequestPacket` — nunca por um pacote dedicado.
- **Achado de gap:** **nenhuma checagem de permissão/OP** — qualquer jogador conectado pode `/gamemode creative` a si mesmo. Isso é um gap de produto/segurança, não de arquitetura de tick — mas vale registro (não pontuado como ZAR por estar fora do escopo desta auditoria de runtime, mas citado nos Riscos).
- **Efeitos colaterais:** persistência síncrona (`PersistPlayerData`), múltiplos protocol calls, `PlayerVisibility.RefreshPeerView`.
- **Por que não remover do tick:** a decisão em si (`SetGameMode` + side-effects) precisa rodar fora da thread de rede (regra 6). Um "Command" processado fora do `IGameSystem` ainda precisaria de um dispatcher rodando em algum lugar determinístico — na prática, reimplementar o mesmo mecanismo com outro nome. Nenhuma implementação madura pesquisada usa um Command Bus dedicado para isto.
- **Classificação:** State transition (discreta). **Manter como System — sem necessidade de extração.**

### ChatSystem (45 linhas, transcrito na íntegra)
- **Trigger real:** drena fila FIFO completa por jogador, todo tick, e faz broadcast O(peers) por mensagem.
- **Achado:** é o único system cujo loop externo **não** filtra `player.IsInGame` antes de drenar a fila (só o loop interno de fan-out filtra `peer.IsInGame`) — inconsistência menor com os outros systems, sem impacto de corretude observado (mensagens de jogador não-InGame não deveriam existir na fila, mas o código não documenta essa garantia).
- **Por que não extrair:** o próprio doc comment já declara o invariante correto — "Handler só enfileira; nunca escreve no RakNetSession dos peers." Isso já é a separação que a seção 6 do brief pede. Uma fila + dispatcher fora do `IGameSystem` seria o mesmo mecanismo com um nome diferente.
- **Classificação:** Event reaction / Replication. **Manter como System.**

### TimeSyncSystem (36 linhas, transcrito na íntegra)
- **Trigger real:** cadência fixa (`tick % 20 == 0`), não dirty-check — broadcast incondicional a cada 20 ticks.
- **Achado:** é o único system que de fato lê `clock.CurrentTick` para se auto-limitar; todos os outros ignoram `clock` (`_ = clock;`). Isto é uma categoria genuinamente diferente — "scheduled periodic task" — mas em 36 linhas, extrair isso para um "Scheduler" genérico violaria a seção 14 do brief (infra congelada) sem ganho medido.
- **Classificação:** Scheduled protocol task. **Manter como System — trivial demais para justificar extração.**

---

## Input → Domain → Replication Flows

Tabela-resumo (evidência completa e trechos de código no relatório de research):

| Flow | Handler | Estado pendente (lock) | Validação no handler | System (decisão) | Replicação |
|---|---|---|---|---|---|
| Movement | `InGameAuthInputHandler.cs:14` | `_movementInput` (`_movementInputLock`, slot) | só `IsSecure()` (floats finitos) — **sem** anti-cheat de velocidade/teleport | `MovementSystem.Tick` | `SendMoveAbsolutes`/`SendActorFlags`/`SendAnimateSwingArm` |
| Block place | `InGameUseItemHandler.cs:31` | `_blockEdits` (`_blockEditLock`, fila cap 8) | hotbar slot bounds, stack validity, placeability, world bounds — **reach fica só no tick** | `BlockSystem.ApplyEdit` | `PublishUpdateBlocks` / `SendUpdateBlock` (reject) |
| Block break | `InGameAuthInputHandler.cs:14,127` | `_digIntents`+`_provisionalDig` (`_digLock`) e `_blockEdits` | dig-timing anti-speedhack já parcialmente no handler (`HandleBreakProgress`) | `BlockSystem.ApplyDig`/`ApplyEdit` | `BlockCrackFanout`, `BlockSoundFanout`, `PublishUpdateBlocks` |
| Chest/interact | `InGameUseItemHandler.cs:76`, `InGameInventoryHandler.cs:14,25` | `_windowIntents` (`_windowLock`, fila cap 4) | **nenhuma checagem de reach/distância** (ZAR-003) | `InventorySystem.ApplyWindow` | `ChestLidFanout.Open/Close`, `SendChestOpen/Content` |
| Equipment | `InGameInventoryHandler.cs:182`, `InGameUseItemHandler.cs:31` | `SelectedHotbarSlot` (sem lock — exceção documentada ADR §80) | bounds check | `EquipmentSystem.Tick` | `SendMobEquipment` |
| GameMode | `InGameSessionHandler.cs:155,180,194` | `_pendingGameMode` (`_gameModeLock`, slot) | parsing de comando; **sem checagem de permissão** | `GameModeSystem.Tick` | `SendPlayerGameType`/`SendLocalAbilities`/`SendAdventureSettings`/`SendCreativeContent` |
| Chat | `InGameSessionHandler.cs:155` | `_pendingChat` (`_chatLock`, fila cap 8) | `TokenBucketRateLimiter` + tamanho máximo | `ChatSystem.Tick` | `SendChat` |

Cada linha desta tabela é o padrão `THREAD → BOUNDARY → STATE OWNERSHIP → DECISION POINT → SIDE EFFECT` pedido no brief; o padrão é consistente e correto em 6 dos 7 fluxos.

**Chamadas de protocolo diretas do handler, fora do tick** (fora da tabela, achado transversal): `PlayerVisibility.RelaySwingArm` (place/break/predict), `session.Protocol.Ui.SendToast` (gamemode inválido), `session.Protocol.Entity.SendLocalAbilities` (pedido de fly ability), `PlayerVisibility.RelaySkin`/`RelayEmote`. Todos same-session ou cosmético (swing arm) — nenhum muta estado autoritativo, mas são exceções não-documentadas à regra "handler só enfileira" (ZAR-014/ZAR-015).

---

## Threading and Ownership

- **Duas execuções concorrentes de verdade:** thread de recepção RakNet (handlers) e o loop de tick do `GameLoop`. `PlayerManager._players` (`ConcurrentDictionary`) é o único ponto de acesso cross-thread desenhado para ser concorrente nesse nível.
- **Handoff padrão:** 8 pares de campo+lock em `Player` (dig, window, movement, block-edit, inventory-stack, chat, game-mode, respawn) — todos seguem exatamente o padrão "handler `Submit*` sob lock, tick `TryConsume*` sob o mesmo lock".
- **Quebra confirmada do padrão (ZAR-001):** `Player.HasBreakTarget`/`BreakTargetX/Y/Z`/`BreakStartedTick`/`BreakRequiredTicks`/`DigHeldStackId`/`LastDigActivityTick` são propriedades simples, sem lock, escritas por `BlockSystem` (thread de tick) **e** por `InGameAuthInputHandler.TrySubmitBreak → Player.ClearBreakTarget → AbortBreak` (thread de rede, síncrono, fora de qualquer fila). É uma race real, não hipotética.
- **Fugas do modelo single-threaded (ZAR-002):** `ChunkStreamSystem.StreamAsync` e `PreSpawnSessionHandler.CompleteSpawnAsync` são continuations assíncronas que, após um `await` de I/O (LevelDB), leem/escrevem `Player.PositionX/Y/Z`, `IsInGame`, `IsSpawning`, `Player.Chunks` fora da thread de tick, sem devolver o resultado por fila. `PlayerChunkTracker` internamente tem lock próprio (`_gate`), então a estrutura dele é segura — o risco está nos campos de `Player` lidos ao redor dela.
- **Persistência:** sempre fire-and-forget (`_ = _storage.PutOverlayAsync(...)`) a partir do caminho síncrono de mutação — a tick nunca espera disco, exceto possivelmente `PersistInventory`/`PersistPlayerData` chamados sincronamente de dentro de `BlockSystem`/`GameModeSystem` (achado com confiança média — não foi confirmado se essas chamadas específicas são bloqueantes; recomenda-se confirmar antes do Phase 1 do plano).

---

## Tick vs Event-Driven Work

Conclusão direta à pergunta central do brief: **hoje, tudo que está no `GameLoop` precisa estar lá — mas por razões diferentes umas das outras**, e essa diferença deveria ser nomeada em comentário/documentação, não necessariamente em uma interface separada:

| Responsabilidade | Por que precisa do tick (evidência) |
|---|---|
| Movement | É a física/posição em si — não existe "decisão" sem avançar o tempo. |
| Block dig timing | Matemática de "elapsed ticks" anti-speedhack depende do clock do tick. |
| Block edit resolution | Precisa ser determinístico e sequencial com Movement (`IsSneaking`) e Gravity. |
| Floor drop pickup | É simulação contínua de proximidade — não é reação a um evento específico. |
| Gravity | Física com estado contínuo (velocidade, posição fracionária) — precisa de um "agora". |
| Inventory (ISR) | Transação all-or-nothing que precisa de ordenação determinística com outras mutações de estado. |
| Chunk streaming | Precisa recomputar posição vs. chunks conhecidos — mas o **início do I/O**, não a decisão, é o que poderia ser mais event-driven (cruzamento de boundary). Validado como não-outlier pela comparação externa. |
| Equipment | Não há evento equivalente hoje; diff-on-tick é o mecanismo mais simples disponível dado o estado atual do código. |
| GameMode | Decisão de domínio com side-effects (persistência, abilities) — precisa rodar fora da thread de rede; tick é o lugar determinístico mais simples que já existe para isso. |
| Chat | Fan-out precisa acontecer fora da thread de rede (regra explícita no próprio doc comment do system) — tick é o mecanismo mais simples. |
| TimeSync | Tarefa periódica pura — tecnicamente não é "simulação", mas trivial demais para justificar um scheduler dedicado. |

**Nenhum destes é "foreach player, if nothing changed, continue" desnecessário no sentido do brief** — cada um dos early-returns (`TryConsume`, dirty-diff) já existe e é barato. O padrão "algo aparentemente event-driven que deveria ficar deferred" também não apareceu: não há nenhum caso de mutação síncrona fora do tick que devesse, para determinismo, esperar o próximo tick (exceto os dois achados de ZAR-002, que são bugs de mecanismo, não de timing intencional).

---

## Block Update / Gravity Model

Já detalhado no comparativo por-system acima. Resumo do fluxo completo:

```
BlockSystem.ApplyEdit (place/break resolvido)
        │  NotifyGravityAfterEdit (único call site, BlockSystem.cs:439)
        ▼
World.GravityPending  — HashSet (dedup) + Queue ativa + Queue deferred, SoftCap 2048
        │  GravitySystem.Tick, MaxStepsPerTick = 2 novas quedas/tick
        ▼
FallingBlockStore.Active — List<FallingBlockEntry>, SoftCap 512 (safety net, não throttle primário)
        │  AdvanceActiveFalls recomputa landing a cada tick (sem cache — corrige bug do design anterior)
        ▼
Land() → World.TrySetBlock (overlay) + UpdateBlock batched + possível re-enqueue em cascata (deferred)
        │
        ▼ (shutdown)
SettleAllPending() — drena tudo, resolve sem animação, sem fan-out, antes do flush de persistência
```

Escala/robustez, ponto por ponto do brief:
- **Escala:** sim, dentro do volume atual — `MaxStepsPerTick=2` é o throttle real; `AdvanceActiveFalls` escaneia toda a lista de ativos sem cap (ZAR-011), mas como o throttle de entrada é 2/tick, a lista ativa nunca cresce rápido o suficiente para isso ser um problema hoje.
- **Ordering:** preservado — cascata via fila deferred promovida só no próximo tick, documentado como correção deliberada de um bug anterior (colapso de torre inteira em 1 tick).
- **Work duplicado:** não há — dedup via `HashSet` na entrada da fila.
- **Scanning desnecessário:** não há scan de mundo; tudo é dirigido por enqueue explícito no ponto de mutação.
- **Cascatas:** suportadas via `_deferred` + `PromoteDeferred()`.
- **Chunk unload:** **não existe conceito de chunk unload no servidor** (overlays vivem em RAM por todo o processo; "unload" só existe do ponto de vista do que um cliente específico já viu). Portanto esta preocupação do brief não se aplica à arquitetura atual — não é uma lacuna, é uma premissa que não existe (confirmado por grep: zero ocorrências de "unload" em `World/`).
- **Shutdown:** correto — `SettleAllPending` antes do flush, sem fan-out (clientes já desconectando).
- **Evoluir para outros tipos de física:** o mecanismo (fila com dedup + cap + cascata deferred) é estruturalmente idêntico ao que PocketMine, Dragonfly e Cuberite usam para "scheduled block updates" genéricos — ver comparação externa. Zenith já implementou a forma certa, só especializada para sand/gravel.

---

## Chunk Streaming Model

Já coberto na análise por-system. Achado central: **tick-polled é a escolha correta e validada externamente**, não um design smell. A única correção recomendada é de mecanismo (a continuation assíncrona tocando `Player` fora de fila — ZAR-002), não de modelo (não deve deixar de ser tick-driven).

---

## Replication Model

O único padrão de replicação repetido no código é o dirty-diff "last replicated vs current" — e aparece em exatamente **dois lugares**: `MovementSystem` (pose/flags, via `IsPoseDirty`/`IsFlagsDirty`) e `EquipmentSystem` (slot/held-id/held-count). Isso é abaixo do limiar de "2 ou 3 casos" que o brief pede para justificar uma abstração — **não introduzir `ReplicationManager`/`DirtyTrackerFramework` agora.** Se um terceiro caso relevante aparecer (ex.: attributes de combate, effects), reavaliar.

---

## Comparison with Other Implementations

Resumo (pesquisa completa com fontes no relatório de research):

| Concern | Zenith | PocketMine-MP | Minestom | Dragonfly | Cuberite |
|---|---|---|---|---|---|
| Movement/input vs tick | `IGameSystem` em ordem de registro | Inline em `network->tick()`, PHP single-threaded | Fase "connection tick" antes do tick de instance/dispatcher | Poll por tick dentro de `Tick()` da entidade | Fila de funções na thread de tick |
| Block updates / gravity | Fila dedup + cap + cascata deferred, entidade `falling_block` real (ADR §95) | Fila de prioridade genérica (`scheduledBlockUpdateQueue`), dedup por posição | Não é core-scoped | Fila de delay genérica (`ScheduleBlockUpdate`), dedup por posição+tipo | Fila de delay (`m_BlockTickQueue`) + "wake" para simuladores de fluido |
| Chunk streaming trigger | Tick-polled, `MaxStartsPerTick` anti-burst | Recalculado por movimento, depois otimizado para polling periódico (~20 ticks) | Batched pelo dispatcher por tick | Loader-attached viewers, ainda tick-polled | Não investigado |
| Equipment/discrete-state | Dirty-diff no tick (`EquipmentSystem`) | Não confirmado para equipment especificamente | Não resolvido | Não resolvido | Não investigado |
| Handler→domain coupling | Handler enfileira, system decide (regra 6) | Handler→event→mutação inline mesma tick (PHP não tem alternativa) | Decoupled via dispatcher/thread ownership por entidade | Decoupled via fila de transações (`handleTransactions`) — analogia mais forte com Zenith | Decoupled via fila de funções na thread de tick |

**Conclusão da comparação:** a fila genérica de "scheduled block update" (dedup + cap) é convergente em 3 implementações independentes — evidência forte de que é a forma certa, e Zenith já a tem (especializada). O modelo de handler-enfileira/tick-decide de Zenith tem sua melhor analogia real na fila de transações do Dragonfly, não em nenhum framework de Commands/Events dedicado. Nenhuma pista de que Systems/Commands/Events resolveria algo que o modelo atual não resolve.

---

## Performance Findings

- **Alocação por-tick confirmada:** `FloorDropStore.Snapshot()` (`FloorDropStore.cs:154-158`) é um iterator `yield return` chamado incondicionalmente todo tick por `BlockSystem.PickupFloorDrops` (`BlockSystem.cs:526`) — aloca um enumerador a cada tick independentemente de haver floor drops (ZAR-006).
- Todos os outros 8 systems usam campos `List<T>` pré-alocados, `.Clear()`ados por tick — sem alocação de coleção no hot path.
- `ChunkStreamSystem.ForEachInSquare` passa uma closure/lambda capturando `player`/`started`/locals por jogador por tick (`ChunkStreamSystem.cs:72`) — alocação de closure, não de coleção, mas real e por-jogador-por-tick.
- `PlayerManager.FillOnline` reusa buffer corretamente; `Online`/`SnapshotOnline()` alocam mas são doc-comentados como "não usar no tick" e de fato não são usados no hot path — só em handlers (frio).
- Nenhum LINQ, boxing ou lista temporária foi encontrado nos hot paths dos 9 systems.

---

## Concurrency Findings

Ver ZAR-001 e ZAR-002 (detalhados na seção de Threading acima e na tabela de findings). Resumo dos primitivos encontrados: `PlayerManager` usa `ConcurrentDictionary` corretamente; `World` usa `ConcurrentDictionary` + `Interlocked` para overlays; `LevelDbChunkStorage` é o único subsistema com thread dedicada + `ConcurrentQueue` + `Task.Run` (correto e intencional, é I/O). Nenhum `SemaphoreSlim`, `async void`, ou `.Result`/`.Wait()` bloqueante foi encontrado em código de gameplay.

---

## Architectural Boundary Violations

- **Regra 3 (Gameplay não referencia Packets):** 0 violações. Único hit de grep é um comentário XML doc mencionando `CraftingDataPacket` por nome, não uma referência de tipo.
- **Regra 4 (Packets não conhecem domínio):** 0 violações.
- **Regra 6 (handler só enfileira intenção):**
  - 1 violação real e não-documentada: `InGameAuthInputHandler.TrySubmitBreak → Player.ClearBreakTarget` (ZAR-001, mesma raiz do bug de concorrência).
  - 1 exceção documentada e aceitável: `SelectedHotbarSlot` (ADR §80, escalar atômico único).
  - Múltiplas exceções não-documentadas de baixo risco: `IsSpawning`/`IsInGame` escritos diretamente por handlers de sessão (são flags de state machine de conexão, não "posição/inventário/mundo" no sentido literal da regra, mas ainda cross-thread sem lock); `RequestAbilityPacket` tratado inteiramente fora do tick sem fila.

---

## Technical Debt

| Item | Evidência |
|---|---|
| Overload de teste morto em produção | `Tick(GameClock clock)` de 1 argumento existe em 8/9 systems (`GravitySystem` é a única exceção correta), sempre delegando para o overload de 2 argumentos, usado **só** por testes (`*Tests.cs`); nunca chamado por `ZenithServer`/`GameLoop`. Cada system carrega um `_onlineScratch` próprio, redundante, só para sustentar esse overload. |
| `BlockSystem` com 3 responsabilidades coesas mas não separadas | Dig lifecycle, block edit resolution, floor drop pickup — já são fases sequenciais distintas no código. |
| Sem testes de arquitetura | Zero `NetArchTest`/`ArchTest`/teste reflection-based de dependência em todo o repo. |
| Sem teste de ordem de execução de systems | Nenhum teste constrói um `GameLoop` real com múltiplos systems registrados e valida uma dependência de ordem (ex.: Block antes de Gravity). |
| Exceções não documentadas à regra 6 | Ver Boundary Violations acima. |

---

## Premature Abstractions to Avoid

Confirmando explicitamente a seção 14 do brief, com evidência que sustenta a recusa:

- **ECS** — nenhuma evidência de necessidade; os 9 systems já são explícitos e pequenos o suficiente.
- **Command Bus / Mediator** — GameModeSystem/ChatSystem já são, na prática, "command handlers" tickados; um bus formal não mudaria threading nem reduziria código.
- **Event Sourcing / CQRS** — nenhum caso de necessidade de replay/audit encontrado.
- **Generic Scheduler / Job System** — `TimeSyncSystem` é o único candidato e tem 36 linhas; não vale a ferramenta.
- **ReplicationManager/DirtyTrackerFramework** — só 2 casos reais (Movement, Equipment); abaixo do limiar para abstrair.
- **VisibilitySystem** — já formalmente deferred em `ARCHITECTURE.md`/ADR §36; `ChunkStreamSystem` explicitamente não é isso e não deveria virar isso sem uma feature concreta de culling.
- **ScheduledBlockUpdateQueue genérico** — validado externamente como a forma certa *quando* houver um segundo caso de física além de gravidade (fluidos/redstone). Não generalizar `GravityPendingStore` agora — não há um segundo consumidor.

---

## Recommended Target Architecture

**Não há mudança de modelo de execução recomendada.** O `GameLoop` + `IGameSystem` continua sendo a unidade de execução correta para tudo que está nele hoje. As mudanças recomendadas são:

```
20 TPS, ordem revisada:

TimeSyncSystem
    ↓
MovementSystem
    ↓
BlockDigSystem        ── novo: extraído de BlockSystem (crack/predict/continue/abort)
    ↓
BlockEditSystem       ── novo: extraído de BlockSystem (place/break/chest-cleanup/reach)
    ↓
GravitySystem         ── já correto após BlockEditSystem
    ↓
FloorDropSystem       ── novo: extraído de BlockSystem (pickup contínuo, sem intent)
    ↓
InventorySystem
    ↓
EquipmentSystem       ── MOVIDO para depois de Block/Inventory (elimina defasagem de 1 tick)
    ↓
ChatSystem
    ↓
GameModeSystem
    ↓
ChunkStreamSystem     ── mecanismo de StreamAsync corrigido para não tocar Player fora de fila
```

Cada fluxo pedido no brief, no modelo recomendado (idêntico ao atual, exceto onde anotado):

```
Movement:      AuthInput → slot pendente → MovementSystem (decide+replica)      [sem mudança]
Chat:          TextPacket → rate-limit no handler → fila → ChatSystem (broadcast) [sem mudança]
GameMode:      /comando → slot pendente → GameModeSystem (decide+persiste+replica) [sem mudança]
Equipment:     write direto (ADR §80) → EquipmentSystem (diff+replica)          [reordenado, depois de Block/Inventory]
Block place:   UseItem → fila BlockEdit → BlockEditSystem (reach+world+replica) [extraído de BlockSystem]
Block break:   AuthInput → fila Dig → BlockDigSystem (timing) → fila BlockEdit → BlockEditSystem [extraído]
Gravity:       BlockEditSystem.NotifyGravityAfterEdit → GravityPending → GravitySystem [sem mudança]
Chunk stream:  posição → diff vs known → StreamAsync (I/O) → *fila de conclusão* → próximo tick emite [mecanismo corrigido]
```

Nenhum destes introduz Commands/Events como tipos de primeira classe — as filas pendentes por-Player já são, na prática, o mecanismo de "command"; não precisam de um nome novo.

---

## Risks

- **Correção do ZAR-002 (ChunkStreamSystem async)** é a mudança de maior risco tecnico do plano — toca um mecanismo de I/O assíncrono já em produção e testado por smoke. Precisa de characterization tests antes (Phase 0 do plano de migração).
- **Extração de `BlockSystem`** é mecanicamente simples (Extract Class) mas toca o arquivo mais grande e mais testado do gameplay (40+ referências de teste ao overload de 1 argumento) — precisa manter a ordem de fases dentro do tick idêntica.
- **Reordenar `EquipmentSystem`** é baixo risco, mas precisa de teste de regressão explícito (hoje não existe teste de ordem de systems — ver ZAR-005).
- **Gap de permissão em `/gamemode`** e **falta de reach check em chest open** (ZAR-003) são achados de segurança/produto que saem do escopo desta auditoria de runtime, mas devem ser registrados como débito a corrigir — risco de exploit em produção enquanto não corrigidos.

---

## Conclusions

O modelo Systems + tick único está certo hoje e continua saudável para mobs/AI/combate/redstone/mais entidades, **desde que**:

1. O bug de concorrência do dig-lock (ZAR-001) seja corrigido antes de qualquer feature nova que dependa de break timing (combate corpo-a-corpo provavelmente vai reusar esse timing).
2. As duas fugas assíncronas do single-threaded model (ZAR-002) sejam corrigidas para não normalizar o padrão "continuation toca Player direto" — isso escalaria mal com mobs/AI, que vão gerar muito mais trabalho assíncrono (pathfinding, etc.) se esse padrão for copiado.
3. `BlockSystem` seja decomposto por triggers semânticos (não por tamanho) antes de redstone/fluids chegarem e competirem pelo mesmo arquivo.
4. Testes de arquitetura existam antes de crescer o número de systems — hoje as regras 3/4/6 são só convenção.

Nenhuma dessas quatro exige abandonar o modelo atual. A resposta à pergunta central do brief — "qual é a unidade de execução correta para cada responsabilidade" — é: **quase tudo já está na unidade certa (tick), e o que parecia estar errado (Chat/GameMode/Equipment como System) na verdade não é um problema real, é uma leitura ingênua de "roda todo tick" como sinônimo de "desperdício".** Os problemas reais são de mecanismo (locks faltando, async sem fila de retorno) e de cohesion de arquivo (BlockSystem), não de modelo de execução.

---

## Findings — Classificados

| ID | Severity | Confidence | Area | Summary |
|---|---|---|---|---|
| ZAR-001 | **P0** | High | Concurrency | Campos de dig/break-target em `Player` mutados sem lock por thread de rede e thread de tick simultaneamente. |
| ZAR-002 | **P1** | High | Concurrency / Threading model | `ChunkStreamSystem.StreamAsync` e `PreSpawnSessionHandler.CompleteSpawnAsync` tocam `Player` fora do tick, sem fila de retorno. |
| ZAR-003 | **P1** | High | Correctness / Security | Abertura de baú não valida reach/distância, ao contrário de place/break. |
| ZAR-004 | **P2** | High | Architecture tests | Zero testes de boundary/arquitetura (regras 3/4/6 só por convenção). |
| ZAR-005 | **P2** | Medium | Reliability | `GameLoop` isola exceção por system sem circuit breaker; system que falha todo tick nunca é desabilitado, só loga para sempre. |
| ZAR-006 | **P2** | High | Performance | `FloorDropStore.Snapshot()` aloca um iterator todo tick incondicionalmente via `BlockSystem.PickupFloorDrops`. |
| ZAR-007 | **P2** | High | Structural debt | `BlockSystem` (627 linhas) mistura 3 disparadores semanticamente distintos (dig, edit, floor-drop pickup) já sequenciais no código mas não extraídos. |
| ZAR-008 | **P3** | High | DX / dead code | Overload `Tick(GameClock)` de 1 argumento morto em produção em 8/9 systems, só usado por testes; cada system carrega um `_onlineScratch` redundante. |
| ZAR-009 | **P3** | Medium | Ordering | `EquipmentSystem` roda antes de `BlockSystem`/`InventorySystem`, causando defasagem de 1 tick na replicação de equipamento após consumo de item. |
| ZAR-010 | **P3** | Medium | Consistency | `ChatSystem` não filtra `IsInGame` no loop externo (só no fan-out interno) — inconsistente com os outros systems, sem impacto de corretude observado. |
| ZAR-011 | **P3** | Medium | Scaling risk | `GravitySystem.AdvanceActiveFalls` escaneia toda `FallingBlocks.Active` (até 512) sem cap de trabalho por tick, ao contrário de `BeginNewFalls`. Não é bug hoje; risco se o volume de blocos em queda simultânea crescer. |
| ZAR-012 | **P3** | Medium | Consistency | Exceções não-documentadas à regra 6: `IsSpawning`/`IsInGame` escritos direto por handlers de sessão; `RequestAbilityPacket` tratado inteiramente fora do tick sem fila. |
| ZAR-013 | **P3** | Low | Verification needed | `PersistInventory`/`PersistPlayerData` chamados sincronamente de dentro de `BlockSystem`/`GameModeSystem` — não confirmado se bloqueiam o tick; recomenda-se confirmar antes do Phase 1 do plano. |

Cada finding detalhado (Evidence / Why it matters / Recommended direction) está desenvolvido inline nas seções correspondentes acima; a tabela serve como índice de rastreamento.

---

# Addendum — Flexibilidade de Runtime, Execution Model e ADRs

Este addendum estende a auditoria acima com um objetivo explícito: **evitar que o Zenith troque uma rigidez ("tudo é `IGameSystem`") por outra rigidez equivalente ("tudo que não precisa de tick vira Command/Event/Service")**. A conclusão da auditoria original — manter os 9 systems, corrigir mecanismo em vez de modelo — é reexaminada aqui item a item contra esse risco, não simplesmente reafirmada.

O produto normativo deste addendum é [ADR §97](../adr/0097-runtime-execution-model.md) (também registrado em `docs/decisions.md`), mais atualizações em `ARCHITECTURE.md` e `AGENTS.md` (já aplicadas — ver diffs desses arquivos). O restante desta seção é o raciocínio que sustenta o ADR.

## 1. `System` como ferramenta, não boundary — auditoria de estado temporário

Para cada campo de pending-state/lock/queue identificado na auditoria original, a pergunta central: **esse estado sobrevive entre o recebimento do input e sua execução por uma razão comportamental real, ou só para atravessar a boundary handler→gameplay?**

| Estado pendente | Razão comportamental real encontrada | Razão fraca? | Verdict |
|---|---|---|---|
| `_movementInput` (slot) | Tick determinism (posição deve mudar de forma síncrona com outras leituras do mesmo tick, ex. `IsSneaking` para sneak-place) + coalescing (múltiplos AuthInput no mesmo tick devem colapsar no último) | Não | **Manter** |
| `_digIntents` + `_provisionalDig` | Tick determinism para o cálculo anti-speedhack (`elapsed ticks` precisa de um timestamp de tick), rate limiting (cap 4). **Achado positivo:** `_provisionalDig` já é um exemplo real, no próprio código, de autorização computada *sincronamente na thread de rede* (mesmo pacote, sem esperar tick) — só a mutação final de mundo espera o tick. Prova que o time já aplica "immediate quando seguro, deferred quando não" na prática. | Não | **Manter** — e citar como precedente positivo |
| `_blockEdits` (fila cap 8) | Ordering com `IsSneaking` (Movement), single-writer sobre `World` autoritativo, rate limiting | Não | **Manter** |
| `_windowIntents` (fila cap 4) | Single-writer sobre `World.Chests` (opener slots) — dois jogadores abrindo o mesmo baú duplo ao mesmo tempo precisam resolver deterministicamente | Não | **Manter** — mas ver ZAR-003 (reach check ausente é ortogonal a esta decisão) |
| `_inventoryStacks` (ISR) | Transaction semantics (snapshot/rollback all-or-nothing) | Não | **Manter** |
| `_pendingChat` (fila cap 8) | Fraca-a-moderada: rate limiting já é resolvido no handler (token bucket); a única razão restante é single-writer sobre o fan-out a N peers (evita N threads de rede escrevendo em M sessions simultaneamente com ordem não-determinística entre peers) | **Parcialmente** — ver discussão abaixo | **Manter, mas é o caso mais fraco da lista** |
| `_pendingGameMode` (slot) | Tick determinism forte: `GameMode` é lido por `BlockSystem`/`InventorySystem` (regras Creative/Survival) — mudar fora do tick criaria uma janela onde regras de bloco/inventário poderiam ler um `GameMode` inconsistente com o resto do tick; também dispara `PlayerVisibility.RefreshPeerView` (afeta *outros* jogadores) e persistência | Não | **Manter** |
| `_pendingRespawn` (flag) | Precisa aplicar no mesmo `MovementSystem.Tick` que já é dono de posição | Não | **Manter** |
| `SelectedHotbarSlot` (sem lock, write direto) | **Já é o caso de "chamada direta" correto** — escalar único, idempotente, auto-corretivo. Nenhuma pending-state aqui. | — | **Já correto — usar como template** |
| `IsSpawning`/`IsInGame` (sem lock, write direto pelos handlers de sessão) | Já são chamadas diretas (não passam por fila) — no espírito certo do addendum. O único gap é não estarem documentadas como exceção deliberada (ao contrário de `SelectedHotbarSlot`, ADR §80) | — | **Reclassificar**: não é uma violação a corrigir com fila (como o audit original implicava em ZAR-012); é o padrão certo, só falta o comentário/ADR explicando por quê |
| `RequestAbilityPacket` (fly ability, sem fila, same-session) | Já é chamada direta, same-session, sem efeito em outros jogadores — comportamento idempotente | — | **Reclassificar**: aceitável como está; só precisa de um comentário curto, não de fila |

**Sobre `_pendingChat` especificamente** (o caso mais próximo de "friction arquitetural" encontrado): em teoria, `ChatSystem` poderia deixar de existir e o handler poderia chamar `runtime.Broadcast(player, message)` diretamente, já que cada `Protocol.Send` para um peer é protegido pelo próprio lock de sessão do RakNet (confirmado na auditoria — "Protocol.Send sob o lock RakNet existente"). A razão para **não fazer essa mudança agora**, seguindo a instrução de "não remover sincronização sem provar corretude": a fila+tick garante que a *ordem de chegada ao broadcaster* é uma só, visível identicamente para todos os peers; uma chamada direta de N threads de handler concorrentes não teria essa garantia (dois jogadores mandando chat no mesmo instante poderiam ser vistos em ordens diferentes por peers diferentes). Isso é uma garantia comportamental real, só mais fraca que as outras da tabela — fica registrada como candidata a revisitar se o padrão de mensagens em massa (ex. um futuro sistema de anúncios) tornar a fila um bottleneck real, não hoje.

## 2. Locks — concorrência inerente vs. concorrência criada pelo desenho

| Lock | Quem escreve | Quem lê | Por que podem overlap | Inerente ou criado pelo desenho? |
|---|---|---|---|---|
| `_movementInputLock` | Rede (`SubmitMovementInput`) | Tick (`MovementSystem`) | Cliente manda input em cadência própria, independente do tick | **Inerente** — chegada de rede é assíncrona por natureza |
| `_digLock` | Rede (`SubmitDigStart`/`Abort`) **e** Tick (`ApplyDig`) | Ambos | Autorização same-packet precisa ser síncrona; mutação de mundo precisa esperar tick | **Inerente**, mas **mal aplicado** — ver ZAR-001: os campos `HasBreakTarget`/`BreakTargetX/Y/Z`/etc. deveriam estar sob este mesmo lock e não estão. O lock certo existe; a cobertura está incompleta. |
| `_blockEditLock`, `_windowLock`, `_inventoryStackLock`, `_chatLock`, `_gameModeLock`, `_respawnLock` | Rede | Tick | Mesma razão do movement — chegada assíncrona de rede vs. consumo determinístico no tick | **Inerente** |
| `PlayerChunkTracker._gate` | Tick (`ChunkStreamSystem`) **e** continuations assíncronas de I/O (`StreamAsync`, `PreSpawnSessionHandler`) | Ambos | I/O de chunk não pode bloquear o tick — a conclusão chega em thread arbitrária do pool | **Inerente** (não dá pra evitar I/O assíncrono aqui), mas a *superfície* do problema é maior do que precisa: campos de `Player` fora deste lock (`PositionX/Y/Z`, `IsInGame`, `IsSpawning`) são tocados pela mesma continuation sem proteção nenhuma — isso não é "lock desnecessário", é "faltou extender a mesma proteção para os campos vizinhos" (ZAR-002). |
| `PlayerManager._players` (`ConcurrentDictionary`) | Rede (connect/disconnect) | Tick (`FillOnline`) | Conexão/desconexão é assíncrona por natureza | **Inerente** |
| `LevelDbChunkStorage._gate` + `_overlayWorker` | Thread dedicada de I/O | Callers assíncronos | I/O de disco real | **Inerente** |

**Conclusão desta seção:** nenhum lock encontrado na auditoria é "criado pela arquitetura" no sentido de existir só porque a decisão arquitetural exige — todos protegem uma concorrência real (rede assíncrona vs. tick, ou I/O assíncrono vs. tick). Os dois problemas reais (ZAR-001, ZAR-002) não são "locks desnecessários que a arquitetura força" — são **locks necessários com cobertura incompleta**. Isso muda a direção da correção: a resposta não é "remover a arquitetura de fila/lock", é "terminar de aplicá-la corretamente" (Phase 3/4 do plano, já assim desenhadas, mantidas sem alteração de direção — só a preferência de abordagem dentro da Phase 3 muda, ver seção "Reavaliação" abaixo).

## 3. Decision matrix — modelos conceituais de execução

Nenhuma linha abaixo implica que o modelo precisa existir como tipo de primeira classe no código — são modelos conceituais para avaliar por feature, conforme pedido.

| Modelo | Pros | Cons | Estado requerido | Sync requerido | Ordering | Latência | Complexidade | Testabilidade | Extensibilidade | Failure modes |
|---|---|---|---|---|---|---|---|---|---|---|
| **Tick System** | Determinístico contra outros systems; simulação contínua natural; já testado no Zenith | Late-bound (espera o próximo tick); scan O(players) mesmo quando nada muda (mitigado por early-continue) | Snapshot compartilhado (`online`), scratch lists reusáveis | Nenhuma extra — já é a "home thread" | Forte (ordem de registro) | +0-50ms (até 1 tick) | Baixa-média por system | Alta (já provado: `Tick()` chamável direto em teste) | Alta (novo system = novo arquivo) | Exceção isolada por `try/catch` no `GameLoop` (ZAR-005: sem circuit breaker) |
| **Immediate Runtime Operation** | Latência zero; sem estado intermediário; simples | Sem ordering garantido contra o tick corrente; exige que a operação seja segura fora do tick (idempotente/thread-safe por si só) | Nenhum | Só se o estado tocado for compartilhado com o tick | Nenhuma implícita — precisa ser irrelevante ou auto-corretiva | Zero | Muito baixa | Alta (chamada direta testável sem harness) | Baixa (cada caso é ad-hoc) | Race silenciosa se aplicado a estado que na verdade precisa de ordering (é o erro a evitar) |
| **Deferred Runtime Operation** (ex.: enqueue explícito para "aplicar no próximo tick", sem ser uma fila polimórfica de intents) | Preserva single-writer sem exigir um `IGameSystem` dedicado | Ainda precisa de *algum* consumidor no tick — só evita criar uma classe nova | Um slot/fila pequena | Sim, mas escopo mínimo | Garantida pelo consumidor escolhido | +0-50ms | Baixa | Média | Média | Se o consumidor for removido/renomeado, o produtor fica silenciosamente sem efeito |
| **Intent Processing** (o padrão já dominante no Zenith) | Separa validação-de-recepção de decisão-autoritativa; testado, documentado, consistente | Requer lock+fila por tipo de intent — overhead de boilerplate por feature nova | Fila/slot com cap, lock dedicado | Sim | Forte (consumida na ordem do tick que a processa) | +0-50ms | Média | Alta | Alta (mesmo molde para novas intents) | Fila cheia = drop silencioso (documentado, mas sem nack ao cliente em alguns casos — ex. block edit) |
| **Command** | Nomeia explicitamente "isto é uma decisão já internalizada" | No Zenith de hoje, seria estruturalmente idêntico a uma Intent com um nome diferente — nenhum ganho medido encontrado | Igual a Intent | Igual a Intent | Igual a Intent | Igual a Intent | Média-alta se vier com Dispatcher/Handler registry | Alta | Alta, mas o custo de setup (dispatcher, registry) só paga se houver muitos comandos heterogêneos | Mesmo perfil de Intent + risco de indireção adicional sem ganho |
| **Domain Event** | Desacopla produtor de N consumidores; já existe (`EventBus`, ADR §21/§78) | Só vale a pena com ≥2 consumidores reais; hoje só `PlayerLoginEvent`/`PlayerQuitEvent` têm consumidor (`PlayerPresenceAnnouncer`) | Nenhum estado pendente — é fire-and-observe | Depende de onde é publicado/assinado (hoje só composição-root subscribe, só rede-thread publish — sem race atual, mas sem guarda também) | Nenhuma garantida (ordem de listeners é ordem de subscribe) | Zero (síncrono, mesma call stack do publish) | Baixa | Alta | Alta (útil para plugins/futuros consumidores) | Exceção por listener isolada (`EventBus.Publish`), mas `Dictionary` não é thread-safe — risco latente se algo um dia subscrever fora do boot |
| **Dirty-State Processing** | Já usado 2x (Movement pose, Equipment) com sucesso; simples, sem alocação | Só compensa com poucos casos — um framework genérico não se justifica ainda (seção "Replication Model" do audit original) | Campos `LastReplicated*` por entidade | Nenhuma extra (mesma thread do tick) | Implícita pela ordem do tick que faz o diff | Até 1 tick de atraso se o produtor rodar depois do consumidor (achado real: ZAR-009) | Baixa | Alta | Baixa-média (cada novo caso é código explícito, de propósito) | Ordem de registro errada = atraso silencioso de 1 tick (não um bug de corretude, mas real) |
| **Scheduled Work** | Único modelo capaz de "execução em tick futuro específico" — já implementado (`GravityPendingStore`: dedup+cap+cascata deferred) | Não serve para nada que não seja "algo deve acontecer depois" | Fila com dedup (HashSet+Queue) | Nenhuma extra (só tick-thread) | Cap explícito por tick evita bursts | Variável, controlada pelo cap | Média | Alta (testado com `Settle()` helper) | Alta — validado externamente como forma correta (PM/Dragonfly/Cuberite convergem) | Fila cresce sem bound se o cap for maior que a taxa de produção (mitigado por SoftCap) |
| **Async Completion** | Necessário quando I/O real está envolvido (chunk load) | Se a continuation tocar estado autoritativo direto, quebra single-writer (ZAR-002 — problema real, não hipotético) | Epoch/token para invalidar resultado obsoleto | Sim — precisa devolver resultado por um boundary protegido, não mutar direto | Nenhuma garantida sem o boundary de retorno | Latência de I/O + até 1 tick para aplicar | Média-alta | Média (precisa simular I/O nos testes) | Média | Continuation resolve depois do jogador já ter saído/mudado de estado — precisa revalidar (o Zenith já faz isso parcialmente com `IsStreamCurrent`, mas não para os campos fora do lock) |
| **Protocol-only Reaction** (ex.: handler responde direto ao cliente, sem tocar estado autoritativo — ex. reject/toast) | Zero overhead, não é gameplay, não precisa de nenhuma garantia de ordering | Só válido quando de fato não muta nada autoritativo | Nenhum | Nenhuma | N/A | Zero | Muito baixa | Alta | Alta | Nenhum risco novo — já é o padrão para toasts/rejects hoje |

## 4. Reavaliação dos findings anteriores à luz do behavior-first

Aplicando a pergunta "essa nova abstração resolve um problema real ou só substitui `System` por outra forma padronizada?" a cada recomendação anterior:

- **ChatSystem, GameModeSystem — mantidos como System.** Já era a conclusão da auditoria original, e ela se sustenta aqui com razão mais explícita: ambos mutam ou expõem estado que outros jogadores/systems leem no mesmo tick (peer visibility, GameMode lido por regras de bloco/inventário) — não são apenas "porque os Systems rodam no tick". A alternativa `Handler → runtime.TryChangeGameMode(...)` direta foi considerada e **rejeitada** para GameMode especificamente porque a mudança precisa ficar ordenada com `BlockSystem`/`InventorySystem` no mesmo tick (regras Creative/Survival não podem ler um `GameMode` "trocado pela metade"); para Chat, a chamada direta foi considerada e mantida como "candidata fraca, não urgente" (ver tabela da seção 1).
- **EquipmentSystem — mantido como System, reordenado (ZAR-009 já cobria isso).** Confirmado: não há caso de uso real hoje para um `EquipmentChanged` event (nenhum outro consumidor além da própria replicação) — introduzir Event aqui seria trocar um mecanismo simples e correto por um mais indireto sem ganho. Sem mudança de recomendação.
- **ChunkStreamSystem — mantido tick-polled, mecanismo de I/O corrigido (ZAR-002), não o modelo.** Confirmado pela comparação externa (nenhuma implementação madura pesquisada faz streaming puramente event-driven-on-movement). Sem mudança de recomendação.
- **ZAR-012 (exceções não-documentadas à regra 6) — revisado.** A auditoria original tratava `IsSpawning`/`IsInGame`/`RequestAbilityPacket` como "violações de baixo risco a corrigir". Sob esta lente, são **o padrão certo** (chamada direta, sem fila) — só falta documentá-las explicitamente como exceção deliberada, no molde de `SelectedHotbarSlot`/ADR §80. Rebaixado de "corrigir com fila" para "documentar com comentário" — ver ZAR-014 abaixo (substitui a recomendação anterior de ZAR-012).
- **ZAR-001 (race de dig/break) — direção de correção revisada.** A auditoria original propunha Opção A (estender o lock) ou Opção B (mover a decisão para o tick via flag na intent) sem preferência clara. Sob esta lente — "prefira o modelo mais simples que preserve autoridade sem lock extra onde possível" — **Opção B passa a ser a preferida**: em vez de adicionar mais superfície de lock (Opção A), remover a necessidade de mutação cross-thread desses campos inteiramente, descrevendo o cancelamento como parte do payload do `BlockEditIntent` consumido só pelo tick. Isso é coerente com "não crie mais lock só para cobrir a arquitetura atual — primeiro veja se dá para não precisar dele". O plano de migração (Phase 3) é atualizado para refletir essa preferência.
- **Nenhum finding novo requereu introduzir Command/Event/Scheduler genérico.** Confirma a conclusão central deste addendum: o problema nunca foi "Systems demais", foi "duas lacunas de sincronização" e "um arquivo grande" — nenhuma delas se resolve com uma nova camada de abstração.

## 5. Novo finding

| ID | Severity | Confidence | Area | Summary |
|---|---|---|---|---|
| ZAR-014 | **P3** | High | Consistency / documentation | `IsSpawning`/`IsInGame` (escritos direto por handlers de sessão) e o fluxo de `RequestAbilityPacket` (tratado inteiramente fora do tick) são exemplos corretos de "immediate runtime operation" mas não estão documentados como exceção deliberada à regra 6, ao contrário de `SelectedHotbarSlot` (ADR §80). **Substitui a recomendação anterior de ZAR-012** ("corrigir com fila") por "documentar com um comentário curto explicando por que a chamada direta é segura aqui", seguindo o precedente de ADR §80. |

## 6. Seams — testabilidade e customização (sem transformar tudo em interface)

Por comportamento já observado na auditoria, os seams que **já existem e se provaram úteis** (não hipotéticos):
- `IChunkStorage` — troca InMemory↔LevelDB, já usado para testes (`WorldTests`) e para o backend real.
- `ILogger` — testável, já usado para capturar mensagens em testes.
- O padrão `TryConsumeX(out ...)` em `Player` — funciona como seam natural para testes de system em isolamento (`new MovementSystem(...)`, `system.Tick(...)`) sem precisar de rede real — é por isso que 16 arquivos de teste conseguem exercitar systems diretamente.

Seams que **não têm caso de uso concreto hoje** e não devem ser criados preventivamente: uma interface para `World`/`Blocks`/`Player` (nenhum caso de plugin/mod real existe; ADR de plugin API já é non-goal explícito), uma interface para `GameClock`/tempo (nenhum teste precisou mockar tempo além do `AdvanceBy` já existente), uma interface para randomness (nenhuma feature usa RNG hoje). Se/quando mobs/AI precisarem de comportamento substituível para teste, revisitar — não antecipar.

## 7. Documentação atualizada

Como parte deste addendum, os seguintes arquivos foram atualizados (diffs já aplicados, não apenas propostos):

- [`docs/adr/0097-runtime-execution-model.md`](../adr/0097-runtime-execution-model.md) — ADR completo (Context/Decision/Heuristic/Invariantes vs. Mecanismos/Consequences).
- [`docs/decisions.md`](../decisions.md) §97 — entrada correspondente no log canônico.
- [`ARCHITECTURE.md`](../../ARCHITECTURE.md) — nova seção "Escolha de execution model (ADR §97)" com a lista estrito/flexível e as 4 frases-guia pedidas.
- [`AGENTS.md`](../../AGENTS.md) — nova seção "Before implementing a new gameplay feature (ADR §97)" com a checklist de 10 perguntas.
