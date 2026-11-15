# Post-impl audit

**Scope:** reinicialização autorizada, correção do cold load de baús persistidos,
Disconnect RakNet e validação real dos ciclos de mesa/baú pelo core instalado.
Working tree de rodadas anteriores preservado. ADR 162.
**Verdict:** Pass with nits.

## Blockers

- Nenhum identificado após a correção. Processo ativo 78844, executável em
  Builds/publish/zenith/chunk-fix-hydration, RakNet UDP 19132 e NetherNet TCP/ICE 19133.

## Nits (non-blocking)

- O probe dirige pacotes pelo core real; não testa mouse/predição/telas do cliente Rust.
- Sessões de teste colocaram mesas/baús na área de teste, principalmente x=66/82,
  y=-60, z=64..67. Não foram substituídos blocos existentes. Os testes não editaram
  inventários de jogadores do usuário; usaram identidades offline próprias.
- O teste HTTP NetherNet autenticado continua fora da evidência deste probe offline.

## Findings and fix

- O primeiro reinício encontrou corrupção de coleção no pre-spawn do mundo existente.
  EnsureHydratedAsync executava ChestStore.TryLoadFromBlob em workers paralelos;
  ChestStore guarda dicionários e arrays de gameplay que pertencem ao tick.
- Workers agora retêm blobs na entrada já existente da hidratação por chunk. Antes
  de publicar a coluna, o tick aplica seeds uma vez, sem substituir um baú já vivo.
  Não foi criada fila genérica nem colocado lock no store para autorizar mais writers.
- Cancelar um viewer não perde o seed: uma leitura posterior do cache ainda pode
  aplicá-lo. Evicção remove os bytes pendentes; uma nova leitura recarrega do storage.
- Disconnect RakNet emitia 0x03 (ConnectedPong) com um único byte, produzindo no core
  o erro inesperado EOF. Agora emite o ID correto 0x15; ConnectedPong mantém 17 bytes.
- Logs de falha pre-spawn agora incluem stack da exceção.

## Layer map

| Path | Role | OK? |
|------|------|-----|
| World/World | Handoff dos bytes de hidratação e aplicação pelo owner | Sim |
| World/ChestStore | Seed distinto de load explícito; preserva conteúdo vivo | Sim |
| Session/ChunkStreamSystem | Aplica seed no tick antes da publicação inicial e pós-spawn | Sim |
| raknet/Network/Protocol/Disconnect | Codec de transporte, sem gameplay | Sim |
| zenith.Tests + raknet.Tests | Regressões de ownership, persistência e pacote | Sim |
| tools/cinnabar-terrain-probe | Modo --containers com dados e código próprios | Sim |

## Freeze / anti-pattern scan

- [x] No new frozen abstraction without ADR; nenhuma abstração congelada alterada.
- [x] No Network/ revival.
- [x] Handler/thread rules OK; a escrita de baús saiu do worker e voltou ao tick.
- [x] Intent queue policy OK; FIFO existente mantida.
- [x] Sem espera de disco no tick e sem nova fila de resultados sem limite.
- [x] Nenhum dado/código/fixture do Cinnabar copiado; core instalado usado isoladamente.

## DX / tests

- Suite completa Release: **1.862 aprovados**, zero falhas/ignorados, em
  Builds/zenith-test-runtime/hydration-disconnect-verified.log (1552 zenith + 310 leaf).
- Após acrescentar o cenário de bootstrap da mesa e corrigir o nome do helper de
  receita, **36 testes focados passaram**, incluindo arquitetura, mesa, baús,
  hidratação e conservação. Registro live-containers-regression.log.
- Leitura paralela de 25 chunks em LevelDB testa ausência de mutação do store antes
  do owner, aplicação única e preservação de edição viva. Evicção/reload e publicação
  pós-spawn também cobertos. O teste falha no comportamento anterior por contrato,
  sem depender da reprodução probabilística da corrupção do Dictionary.
- O teste de mesa usa a receita registrada minecraft:WorkBench_recipeId. O nome
  antigo minecraft:crafting_table no helper estava obsoleto; o catálogo em runtime
  já tinha a receita correta.
- Core real contra o mundo persistente: nove etapas de streaming passaram, incluindo
  81/81 no join/rota/retorno/expansão e 25/25 na redução de raio.
- Modo --containers: quatro tábuas → mesa na grade pessoal → colocar/descontar →
  abrir → oito tábuas na grade 3×3 → craft de baú → fechar/reabrir mesa vazia →
  colocar/descontar baú → abrir 27 slots → depositar/retirar três terras →
  fechar/reabrir baú vazio. Quantidade original de terra restaurada a 64.
- Execução final passou em ~4,5 s: live-containers-final/result.json. Resultado é
  core-gameplay-roundtrip. Confirmações incluem ItemStackResponse OK com stack IDs
  positivos e snapshots novos de UI/baú na reabertura.
- Fonte/probe compilados sem warnings. Publicações chunk-fix e chunk-fix-hydration
  contêm a mesma DLL corrigida, SHA-256
  A27A1DF9442878411BA61DF621A653B9E0B95E281A1FAAE4A3CCB11F94F36398.
- Configuração mantém Builds/data/worlds/world em LevelDB. O processo ativo mostra
  MVID 7c4fab22-0b6e-4fd1-8843-e371b6c146e9. Sem ERROR ou warning 69 no log desta execução.

## Should not be committed

- Builds/, logs, mundos, identidades e executáveis de teste. Sem commit automático.

## Suggested follow-ups

1. Teste visual no Cinnabar conectado ao processo atualizado. Se a tela continuar
   falhando, correlacionar o input real com logs/estado do renderer; esta rodada não
   declara cobertura das telas pelo probe do core.
