# ADR §7 — `UseBlockNetworkIdHashes = true` by default while using FNV palette IDs

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §7`.

**Choice:** Align StartGame with hash-based subchunk palettes (and spawn Y with flat spawn).

**Why:** Empirical: chunks were sent, clients still saw skybox. With the flag false, large FNV hashes were misread as legacy runtime IDs → everything air. Matching generators that “use blocks” (hash mode) fixed terrain without touching chunk send order.
