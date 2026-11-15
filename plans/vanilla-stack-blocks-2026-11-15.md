# Post-impl audit

**Scope:** develop, limites por item (F04) e primeira ampliação de famílias de
construção (F05). Working tree anterior preservado; sem revisão integral das
mudanças de protocolo anteriores. ADRs 160–161.
**Verdict:** Pass with nits.

## Blockers

- Nenhum identificado no escopo.

## Nits (non-blocking)

- Sem teste interativo Cinnabar/BDS nesta rodada; não iniciei nem reiniciei o servidor.
- F05 parcial: 77 nomes com ciclo de bloco sólido, sendo 75 novos além de oak_planks
  e stone_bricks já suportados. Escadas, lajes, portas, camas, orientação de logs,
  estações e outros comportamentos especiais continuam pendentes.
- Lã possui dureza, quebra à mão e self-drop; velocidade especial da tesoura segue
  pendente no catálogo de ferramentas.
- Craft em lote continua conservador: cada saída precisa caber em um stack legal.
- Saves antigos acima do limite permanecem intactos e podem diminuir no mesmo slot.
  Drops os separam antes do commit; falta de células livres recusa tudo e conserva
  a origem. Não há migração destrutiva ou clamp.

## Layer map

| Path | Role | OK? |
|------|------|-----|
| World/ItemStackLimits + data/item_stack_sizes.json | Capacidades por nome; runtime IDs resolvidos da paleta atual | Sim |
| Player/PlayerInventory, InventorySlotResolver | Capacidade de escrita, merge e transferência; exceção de redução legada | Sim |
| World/ChestStore, PlayerInventorySave | Escritas legais e restauração exata de snapshots/saves antigos | Sim |
| Gameplay/Inventory, RecipeRegistry | Concessão criativa e lote limitados; rollback existente preservado | Sim |
| World/FloorDropStore + Gameplay/Replication/FloorDropFanout | Limites de entidades e planejamento atômico das partes | Sim |
| World/SolidBuildingBlocks, Blocks, DigProfiles | Mesma capacidade determina colocação e mineração | Sim |
| Server/Composition/ServerResources | Prepara tabela de itens no boot, junto dos recursos existentes | Sim |
| tools/bedrock-data | Gerador reproduzível stack-sizes e testes independentes | Sim |

## Freeze / anti-pattern scan

- [x] No new frozen abstraction without ADR; tabelas de fatos de domínio, sem API de plugins.
- [x] No Network/ revival.
- [x] Handler/thread rules OK; nenhuma nova escrita autoritativa na rede.
- [x] Intent queue policy OK; FIFO e rollback completos preservados.
- [x] Nenhum I/O novo no tick; dados carregados no boot, fallback embarcado para testes isolados.
- [x] Sem novos campos em Player nem ampliação da lista de writers autorizados.

## DX / tests

- Novos testes em zenith.Tests: capacidades 1/16/64, bloco não empilhável, bag cheia,
  cursor/grade/baú, rollback após escrita anterior, concessão criativa, lote com
  ferramenta, redução legada, floor split/refusal e rollback de baú legado.
- Novos testes em bedrock-data.Tests: fonte por nome sem reutilizar IDs externos,
  limites inválidos e duplicação inclusive de entradas default.
- Famílias: disponibilidade de todos os nomes na paleta, colisão, perfil de mineração,
  colocar/consumir/quebrar/loot com ferramenta, sem loot com ferramenta incorreta,
  obstrução e célula ocupada sem consumo.
- Fixtures antigos de livro encantado/offhand foram ajustados para stacks legais;
  testes de identidade por aux usam flechas nomeadas em vez de IDs numéricos arbitrários.
- Registro da suíte completa: `Builds/zenith-test-runtime/vanilla-stack-blocks-final.log`.
- `dotnet test zenith.sln -c Release --no-restore -m:1`: **1.858 aprovados**, zero
  falhas/ignorados (1.549 em zenith.Tests + 309 nos projetos leaf); inclui os
  contratos de arquitetura.
- Executado o comando stack-sizes contra a fonte pública: as 288 capacidades e o
  SHA-256 gerados coincidem com o artefato embarcado. `git diff --check` sem erros
  de whitespace; avisos de normalização LF/CRLF existentes no working tree.

## Data provenance

- [items.json Bedrock 1.26.60](https://github.com/PrismarineJS/minecraft-data/blob/master/data/bedrock/1.26.60/items.json)
  (MIT): 288 exceções ao limite 64, com hash da fonte no artefato embarcado.
- [blocks.json da mesma versão](https://github.com/PrismarineJS/minecraft-data/blob/master/data/bedrock/1.26.60/blocks.json):
  dureza e ferramentas das famílias. Nenhum dado/código do Cinnabar copiado.
- Atualização de capacidade: comando documentado em tools/bedrock-data/README.md.
  Não há download de dados em runtime.

## Should not be committed

- Builds/, logs, mundos e configuração de teste. Sem commit automático.

## Suggested follow-ups

1. F08: fornalha com slots, combustível, progresso, persistência e ContainerSetData.
2. F05: estados orientados e formas especiais, por família.
3. F09/F10: arco/projéteis e sobrevivência ambiental.
