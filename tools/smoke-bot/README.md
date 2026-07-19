# Zenith protocol smoke bot (ADR §58)

Node client using [bedrock-protocol](https://github.com/PrismarineJS/bedrock-protocol). Lives **in this monorepo** so the bot version pin tracks Zenith wire (`ServerIdentity`: protocol **1001** / **1.26.33**).

## Why here (not another repo / not laptop-only)

| Option | Verdict |
|--------|---------|
| `tools/smoke-bot/` in Zenith | **Chosen** — same PR can bump server + bot; CI can grow later |
| Separate git repo | Rejected — protocol drift + bus factor |
| Local-only scripts | Rejected — dies with one machine; audit anti-pattern |

Out of `src/zenith/` on purpose (Node ≠ C# graph).

## Prerequisites

1. **Node ≥ 20** + npm on the machine (`npm` was not on the agent host when scaffolded).
2. Zenith listening on UDP (default `127.0.0.1:19132`) with LAN auth that allows offline, e.g. `auth.accept` includes `offline` (sample / deploy YAML).
3. From this directory: `npm install`

## Version pin

`ZENITH_BOT_VERSION` must stay aligned with [`ServerIdentity.VersionName`](../../src/zenith/Server/ServerIdentity.cs) (today `1.26.33`).

`bedrock-protocol` may only advertise nearby builds (e.g. `1.26.30`). If join fails with a version error, try the closest supported string and record it here + in the ADR adendo — do not silently widen without noting skew.

## Commands

```bash
# terminal A
dotnet run -c Release --project src/zenith/zenith.csproj

# terminal B
cd tools/smoke-bot
npm install
npm run smoke:join
```

Env overrides:

| Variable | Default |
|----------|---------|
| `ZENITH_HOST` | `127.0.0.1` |
| `ZENITH_PORT` | `19132` |
| `ZENITH_BOT_USERNAME` | `ZenithSmoke` |
| `ZENITH_BOT_VERSION` | `1.26.33` |
| `ZENITH_SMOKE_TIMEOUT_MS` | `30000` |

Exit **0** = reached spawn/join success. Exit **1** = timeout or connection error.

## Scope now / later

- **Now:** `smoke:join` — login path honesty.
- **Later:** place / dig / chest open scripts mirroring Gate A wire steps; optional GitHub Actions job that boots Zenith then runs the bot.
- **Never (this tool):** replace human Gate A UI/mesh checks; Xbox CI auth; launcher mods.

## CI

Leaf `dotnet test` remains the PR gate (`.github/workflows/ci.yml`). This bot is **opt-in** until a dedicated workflow boots the server (ADR §58).
