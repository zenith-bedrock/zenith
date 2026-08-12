# Diagnostics de runtime

`libs/diagnostics` é a fundação interna de profiling e health do Zenith. Ela transforma os
indicadores pontuais de runtime em uma captura estruturada que permite investigar, sem adicionar
`Console.WriteLine` ou instrumentação ad-hoc, se uma degradação vem do tick, de um sistema, de
alocações/GC, do fan-out de pacotes ou do transporte.

Não é um pacote público, uma plataforma de métricas, um dashboard, nem um framework de tracing.
Para a decisão e os limites arquiteturais, veja [ADR §104a](decisions.md#104a-diagnostics-is-a-fixed-layout-leaf-not-a-server-metrics-platform).

## Limites e ownership

```text
GameLoop                   Session                   RakNet
  └─ timings                  └─ packet counts          └─ datagram facts
           \                    |                    /
            \                   |                   /
             └──── Zenith.Diagnostics ──────────────┘
                         │
                         └─ snapshot de leitura → console / JSON
```

- A leaf `Zenith.Diagnostics` não referencia `zenith`, Gameplay, Protocol, Packets ou RakNet.
- `GameLoop` mede o tick completo e os sistemas já registrados; não passa a decidir gameplay.
- `NetworkSession` conta game packets e bytes no seu boundary, sem alterar o protocolo.
- `RakNetServer` continua sendo a fonte dos fatos de datagramas e bytes UDP.
- `ServerRuntimeDiagnostics` é o adaptador em `Server/` que dá nomes concretos às métricas e
  amostra fatos ao fim do tick. A leitura ocorre via `ZenithServer.Diagnostics`.

A instrumentação não cria uma nova fila, scheduler, runtime manager ou modelo de ownership. Ela
apenas observa fatos depois de eles terem sido decididos ou transmitidos.

## Modelo de métricas

O layout é definido uma vez no composition root com `DiagnosticsBuilder`. Cada definição produz
um handle pequeno (`CounterMetric`, `GaugeMetric` ou `TimingMetric`) que aponta para uma posição
em arrays pré-alocados. Depois de `Build()`, o layout é fixo: não há discovery de métricas,
reflection, strings dinâmicas ou registry mutável no caminho quente.

| Tipo | Operação | Semântica |
|------|----------|-----------|
| Counter | `Increment` | Total monotônico, por exemplo pacotes enviados |
| Gauge | `Set` | Última observação, por exemplo players ou TPS |
| Timing | `Begin`/`Dispose` ou `Record` | Última duração, amostras, total e máximo |

Timings podem declarar um pai já definido. Hoje a árvore principal é `tick` →
`tick.system.<nome>`, permitindo identificar o custo de um sistema sem perder o custo total do
tick.

```csharp
var builder = new DiagnosticsBuilder();
var tick = builder.Timing("tick");
var movement = builder.Timing("tick.system.movement", "tick");
var players = builder.Gauge("gameplay.players");
var packets = builder.Counter("network.packets.sent");
var diagnostics = builder.Build();

using (diagnostics.Begin(tick))
using (diagnostics.Begin(movement))
{
    // trabalho já pertencente ao writer atual
}

diagnostics.Set(players, 3);
diagnostics.Increment(packets);
```

## Garantias do hot path

Após a construção do layout, registrar métricas:

- não aloca;
- não usa locks;
- não faz I/O, callbacks, serialização ou reflection;
- não retém estado de domínio;
- usa operações atômicas em arrays, adequadas para os writers atuais de gameplay e rede.

`CaptureSnapshot()` é deliberadamente a fronteira de leitura: ela aloca um objeto de snapshot
imutável e lê os arrays atomicamente. Portanto exportar não deve acontecer dentro de um sistema
de gameplay ou no send path.

Os valores de uma snapshot são uma amostra coerente por métrica, não uma transação global entre
todas as métricas. Isso é correto para observabilidade e evita bloquear writers.

## Cobertura atual do servidor

| Área | Métricas |
|------|----------|
| Tick | duração, TPS, ticks acima de 50 ms e timings por sistema |
| Gameplay | jogadores, atores, overlays de chunk e número de sistemas |
| Runtime | alocações da thread do tick, heap managed, coleções Gen0/1/2 |
| Protocol/session | packets e bytes de game packet enviados/recebidos |
| Transporte | datagramas UDP enviados/recebidos e banda estimada in/out |

`tick.tps` é armazenado multiplicado por 1.000 para preservar três casas decimais em um gauge
inteiro. `network.bandwidth.*` é uma estimativa em bytes por segundo entre duas amostras de
fim-de-tick; ela não substitui os contadores acumulados de RakNet.

## Ler e exportar

O servidor expõe o runtime somente para leitura:

```csharp
var snapshot = server.Diagnostics.CaptureSnapshot();
var console = snapshot.ToConsole();
var json = snapshot.ToJson();
```

`ToConsole()` dá uma visão humana curta. `ToJson()` produz uma representação estruturada com
timestamp, frequência do `Stopwatch` e, para cada métrica, nome, tipo, pai, valor, contagem,
total e máximo. A camada ainda não grava arquivos nem expõe HTTP: o consumidor operacional que
vier a existir deve escolher seu próprio boundary de I/O.

## Comparação, categorias e incidentes

Cada métrica pertence, no layout definido em build, a uma das categorias `Tick`, `Runtime`,
`Gameplay` ou `Network`. A categoria é parte da snapshot e não uma classificação inferida por
reflection durante o export. Timings de sistema seguem sob `Tick`, mantendo a árvore:

```text
Tick      tick, tick.system.*
Runtime   runtime.allocations.*, runtime.gc.*, runtime.managed-bytes
Gameplay  gameplay.players, gameplay.actors, gameplay.chunks.*
Network   network.packets.*, network.packet-bytes.*, network.datagrams.*, network.bandwidth.*
```

`DiagnosticsComparison.Create(baseline, current)` compara snapshots somente no cold path. Para
timings, compara a média das amostras; para counters/gauges, compara o valor observado. O resultado
tem `ToConsole()` e `ToJson()`, além de um diagnóstico provável que correlaciona aumento de tick
com GC, pressão de rede, crescimento de atores ou o maior timing de sistema.

O `DiagnosticsIncidentBuffer` preserva as últimas oito amostras raw em um ring buffer
pré-alocado. O servidor grava uma amostra a cada segundo e sempre que um tick excede 50 ms. Copiar
os arrays para esse buffer não aloca; `CaptureRecent()` só materializa snapshots quando alguém lê
o incidente. Assim um spike conserva o contexto recente sem transformar o tick lento em uma
operação de serialização.

## Comando operacional

O command core protocol-independent já existente expõe uma superfície concreta de DX, sem hooks
de plugin ou command framework adicional:

| Comando | Efeito |
|---------|--------|
| `/diagnostics snapshot` | Captura e substitui o baseline de comparação |
| `/diagnostics compare` | Compara o estado atual com o baseline e mostra a hipótese principal |
| `/diagnostics tick` | Mostra os timings do último tick mais caros |
| `/diagnostics incident` | Mostra a última evidência retida: tick, top system, atores e packet bytes |

Esses comandos somente leem diagnostics. Não submetem intents, não mutam gameplay e não exigem
ordering contra o `GameLoop`; por isso a execução direta no handler de comando é adequada sob o
heurístico da [ADR §97](adr/0097-runtime-execution-model.md).

## Diagnóstico prático

Para a pergunta “por que o servidor está lento?”, capture uma ou mais snapshots enquanto o
problema acontece e compare:

1. `tick` e `tick.over-budget`: confirma pressão no budget de 50 ms.
2. `tick.system.*`: localiza o sistema que explica o custo do tick.
3. `runtime.allocations.thread`, `runtime.managed-bytes` e `runtime.gc.*`: separa pressão de GC
   de custo de lógica.
4. `network.packets.*`, `network.packet-bytes.*` e `network.bandwidth.*`: indica fan-out ou
   egress elevado.
5. `network.datagrams.*`: confirma se a pressão também chegou ao transporte UDP.

Os números não substituem profiling de CPU/memória quando for necessário, mas tornam explícito
qual hipótese deve ser investigada primeiro.

## Verificação e overhead

Os testes em `libs/diagnostics.Tests` verificam counters/gauges/timings/hierarquia, exports e que
o registro e a cópia para o buffer de incidente não alocam após aquecimento. Eles também cobrem
comparação, categorias, hipótese de actor pressure e ordem/capacidade do ring buffer.
`GameLoopTests` confirma que o tick inteiro e um sistema registrado recebem timings.

Para comparar a operação mínima sem diagnostics contra o registro de counter + gauge + timing:

```powershell
dotnet build src/zenith.Benchmarks/zenith.Benchmarks.csproj -c Release
dotnet run --project src/zenith.Benchmarks/zenith.Benchmarks.csproj -c Release --no-build -- --filter *DiagnosticsOverhead* --job short
```

O benchmark mede baseline, counter, gauge, timing, a combinação do tick, captura de snapshot e
comparação. Rode isso em uma máquina sem carga concorrente e trate números como evidência
específica daquela máquina/versão. O requisito permanente é que counter/gauge/timing e a cópia de
incidente mantenham zero alocações e que qualquer custo de CPU seja pequeno e mensurado antes de
aumentar instrumentação.

## Findings de referências

- O runtime .NET também distingue medidas de taxa e valores de snapshot; seguimos a mesma ideia
  conceitual com counters e gauges, mas sem publicar EventCounters ou um endpoint externo.
  [Referência .NET](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/event-counters)
- Os contadores de runtime do .NET incluem allocation rate, heap e coleções Gen0/1/2, que
  confirmam a utilidade desses sinais para investigação de primeira linha. O Zenith mantém suas
  leituras locais e associadas ao tick, em vez de criar integração de monitoring.
  [Catálogo System.Runtime](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/available-counters)
- `ActivitySource`/distributed tracing modela spans e propagação entre processos; isso é um
  problema diferente do diagnóstico local de tick e foi conscientemente mantido fora desta fase.
  [Conceitos de tracing](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-concepts)
- Servidores Bedrock maduros, como [PocketMine-MP](https://github.com/pmmp/PocketMine-MP), são
  referência de operação, não modelo arquitetural: Zenith preserva writer único, boundaries e uma
  biblioteca leaf sem plugin surface.

## Não objetivos desta etapa

- Prometheus, OpenTelemetry ou tracing distribuído;
- endpoint HTTP, dashboard ou polling API;
- plugin/public diagnostics API;
- métricas criadas dinamicamente em runtime;
- locks globais, reflection ou I/O no hot path;
- alterar quem possui e muta estado de gameplay.
