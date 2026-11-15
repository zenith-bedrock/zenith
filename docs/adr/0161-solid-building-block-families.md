# ADR §161 — Solid building block families

Extend placement to named full-cube families whose existing collision and self-drop
model is applicable: planks, wool, concrete, terracotta, and selected stone/bricks.
SolidBuildingBlocks maps these names to dig profiles. Blocks placement and
DigProfiles fallback share that table; supporting placement must also support
survival breaking and tool-gated drops. This is a bounded capability table, not an
accept-any-palette gate or a block/plugin registry.

Only listed names are admitted; stairs, slabs, doors, beds, fluids and stations
remain separate work. Wool uses correct hand breaking and self-drop; shears-speed
support remains deferred with the missing shears tool profile. Existing special
block behavior is unchanged. Hardness/tool facts were checked against the
PrismarineJS minecraft-data Bedrock 1.26.60
[blocks.json dataset](https://github.com/PrismarineJS/minecraft-data/blob/master/data/bedrock/1.26.60/blocks.json) (MIT).

Verify palette availability, place consumption, obstruction/occupied-cell refusal,
survival break authorization, correct-tool loot, wrong-tool no-loot and persistence.
