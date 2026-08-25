using Zenith.World;
using Xunit;
using Zenith.Gameplay.Entities;

namespace Zenith.Tests;

/// <summary>
/// Phase XXIX — characterizes the ground-locomotion resolver directly against a flat test world
/// (grass at <see cref="Blocks.FlatGrassY"/>, stone below, air above — see <c>IntentTestFixture</c>'s
/// default terrain provider) rather than driving a full mob AI, so each physical fact (support,
/// occupancy, gravity, step-up) is provable in isolation. See
/// docs/history/phases/phase-xxix-ground-actor-physics-findings.md.
/// </summary>
public sealed class GroundMobMovementTests
{
    [Fact]
    public void Water_is_not_ground_support()
    {
        var fx = new IntentTestFixture();
        fx.World.TrySetBlock(0, Blocks.FlatGrassY, 0, Blocks.Water);

        Assert.False(GroundMobMovement.IsSupportedGroundCell(fx.World, 0.5f, Blocks.FlatSpawnY, 0.5f));
    }

    [Fact]
    public void Air_is_not_ground_support()
    {
        var fx = new IntentTestFixture();

        // Two blocks above the grass top — nothing but air beneath these feet.
        Assert.False(GroundMobMovement.IsSupportedGroundCell(fx.World, 0.5f, Blocks.FlatSpawnY + 2, 0.5f));
    }

    [Fact]
    public void Full_solid_block_supports_ground_actor()
    {
        var fx = new IntentTestFixture();

        // Default flat terrain: grass top at FlatGrassY, so standing at FlatSpawnY is supported.
        Assert.True(GroundMobMovement.IsSupportedGroundCell(fx.World, 0.5f, Blocks.FlatSpawnY, 0.5f));
    }

    /// <summary>
    /// Regression for ADR §137: ground-cover vegetation (short grass/flowers) added by world-gen
    /// must be passable, unlike every previously-registered block — before this, Blocks.IsSolid
    /// treated anything that wasn't air or fluid as solid, which would have made grass tufts
    /// impassable walls the moment any decoration got placed on the surface.
    /// </summary>
    [Fact]
    public void Ground_cover_decoration_does_not_block_movement_or_support()
    {
        Blocks.EnsureLoaded();
        Assert.False(Blocks.BlocksMovement(Blocks.ShortGrass));
        Assert.False(Blocks.BlocksMovement(Blocks.Dandelion));
        Assert.False(Blocks.BlocksMovement(Blocks.Poppy));
        Assert.False(Blocks.CanSupportGroundActor(Blocks.ShortGrass));
        Assert.True(Blocks.CanOccupy(Blocks.ShortGrass));
    }

    [Fact]
    public void Grass_block_itself_still_blocks_movement_and_supports_ground_actors()
    {
        Blocks.EnsureLoaded();
        Assert.True(Blocks.BlocksMovement(Blocks.GrassBlock));
        Assert.True(Blocks.CanSupportGroundActor(Blocks.GrassBlock));
    }

    /// <summary>
    /// Regression for ADR §138: torch is the second passable-decoration block (after ground cover),
    /// but fence/stone bricks/cobblestone wall are ordinary full-solid building blocks — Zenith
    /// doesn't model partial collision boxes (Phase XXIX), so a fence blocks movement fully rather
    /// than at vanilla's partial height, an accepted simplification.
    /// </summary>
    [Fact]
    public void Torch_is_passable_but_fence_stone_bricks_and_wall_are_solid()
    {
        Blocks.EnsureLoaded();
        Assert.False(Blocks.BlocksMovement(Blocks.Torch));
        Assert.False(Blocks.CanSupportGroundActor(Blocks.Torch));
        Assert.True(Blocks.BlocksMovement(Blocks.OakFence));
        Assert.True(Blocks.BlocksMovement(Blocks.StoneBricks));
        Assert.True(Blocks.BlocksMovement(Blocks.CobblestoneWall));
    }

    [Fact]
    public void Ground_actor_does_not_hover_over_water()
    {
        var fx = new IntentTestFixture();
        fx.World.TrySetBlock(0, Blocks.FlatGrassY, 0, Blocks.Water);

        var y = (float)Blocks.FlatSpawnY;
        var fallSpeed = 0f;
        var changed = GroundMobMovement.ResolveVertical(fx.World, 0.5f, 0.5f, ref y, ref fallSpeed, out var grounded);

        Assert.True(changed);
        Assert.False(grounded);
        Assert.True(y < Blocks.FlatSpawnY);
    }

    [Fact]
    public void Ground_actor_falls_when_support_disappears()
    {
        var fx = new IntentTestFixture();
        var y = (float)Blocks.FlatSpawnY;
        var fallSpeed = 0f;

        // Resting on the default grass top first — confirms the baseline before removing it.
        Assert.False(GroundMobMovement.ResolveVertical(fx.World, 0.5f, 0.5f, ref y, ref fallSpeed, out var restingGrounded));
        Assert.True(restingGrounded);

        fx.World.TrySetBlock(0, Blocks.FlatGrassY, 0, Blocks.Air);
        var changed = GroundMobMovement.ResolveVertical(fx.World, 0.5f, 0.5f, ref y, ref fallSpeed, out var grounded);

        Assert.True(changed);
        Assert.False(grounded);
        Assert.True(fallSpeed > 0f);
    }

    [Fact]
    public void Ground_actor_lands_on_valid_support()
    {
        var fx = new IntentTestFixture();
        var y = (float)(Blocks.FlatSpawnY + 5);
        var fallSpeed = 0f;
        var grounded = false;

        for (var i = 0; i < 40 && !grounded; i++)
            GroundMobMovement.ResolveVertical(fx.World, 0.5f, 0.5f, ref y, ref fallSpeed, out grounded);

        Assert.True(grounded);
        Assert.Equal((float)Blocks.FlatSpawnY, y);
        Assert.Equal(0f, fallSpeed);
    }

    [Fact]
    public void Ground_actor_cannot_walk_through_full_solid_block()
    {
        var fx = new IntentTestFixture();
        fx.World.TrySetBlock(1, Blocks.FlatSpawnY, 0, Blocks.Stone);
        fx.World.TrySetBlock(1, Blocks.FlatSpawnY + 1, 0, Blocks.Stone);

        var moved = GroundMobMovement.TryMoveHorizontal(
            fx.World, 0.5f, Blocks.FlatSpawnY, 0.5f, 1.5f, 0.5f, out _);

        Assert.False(moved);
    }

    [Fact]
    public void Ground_actor_follows_simple_downhill_terrain()
    {
        var fx = new IntentTestFixture();
        // Lower the terrain by one block at x=1: the grass top is gone, but the stone beneath it
        // (default flat terrain) still provides a floor one block down.
        fx.World.TrySetBlock(1, Blocks.FlatGrassY, 0, Blocks.Air);

        var moved = GroundMobMovement.TryMoveHorizontal(
            fx.World, 0.5f, Blocks.FlatSpawnY, 0.5f, 1.5f, 0.5f, out var resolvedY);
        Assert.True(moved);
        Assert.Equal((float)Blocks.FlatSpawnY, resolvedY); // the horizontal step alone never descends

        var y = resolvedY;
        var fallSpeed = 0f;
        var grounded = false;
        for (var i = 0; i < 10 && !grounded; i++)
            GroundMobMovement.ResolveVertical(fx.World, 1.5f, 0.5f, ref y, ref fallSpeed, out grounded);

        Assert.True(grounded);
        Assert.Equal((float)Blocks.FlatGrassY, y); // landed exactly one block lower
    }

    [Fact]
    public void Ground_actor_handles_one_block_step_up_if_supported()
    {
        var fx = new IntentTestFixture();
        // Raise the terrain by one block at x=1: fill the ordinarily-air cell at FlatSpawnY.
        fx.World.TrySetBlock(1, Blocks.FlatSpawnY, 0, Blocks.Stone);

        var moved = GroundMobMovement.TryMoveHorizontal(
            fx.World, 0.5f, Blocks.FlatSpawnY, 0.5f, 1.5f, 0.5f, out var resolvedY);

        Assert.True(moved);
        Assert.Equal((float)(Blocks.FlatSpawnY + 1), resolvedY);
    }

    [Fact]
    public void Ground_actor_behaves_correctly_across_negative_coordinates()
    {
        var fx = new IntentTestFixture();
        fx.World.TrySetBlock(-5, Blocks.FlatGrassY, -5, Blocks.Water);

        Assert.False(GroundMobMovement.IsSupportedGroundCell(fx.World, -4.5f, Blocks.FlatSpawnY, -4.5f));
        Assert.True(GroundMobMovement.IsSupportedGroundCell(fx.World, -104.5f, Blocks.FlatSpawnY, -104.5f));

        var moved = GroundMobMovement.TryMoveHorizontal(
            fx.World, -1.5f, Blocks.FlatSpawnY, -1.5f, -0.5f, -1.5f, out var resolvedY);
        Assert.True(moved);
        Assert.Equal((float)Blocks.FlatSpawnY, resolvedY);
    }

    [Fact]
    public void Spawn_support_does_not_treat_water_as_solid()
    {
        Blocks.EnsureLoaded();
        // Same (0,0)/seed=1 coordinate TerrainProviderTests' existing noise spawn test already
        // proves lands on ordinary generated terrain (not a mountain/cave edge case) — this test
        // adds the Phase XXIX assertion that sibling test didn't check: what's actually below the
        // resolved spawn feet is real solid support, not water.
        var provider = new NoiseTerrainProvider(seed: 1);

        var spawnY = provider.SampleSpawnFeetY(0, 0);
        var belowBlock = provider.SampleBaseBlock(0, spawnY - 1, 0);

        Assert.NotEqual(Blocks.Water, belowBlock);
        Assert.True(Blocks.CanSupportGroundActor(belowBlock),
            $"spawn at (0,{spawnY},0) rests on rid={belowBlock}, which does not support a ground actor");
    }
}
