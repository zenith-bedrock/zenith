using Xunit;
using Zenith.Ecs;
using Zenith.Gameplay.Entities;
using Zenith.Gameplay.Policy;
using Zenith.Modules.Protect;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>Plan 002, phase 1: the environment policy seam, its fault handling, and the first writer that asks it (explosions).</summary>
public sealed class EnvironmentPolicyTests
{
    public EnvironmentPolicyTests() => Blocks.EnsureLoaded();

    private const float Radius = 3f; // CreeperSystem.ExplosionRadius

    private sealed class Throws : IEnvironmentChangePolicy
    {
        public PolicyDecision Evaluate(in EnvironmentChangeProposal proposal) => throw new InvalidOperationException("boom");
    }

    private sealed class Denies(string reason) : IEnvironmentChangePolicy
    {
        public PolicyDecision Evaluate(in EnvironmentChangeProposal proposal) => PolicyDecision.Deny(reason);
    }

    // ---- the set ----

    [Fact]
    public void The_first_denial_wins_and_an_empty_set_allows()
    {
        var policies = new EnvironmentChangePolicies();
        Assert.True(policies.Allows(EnvironmentCause.Fire, 1, 0, 0, 0, 1, 0));

        policies.Register(new Denies("first"));
        policies.Register(new Denies("second"));
        var decision = policies.Evaluate(new EnvironmentChangeProposal(EnvironmentCause.Fire, 1, new BlockCellChange(0, 0, 0, 1, 0)));
        Assert.False(decision.IsAllowed);
        Assert.Equal("first", decision.Reason);
    }

    [Fact]
    public void A_policy_that_throws_denies_and_the_log_stays_readable()
    {
        var log = new RecordingLogger();
        var policies = new EnvironmentChangePolicies(log);
        policies.Register(new Throws());
        policies.Freeze();

        for (var i = 0; i < 2500; i++)
            Assert.False(policies.Allows(EnvironmentCause.Explosion, 1, i, 0, 0, 1, 0));

        // The first three in full, then one line per thousand: 3 + 2, not 2500.
        Assert.Equal(5, log.Lines.Count(line => line.Contains("Throws failed")));
        Assert.Contains(log.Lines, line => line.Contains("(1000 so far)"));
    }

    [Fact]
    public void The_set_is_frozen_after_composition()
    {
        var fx = new IntentTestFixture();
        var policies = new EnvironmentChangePolicies();
        policies.Register(new Denies("x"));
        policies.Freeze();
        Assert.Throws<InvalidOperationException>(() => policies.Register(new Denies("late")));

        // The composed server carries Protect's veto.
        Assert.True(fx.Context.EnvironmentPolicies.Count >= 1);
    }

    [Fact]
    public void The_block_edit_and_container_sets_use_the_same_bounded_log()
    {
        var log = new RecordingLogger();
        var blocks = new BlockEditPolicies(log);
        blocks.Register(new ThrowingBlockPolicy());
        var cells = new[] { new BlockCellChange(0, 0, 0, 1, 0) };
        for (var i = 0; i < 1500; i++)
            Assert.False(blocks.Evaluate(new BlockEditProposal(default!, BlockEditKind.Break, 1, cells)).IsAllowed);

        Assert.Equal(4, log.Lines.Count(line => line.Contains("failed")));
    }

    private sealed class ThrowingBlockPolicy : IBlockEditPolicy
    {
        public PolicyDecision Evaluate(in BlockEditProposal proposal) => throw new InvalidOperationException("boom");
    }

    // ---- Protect ----

    [Fact]
    public void Protect_vetoes_every_cause_inside_a_region_and_none_outside()
    {
        var fx = new IntentTestFixture();
        Assert.True(fx.Context.Protect.Regions.TryAdd(ProtectRegion.Between("vault", 0, 0, 0, 9, 9, 9), out _));

        foreach (var cause in Enum.GetValues<EnvironmentCause>())
        {
            Assert.False(fx.Context.EnvironmentPolicies.Allows(cause, 1, 5, 5, 5, Blocks.Stone, Blocks.Air), $"{cause} inside");
            Assert.True(fx.Context.EnvironmentPolicies.Allows(cause, 1, 20, 5, 5, Blocks.Stone, Blocks.Air), $"{cause} outside");
        }
    }

    [Fact]
    public void Without_any_region_the_environment_is_free_to_change_things()
    {
        var fx = new IntentTestFixture();
        Assert.True(fx.Context.EnvironmentPolicies.Allows(EnvironmentCause.Explosion, 1, 5, 5, 5, Blocks.Stone, Blocks.Air));
    }

    // ---- explosions ----

    private sealed record Blast(IntentTestFixture Fx, int Cx, int Cy, int Cz, int Min, int Max);

    /// <summary>A block of stone around a creeper that is about to explode next to a player; the blast is the creeper's own tick.</summary>
    private static Blast Prepare(Action<IntentTestFixture, int, int, int>? beforeBlast = null)
    {
        var fx = new IntentTestFixture();
        var player = fx.AddInGamePlayer("bystander");
        var system = new CreeperSystem(fx.World, fx.Players, new EntityRuntime(), fx.Context.ItemPalette, fx.Context.EnvironmentPolicies);
        var id = system.SpawnCreeper(player.PositionX, player.PositionY, player.PositionZ + 1f);
        system.Tick(fx.Clock, fx.Players.Online); // ignites
        Assert.True(system.Stores.Positions.TryGet(id, out var at));
        var (cx, cy, cz) = ((int)MathF.Floor(at.X), (int)MathF.Floor(at.Y), (int)MathF.Floor(at.Z));

        const int reach = 5;
        for (var x = cx - reach; x <= cx + reach; x++)
            for (var y = cy - reach; y <= cy + reach; y++)
                for (var z = cz - reach; z <= cz + reach; z++)
                    _ = fx.World.TrySetBlock(x, y, z, Blocks.Stone); // cells below the world floor do not exist

        beforeBlast?.Invoke(fx, cx, cy, cz);

        fx.Clock.AdvanceBy(29);
        system.Tick(fx.Clock, fx.Players.Online); // still fusing
        Assert.True(system.Stores.Entities.IsAlive(id), "the creeper must still be fusing");
        fx.Clock.AdvanceBy(1);
        system.Tick(fx.Clock, fx.Players.Online); // the fuse is done: it explodes
        Assert.Equal(1, system.ExplosionCount);
        return new Blast(fx, cx, cy, cz, -reach, reach);
    }

    private static bool InsideBlast(int dx, int dy, int dz) => MathF.Sqrt(dx * dx + dy * dy + dz * dz) <= Radius;

    [Fact]
    public void Without_a_region_the_crater_is_the_whole_sphere()
    {
        var blast = Prepare();

        for (var dx = blast.Min; dx <= blast.Max; dx++)
            for (var dy = blast.Min; dy <= blast.Max; dy++)
                for (var dz = blast.Min; dz <= blast.Max; dz++)
                {
                    if (!WorldColumn.InRange(blast.Cy + dy)) continue;
                    var block = blast.Fx.World.GetBlock(blast.Cx + dx, blast.Cy + dy, blast.Cz + dz);
                    Assert.True(InsideBlast(dx, dy, dz) ? block == Blocks.Air : block == Blocks.Stone, $"{dx},{dy},{dz}");
                }
    }

    [Fact]
    public void A_crater_stops_at_the_edge_of_a_region_and_carves_everything_else()
    {
        // The region is the half of the blast with x >= centre: the cells beside it are carved, the ones inside it stay.
        var blast = Prepare((fx, cx, cy, cz) =>
            Assert.True(fx.Context.Protect.Regions.TryAdd(ProtectRegion.Between("wall", cx, cy - 20, cz - 20, cx + 20, cy + 20, cz + 20), out _)));

        var carved = 0;
        var kept = 0;
        for (var dx = blast.Min; dx <= blast.Max; dx++)
            for (var dy = blast.Min; dy <= blast.Max; dy++)
                for (var dz = blast.Min; dz <= blast.Max; dz++)
                {
                    if (!WorldColumn.InRange(blast.Cy + dy)) continue;
                    var block = blast.Fx.World.GetBlock(blast.Cx + dx, blast.Cy + dy, blast.Cz + dz);
                    var protectedCell = dx >= 0;
                    var expectedAir = InsideBlast(dx, dy, dz) && !protectedCell;
                    Assert.True(expectedAir ? block == Blocks.Air : block == Blocks.Stone, $"{dx},{dy},{dz}");
                    if (expectedAir) carved++;
                    if (InsideBlast(dx, dy, dz) && protectedCell) kept++;
                }

        Assert.True(carved > 0 && kept > 0, "the blast must straddle the region for this test to mean anything");
    }

    [Fact]
    public void A_policy_that_throws_leaves_the_whole_world_untouched()
    {
        var blast = Prepare((fx, _, _, _) => fx.Context.EnvironmentPolicies.Register(new Throws()));

        for (var dx = blast.Min; dx <= blast.Max; dx++)
            for (var dy = blast.Min; dy <= blast.Max; dy++)
                for (var dz = blast.Min; dz <= blast.Max; dz++)
                    if (WorldColumn.InRange(blast.Cy + dy))
                        Assert.Equal(Blocks.Stone, blast.Fx.World.GetBlock(blast.Cx + dx, blast.Cy + dy, blast.Cz + dz));
    }
}
