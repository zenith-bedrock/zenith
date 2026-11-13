# ADR §19 — Chests — deferred sketch (shipped as §28 MVP)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §19`.

When held sync + §17 smoke are stable: (sketch retained; **MVP landed in §28** — RAM store + container 7 + flat `100+` outside `PlayerInventory`; LevelDB `ct:` / double-chest still out.)

1. `ChestStore` RAM `dict[(x,y,z)] → slots[27]`; hydrate at World boot; Put `ct:x:y:z` async (never sync Get on open/tick).
2. Open/close intents; transfers via explicit dual-store APIs (not flat `100+` inside `PlayerInventory`); rollback snapshots both stores.
3. Wire: ContainerOpen type 0; ISR container **7** ↔ chest 0–26; window-scoped net ids.
4. Break: dump all-or-nothing `TryAdd` else abort.
5. Out: double-chest, Mojang BlockActor, hopper, forge, CreativeContent.
