# Auditoria de fidelidade vanilla e cobertura de gameplay

Data: 2026-11-15. Base: `cb27c69` **mais o working tree atual**, incluindo ADRs 151–157.
Escopo: gameplay comum, inventário/interações e pacotes associados. Código somente leitura;
criados apenas este relatório e a matriz CSV em plans/. Servidor mantido desligado, sem
novos builds/testes, sem alteração de configuração, commit ou push. Baseline da rodada
anterior: 1.820 testes aprovados; não é validação dos cenários novos encontrados aqui.

Método: inspeção dos handlers, donos de estado e projeções; duas revisões paralelas
somente leitura de combate/sobrevivência e inventário/interações. Os achados abaixo
foram conferidos novamente no código pelo agente principal. A documentação antiga
subestima funcionalidades existentes; não foi usada como prova de ausência.

## Conclusão

Atualização após implementação: F01, F02, F03, F06 e F07 receberam correções na
primeira rodada (ADRs 158–159). A tabela abaixo preserva os achados do snapshot
original; consulte `vanilla-first-fixes-2026-11-15.md` para escopo, validação e limites
das correções. F04, F05, F08, F09 e F10 continuam abertos. A implementação não foi
testada interativamente no Cinnabar nesta rodada; o servidor permanece desligado.

Segunda rodada: F04 recebeu limites por item na bolsa, cursor, offhand, armadura,
transferências, criativo, receitas e drops. F05 avançou para 77 nomes de blocos
sólidos de construção (75 adicionais ao conjunto anterior), com mineração e loot.
F05 continua parcialmente aberto para outras famílias e estados; F08/F09/F10
seguem abertos. Evidências e limites em `vanilla-stack-blocks-2026-11-15.md`.

As maiores lacunas não são uma lista de DTOs para adicionar. Há defeitos concretos de
conservação/dano, ações de pacotes existentes sem implementação e ciclos de gameplay
inteiros ainda ausentes. O criativo completo e as 3.819 receitas de grade tornam essas
diferenças mais visíveis, mas não implementam colocação, estações ou efeitos de itens.

Todos os dez achados abaixo têm confiança ALTA no comportamento atual do código.
S = horas; M = aproximadamente um dia; L = vários dias, incluindo testes. Risco refere-se
à correção, não à gravidade do defeito. São estimativas de ordem de grandeza.

| ID | Prioridade | Achado e impacto | Esforço | Risco | Evidência principal |
|---|---|---|---|---|---|
| F01 | P1 | Crafting não trata saídas secundárias: bolo requer devolver 3 baldes; bloco de mel, 4 garrafas; mel→açúcar, 1 garrafa. Caminhos que usam só a primeira saída perdem os recipientes; Create de outro índice é recusado | M | Médio | RecipeRegistry.cs:99; InventorySystem.cs:561; dados embarcados |
| F02 | P1 | Desconexão pode perder itens já movidos para cursor/grades, pois só o inventário principal é salvo | M | Médio | InventorySystem.cs:53; PlayerInventory.cs:378; PlayerWindows.cs:29 |
| F03 | P1 | Dano recebido de um jogador usa o ataque do mob-alvo; o mesmo golpe dá 2 na aranha, 6 no golem e 7 no enderman; PvP fixa 4 | M | Médio | SpiderSystem.cs:117; GolemSystem.cs:146; EndermanSystem.cs:128; PlayerMeleeSystem.cs:29 |
| F04 | P2 | Equipamentos e demais itens usam limite universal 64; criativo gera 64 até para itens não empilháveis no vanilla | M | Médio/alto | PlayerInventory.cs:28,266,324; InventorySystem.cs:330 |
| F05 | P2 | A maioria dos blocos do catálogo continua recusada ao colocar; gate ainda é o conjunto inicial de blocos | M por família / L amplo | Alto | Blocks.cs:191,287; InGameUseItemHandler.cs:160; BlockEditSystem.cs:105 |
| F06 | P2 | Armadura comum reduz dano de queda; apenas Void/Starve/Magic ignoram armadura | S | Baixo | ArmorMitigation.cs:18; MovementSystem.cs:267 |
| F07 | P2 | Limpar efeitos apaga estado no servidor sem enviar MobEffect Remove; o cliente pode manter o efeito antigo até expirar | S | Baixo | EffectSystem.cs:49–54; compare remoção por expiração em :103 |
| F08 | P2 | Fornalha e outras estações não têm ciclo autoritativo; mineração não chega à cadeia normal de fundição | L | Alto | InGameUseItemHandler.cs:129; OpenContainerSession.cs:18; InventoryContainerMap.cs:67 |
| F09 | P2 | Projéteis do jogador são um protótipo: sem arco/carga/munição, sem pitch na criação e sem atingir outro jogador | L | Alto | InGameUseItemHandler.cs:98; ProjectileSystem.cs:139–149,245 |
| F10 | P2 | Sem afogamento, fogo/lava ou sufocamento autoritativo; queda não considera entrada na água | L / M para água primeiro | Alto | HealthState.cs:7–16; WorldModule.cs:21–33; MovementSystem.cs:249–274; Blocks.cs:216–217 |

Os caminhos da tabela são relativos a src/zenith/, exceto os dados. Os números de linha
identificam este snapshot e podem mudar com a implementação.

## Detalhamento das correções de maior retorno

### F01 — Planejar todas as saídas de crafting

RecipeDataReader preserva o array inteiro de saídas; CraftingDataBuilder também o anuncia.
Entretanto, o craft manual e o automático entregam somente Outputs[0]. O handler ainda
recusa Create cujo ResultSlot seja diferente de zero. Conferência direta do gzip:

- `minecraft:honey_block`: bloco de mel + 4 glass_bottle.
- `minecraft:honey_bottle_to_sugar`: 3 sugar + 1 glass_bottle.
- `minecraft:cake`: bolo + 3 bucket.

A manifestação no cliente depende da sequência: um pedido com Create adicional
pode ser recusado com rollback, enquanto o caminho que só cria o resultado principal
omite os recipientes. A insuficiência do contrato está confirmada no código; não foi
reproduzida uma perda em uma sessão de cliente nesta auditoria.

Correção: caracterizar a sequência aceita pelo BDS e planejar resultado principal e
recipientes juntos, com destinos e rollback. Não apenas anunciar mais saídas. Testar
craft manual/automático, inventário cheio, replay e falha no último destino. Conferir
RecipeRegistry.cs:87–101, InventorySystem.cs:551–565 e InGameInventoryHandler.cs:209.

### F02 — Conservar recursos na saída abrupta

NetworkSession.cs:299 agenda persistência no tick, o que é correto para ownership.
InventorySystem.cs:53 salva esse jogador sem recuperar cursor/grades. PackMainBlob
serializa apenas _slots; cursor e grids são efêmeros. Exemplo reproduzível a testar:
mover materiais da bolsa para a grade, desconectar antes do cleanup, entrar novamente.
Os materiais já saíram da bolsa e a grade não faz parte do blob recuperado.
Isso também afeta a identidade estável que participa normalmente da persistência;
a política deliberada de não persistir identidades efêmeras é uma questão distinta.

Correção: um encerramento autoritativo que conte todos os depósitos, cancele pedidos
não aplicados e conserve os itens em destinos recuperáveis antes de salvar. Não repetir
uma devolução de cliente já aceita. Não confiar só em drops que não sobrevivam ao restart.
Testar cursor, grade pessoal, mesa, resultado pendente, inventário cheio e teardown
concorrente ao snapshot do tick.

### F03/F06 — Corrigir a origem e a aplicabilidade do dano

DamageableActorCombatCore.cs:59 recebe diretamente o attackDamage passado pelo sistema
da espécie. Essas mesmas constantes são usadas para o ataque que o mob executa.
Resolver dano a partir do atacante/stack/effects e separar o dano próprio da espécie.
Usar a mesma decisão para mobs e jogadores; testar mão vazia versus arma e o mesmo
atacante contra espécies diferentes. Não misturar esta correção com tolerância de rede
ou uma reescrita da IA.

ArmorMitigation não exclui Fall. A [referência primária do PocketMine](https://github.com/pmmp/PocketMine-MP/blob/stable/src/event/entity/EntityDamageEvent.php)
exclui essa causa de armadura comum; encantamentos são uma camada distinta. Testar
mesma queda com/sem armadura. O código de queda já existe: não é feature ausente.

### F04/F05/F08 — Disponibilidade e funcionamento são contratos distintos

Uma definição por item deve fornecer o limite de stack para todos os caminhos:
criativo, transferências, pickup, receitas e baús. Definir recuperação dos stacks já
acima do limite antes de passar a recusá-los, sem destruir recursos persistidos.

Ampliar colocação por famílias verificadas. Remover IsPlaceable indiscriminadamente
trataria portas, lajes e escadas como cubos e esconderia problemas de estado/colisão.
Começar por cubos comuns e pela colocação da primeira estação que será implementada.

A fornalha é uma boa vertical: colocar/abrir, entrada/combustível/saída, processamento,
inventário cheio, persistência/restart, consumo de combustível e UI de progresso.
Hoje só há destinos PlayerInventory, Chest e CraftingTable. Poções/misturas também
são anunciadas como arrays vazios em CraftingDataPacket.cs:136–138. Outras estações
não devem ser consideradas prontas por adicionarmos o mesmo ContainerOpen.

### F07 — Completar a emissão de um pacote já existente

ClearAll drena os efeitos e retorna; a remoção por expiração já envia EventRemove.
Capturar os tipos anteriores e emitir as remoções após a decisão no tick. Testar
estado do domínio **e** chamadas/pacotes emitidos. O teste atual EffectTests.cs:115–130
verifica apenas o dicionário vazio. Este é um exemplo concreto de envio ausente,
não de codec ausente.

## Pacotes e ações relevantes

Contrato consultado: Mojang/bedrock-protocol-docs, tag v1.26.62, commit
f8d1496b5efc95105bd9ae1f2f2f076a43f71d82, protocolo 2225. A revisão do schema usa
essa tag, não o checkout corrente de outra versão. Ausência verificada no enum
ProtocolInfo.cs, DTOs e mapa InGameSessionHandler.cs:106–125.

| Contrato | Situação atual | O que desbloqueia / prioridade |
|---|---|---|
| ContainerSetData, 0x33, servidor→cliente | Ausente | Progresso/combustível de fornalha e brewing; implementar junto ao domínio da estação |
| BlockActorData, 0x38 | Ausente | Projeção e atualizações de NBT de blocos com estado, incluindo placas; requer modelo e validação do tipo de bloco |
| BookEdit, 0x61, cliente→servidor | Ausente | Editar/assinar livro e pena; validar slot, páginas, autoria e limites antes de persistir |
| BlockPickRequest, 0x22, cliente→servidor | Ausente | Pick block; resolver bloco real e ação permitida no modo atual, sem confiar no item alegado pelo cliente |
| ItemStackRequest | Presente, cobertura parcial | BeaconPayment, CraftLoom e Repair/Disenchant têm vocabulário/decodificação parcial, mas não ação de gameplay suportada no mapper; exigem a estação correspondente |
| MobEffect EventRemove | Presente, emissão faltando no ClearAll | Corrigir F07, sem criar outro pacote |
| InventorySlot, 0x32 | Ausente | Atualização de um slot; atualmente é possível corrigir com InventoryContent/ISR. Otimização, não bloqueador geral |
| NetworkStackLatency, 0x73 | Ausente; schema o marca DEPRECATED | Telemetria/ping, não prioridade para corrigir os fluxos de gameplay acima |

Fontes primárias: [ContainerSetData](https://github.com/Mojang/bedrock-protocol-docs/blob/v1.26.62/json/ContainerSetDataPacket.json),
[BlockActorData](https://github.com/Mojang/bedrock-protocol-docs/blob/v1.26.62/json/BlockActorDataPacket.json),
[BookEdit](https://github.com/Mojang/bedrock-protocol-docs/blob/v1.26.62/json/BookEditPacket.json),
[BlockPickRequest](https://github.com/Mojang/bedrock-protocol-docs/blob/v1.26.62/json/BlockPickRequestPacket.json),
[ItemStackRequestActionType](https://github.com/Mojang/bedrock-protocol-docs/blob/v1.26.62/json/ItemStackRequestActionType.json),
[NetworkStackLatency](https://github.com/Mojang/bedrock-protocol-docs/blob/v1.26.62/json/NetworkStackLatencyPacket.json).

## Ordem recomendada e opções de direção

1. **Conservação:** F01 e F02; nenhum recurso já aceito deve desaparecer.
2. **Combate coerente:** F03 e F06; F07 pode entrar como correção pequena independente.
3. **Inventário válido:** F04, com migração dos stacks existentes.
4. **Primeira cadeia de produção completa:** família de blocos necessária em F05 → fornalha F08.
5. **Sobrevivência/combate ampliado:** água/queda/afogamento de F10 e arco de F09 como verticais separadas.

As opções 4 e 5 são expansão de produto, com custo maior e necessidade de testes BDS.
Os itens 1–3 corrigem contratos existentes. Não foi solicitado escolher planos de
implementação nesta auditoria; não foram criadas dezenas de planos ou feitas mudanças
de produção por inferência.

## Considerados e não classificados como regressão

- Não reportar como ausentes drops no chão, consumo temporizado, mesa 3×3, NBT de
  stacks, fila de inventário, geração de janela ou catálogo: essas partes já existem.
- As 3.819 receitas de grade examinadas usam `crafting_table`; não há evidência de
  receitas de stonecutter indevidamente executadas nessa tabela.
- Ausência de MoveActorDelta, SubChunk ou InventorySlot não prova gameplay quebrado
  quando a funcionalidade equivalente já usa outro fluxo válido.
- MOVE_PLAYER/ANIMATE/LEVEL_SOUND_EVENT em no-op não comprovam, isoladamente, falha
  de movimento, animação ou som: é preciso verificar os outros fluxos já usados.
- Movimento aceita pose do cliente com verificação de finitude e sem colisão/speed
  autoritativos nessa etapa. É uma limitação de autoridade a caracterizar, não prova
  de um bug visual normal nem motivo para adicionar correções sem política definida.
- Alimentos especiais e descoberta progressiva continuam limitações já registradas
  nas rodadas anteriores; não são apresentados como descobertas novas.

## Limites da auditoria e verificação futura

Não foram feitas sessões de cliente/BDS, captura de bytes, benchmark, revisão
exaustiva de todo packet ou nova auditoria de autenticação/transporte. Redstone,
crescimento/farming, dimensões/portais, navegação detalhada de todos os mobs e
comportamento de cada um dos 1.980 itens não foram auditados individualmente.

Para cada correção, começar com um teste da invariável no menor projeto dono.
Executar os casos focados em src/zenith.Tests e depois:
`dotnet test zenith.sln -c Release --no-restore -m:1`.
Preservar mundos, manter o servidor desligado até nova instrução e realizar a
comparação BDS em mundo isolado. Fonte modificada não deve ser substituída por HEAD:
este snapshot inclui o trabalho local já testado das rodadas anteriores.
