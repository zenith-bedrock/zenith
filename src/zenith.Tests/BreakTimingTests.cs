using Zenith.Protocol;
using Zenith.Player;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

public class BreakTimingTests
{
    public BreakTimingTests() => Blocks.EnsureLoaded();

    [Fact]
    public void BreakTicks_empty_hand_matches_hardness_table()
    {
        Assert.Equal(0, Blocks.BreakTicks(Blocks.Air));
        Assert.Equal(15, Blocks.BreakTicks(Blocks.Dirt));
        Assert.Equal(15, Blocks.BreakTicks(Blocks.Sand));
        Assert.Equal(18, Blocks.BreakTicks(Blocks.GrassBlock));
        Assert.Equal(60, Blocks.BreakTicks(Blocks.OakPlanks));
        Assert.Equal(60, Blocks.BreakTicks(Blocks.OakLog));
        Assert.Equal(75, Blocks.BreakTicks(Blocks.Chest));
        Assert.Equal(150, Blocks.BreakTicks(Blocks.Stone));
    }

    [Fact]
    public void CrackEventData_matches_progress_per_tick_scale()
    {
        Assert.Equal(Blocks.CrackProgressMax, Blocks.CrackEventData(0));
        // Truncating division (PM/DF) — Round would finish crack early for many tick counts.
        Assert.Equal(4369, Blocks.CrackEventData(15)); // 65535/15
        Assert.Equal(8191, Blocks.CrackEventData(8));  // shovel dirt — not Round(8192)
        Assert.Equal(3640, Blocks.CrackEventData(18)); // grass — not Round(3641)
        Assert.Equal(436, Blocks.CrackEventData(150)); // stone hand — not Round(437)
    }

    [Fact]
    public void CrackEventData_div_model_never_finishes_before_break_ticks()
    {
        // Client effective duration ≈ floor(65535 / EventData). Must be >= BreakTicks.
        for (var need = 1; need <= 200; need++)
        {
            var data = Blocks.CrackEventData(need);
            var clientTicks = Blocks.CrackProgressMax / data;
            Assert.True(
                clientTicks >= need,
                $"need={need} data={data} clientTicks={clientTicks} — crack would finish early");
        }
    }

    [Fact]
    public void CrackEventData_round_candidate_is_rejected_when_early()
    {
        // Historical bug: Round(65535/8)=8192 → floor(65535/8192)=7 < 8.
        const int need = 8;
        var rounded = (int)Math.Round(Blocks.CrackProgressMax / (double)need);
        Assert.True(Blocks.CrackProgressMax / rounded < need);
        Assert.True(Blocks.CrackProgressMax / Blocks.CrackEventData(need) >= need);
    }

    [Fact]
    public void BeginBreak_same_cell_keeps_started_tick_when_not_restarted()
    {
        var player = new Player.Player("miner", session: null!, runtimeId: 1, uuid: Guid.NewGuid());
        player.BeginBreak(1, 2, 3, tick: 100, requiredTicks: 60);
        Assert.True(player.IsBreakTarget(1, 2, 3));
        Assert.Equal(100ul, player.BreakStartedTick);
        Assert.Equal(60, player.BreakRequiredTicks);

        // Same-cell continue must not call BeginBreak again (handler contract).
        Assert.True(player.IsBreakTarget(1, 2, 3));
        Assert.Equal(100ul, player.BreakStartedTick);
    }

    [Fact]
    public void BeginBreak_new_cell_replaces_target()
    {
        var player = new Player.Player("miner", session: null!, runtimeId: 1, uuid: Guid.NewGuid());
        player.BeginBreak(1, 2, 3, tick: 10, requiredTicks: 15);
        player.BeginBreak(4, 5, 6, tick: 20, requiredTicks: 150);
        Assert.True(player.IsBreakTarget(4, 5, 6));
        Assert.Equal(20ul, player.BreakStartedTick);
        Assert.Equal(150, player.BreakRequiredTicks);
    }
}
