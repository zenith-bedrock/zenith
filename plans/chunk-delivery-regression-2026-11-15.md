# Post-impl audit

**Scope:** publicação desatualizada do Zenith, identificação de build no boot e
probe C# de entrega de chunks pelo core instalado do Cinnabar. Árvore anterior
preservada; sem reescrever a pipeline já corrigida pelo ADR 151.
**Verdict:** Pass with nits.

## Blockers

- Nenhum no escopo da publicação/probe. A instância antiga não foi reiniciada;
  seu executável e configuração originais permanecem intactos.

## Nits (non-blocking)

- Core-only não comprova decode de terreno, mesh ou rendering no cliente completo.
- O HTTP NetherNet do core instalado exige sessão Microsoft; a tentativa offline
  foi recusada antes de alcançar o servidor. Não foi usada a conta do usuário.
- O probe requer mundo flat isolado, origem zero, raio máximo quatro e Creative;
  AuthInput sintético testa streaming, sem simulação de caminhada.

## Findings and action

- Processo observado: PID 34464, executável em Builds/publish/zenith/release.
- DLL dessa pasta: 13/11/2026 16:06, SHA-256
  `027DEBC215C775862818E3AAFACF0AC6E096A1FC0861952E31C99863A1CDDADC`.
- A publicação antiga ainda envia parte dos LevelChunks no canal RakNet 1 e
  não trata RequestChunkRadius (decimal 69) em SpawnResponse. O fonte atual já
  continha a correção no canal zero e a negociação em todas as fases.
- Reprodução real: publicação antiga recebeu somente 40 das 81 colunas e
  registrou o mesmo warning de 69. Faltaram 41 coordenadas, não apenas uma
  diferença em contagem agregada. Entrada sem replay sintético de chunks.
- Publicação nova: Builds/publish/zenith/chunk-fix, SHA-256 da DLL
  `DD0DB9B6ABDDB4353664892EAF7B7DF2BEEE946B2F470846E5BEC1851F279A4D`,
  MVID `a4e92b30-8d36-4482-868b-57b4d1cde7d2`. Configuração copiada do build
  Release atual, com o caminho persistente Builds/data; sem criar/apagar o mundo
  do usuário. O teste usa ZENITH_DATA e world.path em uma pasta separada.
- Log de boot agora mostra MVID e pasta carregada. A publicação antiga permanece
  disponível para reprodução; não confundir seu processo com o novo executável.

## Layer map

| Path | Role | OK? |
|------|------|-----|
| tools/cinnabar-terrain-probe | Cliente de diagnóstico standalone BCL, sem referências ao servidor | Sim |
| Server/ZenithServer | Identificação da assembly no boot | Sim |
| zenith.sln | Inclui build do probe no conjunto de projetos | Sim |
| docs/cinnabar-interoperability | Evidências e limites de aceitação | Sim |

## Freeze / anti-pattern scan

- [x] No new frozen abstraction without ADR: nenhuma abstração de domínio criada.
- [x] No Network/ revival.
- [x] Handler/thread rules OK: sem mudança nos donos de estado/pacotes.
- [x] Intent queue policy OK: filas e budgets existentes preservados.
- [x] Nenhum código/fixture/asset do Cinnabar importado. Apenas inspeção das APIs
  e comportamento; o probe usa código próprio e o core já instalado.
- [x] Processos iniciados para os testes foram encerrados por PID; instâncias do
  usuário não foram paradas. Dados do teste vivem apenas em Builds/.

## DX / tests

- Probe compila em Release sem warnings; comandos e requisitos no seu README.
- Nova publicação compila/publica em Release sem erros.
- 45 testes focados de chunk streaming, tracker, raio, spawn e lifecycle passaram.
- A execução final do probe passou em todas as nove etapas: 81/81 no join e seis
  movimentos, 25/25 ao reduzir e 81/81 ao ampliar o raio. 118 coordenadas únicas,
  56 coordenadas reenviadas, quatro respostas ao pacote 69, aproximadamente 3,8 s.
- Não houve warning 69 nem LevelChunk no canal 1 na nova execução.
- Baseline reproduzido em Builds/zenith-test-runtime/terrain-20261115-205929;
  execução final em terrain-20261115-210319. Logs não são fixtures versionadas.
- `git diff --check` sem erros de whitespace; normalização LF/CRLF existente.

## Should not be committed

- Builds/, executáveis publicados, logs e bancos de teste. Sem commit automático.

## Suggested follow-ups

1. Usar a publicação atualizada no próximo teste manual, após encerrar a instância antiga.
2. Cinnabar developer-control/client MCP para testemunhar terrain admission e
   publicação visual, usando build de desenvolvimento com SDK/recursos corretos.
3. Mod pessoal é viável para ler loaded bitset e revisões de chunks via terrain.read-region;
   o MCP existente cobre mais do percurso até o renderer e evita criar um mod só
   para movimentação/observação. Nenhum mod foi criado nesta rodada.
