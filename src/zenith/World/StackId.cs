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
/// <see cref="Aux"/> is the item's damage/variant value (bed or banner colour, tipped-arrow potion, ...; ADR §147):
/// part of the identity, so stacks that differ only in aux never merge or match. Blocks carry their state in the
/// runtime id and always have aux 0.
/// </summary>
readonly record struct StackId
{
    /// <summary>Largest aux the wire carries (unsigned 16-bit; 32767 is the recipe wildcard and is never a stack's aux).</summary>
    public const int MaxAux = short.MaxValue - 1;

    public StackKind Kind { get; }

    public int Value { get; }

    public int Aux { get; }

    public StackId(StackKind kind, int value, int aux = 0)
    {
        if (aux < 0 || aux > MaxAux)
            throw new ArgumentOutOfRangeException(nameof(aux), aux, $"aux must be 0..{MaxAux}");
        if (kind == StackKind.Block && aux != 0)
            throw new ArgumentOutOfRangeException(nameof(aux), aux, "a block stack carries its state in the runtime id, not aux");
        Kind = kind;
        Value = value;
        Aux = aux;
    }

    public static StackId FromBlock(int blockRuntimeId) => new(StackKind.Block, blockRuntimeId);

    public static StackId FromItem(int itemNetworkId, int aux = 0) => new(StackKind.Item, itemNetworkId, aux);

    public bool IsBlock => Kind == StackKind.Block;

    public bool IsItem => Kind == StackKind.Item;

    /// <summary>Empty / air block stack (domain empty check also uses count).</summary>
    public static StackId AirBlock => FromBlock(0);

    public bool IsAirBlock => Kind == StackKind.Block && (Value == 0 || Value == Blocks.Air);

    /// <summary>Default / unset held stack, or air block identity.</summary>
    public bool IsEmpty => Kind == default || IsAirBlock;
}

