using Xunit;
using Zenith.Gameplay.Entities;

namespace Zenith.Tests;

public sealed class ChunkSpatialIndexTests
{
    [Fact]
    public void Inserted_item_is_returned_by_a_query_covering_its_chunk()
    {
        var index = new ChunkSpatialIndex<string>();
        index.Insert("a", 5f, 5f); // chunk (0, 0)

        var found = Collect(index, 5f, 5f, radius: 1f);

        Assert.Equal(new[] { "a" }, found);
    }

    [Fact]
    public void Rebuild_via_Clear_then_Insert_replaces_prior_membership()
    {
        var index = new ChunkSpatialIndex<string>();
        index.Insert("a", 5f, 5f);
        index.Clear();
        index.Insert("a", 500f, 500f); // moved far away, same chunk key never repopulated

        Assert.Empty(Collect(index, 5f, 5f, radius: 1f));
        Assert.Equal(new[] { "a" }, Collect(index, 500f, 500f, radius: 1f));
    }

    [Fact]
    public void Crossing_a_chunk_boundary_moves_the_item_to_the_new_bucket_on_the_next_rebuild()
    {
        var index = new ChunkSpatialIndex<string>();
        index.Insert("a", 15.9f, 0f); // chunk (0, 0)
        Assert.Equal(new[] { "a" }, Collect(index, 15.9f, 0f, radius: 0.1f));

        index.Clear();
        index.Insert("a", 16.1f, 0f); // chunk (1, 0)

        Assert.Empty(Collect(index, 15.9f, 0f, radius: 0.05f));
        Assert.Equal(new[] { "a" }, Collect(index, 16.1f, 0f, radius: 0.05f));
    }

    [Fact]
    public void Negative_coordinates_map_to_the_correct_negative_chunk()
    {
        var index = new ChunkSpatialIndex<string>();
        index.Insert("negative", -0.1f, -0.1f); // floor(-0.1/16) == chunk (-1, -1)
        index.Insert("positive", 0.1f, 0.1f); // chunk (0, 0)

        Assert.Equal(new[] { "negative" }, Collect(index, -0.1f, -0.1f, radius: 0.01f));
        Assert.Equal(new[] { "positive" }, Collect(index, 0.1f, 0.1f, radius: 0.01f));
    }

    [Fact]
    public void Removed_item_no_longer_appears_after_a_Clear_rebuild_that_omits_it()
    {
        var index = new ChunkSpatialIndex<string>();
        index.Insert("gone", 0f, 0f);
        index.Clear(); // simulates despawn: never re-inserted on the next rebuild

        Assert.Empty(Collect(index, 0f, 0f, radius: 5f));
    }

    [Fact]
    public void An_item_never_appears_more_than_once_per_query()
    {
        var index = new ChunkSpatialIndex<string>();
        index.Insert("solo", 0f, 0f);

        var found = Collect(index, 0f, 0f, radius: 20f); // range spans many chunks, item inserted once

        Assert.Single(found);
    }

    [Fact]
    public void Query_near_a_chunk_edge_finds_a_candidate_in_the_adjacent_chunk()
    {
        var index = new ChunkSpatialIndex<string>();
        index.Insert("neighbor", 16.1f, 0f); // chunk (1, 0)

        // Querying from chunk (0, 0)'s edge with a radius that reaches into chunk (1, 0).
        var found = Collect(index, 15.9f, 0f, radius: 0.3f);

        Assert.Equal(new[] { "neighbor" }, found);
    }

    [Fact]
    public void A_far_object_outside_every_overlapping_chunk_is_not_enumerated()
    {
        var index = new ChunkSpatialIndex<string>();
        index.Insert("far", 500f, 500f);

        var found = Collect(index, 0f, 0f, radius: 5f);

        Assert.Empty(found);
    }

    [Fact]
    public void Radius_range_enumerates_every_intersecting_chunk_not_only_the_center_chunk()
    {
        var index = new ChunkSpatialIndex<string>();
        index.Insert("north", 0f, -16.5f);  // chunk (0, -2)
        index.Insert("south", 0f, 16.5f);   // chunk (0, 1)
        index.Insert("east", 16.5f, 0f);    // chunk (1, 0)
        index.Insert("west", -16.5f, 0f);   // chunk (-2, 0)
        index.Insert("center", 0f, 0f);     // chunk (0, 0)

        var found = Collect(index, 0f, 0f, radius: 17f);

        Assert.Equal(5, found.Count);
        Assert.Contains("north", found);
        Assert.Contains("south", found);
        Assert.Contains("east", found);
        Assert.Contains("west", found);
        Assert.Contains("center", found);
    }

    private static List<string> Collect(ChunkSpatialIndex<string> index, float x, float z, float radius)
    {
        var found = new List<string>();
        foreach (var item in index.EnumerateNearby(x, z, radius))
            found.Add(item);
        return found;
    }
}
