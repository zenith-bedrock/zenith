# ADR §154 — Ground loot, table-grid admission and player melee reach

**Context:** A client test exposed bag-first block loot, a table grid that could be seeded by
tests but not written by normal transfers, and targeted melee retained outside an AI-sized radius.

**Choice:** Accepted block breaks publish loot to FloorDropStore first, with its existing pickup
delay. Preflight all chest contents and block loot before removing the source; no inventory
fallback silently changes this behavior. Preserve tool, creative, gamerule and capacity rules.
Accept TableCraftGrid only in its matching live table session; bind recipe/result actions to
the active/pending personal or table crafting view, preserving generation/replay checks.

Player melee uses eye-to-target-AABB distance, with player reach independent of mob AI attack
distance. Domain mob dimensions feed combat and projection. An explicit attack is consumed
even when out of reach, so it cannot become a delayed hit after the target walks closer.
Hit-invulnerability timing remains unchanged. No new ECS component, dispatcher or combat framework.

**Mode follow-up:** Distinguish survival's 3-block actor reach from creative's
7-block cap, matching the inspected Cinnabar capability definitions. Client picking
has additional input-mode/ray constraints; this does not claim measured BDS server
acceptance tolerances or exact line-of-sight/interpolation parity.

**Verification:** Floor-before-pickup and capacity refusal without loss; table transfers and
crafting through handler+tick, stale/cross-window rejection; reach at hitbox boundary, vertical
separation and no later damage from an old rejected click. Real-client follow-up remains required.
