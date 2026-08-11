namespace Zenith.Player;

enum InventoryStackActionKind : byte
{
    Transfer = 0,
    Swap = 1,
    Drop = 2,
    CraftRecipe = 3,
    Create = 4,
    NoOp = 5,
    CraftCreative = 6
}

/// <summary>Wire container + slot from client ISR — echoed in ItemStackResponse.</summary>
/// <param name="StackNetworkId">
/// Client-advertised stack net id (0 / negative = soft-skip match; positive must match
/// last InventoryProtocol advertisement — ADR §54).
/// </param>
readonly record struct WireSlot(byte ContainerId, byte Slot, int StackNetworkId = 0);

/// <summary>Resolved domain location plus ISR coordinates used only for the OK echo.</summary>
readonly record struct WireTouch(InventorySlotReference Reference, byte ContainerId, byte Slot);

/// <summary>Ação bakeada (referências de slot de domínio, nunca container IDs Bedrock).</summary>
readonly struct InventoryStackAction
{
    public InventoryStackActionKind Kind { get; init; }
    public InventorySlotReference From { get; init; }
    public InventorySlotReference To { get; init; }
    public int Count { get; init; }
    public WireSlot FromWire { get; init; }
    public WireSlot ToWire { get; init; }
    public uint RecipeNetId { get; init; }
    public byte CraftTimes { get; init; }
    public uint CreativeNetId { get; init; }

    public static InventoryStackAction Transfer(InventorySlotReference from, InventorySlotReference to, int count, WireSlot fromWire, WireSlot toWire) => new()
    {
        Kind = InventoryStackActionKind.Transfer,
        From = from,
        To = to,
        Count = count,
        FromWire = fromWire,
        ToWire = toWire
    };

    public static InventoryStackAction Transfer(int from, int to, int count, WireSlot fromWire, WireSlot toWire) =>
        Transfer(Legacy(from), Legacy(to), count, fromWire, toWire);

    /// <summary>Test helper — wire coords filled from a domain reference at apply time if zero.</summary>
    public static InventoryStackAction Transfer(InventorySlotReference from, InventorySlotReference to, int count) =>
        Transfer(from, to, count, default, default);

    /// <summary>Compatibility helper for flat-index characterization tests.</summary>
    public static InventoryStackAction Transfer(int from, int to, int count) =>
        Transfer(Legacy(from), Legacy(to), count, default, default);

    public static InventoryStackAction Swap(InventorySlotReference a, InventorySlotReference b, WireSlot wireA, WireSlot wireB) => new()
    {
        Kind = InventoryStackActionKind.Swap,
        From = a,
        To = b,
        FromWire = wireA,
        ToWire = wireB
    };

    public static InventoryStackAction Swap(int a, int b, WireSlot wireA, WireSlot wireB) =>
        Swap(Legacy(a), Legacy(b), wireA, wireB);

    public static InventoryStackAction Swap(InventorySlotReference a, InventorySlotReference b) =>
        Swap(a, b, default, default);

    public static InventoryStackAction Swap(int a, int b) =>
        Swap(Legacy(a), Legacy(b), default, default);

    public static InventoryStackAction Drop(InventorySlotReference from, int count, WireSlot fromWire) => new()
    {
        Kind = InventoryStackActionKind.Drop,
        From = from,
        Count = count,
        FromWire = fromWire
    };

    public static InventoryStackAction Drop(int from, int count, WireSlot fromWire) =>
        Drop(Legacy(from), count, fromWire);

    /// <summary>Test helper — wire coordinates are derived from the domain reference at apply time.</summary>
    public static InventoryStackAction Drop(InventorySlotReference from, int count) =>
        Drop(from, count, default);

    /// <summary>Compatibility helper for flat-index characterization tests.</summary>
    public static InventoryStackAction Drop(int from, int count) =>
        Drop(Legacy(from), count, default);

    public static InventoryStackAction Craft(uint recipeNetId, byte craftTimes = 1) => new()
    {
        Kind = InventoryStackActionKind.CraftRecipe,
        RecipeNetId = recipeNetId,
        CraftTimes = craftTimes == 0 ? (byte)1 : craftTimes
    };

    /// <summary>Palette pick — NumberOfCrafts on wire is protocol boilerplate (ignored).</summary>
    public static InventoryStackAction CraftCreative(uint creativeNetId) => new()
    {
        Kind = InventoryStackActionKind.CraftCreative,
        CreativeNetId = creativeNetId
    };

    public static InventoryStackAction CreateOutput() => new()
    {
        Kind = InventoryStackActionKind.Create
    };

    public static InventoryStackAction ConsumeNoOp() => new()
    {
        Kind = InventoryStackActionKind.NoOp
    };

    private static InventorySlotReference Legacy(int flat)
    {
        if (!InventorySlotReference.TryFromLegacyFlat(flat, out var reference))
            throw new ArgumentOutOfRangeException(nameof(flat));
        return reference;
    }
}

/// <summary>Intenção ISR pendente — escrita no handler; mutação no InventorySystem.</summary>
readonly struct InventoryStackIntent
{
    public int RequestId { get; init; }
    public InventoryStackAction[] Actions { get; init; }
    /// <summary>
    /// Authoritative open-container session captured while decoding a wire request that touches
    /// <see cref="InventorySlotArea.OpenContainer"/>. Zero means the intent does not depend on
    /// an open container (or is a direct characterization-test intent).
    /// </summary>
    public uint ExpectedOpenContainerGeneration { get; init; }

    public static InventoryStackIntent Create(
        int requestId,
        InventoryStackAction[] actions,
        uint expectedOpenContainerGeneration = 0) => new()
    {
        RequestId = requestId,
        Actions = actions,
        ExpectedOpenContainerGeneration = expectedOpenContainerGeneration
    };
}
