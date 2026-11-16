namespace Zenith.World;

/// <summary>Half from the click; a second slab of the same type on the open half merges into a double slab.</summary>
sealed class SlabShape : BlockShape
{
    public override IReadOnlyList<StateKey> RequiredKeys => [StateKeys.VerticalHalf];

    public override bool TryResolve(ref int runtimeId, in PlaceContext context) =>
        States.TrySet(ref runtimeId, StateKeys.VerticalHalf, context.UpperHalf ? VerticalHalf.Top : VerticalHalf.Bottom);

    /// <summary>
    /// The double slab that replaces <paramref name="existing"/> when <paramref name="placed"/> joins it. With
    /// <paramref name="clickedFace"/> the click was on the existing slab (it must be the open face); without, the
    /// placed slab went into the neighbouring cell and must be the opposite half.
    /// </summary>
    public static bool TryMerge(int existing, int placed, bool requireOppositeHalf, Face? clickedFace, out int doubleSlab)
    {
        doubleSlab = 0;
        var states = Blocks.Catalog.States;
        var family = states.FamilyOf(existing);
        if (family is not { Kind: BlockKind.Slab, DoubleSlab: { } merged } || !ReferenceEquals(family, states.FamilyOf(placed)))
            return false;
        if (!states.TryGet(existing, StateKeys.VerticalHalf, out VerticalHalf existingHalf) ||
            !states.TryGet(placed, StateKeys.VerticalHalf, out VerticalHalf placedHalf))
            return false;

        if (requireOppositeHalf && existingHalf == placedHalf) return false;
        if (clickedFace is { } face && !(face == Face.Up && existingHalf == VerticalHalf.Bottom ||
                                         face == Face.Down && existingHalf == VerticalHalf.Top))
            return false;

        doubleSlab = merged.DefaultId;
        return true;
    }

    /// <summary>True when <paramref name="placed"/> is the double slab a single <paramref name="existing"/> slab becomes.</summary>
    public static bool IsMerge(int existing, int placed)
    {
        var states = Blocks.Catalog.States;
        return states.FamilyOf(existing) is { Kind: BlockKind.Slab, DoubleSlab: { } merged } &&
               ReferenceEquals(merged, states.FamilyOf(placed));
    }
}

/// <summary>Two slabs in one cell: only ever produced by merging, never placed from an item.</summary>
sealed class DoubleSlabShape : BlockShape
{
    public override IReadOnlyList<StateKey> RequiredKeys => [StateKeys.VerticalHalf];

    public override bool TryResolve(ref int runtimeId, in PlaceContext context) => false;
}
