# Auditoria de alinhamento do protocolo 2225

## Estado após implementação e aprofundamento — 2026-11-14

Os achados confirmados abaixo foram corrigidos no código local, com autorização do usuário.
A descrição original da auditoria foi mantida para rastrear a evidência e o motivo das mudanças.

Verificação final: `dotnet test zenith.sln -c Release --no-restore -m:1` terminou com sucesso:
**1700 aprovados, zero falhas** (1416 no projeto principal e 284 nas folhas/ferramentas).
`git diff --check` também passou. A build Release foi atualizada sob Builds ignorado.

| Item | Estado e verificação |
|---|---|
| MobEffect runtime ID | Unsigned; vetores literais para IDs 2, 127 e 128 |
| Skins | DTOs/adaptador/fallback corrigidos; duas capturas próprias de PlayerList lidas e reescritas byte a byte, 293338/293338 bytes por pacote |
| Imagem de persona | A captura tem 256px; limite antigo 128 removido em favor do limite 4096 do contrato |
| Text/AuthInput | Limites antes de alocação/continuação; rejeição sem publicar movimento/inventário |
| Payload completo | EOF exigido especificamente em Text, AuthInput, InventoryTransaction e PlayerSkin; não foi imposto globalmente a decoders parciais |
| Biomas | Teste percorre 89 registros e 187 strings até EOF, validando referências e gates |
| ISR descritores | Empty/Name/Molang/Tag compartilhados por ingredients/results; teste verifica ação seguinte e EOF |
| ISR variantes inválidas | Discriminador validado antes do cast; 256/257 não viram Take/Place |
| Fixtures ISR | Separados índice da união e enum interno, inclusive AutoCraft 11/13 |
| AddPlayer/AddItemActor | Preservam Damage NBT e extra de escudo; vetores próprios de BDS na suite |
| Tick de efeitos/movimento | AuthInput preserva tick no handoff; apenas Gameplay avança o ack; protocolo usa o último processado para jogadores e zero para atores comuns |

A validação independente de crafting percorreu **1219 shaped + 2068 shapeless = 3287 receitas**,
com **16055 ingredientes**, **279 tags** e **3287 requisitos de desbloqueio**, até o fim das duas
primeiras arrays (798370/1063236 bytes do pacote). O formato efetivo dessa captura própria do BDS
confirma o writer atual de ingredientes; não houve mudança baseada no JSON de domínio isolado.
As demais arrays desse pacote não foram consideradas validadas por essa medição.

O aprofundamento confirmou a estrutura das capturas próprias de ResourcePacksInfo (27 bytes) e
UpdateAbilities (33 bytes), além da revisão de regras, StartGame, entidades, NBT e demais inbound.
Não se inventou suporte para packs populados/subchunk requests que não estão modelados.

Permanece pendente **Animate.Hand explícito**: falta captura própria que distinga byte direto de
presença+byte. O caminho atual usa null, cujo byte zero coincide com mainhand. A aceitação visual
de multiplayer/persona e todos os efeitos continua fora da evidência produzida por testes de wire.

### Revisão pós-implementação

**Scope:** diff local de interoperabilidade e codecs 2225, incluindo correções anteriores já
validadas pelo usuário. **Verdict:** Pass with nits; a pendência de captura acima está registrada.

**Blockers:** nenhum identificado na revisão de camadas, ownership ou contratos corrigidos.
**Nits:** Animate.Hand explícito e aceitação visual dos caminhos citados permanecem abertos.

| Camada | Revisão |
|---|---|
| Packets e gerador | Tipos, limites e leitura/escrita; não decidem gameplay |
| Session | Decodifica, valida e publica input; não altera inventário/mundo |
| Player/Gameplay | GameLoop registra tick processado; handoff de movimento preservado |
| Protocol | Projeta estado já decidido, incluindo ticks e dados dos stacks |
| NetherNet/binary | Correções locais e testes nas respectivas folhas |

- Nenhuma nova abstração congelada, fallback de storage ou configuração por ambiente.
- Filas discretas e persistência não foram substituídas; radius/movement mantêm latest-value.
- Probes e saídas continuam em Builds ignorado; não há assets de cliente ou credenciais no diff.
- Testes de bytes esperados, EOF, rejeição sem intents e capturas próprias substituem as suposições
  espelhadas relevantes. Comando de verificação: `dotnet test zenith.sln -c Release --no-restore -m:1`.

## Registro original da auditoria

Data: 2026-11-13. Base: `cb27c69` mais as alterações locais de interoperabilidade ainda não
commitadas. Escopo: codecs implementados em `src/zenith/Packets`, tipos aninhados, gerador e
chamadores imediatos. Esta auditoria não altera código de produção.

Referência oficial fixada: `mojang-protocol-docs` tag `v1.26.62`, commit
`f8d1496b5efc95105bd9ae1f2f2f076a43f71d82`, protocolo 2225. O checkout atual dessa referência
tem outro protocolo e não foi usado como contrato. Para reproduzir as leituras:

```powershell
git -C ../mojang-protocol-docs show v1.26.62:json/SerializedSkin.json
git -C ../mojang-protocol-docs show v1.26.62:json/MobEffectPacketPayload.json
git -C ../mojang-protocol-docs show v1.26.62:json/ActorRuntimeID.json
```

O Cinnabar foi consultado apenas como referência comportamental complementar. Nenhum código,
fixture ou recurso do projeto foi copiado. A aceitação manual de baús e inventário no BDS e no
Zenith corrigido estabelece o caso InventoryTransaction; não estabelece todos os outros pacotes.

## Resultado priorizado

| Prioridade | Achado | Categoria | Esforço | Risco da correção | Confiança |
|---|---|---|---|---|---|
| P1 | Runtime ID de MobEffect usa zigzag em vez de unsigned | Wire | S | Baixo | Alta |
| P1 | Família de skins mantém tipos e coleções anteriores ao contrato atual | Wire | M | Médio | Alta para tipos; coleções corroboradas por codec de referência |
| P2 | Text aceita contagem arbitrária antes de alocar parâmetros | Validação | S | Baixo | Alta |
| P2 | AuthInput continua a leitura após exceder limite de block actions | Validação/alinhamento | S | Baixo | Alta |
| P2 | Testes e fronteira de decode podem aceitar suposições espelhadas/bytes restantes | Verificação | M | Médio | Alta quanto à lacuna |
| P3 | Documentação confunde origem antiga do blob de biomas com incompatibilidade de wire | Documentação | S | Baixo | Alta |

S: horas; M: aproximadamente um dia incluindo testes. São estimativas, não compromissos.

## 1. MobEffect — runtime ID unsigned

`src/zenith/Packets/MobEffectPacket.cs:13-14` declara `[WireVar] long TargetRuntimeId`.
`src/zenith.PacketGenerator/WireTypeVocabulary.cs:44` associa `long` a VarLong/zigzag.
`src/zenith/Protocol/EntityProtocol.cs:245` passa o runtime ID real, convertido para long.
O contrato `MobEffectPacketPayload` ordinal 0 referencia `ActorRuntimeID`, definido como
uint64 com Compression. Portanto, o ID 2 é codificado como 4 e o cliente pode ignorar o efeito
ou associá-lo a outro ator. Não é apenas uma divergência de nomenclatura do tipo CLR.

Correção sugerida: representar explicitamente o unsigned no DTO e ajustar o adaptador. Verificar
IDs 2, 127 e 128 lendo o corpo com unsigned varlong e bytes esperados independentes do decoder
gerado. Os demais campos de MobEffect não precisam mudar por causa desse achado.

## 2. PlayerList/PlayerSkin — família SerializedSkin

Evidências de produção:

- `SerializedSkin.cs:42,63,70,114,129,134`: contagens de animações/persona/tints UInt32 LE;
  as coleções Cereal atuais usam contagem VarUInt.
- `SerializedSkin.cs:60-61,125-126`: ArmSize e SkinColor como VarString. O contrato 2225
  especifica ArmSize como uint8 Enum-as-Value e SkinColor como Color/int32.
- `SkinWire.cs:32,39-42`: o fallback repete o formato antigo; corrigir apenas o DTO completo
  deixaria o caminho mais comum de placeholder ainda desalinhado.
- `SkinAnimation.cs:15,17,24,26`: enums UInt32 LE onde AnimatedImageData pede Compression.
- `SkinPersonaPiece.cs:16,33`: PieceType string onde SerializedPersonaPieceHandle pede
  uint32 Enum-as-Value.
- `SkinPersonaTintPiece.cs:12-18,24-28`: tint como strings/contagem; TintMapColor define quatro
  Colors int32, e a chave do mapa precisa seguir o contrato de PieceType.

Impacto: erro compartilhado por PlayerList e PlayerSkin, especialmente ao receber outro jogador,
alterar skin ou usar persona/animações. A falha pode aparecer depois do primeiro campo divergente.

Correção sugerida: revisar o conjunto completo, incluindo fallback e conversão dos valores
vindos do JSON de login. Não substituir todos os UInt32 indiscriminadamente: largura/altura da
imagem continuam campos fixos. Antes da alteração, obter uma captura própria de PlayerList ou
PlayerSkin do BDS e validá-la com leitura independente até EOF, incluindo arrays não vazios.

`PlayerListPacketTests.cs:59` e `PlayerSkinPacketTests.cs:219` usam o próprio SerializedSkin.Read
para verificar o writer. Esses round trips podem preservar um erro compartilhado. TrustedSkinFlag
e ProfileHash NÃO estão ausentes: os chamadores os escrevem depois de Skin.Write. GeometryVersion
também não é um problema de tipo: MinEngineVersion é string.

## 3. Text — limite antes da alocação

`src/zenith/Packets/TextPacket.cs:95-96` lê uma contagem e aloca `new string[count]` sem limite.
`InGameSessionHandler.cs:140` decodifica antes de descartar tipos diferentes de chat.
`MessageAndParams.json` limita Parameter List a quatro itens.

Impacto: um payload pequeno fora do contrato pode provocar tentativa de alocação desproporcional.
Correção sugerida: rejeitar antes de alocar, testar 4/5/extremo e usar rejeição segura do handler.
Não acrescentar marcadores de presença aos campos Text apenas porque não constam em required.

## 4. AuthInput — rejeitar excesso de ações

`src/zenith/Packets/PlayerAuthInputPacket.cs:155-156` retorna `[]` para contagem acima de 100,
sem consumir as entradas nem rejeitar o corpo. Decode continua nos opcionais de veículo e no
restante dos floats. O contrato limita Player Block Actions a 100.

Impacto: bytes de ações podem ser interpretados como outros campos e o pacote inválido pode
publicar movimento com ações vazias. Correção sugerida: lançar InvalidDataException, testar
100/101 e verificar que a rejeição não publica intents. Isso não é prova de falha com cliente
conforme; é uma divergência no tratamento de entrada fora do contrato.

## 5. Verificação independente e consumo completo

`src/zenith/Packets/DataPacket.cs:47-48` decodifica e descarta o stream sem verificar EOF.
`InGameSessionHandler.cs:28-29` considera sucesso qualquer retorno sem exceção. Isso ajuda a
ocultar campos esquecidos, especialmente quando o teste constrói o mesmo formato que o decoder.

Não impor EOF global sem inventário dos DTOs: alguns são deliberadamente parciais. Identificar
os completos e testar corpo válido, truncamento e byte extra. Priorizar MobEffect e skins;
depois fixar a validação estrutural de biomas em `SpawnPathWireTests`, hoje limitada a ID,
comprimento e primeiro byte. Nenhuma nova infraestrutura genérica é necessária.

## 6. Biomas — falso positivo descartado

`BiomeDefinitionListBlob.cs:7` e `data/manifest.json` registram origem BDS 1.26.52.3/2193.
`docs/protocol-churn.md:74` conclui que os opcionais 2225 tornam o blob incompatível.
Contudo, a leitura independente do blob atual seguindo o contrato 2225 consumiu exatamente
7667/7667 bytes: 89 registros, tags presentes nos 89, chunk-generation ausente nos 89 e 187
strings. O blob já contém os gates necessários.

A proveniência antiga continua verdadeira e a atualização semântica de dados pode ser necessária;
isso não demonstra desalinhamento de bytes. Corrigir a conclusão documental e fixar o teste
estrutural é preferível a regenerar dados por uma suposição incorreta sobre required/opcionais.

## Pendências que exigem evidência adicional

- **Animate.Hand:** `AnimatePacket.cs` usa optional byte; o codec de referência consultado usa
  byte direto, e o schema oficial tem default mainhand. Null produz 00 nos dois modelos;
  mão explícita distingue os formatos. Confiança média: capturar mainhand/offhand antes de mudar.
- **RecipeIngredient:** o JSON de domínio e a serialização de CraftingData não parecem iguais,
  mas ADR §147 e o oracle de captura do BDS sustentam o writer atual. Não converter usando apenas
  o JSON de domínio. Validar o corpo binário original e identificar o tipo efetivamente serializado.

## Cobertura e limites

- Os 84 IDs declarados em ProtocolInfo coincidem com os wrappers oficiais de mesmo nome.
- Revisados: join/StartGame/nested, packs, chunks/publisher/radius, abilities/adventure, registries,
  inventário/ISR/transações/AuthInput, entidades/movimento/metadata, skins, efeitos, som, comandos,
  texto/forms/emotes, clocks, regras e tipos aninhados usados por esses caminhos.
- Não se encontrou outra divergência comprovada de framing nos caminhos revisados. Isso não é
  certificação de compatibilidade completa nem de pacotes que Zenith não implementa.
- Não foram exercitados nesta auditoria packs populados, client-side chunk generation, todos os
  payloads não modelados de clocks, crafting visual, persona em multiplayer ou cliente oficial.
- Baseline anterior à auditoria: build sem warnings/erros, suite completa 1674 testes aprovada e
  40 testes focados após a ampliação de previsão de inventário. Não foram executados testes novos
  como parte desta inspeção somente leitura.

Ordem recomendada: oracle independente de skins; corrigir MobEffect e skins; rejeições de
Text/AuthInput; validação estrutural/EOF e documentação. Esta é a lista de achados, não uma
autorização ou um plano executado de mudança dessas famílias.
