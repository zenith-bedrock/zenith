# Zenith — Architecture Refactor Plan

Companion do [`docs/audit/ZENITH_ARCHITECTURE_AUDIT.md`](../audit/ZENITH_ARCHITECTURE_AUDIT.md) e do [ADR §97](../adr/0097-runtime-execution-model.md). Este plano **não** propõe reescrever o runtime — o modelo `GameLoop` + `IGameSystem` foi validado na auditoria e permanece. As fases abaixo corrigem os findings ZAR-001 a ZAR-014, na ordem de risco/dependência, mantendo o servidor funcional a cada fase.

**Princípio explícito (ADR §97):** este plano não existe para substituir Systems por Commands/Events — a auditoria (incluindo o addendum de flexibilidade de runtime) não encontrou nenhum caso real que se beneficie dessa troca. O objetivo é: comportamento correto → characterization tests → remover complexidade acidental (locks incompletos, continuations que mutam estado fora do tick, alocação por-tick) → extrair padrões só depois de evidência (nenhuma extração de Command/Event está neste plano; a única extração é `BlockSystem` em 3 classes, todas ainda `IGameSystem`, porque os 3 disparadores já eram sequenciais no código) → reforçar invariantes estáveis (Phase 7). Se, durante a execução, qualquer fase parecer estar "trocando `IGameSystem` por outra abstração padronizada" sem uma razão comportamental concreta documentada, parar e revisitar o ADR §97 antes de prosseguir.

Convenção de saída: cada fase termina com `dotnet test zenith.sln` verde e, quando tocar um fluxo coberto pelo smoke bot externo, uma passada manual do gate relevante em `docs/alpha-gate.md` antes de prosseguir.

---

## Phase 0 — Characterization tests

**Goal:** ter uma rede de segurança antes de qualquer mudança de mecanismo, especialmente para os dois pontos de maior risco (ZAR-001, ZAR-002).

**Files/components affected:** `src/zenith.Tests/` (novos arquivos apenas — nenhum código de produção muda).

**Behavior preserved:** 100% — fase é só de testes.

**Changes:**
- Teste que reproduz a race do ZAR-001: dispara `BlockSystem.ApplyDig`/`ApplyEdit` concorrentemente com `Player.ClearBreakTarget()` chamado como o handler faz hoje (fora de qualquer lock), usando `Task.WhenAll` + repetição para tornar a race observável (ex.: `HasBreakTarget` inconsistente com `BreakRequiredTicks`). Este teste **deve falhar hoje** — serve para provar o bug antes de corrigi-lo (Phase 3) e depois para provar a correção.
- Teste de ordem de registro: constrói um `GameLoop` real (não systems isolados) com a ordem atual de `ZenithServer`, e valida uma dependência de ordem já conhecida (`IsSneaking` aplicado por `MovementSystem` antes de `BlockSystem` resolver sneak-place) — fecha a lacuna "nenhum teste de ordem" (ZAR-004).
- Teste de golden-path para `ChunkStreamSystem.StreamAsync`: confirma que hoje a continuation resolve mesmo se `player.IsInGame` mudar entre o início do `await` e a resolução — para não regredir esse comportamento ao trocar o mecanismo em Phase 4.
- Teste de referência para `FloorDropStore.Snapshot()` sendo chamado incondicionalmente por tick (conta invocações) — vai virar teste de regressão para ZAR-006.

**Tests required:** os próprios (são o entregável).

**Benchmarks required:** nenhum.

**Risks:** baixo — só leitura/observação.

**Exit criteria:** os 4 testes acima existem e passam (exceto o de race, que deve **falhar** deliberadamente até Phase 3 — documentar isso no próprio teste com `// TODO(ZAR-001): remove [Skip]/invert assertion after Phase 3`).

---

## Phase 1 — Runtime/API cleanup (ZAR-008)

**Goal:** remover o overload de teste morto em produção sem quebrar os 40+ call sites de teste que dependem dele.

**Files/components affected:** os 8 systems com `Tick(GameClock clock)` (todos exceto `GravitySystem`), e os arquivos de teste que chamam esse overload.

**Behavior preserved:** comportamento de produção idêntico (o overload nunca era chamado por `ZenithServer`/`GameLoop`).

**Changes:**
- Substituir, nos testes, `system.Tick(fx.Clock)` por `system.Tick(fx.Clock, fx.Players.FillOnlineSnapshot())` (ou helper equivalente no fixture de teste) — ou, alternativamente, mover o `FillOnline` para dentro do fixture de teste (`IntentTestFixture`) em vez de dentro de cada system.
- Remover o overload de 1 argumento e o campo `_onlineScratch` de cada um dos 8 systems.
- `GravitySystem` já está no formato final — usar como referência de "como os outros 8 devem ficar".

**Tests required:** suíte existente (`BreakDurationTests`, `ChestPairTests`, `IntentContractTests`, `FallDamageTests`, `MovementDirtyTests`, `TickBatchEgressTests`, `TimeSyncSystemTests`, `PlayerVisibilityJoinTests`) precisa compilar e passar após o ajuste de chamada.

**Benchmarks required:** nenhum (mudança não tem impacto de perf esperado — o overload nunca rodava em produção).

**Risks:** baixo — mecânico, mas toca muitos arquivos de teste; fazer 1 system por commit para isolar falhas.

**Exit criteria:** `dotnet test zenith.sln` verde; grep por `Tick(GameClock clock)` de 1 argumento retorna zero resultados em `Gameplay/Systems/`.

---

## Phase 1b — Documentar exceções diretas à regra 6 (ZAR-014)

**Goal:** fechar o achado do addendum de flexibilidade de runtime (ADR §97): `IsSpawning`/`IsInGame` (escritos direto por handlers de sessão) e o fluxo de `RequestAbilityPacket` já seguem o padrão correto ("immediate runtime operation" — chamada direta, sem fila), só falta documentá-los como exceção deliberada, no molde do comentário que já existe em `Player.SelectedHotbarSlot` (ADR §80).

**Files/components affected:** `Player.cs` (doc comment em `IsSpawning`/`IsInGame`), `InGameSessionHandler.cs`/`SpawnResponseSessionHandler.cs`/`PreSpawnSessionHandler.cs` (comentário no ponto de escrita), handler de `RequestAbilityPacket`.

**Behavior preserved:** 100% — só comentários, nenhuma mudança de código executável.

**Changes:** adicionar doc comment citando o ADR §97 e explicando por que a escrita direta é segura ali (escalar simples/idempotente, sem efeito em ordering de outros systems), seguindo literalmente o texto que já existe para `SelectedHotbarSlot`.

**Tests required:** nenhum novo — é documentação, não comportamento.

**Benchmarks required:** nenhum.

**Risks:** nenhum.

**Exit criteria:** os pontos de escrita direta citados no addendum da auditoria têm comentário explicando a exceção.

---

## Phase 2 — Reach check em chest open (ZAR-003)

**Goal:** fechar o gap de validação antes de tocar em qualquer outra coisa relacionada a blocos — é o fix isolado de menor risco e maior valor de segurança.

**Files/components affected:** `InGameUseItemHandler.cs` (ou `InventorySystem.ApplyWindow`, decisão de onde a checagem deve morar — recomendado no handler, no mesmo estilo do bounds-check de hotbar slot que já existe ali, OU em `ApplyWindow` para ficar simétrico com `BlockSystem.IsWithinReach`).

**Behavior preserved:** abrir baú dentro do reach normal continua idêntico; só rejeita abertura fora de alcance.

**Changes:**
- Reusar `BlockSystem.IsWithinReach` (hoje `internal static`, tornar acessível de `InventorySystem`/handler) ou duplicar a mesma fórmula de distância euclidiana com `MaxBlockReach`.
- No reject, seguir o padrão já existente (`ResyncCellToBreaker`-like): não abrir a janela, opcionalmente logar debug.

**Tests required:** novo teste em `ChestPairTests.cs` (ou novo arquivo `ChestReachTests.cs`): abrir baú a >6 blocos deve ser rejeitado; abrir a <6 blocos continua OK.

**Benchmarks required:** nenhum.

**Risks:** baixo. Único cuidado: confirmar que nenhum fluxo legítimo hoje abre baú de longe (ex.: reabertura automática após reconectar) — revisar `PreSpawnSessionHandler`/`OnEnable` antes de aplicar.

**Exit criteria:** teste novo passa; smoke gate #11 (rearrange/place/break) e o double-chest smoke continuam OK manualmente.

---

## Phase 3 — Fix da race de concorrência (ZAR-001)

**Goal:** eliminar a mutação sem lock dos campos de dig/break-target, fechando o achado P0 da auditoria.

**Files/components affected:** `Player.cs` (`AbortBreak`, `ClearBreakTarget`, `BeginBreak` e os campos `HasBreakTarget`/`BreakTargetX/Y/Z`/`BreakStartedTick`/`BreakRequiredTicks`/`DigHeldStackId`/`LastDigActivityTick`), `InGameAuthInputHandler.cs` (`TrySubmitBreak`).

**Behavior preserved:** comportamento observável pelo cliente idêntico — mesma timing de break, mesmo resultado de predict/abort. Só a sincronização interna muda.

**Changes:** duas opções — **preferência revisada à luz do ADR §97** (evitar adicionar mais superfície de lock quando dá para remover a necessidade de mutação cross-thread inteiramente):
- **Opção A (mínima, fallback):** envolver todos os 7 campos sob o `_digLock` já existente (`AbortBreak`/`BeginBreak`/`ClearBreakTarget` passam a fazer `lock (_digLock) { ... }` em torno de toda a mutação, não só de `_provisionalDig`). `BlockSystem` (thread de tick) também precisa tomar o mesmo lock ao ler/escrever esses campos — que hoje não toma nenhum. Resolve a race, mas adiciona lock a mais 7 campos permanentemente.
- **Opção B (preferida):** mover a chamada de `ClearBreakTarget()`/`CancelPendingDigStart` de dentro de `TrySubmitBreak` (handler, thread de rede) para dentro de `BlockSystem.ApplyEdit` (tick), fazendo o handler apenas enfileirar a intenção de "cancelar dig ao confirmar break" como parte do próprio `BlockEditIntent` (ex.: um flag `CancelsDigOnApply`) em vez de mutar `Player` diretamente. Isso remove a violação da regra 6 **e** remove a necessidade de lock nesses 7 campos por completo — nenhuma thread de rede voltaria a tocá-los depois desta mudança, então eles podem voltar a ser propriedades simples, só que agora com um único escritor real (o tick). É a aplicação direta do heurístico do ADR §97: "o lock protege uma concorrência inerente, ou foi criada por um design que dava para evitar?" — aqui, dava para evitar.
- Risco a validar antes de escolher B: o comentário original ("same-AuthInput Continue possa retarget") sugere que o cancelamento precisava ser síncrono no mesmo AuthInput. Investigar se isso ainda vale quando o cancelamento vira parte do payload consumido no tick — como `BlockEditIntent` já é consumido no mesmo tick em que chega (não há fila de 1-tick-de-atraso adicional aqui), o timing observável pelo cliente não deveria mudar. Se a investigação confirmar isso, ir direto para B; se não, aplicar A como fallback e reavaliar B depois com mais characterization tests.

**Tests required:** o teste de race de Phase 0 deve passar a ser verde (a race não deve mais ser reprodutível); manter/estender `BreakTimingTests.cs`, `BreakDurationTests.cs` para cobrir os cenários de abort/retarget dentro do mesmo AuthInput.

**Benchmarks required:** nenhum — lock já existe, só está sendo estendido para cobrir mais campos; overhead desprezível (contenção rara, 1 write por dig/break por jogador).

**Risks:** médio — é o coração do sistema de dig/break, e qualquer erro de lock pode causar deadlock (evitar lock aninhado entre `_digLock` e qualquer outro lock de `Player` na mesma ordem sempre) ou regressão de timing perceptível pelo cliente.

**Exit criteria:** teste de race de Phase 0 verde; `BreakTimingTests`/`BreakDurationTests` verdes; smoke gates de survival dig (S41, H1-1) revalidados manualmente.

---

## Phase 4 — Corrigir as fugas assíncronas do modelo single-threaded (ZAR-002)

**Goal:** `ChunkStreamSystem.StreamAsync` e `PreSpawnSessionHandler.CompleteSpawnAsync` não devem mais tocar `Player` diretamente de uma continuation fora do tick.

**Files/components affected:** `ChunkStreamSystem.cs`, `PreSpawnSessionHandler.cs`, `PlayerChunkTracker.cs` (possivelmente um novo mailbox), `Player.cs` (novo campo pendente, se necessário).

**Behavior preserved:** streaming de chunk continua assíncrono (I/O não bloqueia o tick); latência de entrega de chunk não deve regredir de forma perceptível.

**Changes:**
- Introduzir um mecanismo de "resultado pendente" simétrico ao já usado para intents (fila/slot com lock) em `PlayerChunkTracker` ou em `Player`: a continuation assíncrona, ao terminar o `await`, **não** chama `ColumnSend.EmitToSession` nem lê `player.IsInGame`/`IsSpawning` diretamente — em vez disso, enfileira o resultado (`column` pronto para um `(chunkX, chunkZ, epoch)`) em uma fila protegida por lock.
- `ChunkStreamSystem.Tick` (ou um novo passo no início do `Tick`) drena essa fila de resultados prontos **na thread de tick**, revalida epoch/`IsInGame`/`IsSpawning` (mesma lógica que hoje está na continuation) e só então chama `ColumnSend.EmitToSession`.
- Mesmo tratamento para `PreSpawnSessionHandler.CompleteSpawnAsync`: em vez de mutar `Player.Chunks`/ler `PositionX/Y/Z` direto na continuation, enfileirar "PreSpawn pronto para aplicar" e ter algo consumindo isso no próximo tick (pode ser o próprio `ChunkStreamSystem`, ou um pequeno consumo dedicado dentro do fluxo de entrada em `InGameSessionHandler.OnEnable`).
- **Não** introduzir um mailbox genérico reutilizável para qualquer async-completion — resolver especificamente estes dois casos, seguindo o mesmo padrão simples que já existe para as pending intents (é o mesmo mecanismo, só invertendo a direção: rede→tick para dado pronto vindo de I/O→tick).

**Tests required:** teste de golden-path de Phase 0 estendido para garantir que o resultado só é aplicado na próxima tick após o I/O completar, nunca antes, e nunca fora da thread de tick (pode-se instrumentar um teste que verifica `Thread.CurrentThread` — ou, mais simples, verificar que a emissão de chunk nunca acontece durante uma chamada de teste que não invocou `Tick`).
- Regressão de `ChunkStreamTests.cs`.

**Benchmarks required:** medir latência de tempo-até-chunk-visível antes/depois (BenchmarkDotNet ou medição manual via smoke bot) — a fila adiciona no mínimo 1 tick (50ms) de latência entre I/O terminar e o chunk ser enviado; confirmar que isso é aceitável (é — hoje a segurança contra emissão fora de tick já não existia, então isso é estritamente uma correção, não uma regressão de UX perceptível).

**Risks:** médio-alto — é o segundo maior fix do plano; toca o caminho de join/streaming que tem o maior número de smoke gates dependentes (#1, #3, #10). Fazer com feature-adjacent characterization tests (Phase 0) já em vigor.

**Exit criteria:** grep confirma que nenhuma continuation assíncrona em `Gameplay/`/`Session/` lê ou escreve campos de `Player` fora de um lock ou fora da thread de tick; smoke gates #1, #3, #10 revalidados manualmente.

---

## Phase 5 — Decomposição de BlockSystem (ZAR-007)

**Goal:** separar os 3 disparadores semanticamente distintos já presentes em `BlockSystem` em 3 classes, sem mudar o modelo de execução (todas continuam `IGameSystem`, todas continuam no tick, na mesma posição relativa).

**Files/components affected:** `BlockSystem.cs` → dividido em `BlockDigSystem.cs`, `BlockEditSystem.cs`, `FloorDropSystem.cs`; `ZenithServer.cs` (registro); todos os testes que hoje fazem `new BlockSystem(...)`.

**Behavior preserved:** comportamento idêntico, ordem de execução idêntica (as 3 fases já rodam nessa ordem dentro do `Tick` atual — a extração só torna os limites explícitos).

**Changes:**
- `BlockDigSystem`: `ApplyDig`, `UpdateDigToolIfHeldChanged`, `AbortIdleDigIfStale` e o consumo de `TryConsumeDig`.
- `BlockEditSystem`: `ApplyEdit` e todo o entorno (reach, creative/survival, chest pairing/cleanup, `NotifyGravityAfterEdit`, drop-on-break via `ShouldDropBrokenBlock`/`DepositFloorDrop`) e o consumo de `TryConsumeBlockEdit`.
- `FloorDropSystem`: `PickupFloorDrops` inteiro, incluindo o snapshot/rollback transacional de inventário.
- Registrar na mesma janela de posição do `BlockSystem` atual: `MovementSystem → BlockDigSystem → BlockEditSystem → GravitySystem → FloorDropSystem → InventorySystem` (nota: mover `FloorDropSystem` para depois de `GravitySystem` é uma escolha segura porque pickup não depende de gravidade; manter antes de `InventorySystem` porque hoje roda antes por transitividade de estar dentro do mesmo `BlockSystem.Tick`, mas não há dependência real encontrada — validar com o teste de ordem de Phase 0 se a posição exata importa).
- Resolver ZAR-006 (alocação do `FloorDropStore.Snapshot()`) como parte desta fase, já que `FloorDropSystem` está sendo tocado: trocar o iterator `yield return` por um método que preenche um `List<T>` reutilizável (mesmo padrão dos outros systems), ou adicionar um early-return se `_world.FloorDrops.Count == 0` antes de chamar `Snapshot()`.

**Tests required:** todos os testes que hoje instanciam `BlockSystem` diretamente precisam ser redistribuídos entre os 3 novos tipos (`BreakDurationTests`, `ChestPairTests`, `LevelSoundEventPacketTests`, `IntentContractTests`, etc.) — é o maior volume de trabalho mecânico do plano inteiro. Fazer 1 sub-sistema por commit.
- Novo teste de regressão para ZAR-006: contar alocações via `GC.CollectionCount` antes/depois, ou simplesmente confirmar que `Snapshot()` não é chamado quando `FloorDrops.Count == 0`.

**Benchmarks required:** benchmark antes/depois do tick completo (BenchmarkDotNet, já usado no projeto) para confirmar que dividir em 3 chamadas de `Tick()` sequenciais não adiciona overhead mensurável vs. 1 chamada monolítica (não deveria — é o mesmo trabalho, só reorganizado).

**Risks:** alto volume de mudança mecânica (maior arquivo do gameplay, mais testes dependentes), mas baixo risco semântico — é Extract Class, não redesenho. O risco real é quebrar um teste por engano ao mover código; mitigar com 1 sub-sistema por commit e suíte completa verde entre commits.

**Exit criteria:** `BlockSystem.cs` não existe mais; os 3 novos arquivos passam `dotnet test zenith.sln`; benchmark de tick completo dentro de ±5% do baseline; smoke gates #6, #8, #11 (place/break/rearrange) revalidados manualmente.

---

## Phase 6 — Reorder de EquipmentSystem (ZAR-009)

**Goal:** eliminar a defasagem de 1 tick entre consumo de item (Block/Inventory) e replicação de equipamento.

**Files/components affected:** `ZenithServer.cs` (só a ordem de `Register`).

**Behavior preserved:** comportamento melhora (replicação mais rápida), nenhuma regressão esperada — `EquipmentSystem` não depende de nada que rode antes dele hoje além de `PlayerManager`.

**Changes:** mover `gameLoop.Register(new EquipmentSystem(players))` para depois de `InventorySystem` (ou `FloorDropSystem`, se Phase 5 já rodou) na composition root. Adicionar o comentário de ordering que falta hoje (seguindo o estilo do comentário existente sobre Movement/IsSneaking).

**Tests required:** teste de ordem de Phase 0 estendido para cobrir este caso: "consumir 1 item via BlockEditSystem no tick N deve refletir em `SendMobEquipment` no mesmo tick N, não N+1".

**Benchmarks required:** nenhum.

**Risks:** muito baixo — mudança de 1 linha, mas só é segura por existir o teste de ordem (por isso vem depois de Phase 0/5, não antes).

**Exit criteria:** teste novo passa; smoke gate #12 (held peer) revalidado manualmente.

---

## Phase 7 — Architecture enforcement (ZAR-004)

**Goal:** transformar as regras 3/4/6 do `ARCHITECTURE.md` em testes automatizados, para que os achados desta auditoria não precisem ser redescobertos manualmente na próxima revisão.

**Files/components affected:** novo projeto ou arquivo de teste, ex. `src/zenith.Tests/Architecture/BoundaryTests.cs`.

**Behavior preserved:** N/A — só testes.

**Changes:**
- Avaliar `NetArchTest.NUnit`/`NetArchTest.xUnit` (ou equivalente compatível com o test runner já usado no projeto) vs. uma verificação reflection-based hand-rolled simples (dado o tamanho do projeto, hand-rolled é provavelmente suficiente e evita uma dependência nova — decidir com base no que o `.csproj` de testes já referencia).
- Codificar como teste, no mínimo:
  - `Zenith.Packets.*` nunca referencia `Zenith.Gameplay`/`Zenith.Player`/`Zenith.World`/`Zenith.Server` (regra 4).
  - `Zenith.Gameplay.*` nunca referencia `DataPacket`/`BinaryStream`/tipos de `Zenith.Packets` (regra 3).
  - Nenhum tipo em `Zenith.Session.Handler` escreve em propriedades de `Zenith.Player.Player` fora da lista de exceções documentadas (`SelectedHotbarSlot` e as outras já registradas em ZAR-012) — este é o mais difícil de automatizar por reflection pura; pode começar como uma checklist manual documentada em vez de teste automatizado, e evoluir depois.
- Documentar as exceções aceitas (ADR §80 e as descobertas em ZAR-012) explicitamente na lista de exclusões do teste, para que uma nova exceção precise ser adicionada deliberadamente (e revisada), não silenciosamente ignorada.

**Tests required:** os próprios testes de arquitetura (são o entregável).

**Benchmarks required:** nenhum.

**Risks:** baixo. Único cuidado: reflection-based tests podem ficar frágeis a mudanças de namespace — manter o teste simples e específico às 2-3 regras que realmente importam, não tentar codificar toda a filosofia do `ARCHITECTURE.md`.

**Exit criteria:** os testes de boundary existem, passam contra o estado atual (pós Phase 1-6), e falham deliberadamente se alguém reintroduzir uma violação (validar isso com um teste "meta" temporário que injeta uma violação de propósito e confirma que o teste de arquitetura pega).

---

## Phase 8 — Benchmark e validação de regressão final

**Goal:** confirmar que a soma de todas as fases não degradou performance nem comportamento, antes de considerar o plano concluído.

**Files/components affected:** `src/zenith.Benchmarks/`.

**Behavior preserved:** validado, não alterado.

**Changes:** nenhuma de produção — rodar a suíte de benchmarks existente (BenchmarkDotNet) antes/depois do conjunto completo de fases e comparar; rodar a checklist completa de `docs/alpha-gate.md` manualmente uma vez de ponta a ponta.

**Tests required:** `dotnet test zenith.sln` completo, verde.

**Benchmarks required:** benchmark de tick completo (todos os 9→11 systems), benchmark de `ChunkStreamSystem` (latência de I/O até emissão), benchmark de `BlockEditSystem`/`FloorDropSystem` isolados.

**Risks:** nenhum novo — é a fase de confirmação.

**Exit criteria:** todos os benchmarks dentro de ±10% do baseline pré-plano (documentado em `docs/dx.md` conforme convenção do projeto); alpha-gate completo sem regressão; findings ZAR-001 a ZAR-011 marcados como resolvidos na auditoria (ZAR-012/ZAR-013 podem ficar como débito documentado e não bloqueante, dado que são P3/confiança baixa-média).

---

## Decision Matrix

| Component | Current model | Recommended model | Keep / Change | Reason |
|---|---|---|---|---|
| MovementSystem | Tick | Tick | **Keep** | Simulação de física/posição — não há decisão sem avançar tempo. |
| BlockSystem | Tick (1 classe, 3 fases) | Tick (3 classes: BlockDigSystem/BlockEditSystem/FloorDropSystem) | **Change (decompose, not remove from tick)** | 3 disparadores semanticamente distintos já sequenciais no código; nenhum deles deixa de precisar do tick. |
| GravitySystem | Tick, fila com dedup+cap+cascata deferred | Tick, mesmo modelo | **Keep** | Validado externamente (PM/Dragonfly/Cuberite convergem no mesmo padrão de fila genérica). |
| InventorySystem | Tick, transacional snapshot/rollback | Tick, mesmo modelo | **Keep** | Precisa de ordenação determinística com outras mutações de estado. |
| ChunkStreamSystem | Tick, I/O assíncrono fire-and-forget tocando Player fora de fila | Tick, mesmo trigger, resultado de I/O devolvido via fila drenada no tick | **Change (mechanism only)** | Streaming tick-polled é correto (validado externamente); o bug é a continuation tocar `Player` fora do tick (ZAR-002). |
| EquipmentSystem | Tick, dirty-diff, registrado antes de Block/Inventory | Tick, mesmo dirty-diff, registrado depois de Block/Inventory | **Change (reorder only)** | Só 2 casos de dirty-diff no código inteiro (Movement, Equipment) — abaixo do limiar para abstrair; o problema real era ordering, não o modelo. |
| GameModeSystem | Tick, consome slot overwrite-latest | Tick, mesmo modelo | **Keep** | Decisão de domínio com side-effects; precisa rodar fora da rede; nenhuma implementação madura usa Command Bus dedicado para isto. |
| ChatSystem | Tick, drena fila FIFO, broadcast | Tick, mesmo modelo | **Keep** | Já é a separação correta ("handler só enfileira, nunca escreve no RakNetSession dos peers"). |
| TimeSyncSystem | Tick, cadência fixa (1x/s) | Tick, mesmo modelo | **Keep** | 36 linhas, trivial demais para justificar um scheduler dedicado. |

---

## Comparativo externo (referência)

Ver seção "Comparison with Other Implementations" na auditoria para a tabela completa com fontes. Resumo de uso neste plano: a validação externa é o motivo pelo qual Gravity e ChunkStreamSystem **não** têm fase de "mudança de modelo" neste plano — só mudança de mecanismo (Phase 4) ou nenhuma mudança.

---

## Ordem de execução recomendada

```
Phase 0 (characterization tests)
   │
   ├──▶ Phase 1  (cleanup overloads)         ──┐
   ├──▶ Phase 1b (documentar exceções §97)    │  podem rodar em paralelo,
   ├──▶ Phase 2  (reach check chest)          │  são independentes entre si
   │                                           │
   ▼                                           │
Phase 3 (fix race ZAR-001, preferir Opção B)  ◀┘
   │
   ▼
Phase 4 (fix async escapes ZAR-002)   ── maior risco técnico, fazer isolado
   │
   ▼
Phase 5 (decompose BlockSystem)       ── maior volume mecânico, fazer isolado
   │
   ▼
Phase 6 (reorder Equipment)           ── depende do teste de ordem de Phase 0
   │
   ▼
Phase 7 (architecture tests)          ── pode rodar em paralelo com Phase 5/6
   │
   ▼
Phase 8 (benchmark + validação final)
```

Phases 1 e 2 podem ser feitas em paralelo com Phase 0 terminando (não dependem de characterization tests específicos). Phases 3 e 4 devem ser sequenciais e isoladas — são os dois maiores riscos do plano e não devem ser misturadas no mesmo commit/PR. Phase 5 é o maior volume de arquivos tocados; fazer depois de 3/4 estarem estáveis para não competir por atenção de review com os fixes de concorrência.
