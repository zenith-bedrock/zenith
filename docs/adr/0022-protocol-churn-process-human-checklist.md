# ADR §22 — Protocol churn process (human checklist)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §22`.

**Choice:** Manual review on a trimestral cadence (or when targeting a new Bedrock client), documented in [`docs/protocol-churn.md`](../protocol-churn.md). No scraper / decorative CI.

**Why:** Protocol SSOT already lives in `ServerIdentity`; the gap was process, not another code abstraction.

**Deferred:** Automated CI against remote protocol schemas.
