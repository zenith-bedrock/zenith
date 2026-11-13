# ADR §36 — Store honesty (warn + floor refuse-cap)

> Part of the [decision log](README.md). Code and docs cite this record as `ADR §36`.

**Choice:**

1. **Overlays** (`World._blockOverrides`): SoftCap `10_000` on **new** keys (`TrySetBlock`); compact when rid == flat base; warn once at refuse. Hydrate bypasses SoftCap.
2. **ChestStore:** SoftCap `10_000` on **new** cells (`TryEnsure`); hydrate bypasses SoftCap.
3. **FloorDropStore:** SoftCap `2048` — `TryAddOrMerge` refuses **new** cell keys at cap + Warning once; merging into an existing same-item cell still succeeds.

**Why:** Rule 7 — unbounded overlay/chest RAM under grief is a production DoS, not “known debt to ignore.” Compact-to-base keeps honest dig of restored sky cells without inventing eviction I/O on tick. Dig of virgin terrain at SoftCap may refuse — same honesty class as floor SoftCap.

**Deferred:** Blocks→Context; BoundedStore / VisibilitySystem; overlay eviction with I/O on tick / column rewrite.

**Adendo (jul 2026 — column index):** Secondary `_overlaysByChunk` map updated only via `StoreOverlay` / `RemoveOverlay` (same path as LevelDB hydrate/delete). `GetOverlaysInColumn` is O(bucket) instead of scanning all overlays. Flat `_blockOverrides` remains SSOT for `GetBlock` / `OverrideCount`. Compact-to-base removes keys; SoftCap refuses new keys (§36 SoftCap shipped adendo). Not zero-alloc — result list is still O(column size).

**Adendo (jul 2026 — overlay SoftCap deferred):** Audit pushed SoftCap-on-overlays like FloorDrop. **Not shipped:** Survival break of base terrain inserts a new air overlay key — SoftCap refuse would silently block dig after ~10k edits. Overlays stay **warn-only** until an eviction/compaction ADR (possibly drop air-overlays that match base). Floor SoftCap `2048` unchanged.

**Adendo (jul 2026 — overlay SoftCap shipped):** Prior deferral was too kind to grief. **Shipped:**

1. `World.OverrideSoftCap` (`10_000`): refuse **new** overlay keys; overwrite existing always OK.
2. **Compact-to-base:** writing rid == flat `SampleBaseBlock` removes the overlay key + `DeleteOverlayAsync` (frees SoftCap budget; place→break on sky cells no longer accumulate).
3. Dig of virgin base that needs a new air key **can** SoftCap-refuse after cap — honest LAN bound (resync, no orphan dumps). BlockSystem checks `CanAcceptBlockWrite` / `TrySetBlock` before chest dump / place consume.
4. **ChestStore.SoftCap** (`10_000`): `TryEnsure` refuses new cells; hydrate bypasses SoftCap.
5. Floor SoftCap `2048` unchanged. Full eviction / column rewrite still Deferred.

**Process (audit refresh):** dirty tree ahead of `origin` after closing a hygiene gap is a **process failure**, not WIP — commit+push same day. Leaf CI on push/PR is required; Bedrock E2E CI remains beta-hard (see [`robustness-dx-debt.md`](../robustness-dx-debt.md)).
