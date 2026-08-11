# ADR §97 — Choose Execution Model by Behavior, Not by Available Abstraction

> Espelha `docs/decisions.md` §97 (log canônico de ADRs). Este arquivo existe como documento standalone porque o conteúdo é normativo/duradouro (um heurístico de decisão), não uma entrada de "feature shipped" como as demais entradas de `decisions.md`. Em caso de divergência, `decisions.md` §97 é a entrada oficial; este arquivo é a versão longa.

## Context

Uma revisão do runtime encontrou nove `IGameSystem` e vários fluxos de estado pendente, lock e fila. A pergunta não era se sistemas são "bons" ou "ruins", mas qual comportamento realmente precisa de tick. Ao questionar `Chat`, `GameMode` e `Equipment`, fica fácil trocar uma rigidez por outra: sair de "tudo é `IGameSystem`" para "tudo que não precisa de tick vira `Command`/`Event`/`Service`". Nenhuma das duas é uma heurística útil.

O Zenith tem hoje, e vai ter cada vez mais conforme mobs/AI/combate/redstone/fluidos chegarem, tipos de trabalho com semânticas genuinamente diferentes:

```text
continuous simulation       (Movement, Gravity)
discrete player actions     (place, break, chat, gamemode)
state transitions           (gamemode, respawn)
scheduled work              (gravity's deferred cascade, future block ticks)
async completion            (chunk I/O, future world-gen/pathfinding)
replication                 (movement pose, equipment diff)
protocol synchronization    (time sync)
```

Forçar tudo através de `IGameSystem.Tick()` gera polling, estado temporário, locks e ordering implícito onde não são necessários. Forçar tudo para `Command`/`Event`/`Service` gera a mesma fricção com outros nomes: dispatcher, registry e estado intermediário em vez de uma operação direta. As implementações de referência estudadas (PocketMine, Minestom, Dragonfly e Cuberite) convergem no princípio prático: uma transição discreta usa o mecanismo mais direto que preserva as garantias de autoridade, ordering e teste de que ela precisa.

## Decision

**A unidade de execução é escolhida pelo comportamento real da feature, não pela abstração disponível.** Isso vale nos dois sentidos:

- `IGameSystem` é apropriado quando a responsabilidade precisa de avaliação/simulação periódica, ou de ordering determinístico dentro do tick contra outras mutações de estado autoritativo.
- Uma operação pode ser uma **chamada direta e imediata** (`Handler → Gameplay Runtime API → Domain operation`) quando não há nenhuma dessas necessidades — desde que thread ownership, autoridade e ordering continuem corretos. O Zenith já tem um exemplo real e correto disso: `Player.SelectedHotbarSlot` é escrito diretamente pelo handler (sem fila, sem lock) porque é um escalar único, idempotente e auto-corretivo — documentado como exceção deliberada em ADR §80. Esse é o *template*, não a exceção que deveria incomodar.
- `Command`, `Event`, `Dirty flag`, `Scheduler`, `Dispatcher` são mecanismos válidos **quando o comportamento realmente pede** desacoplamento (múltiplos consumidores), reação a algo que já aconteceu, ou execução futura agendada — não como padrão automático para "coisa discreta que não é Movement".

### Decision heuristic (guidance, não regra absoluta)

```text
Does it need continuous/tick evaluation (accumulated state, physics, determinism vs other systems)?
    YES → IGameSystem
    NO
      ↓
Does it represent client intent requiring authoritative validation before it can be trusted?
    YES → Intent (pending state consumed on tick) — mas só se a validação/mutação
           realmente precisar do single-writer do tick (ver invariantes abaixo).
           Caso contrário, considere uma runtime API síncrona chamada direto do handler.
    NO
      ↓
Is it a discrete internal state transition with side effects that must be ordered
against other authoritative state (persistence, peer-visible fields)?
    YES → direct runtime operation on the gameplay thread (via tick-consumed
           pending state, or an immediate call if no cross-player ordering is at stake)
    NO
      ↓
Is it reacting to something that already happened, with multiple independent consumers?
    YES → domain event (EventBus já existe, ADR §21/§78) — só se houver ≥2 consumidores reais
    NO
      ↓
Does it need to run at a specific future tick/time, independent of any input?
    YES → scheduled work (fila com dedup+cap, no molde de GravityPendingStore)
```

Este flowchart é guidance para a pergunta "qual mecanismo", não uma tabela de lookup obrigatória — a seção seguinte lista as perguntas comportamentais que devem ser respondidas antes de aplicar o flowchart.

### Perguntas obrigatórias antes de escolher o mecanismo

1. O que dispara este comportamento?
2. Quem é o dono do estado autoritativo?
3. Precisa de avaliação contínua (acumula estado entre chamadas)?
4. Ordering contra outro trabalho de gameplay importa?
5. Precisa de estado temporário, ou pode executar direto?
6. Precisa de sincronização cross-thread — e essa concorrência é inerente ao problema (I/O real-time, chegada assíncrona de rede) ou foi criada pelo próprio desenho (duas abstrações que não precisavam se falar por fila)?
7. Pode executar imediatamente/sincronamente dentro do runtime de gameplay?
8. Uma abstração (Command/Event/Dispatcher/Scheduler) é justificada por **casos de uso reais concretos hoje** — não hipotéticos — ou por um único caso que caberia numa chamada direta?
9. Como isso será testado? Precisa de um seam (interface substituível) ou um teste direto do método já basta?
10. Como a referência externa relevante (PocketMine/Dragonfly/Minestom/Cuberite) resolve o mesmo comportamento — e essa solução é aplicável, parcialmente aplicável, ou inadequada dadas as restrições do Zenith (single-thread, C#, sem ECS)?

## Invariantes estritos (não-negociáveis) vs. mecanismos flexíveis

Esta é a distinção central que este ADR formaliza — **repetir isto em `ARCHITECTURE.md`/`AGENTS.md`**:

```text
STRICT (nunca trocar sem uma feature concreta forçando e um novo ADR):
  - Transport (RakNet) não decide gameplay.
  - Serialization (Packets) não conhece regras de domínio.
  - Packets são representação de wire, nada mais.
  - Handlers de rede não se tornam implementação de gameplay
    (podem decodificar, validar segurança do dado, e chamar uma API do runtime
     de gameplay — não podem *ser* a decisão de gameplay).
  - Mutação de estado autoritativo tem dono claro e normalmente um único escritor no
    contexto de gameplay que a ordena (hoje, o tick para Player/World/inventário;
    exceções devem ser explícitas e justificadas).
  - Thread ownership e concorrência são explícitos — produtores de rede/I/O publicam
    intents ou conclusões imutáveis em boundaries limitados; não recuperam autoridade
    para mutar estado autoritativo após o handoff. `single-writer` não torna o servidor
    inteiro single-threaded: transporte, compressão, storage, caches e métricas podem
    continuar concorrentes.
  - Locks/atomics concentram-se nesses boundaries; nunca são mantidos durante await,
    I/O, envio de protocolo ou callback externo, nem autorizam múltiplos writers de
    um estado composto de gameplay.
  - Correctness > uniformidade estética de código.

FLEXIBLE (escolher por comportamento, revisar quando a feature mudar):
  - IGameSystem
  - Command
  - Event
  - Intent / pending state
  - Queue
  - Dirty flag
  - Scheduler
  - Direct runtime call
  - Dispatcher
  - Service
```

Nenhum item da lista FLEXIBLE deve virar regra universal ("toda feature discreta é um Command") do mesmo jeito que "toda feature de gameplay é um `IGameSystem`" não deveria ser regra universal — esse é exatamente o erro que este ADR existe para prevenir nos dois sentidos.

Da mesma forma, single-writer é uma regra de **ownership de gameplay autoritativo**, não uma
exigência de que todo trabalho execute no `GameLoop`. Antes de criar um handoff, provar que a
operação precisa mutar/consultar essa autoridade ou ser ordenada deterministicamente com ela;
caso contrário rede, storage, I/O, compressão, logging, métricas e caches executam no seu contexto
natural. Antes de abrir uma exceção de mutação fora do owner, documentar por que o estado não é
autoritativo ou por que a exceção preserva sua corretude.

## Consequences

**Positive**
- Menos polling artificial e menos estado temporário existindo só para atravessar uma boundary.
- Menos locks criados pela arquitetura em vez de pelo problema (ex.: o exemplo real já existente de `SelectedHotbarSlot` prova que isso já funciona no Zenith).
- Latência menor quando execução imediata é válida (ex.: uma futura ability toggle não precisa esperar o próximo tick se for same-session e idempotente).
- Semântica melhor — cada mecanismo escolhido comunica *por que* aquele comportamento existe daquele jeito, em vez de "porque é assim que Systems funcionam aqui".
- Abstrações (Command, Event, Scheduler genérico) só nascem quando 2-3 casos reais comprovarem o padrão — evita over-engineering nos dois sentidos (Systems-para-tudo e Commands/Events-para-tudo).

**Negative**
- O runtime terá mais de um modelo de execução coexistindo (Systems + Intents + chamadas diretas + eventualmente Events/Scheduled work) — isso exige disciplina: quem revisa uma PR precisa entender por que aquele mecanismo foi escolhido, não só copiar o padrão do arquivo vizinho.
- Ordering entre mecanismos diferentes precisa ser entendido explicitamente (ex.: uma chamada direta de handler não tem a garantia de "depois do Movement deste tick" que um System tem — se isso importar, a feature não deveria ser uma chamada direta).
- Menos uniformidade superficial — nem todo "input do jogador" vai parecer com os outros no código. Esta perda é aceitável quando resulta em corretude semântica maior; não é aceitável se usada como desculpa para inconsistência não-documentada (toda exceção ao padrão-padrão precisa de um comentário explicando por quê, no molde do que já existe para ADR §80).

## Status

Esta decisão orienta mudanças novas e revisões de código. As regras de camada e de ownership resumidas aqui também aparecem em `ARCHITECTURE.md`; `ArchitectureBoundaryTests` protege automaticamente as dependências de camada que podem ser verificadas por código. A escolha concreta de mecanismo continua sendo uma decisão de feature, registrada em ADR quando introduzir uma camada ou compromisso novo.
