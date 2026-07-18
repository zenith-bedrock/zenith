namespace Zenith.World;

/// <summary>
/// Discriminated stack identity (ADR §55). Never compare <see cref="Value"/> across kinds.
/// </summary>
enum StackKind : byte
{
    Block = 1,
    Item = 2
}

/// <summary>
/// Inventory / chest / craft / floor-drop identity.
/// Block Value = <c>BlockRuntimeId</c>; Item Value = <c>ItemNetworkId</c>.
/// </summary>
readonly record struct StackId(StackKind Kind, int Value)
{
    public static StackId FromBlock(int blockRuntimeId) => new(StackKind.Block, blockRuntimeId);

    public static StackId FromItem(int itemNetworkId) => new(StackKind.Item, itemNetworkId);

    public bool IsBlock => Kind == StackKind.Block;

    public bool IsItem => Kind == StackKind.Item;

    /// <summary>Empty / air block stack (domain empty check also uses count).</summary>
    public static StackId AirBlock => FromBlock(0);

    public bool IsAirBlock => Kind == StackKind.Block && (Value == 0 || Value == Blocks.Air);

    /// <summary>Default / unset held stack, or air block identity.</summary>
    public bool IsEmpty => Kind == default || IsAirBlock;
}

