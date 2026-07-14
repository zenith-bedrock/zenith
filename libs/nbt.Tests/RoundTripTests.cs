using Xunit;
using Zenith.Nbt;

namespace Zenith.Nbt.Tests;

public class RoundTripTests
{
    public static IEnumerable<object[]> Encodings() =>
    [
        [NbtEncoding.LittleEndian],
        [NbtEncoding.Network],
        [NbtEncoding.BigEndian]
    ];

    [Theory]
    [MemberData(nameof(Encodings))]
    public void RoundTrip_byte_short_int_long_float_double(NbtEncoding encoding)
    {
        AssertRoundTrip(encoding, NbtTag.Byte(unchecked((sbyte)200)));
        AssertRoundTrip(encoding, NbtTag.Short(-300));
        AssertRoundTrip(encoding, NbtTag.Int(-604749536));
        AssertRoundTrip(encoding, NbtTag.Long(long.MinValue + 7));
        AssertRoundTrip(encoding, NbtTag.Float(1.5f));
        AssertRoundTrip(encoding, NbtTag.Double(Math.PI));
    }

    [Theory]
    [MemberData(nameof(Encodings))]
    public void RoundTrip_string_and_arrays(NbtEncoding encoding)
    {
        AssertRoundTrip(encoding, NbtTag.String("minecraft:stone"));
        AssertRoundTrip(encoding, NbtTag.ByteArray([1, 2, 255]));
        AssertRoundTrip(encoding, NbtTag.IntArray([-1, 0, 42]));
        AssertRoundTrip(encoding, NbtTag.LongArray([long.MaxValue, -3]));
    }

    [Theory]
    [MemberData(nameof(Encodings))]
    public void RoundTrip_list_and_compound(NbtEncoding encoding)
    {
        var list = new NbtList(NbtType.Int);
        list.Add(NbtTag.Int(1));
        list.Add(NbtTag.Int(2));
        AssertRoundTrip(encoding, NbtTag.List(list));

        var compound = new NbtCompound();
        compound.Set("name", NbtTag.String("minecraft:air"));
        compound.Set("network_id", NbtTag.Int(-604749536));
        var states = new NbtCompound();
        compound.Set("states", NbtTag.Compound(states));
        AssertRoundTrip(encoding, NbtTag.Compound(compound));
    }

    [Theory]
    [MemberData(nameof(Encodings))]
    public void RoundTrip_empty_compound_root(NbtEncoding encoding)
    {
        var root = new NbtNamedTag("", NbtTag.Compound(new NbtCompound()));
        var bytes = NbtCodec.Encode(root, encoding);
        var decoded = NbtCodec.Decode(bytes, encoding);
        Assert.Equal("", decoded.Root.Name);
        Assert.Equal(NbtType.Compound, decoded.Root.Tag.Type);
        Assert.Equal(0, decoded.Root.Tag.AsCompound().Count);
        Assert.Equal(bytes.Length, decoded.BytesConsumed);
    }

    [Theory]
    [MemberData(nameof(Encodings))]
    public void Indexers_work_on_compound_and_list(NbtEncoding encoding)
    {
        _ = encoding;
        var inner = new NbtList(NbtType.String);
        inner.Add(NbtTag.String("a"));
        var compound = new NbtCompound();
        compound.Set("items", NbtTag.List(inner));
        var tag = NbtTag.Compound(compound);
        Assert.Equal("a", tag["items"][0].AsString());
    }

    private static void AssertRoundTrip(NbtEncoding encoding, NbtTag tag)
    {
        var named = new NbtNamedTag("t", tag);
        var bytes = NbtCodec.Encode(named, encoding);
        var decoded = NbtCodec.Decode(bytes, encoding);
        Assert.Equal("t", decoded.Root.Name);
        Assert.Equal(tag, decoded.Root.Tag);
    }
}

public class LimitsTests
{
    [Fact]
    public void Depth_limit_rejects_deep_nesting()
    {
        // Build compound nesting deeper than MaxDepth via recursive payload encode...
        // Easier: craft LE bytes for nested compounds past limit.
        var writer = new NbtWriter(NbtEncoding.LittleEndian);
        // Manually craft by building nested compounds in memory then encode — NbtWriter doesn't check depth.
        // Reader must reject. Build bytes: name "" + type compound, repeatedly open compounds without End until decode fails.
        NbtTag tag = NbtTag.Compound(new NbtCompound());
        for (var i = 0; i < NbtLimits.MaxDepth + 5; i++)
        {
            var c = new NbtCompound();
            c.Set("n", tag);
            tag = NbtTag.Compound(c);
        }

        var bytes = NbtCodec.Encode(new NbtNamedTag("", tag), NbtEncoding.LittleEndian);
        Assert.Throws<NbtException>(() => NbtCodec.Decode(bytes, NbtEncoding.LittleEndian));
    }

    [Fact]
    public void Oversized_length_before_alloc_fails()
    {
        // LE: compound "" then byte array tag named "x" with length Int.MaxValue
        // type compound, name "", then type bytearray, name "x", length 0x7FFFFFFF, no data
        var buf = new List<byte> { 0x0A, 0x00, 0x00 }; // compound + empty name
        buf.Add(0x07); // bytearray
        buf.Add(0x01); buf.Add(0x00); buf.Add((byte)'x'); // name "x"
        buf.AddRange(BitConverter.GetBytes(int.MaxValue)); // length
        Assert.Throws<NbtException>(() => NbtCodec.Decode(buf.ToArray(), NbtEncoding.LittleEndian));
    }

    [Fact]
    public void Wrong_type_AsInt_throws_clear_error()
    {
        var tag = NbtTag.String("nope");
        var ex = Assert.Throws<NbtException>(() => tag.AsInt());
        Assert.Contains("Expected NBT Int", ex.Message);
    }
}
