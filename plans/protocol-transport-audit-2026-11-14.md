# Segunda auditoria — batches Bedrock, RakNet e NetherNet

## Implementação autorizada — 2026-11-14

Os nove achados foram tratados. O registro original abaixo foi preservado para rastrear a
evidência. A escolha dos limites/ownership está na ADR §152.

**Validação final:** `dotnet test zenith.sln -c Release --no-restore -m:1` terminou com sucesso:
**1729 testes aprovados, zero falhas** (1424 principais, 48 RakNet, 27 NetherNet, 53 binary,
177 nas demais folhas/ferramentas). `git diff --check` passou; build Release atualizada em Builds.

| Achado | Correção e regressão |
|---|---|
| ACK-01 | Decoder preserva o intervalo exato; valida formato e orçamento agregado de 4096 sequências antes de aplicar ACK/NACK |
| BATCH-01 | Entrada fechada e fechamento durante o batch interrompem despacho; transição legítima de handler continua funcionando |
| RAK-01 | Gap e ACKs/NACKs pendentes limitados a 4096; excesso encerra antes de materializar o gap |
| RAK-02 | Ordem futura limitada a distância 4096, 2048 frames e 8 MiB agregados por sessão; orçamento é liberado ao drenar |
| NN-01 | Reserva compartilhada por peer de bytes/mensagens/canais antes da cópia, inclusive na negociação; overflow registra falha e o owner fecha fora do callback |
| VAR-01 | Bits excedentes no quinto/décimo byte rejeitados; máximos uint32/uint64 continuam válidos |
| NN-02 | Segmentos usam o mínimo local/remoto; integração nativa recebe 40000 bytes respeitando o máximo remoto de 4096 |
| RAK-03 | Índices reliable, ordered e sequenced usam aritmética circular de 24 bits; contadores de saída também são normalizados |
| POOL-01 | Falha devolve o buffer atual; testes verificam aluguel/devolução em leitura normal, IOException e limite excedido |

Testes relevantes: InboundBudgetTests (ACK exato, agregado, gaps, retenção, reordenação e wrap),
WebRtcAdmissionTests (orçamentos antes do tick/fechamento seguro), integração nativa com limites
262144 e 4096, BinaryStreamTests (overflow/extremos), GamePacketDispatchTests e InflateCapTests.
Nenhum teste materializa milhões de entradas. Fechamento por orçamento é reportado como decisão
do servidor no adaptador RakNet.

### Revisão pós-implementação

**Scope:** folhas binary/RakNet/NetherNet, fronteira de dispatch e regressões correspondentes.
**Verdict:** Pass. **Blockers:** nenhum identificado na revisão de camadas e recursos.
**Nits/limites:** orçamento de saída nativo (`BufferedAmount => 0`) continua sendo a limitação
preexistente documentada; esta mudança não afirma corrigir todos os modos de sobrecarga.

| Camada | Resultado |
|---|---|
| binary | Rejeição de overflow sem mudar representação dos valores válidos |
| RakNet | Retenção limitada, índices circulares, ACKs exatos; sem dependência de gameplay |
| NetherNet | Contabilidade nos handoffs; nenhuma chamada de dispose nativo no callback de mensagem |
| Session/Network | Dispatch interrompido no fechamento; motivos de encerramento preservam a origem |

Não foram introduzidos scheduler, plugin API, novos arquivos de configuração ou fallbacks de
storage. O orçamento de recebimento compartilhado é local ao transporte. Saídas e probes
permanecem em Builds ignorado, sem recursos de cliente ou credenciais no diff.

## Registro original

Data: 2026-11-14. Base: `cb27c69` com o diff local das correções anteriores. Inspeção somente
leitura: nenhuma alteração em produção, nenhum novo servidor, build ou teste executado nesta
rodada. O baseline de 1700 testes aprovados é da rodada anterior, não prova destes novos casos.
Os achados foram conferidos nos arquivos de produção, após revisão dos relatórios auxiliares.

## Prioridades

| ID | Prioridade | Problema | Esforço | Risco da correção | Confiança |
|---|---|---|---|---|---|
| ACK-01 | P1 | ACK/NACK pode confirmar/retransmitir além do intervalo recebido | S | Baixo | Alta |
| BATCH-01 | P1 | Batch continua despachando depois de desconectar | S/M | Médio | Alta |
| RAK-01 | P1 | Salto de sequência materializa milhões de NACKs | M | Médio | Alta |
| RAK-02 | P1 | Reliable ordered retém frames futuros sem limite | M | Médio | Alta |
| NN-01 | P1 | Orçamentos de entrada não valem durante a negociação nativa | M | Médio | Alta |
| VAR-01 | P2 | Overflow no último byte do varint vira valor pequeno válido | S | Baixo | Alta |
| NN-02 | P2 | Segmentação ignora máximo de mensagem anunciado pelo peer | S/M | Baixo/médio | Alta na omissão; efeito nativo requer integração |
| RAK-03 | P2 | Wrap de 24 bits não é tratado nas janelas reliable/ordered | M | Médio | Alta |
| POOL-01 | P3 | Falha do inflater não devolve buffer alugado ao pool | S | Baixo | Alta |

S: horas; M: aproximadamente um dia incluindo regressões. Estimativas de correção, não tempo
medido. A ordem acima considera impacto e dependências; não implica que P2 seja dispensável.

## ACK-01 — Não expandir um intervalo além do que o peer confirmou

`src/raknet/Network/Protocol/AcknowledgePacket.cs:85-91` usa `end + start > 512` e substitui
o fim por `start + 512`. Para o intervalo legítimo `1000..1001`, isso produz `1000..1512`:
513 sequências em vez de duas. O limitador pode aumentar o intervalo original.

`src/raknet/RakNetSession.cs:403-407` remove os backups das sequências decodificadas. Logo,
pacotes ainda não confirmados podem perder sua possibilidade de retransmissão, inclusive em
tráfego legítimo. NACK usa o mesmo decoder e pode causar retransmissões indevidas.

Correção sugerida: validar início/fim e extensão sem aumentar o intervalo; aplicar limite
agregado de expansão. Não assumir que um intervalo excessivo pode confirmar uma porção inventada.
Teste seguro sem rede: range `1000..1001` gera exatamente dois números; ao aplicá-lo à sessão,
backup 1002 permanece. Cobrir limites próximos de `0xFFFFFF`, range invertido e orçamento agregado.

## BATCH-01 — Interromper despacho após fechamento

`src/zenith/Session/NetworkSession.cs:365-370` processa todos os subpacotes sem consultar
`IsClosed`. No RakNet, `Disconnect`/`Close` (`src/raknet/RakNetSession.cs:118-147`) fecha e
dispara o callback de teardown sincronamente. `GameSessionHost.Close` remove a sessão e executa
`HandleClose`, mas o loop que já estava em andamento conserva a referência e continua.

Exemplo de transição: RequestNetworkSettings incompatível causa `FlushAndDisconnect`
(`LoginSessionHandler.cs:270`); um Login válido seguinte no mesmo batch ainda pode atingir
`PlayerManager.TryAdd` (`LoginSessionHandler.cs:215-235`). A limpeza já ocorreu antes da criação
desse jogador, portanto pode restar um Player órfão sem um próximo callback de fechamento.
Isso não é uma alegação de quebra da validação criptográfica: o segundo Login precisa ser válido.

Correção sugerida: verificar fechamento na entrada e após cada despacho, preservando as
transições legítimas de handler dentro de um batch. Regressões: handler fecha no primeiro de
dois subpacotes e o segundo não executa; rejeição de protocolo seguida de Login deixa o manager
vazio; batch legítimo que muda de handler continua funcionando.

## RAK-01 — Limitar a janela de gaps antes de criar NACKs

`src/raknet/RakNetSession.cs:481-488` percorre todo o gap de 24 bits e insere cada sequência
perdida em um HashSet, sob `_sessionLock`. `IsSequenceNewer` aceita avanço inferior à metade
do espaço: um salto de 0 para `0x7FFFFF` implica 8.388.606 entradas intermediárias.
O recebimento ocorre no loop UDP do servidor; o tick ainda copia/ordena o conjunto para NACK.

Impacto: datagramas pequenos causam trabalho e retenção desproporcionais antes de login Bedrock.
O rate limiter offline não é um limite para esse caminho de sessão já criada.
Correção sugerida: janela circular máxima e política explícita para saltos fora dela, antes de
materializar o gap. Preservar reordenação/wrap legítimos. Testar com salto moderado, como 10000,
exigindo retenção limitada ou fechamento; não executar teste de milhões de entradas.

## RAK-02 — Limitar frames reliable ordered futuros

`src/raknet/RakNetSession.cs:759-762` guarda frames futuros em `InputOrderingQueue` sem teto
de distância, quantidade ou bytes. A deduplicação de reliable (`:511-530`) limita MessageIndex,
mas a janela avança quando esses índices chegam contíguos.

Assim, MessageIndex 0,1,2... com OrderIndex 1,2,3... avança a deduplicação enquanto a ordem
esperada permanece zero. Os payloads ficam retidos e nenhum batch precisa chegar ao login.
`Incoming` renova LastSeen a cada datagrama; timeout não substitui o limite de retenção.

Correção sugerida: orçamento por canal e sessão e distância futura circular máxima antes da
inserção. Teste seguro com centenas de frames pequenos e orçamento reduzido; confirmar teto/
fechamento e que um caso legítimo fora de ordem drena quando o índice faltante chega.

## NN-01 — Aplicar orçamento na admissão dos callbacks

`src/nethernet/Axolotl/Transport/WebRtc/WebRtcTransport.cs:160-165` copia e enfileira cada
mensagem nativa incondicionalmente. Os limites de bytes/mensagens são consultados apenas em
`src/nethernet/Axolotl/Session/Session.cs:207-225`, após existir uma sessão e ocorrer seu tick.

Durante a negociação (`TransportNegotiation.cs:191-224`), o peer pode abrir apenas um dos
canais e enviar enquanto o servidor espera o segundo. Ainda não existe Session.Update para
limitar essa fila. `MaxPendingDataChannels` é definido em Transport.cs e não é consumido no
recebimento; a criação/enfileiramento de wrappers em WebRtcTransport.cs:241 também não tem teto.

Impacto: os orçamentos configurados não constituem limite real de memória nessa fase.
Correção sugerida: contabilizar/reservar antes da cópia; rejeitar excesso de mensagens, bytes e
canais; coordenar fechamento fora de operações nativas inseguras em callbacks. Preservar a
correção anterior que captura o primeiro pacote legítimo antes do poll.
Testes com fake de IRtcDataChannel e limites pequenos, sem rede: exceder orçamento sem tick;
anunciar cinco canais com orçamento quatro; verificar retenção limitada e falha registrada.

## VAR-01 — Rejeitar overflow, além de excesso de bytes

`libs/binary/BinaryStream.cs:514-536` limita o comprimento a cinco bytes, mas não os bits
válidos do quinto. `82 80 80 80 10` excede uint32, porém resulta em 2 por truncamento do shift.
O equivalente em `ReadUnsignedVarLong` (`:545`) permite bits excedentes no décimo byte.

Impacto: comprimentos/IDs malformados podem ser aceitos como valores pequenos válidos, inclusive
no framing de batches. Isso não demonstra, sozinho, um bypass de autorização.
Correção sugerida: no quinto byte, payload máximo `0x0F`; no décimo, `0x01`, antes do shift.
Não confundir isso com exigir representação mínima. Testar máximos válidos uint32/uint64,
overflow terminal e prefixo de comprimento inválido que não chega a despachar um pacote.

## NN-02 — Respeitar o máximo de mensagem remoto

`SessionDescription.GetMaxMessageSize` (`src/nethernet/Axolotl/Sdp/SessionDescription.cs:82-96`)
valida o campo SDP. Porém `WebRtcTransport.cs:446` passa apenas o máximo local ao peer e
`WebRtcMessageChannel.MaxMessageSize` devolve esse valor fixo. O segmentador escolhido em
`SessionManager.cs:57-62` calcula o mínimo entre dois valores locais, sem aplicar o remoto.

Um peer com máximo 4096 pode receber tentativa de envio de segmento até 262144 bytes. O efeito
exato depende do binding nativo (rejeição de Send/fechamento), mas a omissão no limite usado
pelo segmentador é comprovada. Não foi executado novo teste nativo nesta auditoria.
Correção sugerida: usar mínimo de orçamento local e valor do SDP, sem overflow uint32.
Teste de integração existente adaptado: peer com MaxMessageSize=4096 e payload confiável de
40000; exigir segmentos dentro do limite e remontagem exata. Não usar reflexão de handles privados.

## RAK-03 — Tratar wrap também em reliable e ordered

`Frame.cs` lê/escreve MessageIndex, SequenceIndex e OrderIndex como triads de 24 bits.
Contudo, `RakNetSession.cs:513,524-525` compara/incrementa a janela reliable como uint32 comum;
`:724,738,760` faz o mesmo com a ordem/sequência por canal. Após processar `0xFFFFFF`, o próximo
valor no fio é zero, enquanto a expectativa local pode estar em `0x1000000`: zero é descartado.

Impacto: sessão longa pode parar de entregar mensagens válidas. A correção existente de wrap
em FrameSet não resolve estes índices internos; SequenceWraparoundTests cobre somente FrameSet.
Correção sugerida: aritmética circular de 24 bits nas janelas e comparações, coordenada com os
limites de reordenação. Testar sem milhões de envios: posicionar os contadores perto da borda
em uma sessão de teste e alimentar `0xFFFFFF,0,1`, incluindo duplicata antiga e fora de ordem.

## POOL-01 — Devolver o aluguel se a leitura descomprimida falhar

`src/zenith/Session/NetworkSession.cs:391-417` aluga buffers, mas não os devolve se inflater.Read
lançar. O chamador só recebe a referência após retorno normal (`:341`), portanto seu finally
não recupera o aluguel nesse caso. O ramo de tamanho excedido já possui Return próprio.

Impacto: falhas repetidas perdem reutilização e aumentam alocação/GC. Não é vazamento permanente:
o GC ainda pode recuperar os arrays. Garantir Return em todas as falhas, sem double-return,
e testar leitura que lança, leitura normal e limite de tamanho.

## Cobertura, descartes e limites

- Revisados: ACK/NACK, gaps, reliable/ordered/sequenced, fragmentação, framing/compressão Bedrock,
  despacho/fechamento de sessão, segmentação e filas NetherNet; leitura suplementar de comandos,
  emotes, forms, Disconnect, Respawn e clocks. Não apareceu novo desalinhamento comprovado de
  payload nestes auxiliares; isso não certifica todos os seus ramos.
- Não repetidos: canal RakNet 0, identidade de retransmissão, ZigZag, callback inicial NetherNet,
  skins/ISR/MobEffect/NBT/ticks e demais correções do relatório anterior.
- O nome ZLIB com DeflateStream não prova erro: o caminho Bedrock usa raw-flate. As janelas
  de subpacotes verificam bytes restantes; Snappy não implementado é rejeitado explicitamente.
- Skin mid-game não fica automaticamente obsoleta em late join: PeerRelay atualiza Session.Skin.
- Label do canal nativo é cacheado no binding; não foi reportado como acesso após dispose.
- `BufferedAmount => 0`/ausência de orçamento de saída já é limitação documentada na ADR §143,
  não descoberta nova. A fila de comandos do runner também não tem teto; precisa ser considerada
  quando esse orçamento for implementado, sem apresentar limitação conhecida como novo bug.
- Os contadores de fragmentos, ordem de countdown e rejeição de fragmentação unreliable não
  apresentaram nova divergência comprovada nesta leitura.
- Não houve prova de carga real, teste hostil de rede nem novo cliente gráfico. Casos descritos
  acima são reproduções unitárias propostas; evitar materializar os exemplos de milhões.

Ordem recomendada: ACK e interrupção após fechamento; orçamento circular de gaps/ordered e
wrap na mesma revisão RakNet; limites de admissão NetherNet e tamanho remoto; overflow de
varints e propriedade dos buffers. Este arquivo registra achados e critérios de regressão,
não implementação já realizada nem uma nova declaração de compatibilidade total.
