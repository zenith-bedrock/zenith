# ADR §4 — GameLoop + pending input over Actor / ECS / Scheduler

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §4`.

**Choice:** Single-threaded 20 TPS loop; freeze Actor/ECS/DI/plugin frameworks until a feature forces them.

**Why:** Bedrock servers die from accidental architecture, not from missing an actor library. Intent queues + ordered systems match how movement and block place already work. Reintroducing ECS “because industry” would dilute clarity before product need.
