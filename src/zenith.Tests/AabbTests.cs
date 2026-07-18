using Zenith.Geometry;
using Zenith.World;
using Xunit;

namespace Zenith.Tests;

public class AabbTests
{
    [Fact]
    public void AbsoluteWireY_adds_network_offset_not_bare_feet()
    {
        // Regression: feet domain + bare Absolute sank peers (~eye height into floor).
        Assert.Equal(1.621f, EntityHitboxes.PlayerNetworkOffset);
        Assert.Equal(Blocks.FlatSpawnY + 1.621f, EntityHitboxes.AbsoluteWireY(Blocks.FlatSpawnY));
        Assert.NotEqual(Blocks.FlatSpawnY, EntityHitboxes.AbsoluteWireY(Blocks.FlatSpawnY));
    }

    [Fact]
    public void StandingForPlaceCheck_does_not_intersect_flush_adjacent_BlockCell()
    {
        // Feet at x+1.5 (StandForPlace) vs cell at x — flush face, no volume overlap.
        var cell = EntityHitboxes.BlockCell(7, 64, 7);
        var standing = EntityHitboxes.StandingForPlaceCheck(7 + 1.5f, 64f, 7 + 0.5f);
        Assert.False(cell.Intersects(standing));
    }

    [Fact]
    public void StandingForPlaceCheck_intersects_own_BlockCell()
    {
        var cell = EntityHitboxes.BlockCell(6, 64, 6);
        var standing = EntityHitboxes.StandingForPlaceCheck(6 + 0.5f, 64f, 6 + 0.5f);
        Assert.True(cell.Intersects(standing));
    }

    [Fact]
    public void Intersects_overlapping_boxes()
    {
        var a = Aabb.FromMinMax(0, 0, 0, 1, 1, 1);
        var b = Aabb.FromMinMax(0.5f, 0.5f, 0.5f, 1.5f, 1.5f, 1.5f);
        Assert.True(a.Intersects(b));
    }

    [Fact]
    public void Intersects_touching_edges_is_false()
    {
        // Half-open style: Max == other.Min → no volume overlap.
        var a = Aabb.FromMinMax(0, 0, 0, 1, 1, 1);
        var b = Aabb.FromMinMax(1, 0, 0, 2, 1, 1);
        Assert.False(a.Intersects(b));
    }

    [Fact]
    public void Expand_grows_each_side()
    {
        var a = Aabb.FromMinMax(0, 0, 0, 1, 1, 1).Expand(1, 0.5f, 1);
        Assert.Equal(-1, a.MinX);
        Assert.Equal(-0.5f, a.MinY);
        Assert.Equal(-1, a.MinZ);
        Assert.Equal(2, a.MaxX);
        Assert.Equal(1.5f, a.MaxY);
        Assert.Equal(2, a.MaxZ);
    }

    [Fact]
    public void Player_expand_intersects_item_same_cell()
    {
        Blocks.EnsureLoaded();
        var playerBb = EntityHitboxes.PlayerStanding(5.5f, 64f, 5.5f)
            .Expand(EntityHitboxes.PickupExpand);
        var itemBb = EntityHitboxes.ItemAtCell(5, 64, 5);
        Assert.True(playerBb.Intersects(itemBb));
    }

    [Fact]
    public void Player_one_block_above_item_does_not_intersect_with_pickup_expand()
    {
        // Expand +0.5 Y from feet does not reach an item entity one full block below.
        Blocks.EnsureLoaded();
        var feetY = Blocks.FlatGrassY + 1f;
        var playerBb = EntityHitboxes.PlayerStanding(8.5f, feetY, 8.5f)
            .Expand(EntityHitboxes.PickupExpand);
        var itemBb = EntityHitboxes.ItemAtCell(8, Blocks.FlatGrassY, 8);
        Assert.False(playerBb.Intersects(itemBb));
    }

    [Fact]
    public void Player_adjacent_same_y_intersects()
    {
        Blocks.EnsureLoaded();
        var playerBb = EntityHitboxes.PlayerStanding(5.5f, 64f, 5.5f)
            .Expand(EntityHitboxes.PickupExpand);
        var itemBb = EntityHitboxes.ItemAtCell(6, 64, 5);
        Assert.True(playerBb.Intersects(itemBb));
    }

    [Fact]
    public void Too_far_horizontally_no_intersect()
    {
        Blocks.EnsureLoaded();
        var playerBb = EntityHitboxes.PlayerStanding(0.5f, 64f, 0.5f)
            .Expand(EntityHitboxes.PickupExpand);
        var itemBb = EntityHitboxes.ItemAtCell(20, 64, 0);
        Assert.False(playerBb.Intersects(itemBb));
    }
}
