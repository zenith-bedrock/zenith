namespace Zenith.World;

/// <summary>
/// Dig duration in GameLoop ticks (20 TPS). Dragonfly/wiki BreakDuration without
/// haste/water/airborne/Efficiency (ADR §27 / §55).
/// Missing <see cref="DigProfiles"/> → returns -1 (Survival must not dig).
/// </summary>
static class BreakDuration
{
    /// <summary>Empty-hand dig; -1 if block has no dig profile.</summary>
    public static int BreakTicks(int blockRuntimeId) =>
        BreakTicks(blockRuntimeId, held: default);

    /// <summary>
    /// Dig ticks for block + held stack. Returns -1 when block has no <see cref="DigProfiles"/> entry.
    /// Instant break returns 0.
    /// </summary>
    public static int BreakTicks(int blockRuntimeId, StackId held)
    {
        Blocks.EnsureLoaded();
        DigProfiles.EnsureLoaded();
        if (blockRuntimeId == Blocks.Air)
            return 0;

        if (!DigProfiles.TryGet(blockRuntimeId, out var profile))
            return -1;

        var tool = held.IsItem ? Tools.AsTool(held.Value) : ToolInfo.None;
        var canHarvest = IsHarvestable(profile, tool);
        // Speed depends only on tool *kind* matching (Phase XXVI) — a wood pickaxe on a diamond-tier
        // block still digs faster than bare hands, it just doesn't harvest the drop. Kind-matched-
        // but-under-tier is now a real case (see MinHarvestTier); previously resetting to 1.0x here
        // was a no-op since harvestability and effectiveness both only ever checked Kind.
        var speed = IsEffective(profile, tool) ? tool.BaseMiningEfficiency : 1.0;

        var damage = speed / profile.DestroySpeed / (canHarvest ? 30.0 : 100.0);
        if (damage >= 1.0)
            return 0;

        return (int)Math.Ceiling(1.0 / damage);
    }

    public static bool IsEffective(DigProfile profile, ToolInfo tool) =>
        tool.Kind != ToolKind.None && tool.Kind == profile.EffectiveTool;

    public static bool IsHarvestable(DigProfile profile, ToolInfo tool)
    {
        if (profile.HarvestTool == ToolKind.None)
            return true;
        return tool.Kind == profile.HarvestTool && tool.Tier >= profile.MinHarvestTier;
    }
}
