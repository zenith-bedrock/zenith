# ADR §58 — Protocol smoke bot — separate Bun repo (not inside Zenith C#)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §58`.

**Choice:**

1. **Location:** dedicated repo [`zenith-bedrock/zenith-smoke-bot`](https://github.com/zenith-bedrock/zenith-smoke-bot) (sibling checkout). **Not** under `zenith/tools/` or `src/zenith/` — keeps the server tree C#-only (no Node/TS mixed into product PRs).
2. **Stack:** [Bun](https://bun.sh) + Prismarine [`bedrock-protocol`](https://github.com/PrismarineJS/bedrock-protocol). Offline/`self-signed` LAN auth for local + future CI. **Not** Mineflayer (Java). **Not** Bedrock Launcher mods as the CI path.
3. **Role:** automate **repetitive wire smokes** (join → InGame, later place/break/chest open). Complements human Gate A; does **not** replace official-client UI/mesh/crash checks.
4. **Boundary:** bot is a normal Bedrock client. No references into Zenith Gameplay/World; Zenith does not depend on the bot.
5. **CI:** Zenith leaf `dotnet test` stays the PR gate. Bot is **opt-in** until a job boots Zenith + runs join. Do not block Zenith merges on bot harness immaturity.
6. **Version pin:** `createClient({ version: "1.26.30" })` — minecraft-data protocol **1001**, matches Zenith `ServerIdentity.ProtocolVersion`. Zenith `VersionName` is `1.26.33` (same protocol id; document skew in bot README).

**Why:** In-monorepo `tools/smoke-bot/` mixed JS into a C# product and was rejected for DX/review clarity. A second repo is acceptable when the tool is a different language and must not pollute server PRs; pin docs + README keep protocol skew honest.

**Supersedes:** brief in-monorepo spike on `develop` (`9fa6f29`) — removed; use the dedicated repo.

**MVP:** `bun run smoke:join` — connect offline → spawn → exit 0/1. First-wave #2–#10 (`place` … `two-client` + `graceful-persist`) live in the same repo (`bun run smoke:first10`).

**Non-goals:** full Mineflayer API; Xbox auth in CI; launcher injection; replacing human Gate A for `v0.0.2-alpha`; embedding Node in the Zenith solution.
