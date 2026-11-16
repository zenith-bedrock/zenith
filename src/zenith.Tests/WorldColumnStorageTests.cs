using Xunit;
using Zenith.Player;
using Zenith.World;

namespace Zenith.Tests;

public sealed class SectionTests
{
    public SectionTests() => Blocks.EnsureLoaded();

    [Fact]
    public void Uniform_section_reads_back_one_id_and_a_set_splits_it()
    {
        var section = Section.Uniform(Blocks.Stone);
        Assert.Equal(Blocks.Stone, section.Get(0, 0, 0));
        Assert.Equal(Blocks.Stone, section.Get(15, 15, 15));

        Assert.True(section.Set(Section.IndexOf(3, 4, 5), Blocks.Dirt));
        Assert.Equal(Blocks.Dirt, section.Get(3, 4, 5));
        Assert.Equal(Blocks.Stone, section.Get(3, 4, 6));
    }

    [Fact]
    public void Setting_the_same_id_reports_no_change()
    {
        var section = Section.Uniform(Blocks.Stone);
        Assert.False(section.Set(0, Blocks.Stone));
    }

    [Fact]
    public void A_section_overwritten_back_to_air_reads_as_all_air()
    {
        var section = Section.Uniform(Blocks.Air);
        var index = Section.IndexOf(1, 1, 1);
        section.Set(index, Blocks.Stone);
        Assert.False(section.IsAllAir);
        section.Set(index, Blocks.Air);

        Assert.True(section.IsAllAir);
        Assert.Equal(Blocks.Air, section.Get(index));
    }

    [Fact]
    public void Snapshot_matches_the_cells()
    {
        var section = Section.Uniform(Blocks.Air);
        section.Set(Section.IndexOf(2, 3, 4), Blocks.Stone);
        section.Set(Section.IndexOf(5, 6, 7), Blocks.Dirt);

        var cells = new ushort[Section.Volume];
        var palette = new int[Section.Volume];
        Assert.True(section.TrySnapshot(cells, palette, out var count, out var uniform));
        Assert.False(uniform);
        Assert.Equal(Blocks.Stone, palette[cells[Section.IndexOf(2, 3, 4)]]);
        Assert.Equal(Blocks.Dirt, palette[cells[Section.IndexOf(5, 6, 7)]]);
        Assert.Equal(Blocks.Air, palette[cells[0]]);
    }

    [Fact]
    public void Version_moves_on_every_change()
    {
        var section = Section.Uniform(Blocks.Air);
        var before = section.Version;
        section.Set(0, Blocks.Stone);
        Assert.NotEqual(before, section.Version);
    }
}

public sealed class ColumnCodecTests
{
    public ColumnCodecTests() => Blocks.EnsureLoaded();

    private static WorldColumn Sample()
    {
        var column = new WorldColumn(new ChunkCoord(3, -2), 0, biomeNetworkId: 1);
        column.Set(1, 64, 2, Blocks.Stone);
        column.Set(15, -64, 15, Blocks.Dirt);
        column.Set(8, 300, 8, Blocks.Sand);
        return column;
    }

    [Fact]
    public void Round_trip_keeps_cells_and_biome()
    {
        var original = Sample();
        var bytes = ColumnCodec.Encode(original, Blocks.Palette);
        var back = ColumnCodec.Decode(bytes, original.Coord, 0, Blocks.Palette);

        Assert.Equal(Blocks.Stone, back.Get(1, 64, 2));
        Assert.Equal(Blocks.Dirt, back.Get(15, -64, 15));
        Assert.Equal(Blocks.Sand, back.Get(8, 300, 8));
        Assert.Equal(Blocks.Air, back.Get(0, 100, 0));
        Assert.Equal(original.GetWire().Payload, back.GetWire().Payload);
    }

    [Fact]
    public void Round_trip_keeps_a_chest_with_its_items()
    {
        var original = Sample();
        var chest = new ContainerEntity();
        chest.Slots[4] = InventorySlot.OfBlock(Blocks.Dirt, 9);
        original.SetEntity(1, 64, 2, chest);

        var back = ColumnCodec.Decode(ColumnCodec.Encode(original, Blocks.Palette), original.Coord, 0, Blocks.Palette);

        Assert.True(back.TryGetEntity(1, 64, 2, out var entity));
        var restored = Assert.IsType<ContainerEntity>(entity);
        Assert.Equal(Blocks.Dirt, restored.Slots[4].Id.Value);
        Assert.Equal(9, restored.Slots[4].Count);
        Assert.Empty(restored.Openers);
    }

    [Fact]
    public void A_corrupted_blob_throws_instead_of_producing_a_column()
    {
        var bytes = ColumnCodec.Encode(Sample(), Blocks.Palette);
        Assert.Throws<InvalidDataException>(() =>
            ColumnCodec.Decode(bytes.AsMemory(0, bytes.Length / 2), new ChunkCoord(3, -2), 0, Blocks.Palette));
        Assert.Throws<InvalidDataException>(() =>
            ColumnCodec.Decode(new byte[] { 1, 2, 3 }, new ChunkCoord(3, -2), 0, Blocks.Palette));
    }
}

public sealed class WorldColumnTests
{
    public WorldColumnTests() => Blocks.EnsureLoaded();

    [Fact]
    public void Edit_version_moves_only_when_a_cell_changes()
    {
        var column = new WorldColumn(new ChunkCoord(0, 0), 0, 1);
        var v0 = column.EditVersion;
        column.Set(1, 10, 1, Blocks.Stone);
        var v1 = column.EditVersion;
        Assert.True(v1 > v0);
        column.Set(1, 10, 1, Blocks.Stone);
        Assert.Equal(v1, column.EditVersion);
    }

    [Fact]
    public void Wire_reaches_up_to_the_highest_non_air_section()
    {
        var column = new WorldColumn(new ChunkCoord(0, 0), 0, 1);
        column.Set(0, -64, 0, Blocks.Stone);
        Assert.Equal(1, column.GetWire().SubChunkCount);
        column.Set(0, 0, 0, Blocks.Stone); // section 4
        Assert.Equal(5, column.GetWire().SubChunkCount);
    }

    [Fact]
    public void Entities_are_added_found_and_removed()
    {
        var column = new WorldColumn(new ChunkCoord(0, 0), 0, 1);
        column.SetEntity(1, 64, 2, new ContainerEntity());
        Assert.Equal(1, column.EntityCount);
        Assert.True(column.TryGetEntity(1, 64, 2, out _));
        Assert.True(column.RemoveEntity(1, 64, 2, out _));
        Assert.Equal(0, column.EntityCount);
    }
}
