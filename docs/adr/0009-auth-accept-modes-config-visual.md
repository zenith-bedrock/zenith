# ADR §9 — Auth accept modes (config visual)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §9`.

**Choice:** `auth.accept` YAML list of named modes: `xbox` | `self-signed` | `offline`. Default LAN = all three + boot WARNING. Public = `[xbox]` only (strict chain checks). `offline` is parse fallbacks only — does not open AuthenticationType SELF_SIGNED. Obsolete `require-chain-signatures` aborts boot with actionable message (no auto-migrate / rewrite).

**Why:** Bool soft/hard mixed “who may join” with crypto. Ops need a readable multi-select. Loud fail on bad YAML matches gamemode / unmatched-key DX.
