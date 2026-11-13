# ADR §23 — Zenith.LevelDB robustness tests + flush fault hook

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §23`.

**Choice:** Keep ZLDB / RAM constraint. Add tests for mid-flush (orphan `.ldb`, `CURRENT` still old), missing/corrupt `CURRENT`, truncated WAL, and Close vs Put contract. Internal hook `AfterTableWriteBeforeCurrent` between table write and CURRENT publish for deterministic mid-flush simulation (`InternalsVisibleTo` already present).

**Why:** README already promised crash behavior; six happy-path tests did not prove it.

**Deferred:** WAL CRC framing; subprocess crash tests as primary CI mechanism.
