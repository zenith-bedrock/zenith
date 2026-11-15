# Post-impl audit

**Scope:** develop, primeira implementação dos achados F01/F02/F03/F06/F07 da
auditoria vanilla de 2026-11-15. A árvore já continha mudanças de protocolo e
gameplay de rodadas anteriores; este relatório não declara revisão integral delas.
**Verdict:** Pass with nits.

## Blockers

- Nenhum identificado no escopo desta rodada.

## Nits (non-blocking)

- Falta teste interativo das receitas com múltiplas saídas no Cinnabar/BDS.
  O caminho de bytes ISR → handler → tick está coberto por teste automatizado.
- Limite universal de stack continua 64 (F04); lotes de crafting que excedam a
  capacidade de qualquer resultado são recusados antes do consumo de ingredientes.
- Dano base cobre mão, ferramentas por material e tridente; críticos,
  encantamentos, Strength/Weakness, desgaste e armas especiais continuam fora.

## Layer map

| Path | Role | OK? |
|------|------|-----|
| Gameplay/Inventory, RecipeRegistry | Consumo e custódia de todas as saídas; rollback completo | Sim |
| Player/PlayerCraftUi, PlayerWindows | Resultados indexados e recuperação de UI fechada sem cruzar pedidos aceitos | Sim |
| World/PlayerInventorySave, World | Bolsa e custódia transitória no mesmo registro; leitura legada | Sim |
| Session/Handler/InGameInventoryHandler | Preserva índice Create na intenção, sem mutação de inventário | Sim |
| Session/Handler/LoginSessionHandler | Restaura jogador privado antes da publicação | Sim |
| Gameplay/Entities, Survival | Dano do atacante, armadura e remoção de efeitos | Sim |
| Server/Composition/PlayerInputModule | Injeta paleta existente no combate PvP | Sim |
| zenith.Tests | Conservação, replay, rollback, persistência e combate | Sim |

## Freeze / anti-pattern scan

- [x] No new frozen abstraction without ADR; decisões registradas em ADRs 158–159.
- [x] No Network/ revival.
- [x] Handler/thread rules OK; login restaura objeto privado, gameplay continua no tick.
- [x] Intent queue policy OK; FIFO existente preservada.
- [x] Nenhum Put bloqueante ou novo fallback de armazenamento.

## DX / tests

- Testes de domínio e integração de intenção em zenith.Tests, menor projeto com
  acesso aos donos de estado; sem novos testes nas bibliotecas independentes.
- Cobertura: craft manual/automático com garrafas, Create fora de ordem, bytes ISR,
  replay, índice inválido com rollback, segunda receita com saída pendente, morte
  sem dupla contagem, desconexão/reload, bolsa cheia e recuperação repetida,
  leitura legada, envelope truncado sem commit parcial, dano por arma/espécie/PvP,
  queda com armadura e remoção de efeitos sem repetição.
- `dotnet test zenith.sln -c Release --no-restore -m:1`: 1.827 aprovados,
  zero falhas e zero ignorados (1.522 em zenith.Tests + 305 nos projetos leaf).
  Inclui testes de fronteiras de arquitetura. Registro:
  `Builds/zenith-test-runtime/vanilla-first-fixes-verified.log`.
- `git diff --check`: sem erros de whitespace (avisos de normalização LF/CRLF do Git).
- Persistência mantém a durabilidade write-behind existente; não garante sobreviver
  a queda de energia antes do flush. Saves de armadura/offhand não foram unificados.
- Servidor de teste mantido desligado; mundo existente preservado.

## Should not be committed

- Builds/, logs, bancos de mundo e configurações locais. Nenhum dado do Cinnabar
  foi copiado para código ou fixtures. Sem commit automático nesta revisão.

## Suggested follow-ups

1. F04: limites por item em todos os caminhos de inventário e criativo.
2. F05: colocação por famílias, com estados e interação reais.
3. F08: fornalha com inventário, combustível, progresso, persistência e projeção.
4. F09/F10: projéteis e sobrevivência ambiental.
