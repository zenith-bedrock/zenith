# ADR §50 — Split log levels (server vs RakNet)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §50`.

**Choice:** Two `Logger` instances at composition root (`ZenithServer`), levels from `zenith.yml` `log.server` / `log.raknet`. Aliases (`none`|`info`|`warn`|`error`|`debug`|`all`) map to **severity ladders** on the existing flag enum (`info` = Info|Warning|Error). Defaults ops-first: server `info`, raknet `warn`. YAML without a `log:` block keeps C# defaults (no rewrite of operator files). No Serilog, no categories, no call-site changes in `raknet`.

**Why:** Shared `LogLevel.All` made Bedrock Debug unusable — datagram spam drowned join/place. Transport is mature; gameplay is the active debug surface. Rule 7: two loggers beat a logging framework.

**Smoke:** Default boot — join Info, no `Connected PID` flood. `log.raknet: debug` + `log.server: info` → only transport Debug.
