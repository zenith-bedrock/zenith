using Xunit;
using Zenith.World;

namespace Zenith.Tests;

/// <summary>
/// The single-source-of-truth conversion this class exists to guarantee (see its own doc comment):
/// the int (arithmetic-shift) and float (Math.Floor) overloads must agree for every input, including
/// negative coordinates crossing a chunk boundary — the case the two independent implementations it
/// replaced were most likely to silently diverge on.
/// </summary>
public class ChunkMathTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(15, 0)]
    [InlineData(16, 1)]
    [InlineData(31, 1)]
    [InlineData(32, 2)]
    [InlineData(-1, -1)]
    [InlineData(-16, -1)]
    [InlineData(-17, -2)]
    [InlineData(-32, -2)]
    public void Int_overload_floors_to_the_containing_chunk(int block, int expectedChunk)
    {
        Assert.Equal(expectedChunk, ChunkMath.BlockToChunk(block));
    }

    [Theory]
    [InlineData(0f, 0)]
    [InlineData(15.9f, 0)]
    [InlineData(16f, 1)]
    [InlineData(-0.1f, -1)]
    [InlineData(-16f, -1)]
    [InlineData(-16.1f, -2)]
    public void Float_overload_floors_to_the_containing_chunk(float block, int expectedChunk)
    {
        Assert.Equal(expectedChunk, ChunkMath.BlockToChunk(block));
    }

    /// <summary>The two overloads must never disagree for a whole-number input — that agreement is the entire reason this class exists.</summary>
    [Theory]
    [InlineData(-100)]
    [InlineData(-33)]
    [InlineData(-17)]
    [InlineData(-16)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(1000)]
    public void Int_and_float_overloads_agree_for_whole_numbers(int block)
    {
        Assert.Equal(ChunkMath.BlockToChunk(block), ChunkMath.BlockToChunk((float)block));
    }
}
