using Xunit;

namespace Zenith.Nbt.Tests;

public class HardeningAndDxTests
{
    [Fact]
    public void A_forged_int_array_length_is_refused_before_it_allocates()
    {
        // type IntArray, name "a", declared length 16 million, no data behind it
        var data = new List<byte> { (byte)NbtType.IntArray, 1, 0, (byte)'a' };
        data.AddRange(BitConverter.GetBytes(16_000_000));
        Assert.Throws<NbtException>(() => NbtCodec.Decode(data.ToArray(), NbtEncoding.LittleEndian));
    }

    [Fact]
    public void A_forged_list_count_is_refused()
    {
        var data = new List<byte> { (byte)NbtType.List, 1, 0, (byte)'l', (byte)NbtType.Int };
        data.AddRange(BitConverter.GetBytes(1_000_000));
        Assert.Throws<NbtException>(() => NbtCodec.Decode(data.ToArray(), NbtEncoding.LittleEndian));
    }

    [Fact]
    public void A_varint_that_overflows_32_bits_is_refused()
    {
        // Network encoding: string length is an unsigned varint; five bytes with the top nibble set overflows.
        byte[] data = [(byte)NbtType.Int, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F];
        Assert.Throws<NbtException>(() => NbtCodec.Decode(data, NbtEncoding.Network));
    }

    [Fact]
    public void Concatenated_roots_are_all_read()
    {
        var a = NbtCodec.Encode(new NbtNamedTag("", NbtTag.Compound(new NbtCompound().SetInt("x", 1))), NbtEncoding.LittleEndian);
        var b = NbtCodec.Encode(new NbtNamedTag("", NbtTag.Compound(new NbtCompound().SetInt("x", 2))), NbtEncoding.LittleEndian);

        var roots = NbtCodec.DecodeAll([.. a, .. b], NbtEncoding.LittleEndian);

        Assert.Equal(2, roots.Count);
        Assert.Equal(1, roots[0].Tag.AsCompound()["x"].AsInt());
        Assert.Equal(2, roots[1].Tag.AsCompound()["x"].AsInt());
    }

    [Fact]
    public void The_level_dat_header_round_trips()
    {
        var root = new NbtNamedTag("", NbtTag.Compound(new NbtCompound().SetString("LevelName", "Zenith").SetInt("StorageVersion", 10)));

        var file = NbtCodec.EncodeWithHeader(root, storageVersion: 10);
        var decoded = NbtCodec.DecodeWithHeader(file, out var version);

        Assert.Equal(10, version);
        Assert.Equal("Zenith", decoded.Root.Tag.AsCompound()["LevelName"].AsString());
        Assert.Throws<NbtException>(() => NbtCodec.DecodeWithHeader(file.AsSpan(0, file.Length - 3), out _));
    }

    [Fact]
    public void Remove_keeps_the_order_and_the_lookup_of_the_rest()
    {
        var compound = new NbtCompound().SetInt("a", 1).SetInt("b", 2).SetInt("c", 3);

        Assert.True(compound.Remove("a"));
        Assert.False(compound.Remove("a"));

        Assert.Equal(2, compound.Count);
        Assert.Equal(["b", "c"], compound.Entries.Select(e => e.Key));
        Assert.True(compound.TryGetInt("c", out var c));
        Assert.Equal(3, c);
        Assert.False(compound.TryGetString("b", out _)); // wrong type
    }
}
