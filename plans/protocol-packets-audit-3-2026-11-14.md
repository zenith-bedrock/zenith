# Terceira auditoria de pacotes — protocolo 2225

## Implementação autorizada — 2026-11-14

Os seis achados foram corrigidos no código local. O registro original abaixo foi preservado
para rastrear as evidências. Esta implementação não inclui a hipótese sobre String=58,
que continua sem confirmação suficiente para alterar o tipo.

| Achado | Correção e regressão |
|---|---|
| PACK-01 | Tag validada antes do cast; contagem limitada a 65535 e aos bytes restantes antes de alocar; EOF obrigatório; negociação inválida fecha sem bootstrap |
| CMD-01 | Operator projetado como `gamedirectors`, mantendo `any` e o formato textual |
| CMD-02 | Enum fixo usa Valid\|Enum\|index; teste lê símbolos literais e metadata completa até EOF |
| ENTITY-01 | Callback afterSpawn existente anuncia Ignited somente ao novo observador quando o fuse já está ativo |
| ATTR-01 | Projeção usa saturação/exaustão atuais; clamp apenas no wire para exaustão; deltas de fome/saturação imediatos e refresh periódico de exaustão não zero a cada segundo |
| ENTITY-02 | SendPlayerActorOf envia saúde atual após AddPlayer; regressões cobrem late join e RefreshPeerView |

Foram acrescentados 24 casos de teste. Um teste antigo de bootstrap exigia a ausência de
UpdateAttributes e foi atualizado para exigir o novo seed após AddPlayer. O teste de saúde
usa uma skin sintética de um pixel para manter o foco na ordem dos pacotes, sem depender de
remontagem de fragmentos RakNet.

**Validação final:** `dotnet test zenith.sln -c Release --no-restore -m:1` terminou com sucesso:
**1753 testes aprovados, zero falhas** (1448 principais, 48 RakNet, 27 NetherNet, 53 binary e
177 nas demais folhas/ferramentas). `git diff --check` passou. Build Release atualizada em
Builds ignorado. Nenhum novo servidor ou cliente gráfico foi iniciado nesta implementação.

### Post-impl audit

**Scope:** codecs/adaptador de comandos, negociação de packs, projeções de saúde/vitais e
seed de creeper, mais testes e notas nas ADRs §34/§91/§100.
**Verdict:** Pass with nits; suíte completa aprovada.

**Blockers:** nenhum identificado na revisão de camadas, ownership ou contratos corrigidos.
**Nits:** aceitação visual de autocomplete, HUD e animação continua dependendo de cliente real;
os testes verificam bytes emitidos e decisões de projeção.

| Camada | Revisão |
|---|---|
| Packets | Somente DTOs, limites e codecs; sem dependência de domínio |
| Session | Valida e encerra negociação inválida; traduz metadata sem decidir gameplay |
| Protocol | Projeta valores atuais e saúde inicial; não altera vitais |
| Gameplay | Mantém autoridade e decide cadência/seeds por observador no tick |

- [x] Sem nova abstração congelada, scheduler, DI ou plugin API; afterSpawn já existia.
- [x] Sem expansão de Network, mudança de persistência ou de filas de intents.
- [x] Testes de folha em zenith.Tests; sem servidor completo para esses casos.
- [x] Saídas permanecem em Builds ignorado; sem assets de cliente ou credenciais adicionados.

## Registro original da auditoria

Data: 2026-11-14. Base: `cb27c69` mais as correções locais das duas rodadas anteriores.
Inspeção somente leitura de produção; este relatório é o único arquivo criado nesta rodada.
Os achados anteriores resolvidos não foram reabertos. Nenhum commit, push, build, servidor ou
teste foi executado nesta inspeção. O baseline anterior de **1729 testes aprovados** não cobre
automaticamente os casos novos abaixo.

Referência oficial: `../mojang-protocol-docs`, tag `v1.26.62`, commit
`f8d1496b5efc95105bd9ae1f2f2f076a43f71d82`, protocolo 2225. O checkout corrente não é o contrato.
Exemplo reproduzível: `git -C ../mojang-protocol-docs show v1.26.62:json/CommandPermissionLevel.json`.
Não foi copiado código nem recurso do Cinnabar.

## Resultado priorizado

| ID | Prioridade | Achado | Tipo | Esforço | Risco da correção | Confiança |
|---|---|---|---|---|---|---|
| PACK-01 | P1 | ResourcePackClientResponse aloca pela contagem recebida e reduz discriminador antes de validar | Validação de entrada | S | Baixo | Alta |
| CMD-01 | P2 | AvailableCommands anuncia permissão `operator`, fora do enum do protocolo | Valor no fio | S | Baixo | Alta |
| CMD-02 | P2 | Parâmetros de enum fixo omitem o bit Valid no tipo anunciado | Valor no fio | S | Baixo | Alta |
| ENTITY-01 | P2 | Novo observador de creeper em fuse não recebe Ignited atual | Projeção inicial | S | Baixo | Alta |
| ATTR-01 | P2 | Saturação e exaustão enviadas como constantes divergem do jogador | Projeção de estado | S/M | Baixo/médio | Alta |
| ENTITY-02 | P2 | AddPlayer não é seguido da saúde atual do peer | Projeção inicial | S | Baixo | Alta |

S: alteração localizada com regressão; M: exige decidir também a cadência de sincronização.
As prioridades não afirmam que houve exploração ou reproduções visuais desses novos casos.

## PACK-01 — Validar a negociação antes de converter ou alocar

Evidência: `src/zenith/Packets/ResourcePackClientResponsePacket.cs:30` converte imediatamente
o discriminador uint32 recebido para byte. Valores fora das quatro alternativas podem virar
0–3 e alcançar o switch válido de `src/zenith/Session/Handler/ResourcePacksSessionHandler.cs:26`.
As linhas 35–36 do decoder usam a contagem recebida diretamente em `new string[count]`, antes
de validar limite ou dados restantes. Um payload pequeno pode solicitar uma alocação muito
maior que seu próprio tamanho; o limite do batch não limita essa amplificação.

O contrato `ResourcePackClientResponsePacketPayload.json` define a união com discriminador
uint32; apenas Downloading carrega a lista. `Resource_Pack_Client_Response_-_Downloading.json`
publica o teto 65535. Os testes atuais de status verificam alternativas válidas e duas IDs,
sem rejeição de contagens inválidas ou discriminadores fora da união.

Correção recomendada: validar a alternativa no tipo completo antes de qualquer cast; validar
contagem não negativa, teto e plausibilidade frente aos bytes restantes antes da alocação.
Manter o formato válido atual e rejeitar o payload completo malformado sem iniciar bootstrap.
Não introduzir um requisito artificial de sequência do handshake sem verificar clientes reais.

Regressões propostas: alternativas válidas preservadas; alternativa fora da união rejeitada
sem emitir StartGame; contagem acima do teto ou incompatível com o restante rejeitada antes de
alocar; lista válida truncada não provoca transição. Usar valores pequenos/um acima do limite,
sem testes que tentem alocações gigantes. Estender `ResourcePackClientResponseStatusTests` e
`ResourcePacksSessionHandlerTests`.

## CMD-01 — Projetar Operator para o token Bedrock correto

Evidência: `src/zenith/Session/BedrockCommandAdapter.cs:29` transforma
`CommandPermission.Operator` em `"operator"`. O encoder
`src/zenith/Packets/AvailableCommandsPacket.cs:24` transmite essa string literalmente.
`CommandPermissionLevel.json` do contrato tem Any, GameDirectors, Admin, Host, Owner e Internal;
não tem Operator. A representação textual deve ser preservada, corrigindo o token.

A captura própria `../bds-reference/out/available_commands.json`, campo
`command_data[].permission_level`, confirma `gamedirectors` em 60 comandos, incluindo
gamemode, gamerule e summon. Os outros valores capturados são admin (7), any (4), host (1)
e owner (10); nenhum operator. No Zenith, gamerule e summon usam Operator quando registrados
(`src/zenith/Gameplay/Commands/CommandRuntime.cs:68` e `:75`).

Impacto: metadata entrega um valor fora do contrato ao cliente. O comportamento exato da UI
na presença desse token ainda requer validação visual; não se afirma perda de permissões
autoritativas no servidor. Correção localizada no adaptador, sem novo sistema de permissões.

Regressão proposta: catálogo mínimo com comandos Any e Operator; verificar any/gamedirectors
no DTO e ler o payload completo emitido, incluindo o comando seguinte e EOF. Os testes atuais
de `BedrockCommandAdapterTests` não verificam permissões e o teste de encode só lê o packet ID.

## CMD-02 — Incluir Valid nos tipos de parâmetros enum

Evidência: `src/zenith/Session/BedrockCommandAdapter.cs:38` projeta EnumCommandArgument como
`Enum | index`, enquanto os tipos int, target e string já recebem `Valid | tipo`.
Os valores locais são Enum=0x200000 e Valid=0x100000. A captura RAW própria
`../bds-reference/out/join/047_available_commands.bin`, no comando com descrição
`commands.aimassist.description`, tem o parâmetro `set` com tipo little-endian
`22 00 30 00`, isto é, `0x00300022` (Valid | Enum | índice 34).
No mesmo registro, `players` tem `08 00 10 00`, confirmando a mesma posição do bit Valid.
O adaptador atual produziria `22 00 20 00` para aquele índice de enum.

Impacto: os parâmetros de enum fixo de gamemode/effect e dos comandos condicionais gamerule/
summon são anunciados sem o marcador de tipo válido. É uma divergência concreta da metadata;
o efeito exato na UI/autocomplete requer teste de cliente. Não alterar os índices, o tamanho
uint32 do tipo nem a autorização do comando para corrigir essa composição de bits.

Regressão proposta: enum com índice conhecido; exigir `Valid | Enum | index` no DTO e bytes
literais correspondentes no payload, preservando optional e os campos seguintes até EOF.
Usar o vetor independente acima como evidência, sem incorporar a captura inteira ao repositório.
Pode ser corrigido e verificado junto de CMD-01 nos testes do adaptador.

## ENTITY-01 — Enviar Ignited ao anunciar um creeper que já está em fuse

Evidência: `src/zenith/Gameplay/Entities/CreeperSystem.cs:143` chama Reconcile sem afterSpawn;
`:160` envia Ignited apenas quando IsFusing muda. O AddActor emitido por
`src/zenith/Protocol/EntityProtocol.cs:413` só contém metadados básicos de dimensões/flags.
Assim, um observador admitido depois da transição recebe o ator sem o estado atual do pavio.

Impacto: estado visual de perigo ausente para esse observador até desarme ou explosão.
É uma ausência de projeção, não um erro de tamanho/ordem do SetActorData.
Usar o callback afterSpawn já existente em `ActorReplication.Reconcile`; o sistema de
minecart já usa esse mecanismo para anunciar ocupação a novos observadores.

Regressão proposta: A vê o creeper e inicia fuse; B passa a vê-lo antes da explosão, mantendo
A perto. B recebe spawn seguido de Ignited=true; A não recebe uma transição redundante.
Cobrir também o anúncio inicial sem fuse. Gameplay decide o estado; Protocol escreve flags.

## ATTR-01 — Projetar os valores atuais de saturação e exaustão

Evidência: `src/zenith/Packets/UpdateAttributesPacket.cs:43` fixa saturação em 20 e `:44`
fixa exaustão em 0. `src/zenith/Protocol/EntityProtocol.cs:687` projeta Player sem passar esses
campos. Entretanto `src/zenith/Player/Player.cs:172` começa saturação em 5 e HungerSystem
altera saturação/exaustão ao comer, correr e regenerar (`:88`, `:120`, `:126`, `:133`).

Impacto confirmado: os valores Current enviados divergem da autoridade, inclusive no primeiro
anúncio. Não foi demonstrado nesta rodada um efeito específico no HUD ou na previsão do cliente.
Não há evidência de desalinhamento dos seis floats, modifiers ou tick de UpdateAttributes.

Correção recomendada: parametrizar os valores na projeção atual, mantendo defaults explícitos
para a fábrica de defaults. Decidir a cadência no Gameplay: hoje HungerSystem geralmente envia
ao mudar fome/saúde, portanto apenas trocar constantes não garante deltas de saturação quando
a fome permanece igual. Não enviar um pacote completo a cada tick de exaustão sem necessidade.
Considerar os limites do atributo ao projetar estados transitórios; não alterar a autoridade
apenas para satisfazer a serialização.

Regressão proposta: jogador com saturação 3 e exaustão 2; ler Current de ambos no pacote real
de SendPlayerAttributes. Cobrir início com saturação 5 e a decisão de sincronização quando
saturação muda sem alterar fome. Manter os testes dos demais atributos.

## ENTITY-02 — Completar a projeção inicial do jogador com saúde

Evidência: `src/zenith/Protocol/EntityProtocol.Ports.cs:65` anuncia AddPlayer, armor e offhand,
sem SendHealth. `src/zenith/Gameplay/Replication/PlayerVisibility.cs:78` documenta que esse
método deveria fornecer a saúde ao novo observador. RelayHealth só envia aos peers existentes
quando ocorre uma nova mudança. Em contraste, `ActorReplication.cs:27` já envia a saúde atual
de mobs depois do anúncio.

Impacto: late join e reconstrução por RefreshPeerView não recebem a saúde atual do jogador
até a próxima alteração. Completar SendPlayerActorOf com o UpdateAttributes de saúde atual,
preservando a ordem posterior ao AddPlayer e o caminho separado do HUD local.

Regressão proposta: jogador com 7/20 de vida; anunciar a um novo peer e ler UpdateAttributes
com runtime correto, Current=7 e Max=20 após AddPlayer. Cobrir também RefreshPeerView.

## Cobertura, descartes e limites

Além dos casos acima, foram revisados codecs e chamadores de AddActor/AddPlayer/SetActorData,
MoveActorAbsolute, ActorLink, EntityMetadata/PropertySync, atributos, equipamento/armor,
LevelSoundEvent/PlaySound/StopSound/LevelEvent, AvailableCommands/CommandRequest, forms,
containers, CreativeContent, UnlockedRecipes, WorldClocks, BlockPos e teleporte.
Não se encontrou outro novo desalinhamento estrutural comprovado nessas leituras.

- PropertySync vazio e Body vazio em armor de jogador são limites deliberados, não campos ausentes.
- Cast ulong→long antes de writers unsigned preserva bits; não constitui por si só falha de ID.
- ActorLink preserva veículo antes de passageiro; os offsets de posição dependem do tipo de ator.
- Não se inferiu presence bool a partir de required/default, nem se converteram enums textuais
  para números só por causa do underlying type.
- Nomes de som de golem/enderman ainda precisam de validação visual; o contrato aceita string
  opaca e não comprova que os recursos existem ou que os eventos produzem o efeito desejado.
- Form responses e server settings intencionalmente ignorados não são uma pipeline implementada
  com problema de dispatch. Não há sistema de forms ativo a completar nesta auditoria.
- O parse symbol String=58 do adaptador difere do 56 usado em vários argumentos de texto da
  captura. Sem provar a semântica de ambos os símbolos, isso não confirma que 58 seja inválido
  para os argumentos do Zenith. Investigar antes de substituir a constante; não é um sétimo bug.
- Os formatos de clocks e crafting previamente comparados com captura não foram modificados.
  A cadência dos clocks novos e ticks de teleporte não foi declarada incorreta sem prova adicional.
- Não houve nova captura, teste gráfico, carga, fuzzing nem auditoria exaustiva dos pacotes não
  implementados. Animate.Hand explícito permanece com a pendência anterior de captura.

Ordem recomendada: PACK-01, CMD-01/CMD-02 e os seeds de entidades, depois ATTR-01 com decisão de cadência.
Os achados são independentes; não exigem nova abstração, scheduler, plugin API ou modelo de atributos.
Para uma implementação posterior, rodar os testes focados e então
`dotnet test zenith.sln -c Release --no-restore -m:1`, seguido de `git diff --check`.
