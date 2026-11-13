# ADR §48 — Folder layout = architecture roles (no Network/ junk drawer)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §48`.

**Choice:** Promote `Packets/`, `Protocol/`, and `Session/` to siblings under `src/zenith/` (namespaces `Zenith.Packets` / `Zenith.Protocol` / `Zenith.Session`). Relocate glue: `ProtocolGate` + `ColumnSend` → Protocol; `ZenithSessionListener` + `PlayerVisibility` → Session. Delete `Network/`. Fix Packets→domain leaks (`StartGame` version strings from Protocol; `ItemRegistryWireEntry` DTO; drop unused World using on CreativeContent). Refresh ARCHITECTURE Layout + dx workflow.

**Why:** ~⅔ of the server tree lived under `Network/`, mixing serialize / transmit / session SM — folders contradicted ARCHITECTURE roles (decide ≠ transmit ≠ serialize).

**Deferred:** Split `World/` (ItemPalette / chests → `Item/` etc.); rename type `NetworkSession`.
