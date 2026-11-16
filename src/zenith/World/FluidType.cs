namespace Zenith.World;

/// <summary>Which liquid a block is, if any (the <c>Water</c> and <c>Lava</c> traits in block_definitions.json).</summary>
enum FluidType : byte
{
    None = 0,
    Water,
    Lava
}
