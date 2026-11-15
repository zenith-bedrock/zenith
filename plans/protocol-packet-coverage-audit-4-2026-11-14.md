# Quarta auditoria — inventário e lacunas por pacote

## Implementação autorizada — 2026-11-14

Os quatro achados confirmados foram tratados. O registro original abaixo foi preservado;
as hipóteses de Animate.Hand e String=58 não motivaram alterações especulativas.

| Achado | Correção e evidência |
|---|---|
| AUTH-01 | FULL usa FranchiseTokenValidator para assinatura RS256, emissor, audiência e validade; verifica ES384 ClientData com a chave NIST P-384 do cpk autenticado; nunca usa certificado legacy como identidade FULL |
| AUTH-02 | AuthenticationType fora do enum é rejeitado antes da política e do parsing de identidade |
| MOVE-01 | GameLoop marca teleporte pendente em respawn/dismount; mailbox preserva HandledTeleport do AuthInput/PlayerAction e descarta pose/ack antigos ao começar outro teleporte; pose fica bloqueada até o ack |
| LOGIN-01 | Prefixo unsigned confirmado por referência independente; decoder usa janela limitada e exige EOF interno/externo; JSON null e truncamento são rejeitados antes da identidade |

As chaves existentes são carregadas antes dos loops/transports e mantidas em cache com validade.
Recebimento faz apenas verificação com chaves frescas em memória; não espera HTTP. Se o serviço
de chaves falhar, FULL rejeita até recuperação, com aviso explícito. SELF_SIGNED/offline continuam
conforme auth.accept. O binding adicional de FULL no NetherNet foi mantido.

O parser estrutural legacy deixou de ser apresentado como verificação de assinatura. O caminho
moderno FULL verifica claims e prova de posse independentemente da presença de Certificate.
Esta implementação não adiciona autenticação por certificados legacy isolados.
FULL não usa os fallbacks offline mesmo quando o servidor aceita também offline; um token
válido sem UUID/XUID assinado permanece efêmero, sem promover SelfSignedId do ClientData.

A barreira de spawn inicial já descarta inputs até a confirmação de inicialização e foi preservada.
O novo estado de teleporte pertence ao Player e é escrito pelo Gameplay; handlers publicam
somente reconhecimento. A sinalização é limitada e idempotente, sem uma nova fila ilimitada.

Testes usam RSA/P-384 gerados localmente, cliente com chave errada, curva errada, ClientData
alterado, token expirado, emissor errado, campos obrigatórios ausentes e chaves indisponíveis.
Há vetores de Login independentes de Encode, rejeições no handler sem registro de identidade,
e regressões de ack sobrescrito, ack anterior, respawn, dismount e desconexão.
O teste de lifecycle usa auth self-signed para permanecer independente de HTTP.

### Post-impl audit

**Scope:** Session/Packets, handoff de movimento e Gameplay, preparação/refresh de chaves no
lifecycle existente, testes e ADR §153. **Verdict:** Pass with nits.

**Verificação:** `dotnet test zenith.sln -c Release --no-restore -m:1` passou com **1780 testes**
(1475 principais e 305 nas demais folhas), zero falhas. São 27 casos novos nesta rodada.
O ajuste final que mantém FULL sem XUID livre de fallback offline foi verificado novamente
com os testes focados de autenticação, cache, Login e lifecycle. Build Release atualizada em
Builds ignorado; `git diff --check` passou. Não foi iniciada uma sessão manual de gameplay.

A representação unsigned do Connection Request foi corroborada no wrapper/ByteSlice de uma
referência primária independente e fixada por vetor literal. A descoberta OpenID do emissor
confirma issuer/RS256/JWKS; consulta pública ao JWKS retornou três chaves RSA. Isso valida a
disponibilidade/formato do serviço nesta verificação, não um login real de usuário.

**Blockers:** nenhum identificado na revisão de contratos/camadas. **Nits:** login FULL com
cliente Xbox real e animação/retomada visual do cliente não foram exercitados; os testes
demonstram prova criptográfica sintética, formatos e transições de autoridade.

| Camada | Resultado |
|---|---|
| Packets | Limites, prefixos e flags; sem domínio/HTTP |
| Session | Rejeição de autenticação e provas antes de identidade/persistência; sem mutação de pose |
| Player | Mailbox existente preserva ack sob o mesmo lock de movimento |
| Gameplay | Decide espera/retomada e mantém posições autoritativas |
| Server | Prepara cache fora do tick e encerra refresh com a vida do servidor |

- [x] Sem nova abstração congelada, scheduler, DI, plugin API ou biblioteca de criptografia.
- [x] Sem downgrade silencioso de FULL para self-signed/offline.
- [x] Sem locks atravessando await; networking de chaves fora do GameLoop/receive.
- [x] Sem mudanças de persistência, tokens reais, recursos copiados ou artefatos fora de Builds.

## Registro original da auditoria

Data: 2026-11-14. Base: `cb27c69` mais as correções locais das três rodadas anteriores.
Produção somente leitura: criados apenas este relatório e o CSV em plans/. Nenhum build,
teste, servidor, alteração de configuração, commit ou push nesta rodada. O baseline anterior
é **1753 testes aprovados**, não uma reprodução dos novos casos encontrados aqui.

Contrato fixado: `../mojang-protocol-docs`, tag `v1.26.62`, commit
`f8d1496b5efc95105bd9ae1f2f2f076a43f71d82`, protocolo 2225. O checkout corrente da referência
tem outro protocolo. Não foram copiados código, recursos ou fixtures do Cinnabar.

## O que ainda não estava registrado individualmente

As revisões anteriores cobriam famílias inteiras, mas não forneciam um parecer por DTO. Assim,
ausência do nome de uma classe no texto não prova que ela nunca foi lida. A lacuna comprovada
era a rastreabilidade da profundidade por pacote, além dos opcionais e caminhos sem captura.

O inventário atual contém:

- **80 DTOs com ID próprio**, todos relacionados no
  [CSV de cobertura](protocol-packet-coverage-2026-11-14.csv), com registro anterior,
  revisão nesta rodada e arquivos de testes que referenciam literalmente o tipo.
- **4 IDs somente reconhecidos**, sem DTO: ClientCacheStatus (0x81), ServerboundLoadingScreen
  (0x138), ClientCameraAimAssist (0x141) e SetPlayerInventoryOptions (0x133). São caminhos
  intencionalmente ignorados; não devem ser contabilizados como codecs implementados.
- **1 wrapper StaticDataPacket** para replay dos dados fixos, além dos 80 DTOs. Oito nomes são
  enviados diretamente no join: VoxelShapes (337), SyncEntityProperty (165),
  AvailableEntityIdentifiers (119), PlayerFog (160), CameraPresets (198),
  CameraAimAssistPresets (320), CameraSpline (338) e TrimData (302).

Foram examinados/reconferidos **38 DTOs** nesta rodada, incluindo a pendência anterior de
Animate.Hand, que continua sem conclusão para mão explícita. Isso não representa 38 codecs
novos nem 38 certificações de compatibilidade. Os outros 42 conservam as evidências e os
limites das auditorias anteriores, indicados no CSV.

As referências literais em testes são um índice de navegação: um teste pode usar tipos
inferidos, bytes literais ou testes genéricos sem citar o nome do DTO. A coluna vazia não prova
ausência de cobertura; uma coluna preenchida também não prova um oracle independente.

## Prioridades novas

| ID | Prioridade | Problema | Esforço | Risco da correção | Confiança |
|---|---|---|---|---|---|
| AUTH-01 | P1 | Modo Xbox estrito não verifica criptograficamente a identidade no caminho RakNet | M/L | Alto | Alta |
| AUTH-02 | P1 | AuthenticationType desconhecido escapa dos gates de auth.accept no RakNet | S | Baixo | Alta |
| MOVE-01 | P2 | Teleporte não suspende posições previstas até HandledTeleport | M | Médio | Alta |
| LOGIN-01 | P2 | Login ignora o tamanho declarado do Connection Request | S | Baixo/médio | Alta na fronteira ignorada |

As falhas de autenticação são distintas do vínculo SDP/FULL do NetherNet corrigido antes.
Não houve tentativa de entrada com credenciais falsas, captura de terceiros ou prova de
exploração. O impacto abaixo deriva do fluxo de aceitação e persistência observado no código.

## AUTH-01 — A opção estrita não estabelece prova criptográfica

Evidência:

- `src/zenith/Server/ServerConfig.cs:116`: RequireStrictXbox é verdadeiro para accept exatamente xbox.
- `docs/adr/0009-auth-accept-modes-config-visual.md:5`: esse modo é apresentado como a escolha pública.
- `src/zenith/Session/LoginIdentity.cs:337`: ValidateChainSignatures apenas verifica formato,
  presença do campo x5u e assinatura decodificada não vazia. Não verifica assinatura, cadeia
  de confiança, emissor ou validade temporal.
- `src/zenith/Session/Handler/LoginSessionHandler.cs:106`: Certificate vazio pula até essa
  validação estrutural; depois ParseIdentityToken lê claims do payload.
- `src/zenith/Session/LoginIdentity.cs:42`, `:56` e `:73`: nome e UUID/XUID alegados podem
  produzir uma identidade estável sem prova de assinatura.
- `src/zenith/Network/RakNet/RakNetTransport.cs:19`: não há identidade autenticada de transporte
  nessa conexão, e RequiresTransportIdentityBinding é false. GameSessionHost cria diretamente
  LoginSessionHandler, sem outra etapa que estabeleça a confiança ausente.
- `LoginSessionHandler.cs:168` carrega inventário pela identidade resultante antes de entrar.

Impacto: a configuração que anuncia autenticação Xbox estrita não garante que as claims do
login pertençam ao remetente. Uma identidade alegada pode selecionar dados persistidos; a
rejeição de uma assinatura vazia não é autenticação. O modo LAN permissivo não é o achado.

Correção recomendada: validar FULL com um verificador criptográfico compatível com o protocolo
atual antes de aceitar claims ou carregar dados, com confiança/validade e vínculo dos tokens
relevantes. Ausência de certificado não pode simplesmente desativar a prova exigida; investigar
o contrato do token moderno antes de tornar Certificate obrigatório indiscriminadamente.
Preservar os modos SELF_SIGNED/offline como políticas explícitas. Reutilizar mecanismos
adequados já existentes quando possível, sem implementar criptografia própria nem transferir
autenticação de jogo para o adaptador RakNet.

Testes recomendados: chaves e tokens sintéticos locais; rejeitar assinatura inválida/expirada,
emissor não confiável, claims alteradas e ausência de prova no modo estrito. Exigir que não haja
Player aceito, deslocamento de jogador existente ou carga de inventário antes da validação.
Um caso positivo precisa de uma cadeia/token controlado realmente verificado. Não usar tokens
reais nem chamadas obrigatórias de rede nos testes unitários. As regressões existentes de
NetherNet precisam continuar passando, sem presumir que resolvem o caminho RakNet.

## AUTH-02 — Validar a alternativa antes de aplicar a política de modos

`src/zenith/Session/Handler/LoginSessionHandler.cs:69` lê AuthenticationType como int. As
ramificações rejeitam Guest e conferem a política apenas para Full=0 e SelfSigned=2; um valor
fora dessas alternativas continua até o parsing. O vínculo de transporte rejeitaria a
alternativa desconhecida no NetherNet, mas esse gate não existe no RakNet.

Impacto: o valor desconhecido não corresponde a nenhum modo autorizado por auth.accept, mas
pode atravessar seus gates. É uma validação ausente, independente de a verificação de
assinaturas também precisar ser corrigida.

Correção: validar o domínio completo de AuthenticationType antes de qualquer branch de
aceitação. Rejeitar alternativas desconhecidas e manter Guest explicitamente sem suporte.
Não interpretar offline como uma alternativa adicional do enum: hoje é uma política de fallback.

Testes: valores fora do enum, incluindo negativo e um acima do maior valor, com todos os modos
de configuração relevantes. O handler deve encerrar sem registrar Player ou iniciar packs.
Preservar os casos legítimos FULL/SELF_SIGNED e os testes de vínculo de transporte.

## MOVE-01 — Respeitar o reconhecimento do teleporte

O contrato `PlayerActionType.json` define HandledTeleport: ignorar posições previstas entre o
envio de MovePlayer Teleport e o reconhecimento; corresponde também ao InputData 37 de AuthInput.

Evidência: `src/zenith/Packets/PlayerAuthInputPacket.cs:41` e seu switch em `:102` não preservam
o flag 37. `src/zenith/Session/Handler/InGameAuthInputHandler.cs:129` ignora PlayerAction de
HandledTeleport. `MovementInputState` não carrega esse reconhecimento. O respawn envia
teleporte em `Gameplay/Survival/MovementSystem.cs:288`, e o dismount em
`Gameplay/Entities/MinecartSystem.cs:245`; não há uma barreira correspondente. MovementSystem
volta a aplicar qualquer input finito em `:90`.

Impacto: uma posição anterior ao teleporte ainda em trânsito pode sobrescrever a posição
autoritativa depois de respawn/dismount. Registrar um tick monotônico como ack de processamento
não impede que uma pose antiga seja aplicada. Não se afirma que todo cliente reproduz o
problema visual em toda tentativa; a falta da barreira é comprovada no caminho de decisão.

Correção recomendada: gameplay deve possuir o estado de reposicionamento pendente e retomada;
Protocol apenas transmite a decisão. Preservar HandledTeleport no handoff apropriado e cobrir
as duas formas de reconhecimento relevantes ao modo atual. Não mutar esse estado autoritativo
no callback de rede e não transformar um flag recebido sem teleporte pendente em permissão
geral para aceitar pose antiga. Considerar spawn inicial, morte, segundo teleporte e desconexão.

Regressões: iniciar reposicionamento, entregar inputs antigos antes do reconhecimento e exigir
que a nova posição permaneça; entregar reconhecimento e novo input e permitir movimento.
Cobrir respawn, dismount, input que já estava pendente e reconhecimento fora de contexto.
Os testes devem verificar estado e pacotes/handoff, sem depender de temporização real.

## LOGIN-01 — Ler o Connection Request dentro da fronteira declarada

`src/zenith/Packets/LoginPacket.cs:58` lê o prefixo do Connection Request e descarta o valor;
as duas strings internas são lidas diretamente do restante do pacote. Não há conferência de
fronteira nem EOF completo. Assim, uma estrutura com comprimento externo incompatível ainda
pode ser aceita se os comprimentos internos permitirem o parsing.

O wrapper oficial `LoginPacketPayload.json` descreve Connection Request como string delimitada.
O encoder em `LoginPacket.cs:49` escreve o prefixo com WriteVarInt; o decoder usa ReadVarInt.
`CriticalPacketRoundTripTests` usa esse mesmo par. A questão da representação exata desse
prefixo deve ser fechada com um vetor independente de Login, não com esse round trip espelhado.
O valor ignorado impede que a leitura atual detecte a diferença por comprimento.

Impacto confirmado: a fronteira declarada não é validada. A substream do batch já limita o
pacote inteiro, portanto isso não demonstra leitura do pacote seguinte nem uma quebra de
login dos clientes conformes. O encoder é sobretudo útil aos testes/tools, não um envio normal
do servidor; não atribuir ao writer um defeito visual de login ainda não reproduzido.

Correção: confirmar a representação em captura/fixture independente; criar a janela limitada
ao Connection Request declarado, validar os comprimentos internos e rejeitar sobras/truncamento.
Exigir rejeição antes do parsing/aceitação de identidade, mantendo o formato válido atual.
Testar comprimento externo menor/maior que os dados, strings internas truncadas, sobras e um
vetor válido cuja expectativa não venha do próprio Encode.

## Conferências sem outro defeito comprovado

Foram comparados individualmente handshake/status, RequestAbility, ações/interação/respawn,
inicialização, hotbar/radius, ActorEvent/BlockEvent/TakeItemActor/DeathInfo, pequenos setters,
AdventureSettings, ItemRegistry e Jigsaw. A reconferência complementar inclui Emote/EmoteList,
ResourcePackStack, InventoryContent/ItemStackResponse, LevelChunk/publisher, UpdateBlock,
SetActorMotion, regras e remoção. Os tipos aninhados consultados incluem BlockPos, Actor IDs,
FullContainerName, ItemStackNetId, Experiments, AdventureSettings e DeathCauseMessageType.

- Pequenos setters mantêm os tipos esperados: saúde/tempo/gamemode/radius signed comprimidos;
  dificuldade unsigned; commands enabled e adventure flags booleanos.
- PlayerAction/Respawn se vinculam ao player da sessão; ignorar o runtime ID alegado não permite
  agir no jogador nomeado por esse campo. Isso não é, sozinho, impersonação de outro ator.
- FullContainerName e extras de inventário não foram alterados por suposições de optional.
- Nenhuma lista vazia de packs/caches/propriedades foi tratada como suporte populado quebrado.

## Dados estáticos: evidência e testes que ainda faltam

O manifesto `src/zenith/data/join_static.json` contém **24 corpos**, de BDS 1.26.63.1/protocolo
2225. Cada ID/comprimento/SHA256 foi comparado com os arquivos próprios apontados por
`../bds-reference/out/join/index.json`: **24/24 correspondem exatamente**.
StaticDataPacket apenas acrescenta ID varuint e copia o corpo intacto.

Isso comprova a origem e o replay exato, não um decoder independente para todos os campos.
`JoinStaticDataTests` verifica protocolo, quantidade por nome, payload não vazio e replay
byte a byte de EntityIdentifiers. O loader confere hashes de todos os corpos. Faltam testes
estruturais específicos para cada variante de camera/voxel/trim/fog/property sync.
Tratar isso como melhoria de cobertura, não como bug wire demonstrado por origem antiga.

Jigsaw já é decodificado como um único compound Network-NBT até EOF no load e nos testes de
SpawnPath. ItemRegistry tem teste sintético de entradas com/sem componentes, ID negativo e
NBT até EOF; a ordem confere com ItemData. Não houve novo erro comprovado nesses dois DTOs.

## Limites e sequência recomendada

Priorizar AUTH-02 e AUTH-01, depois a barreira de teleporte e a fronteira de Login. Para a
correção de identidade, validar o desenho antes de reutilizar qualquer verificador: o vínculo
de transporte e a prova de emissor são contratos distintos. Nenhuma correção foi aplicada
nesta inspeção. Depois de implementar, executar testes focados e
`dotnet test zenith.sln -c Release --no-restore -m:1`, mais `git diff --check`.

Continuam abertos Animate.Hand explícito, parse symbol String=58 versus 56, opcionais/packs
populados não implementados, conteúdo completo das demais arrays de CraftingData, cobertura
estrutural dos blobs e validação gráfica. Não foram feitas novas capturas nem testes de carga.
