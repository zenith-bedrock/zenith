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
readonly record struct WireSlot(byte ContainerId, byte Slot);

/// <summary>Domain flat + wire coords for ISR OK echo.</summary>
readonly record struct WireTouch(int Flat, byte ContainerId, byte Slot);

/// <summary>Ação bakeada (slots domínio flat/cursor/craft/chest).</summary>
readonly struct InventoryStackAction
{
    public InventoryStackActionKind Kind { get; init; }
    public int From { get; init; }
    public int To { get; init; }
    public int Count { get; init; }
    public WireSlot FromWire { get; init; }
    public WireSlot ToWire { get; init; }
    public uint RecipeNetId { get; init; }
    public byte CraftTimes { get; init; }
    public uint CreativeNetId { get; init; }

    public static InventoryStackAction Transfer(int from, int to, int count, WireSlot fromWire, WireSlot toWire) => new()
    {
        Kind = InventoryStackActionKind.Transfer,
        From = from,
        To = to,
        Count = count,
        FromWire = fromWire,
        ToWire = toWire
    };

    /// <summary>Test helper — wire coords filled from flat via TryToWire at apply time if zero.</summary>
    public static InventoryStackAction Transfer(int from, int to, int count) =>
        Transfer(from, to, count, default, default);

    public static InventoryStackAction Swap(int a, int b, WireSlot wireA, WireSlot wireB) => new()
    {
        Kind = InventoryStackActionKind.Swap,
        From = a,
        To = b,
        FromWire = wireA,
        ToWire = wireB
    };

    public static InventoryStackAction Swap(int a, int b) =>
        Swap(a, b, default, default);

    public static InventoryStackAction Drop(int from, int count, WireSlot fromWire) => new()
    {
        Kind = InventoryStackActionKind.Drop,
        From = from,
        Count = count,
        FromWire = fromWire
    };

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
}

/// <summary>Intenção ISR pendente — escrita no handler; mutação no InventorySystem.</summary>
readonly struct InventoryStackIntent
{
    public int RequestId { get; init; }
    public InventoryStackAction[] Actions { get; init; }

    public static InventoryStackIntent Create(int requestId, InventoryStackAction[] actions) => new()
    {
        RequestId = requestId,
        Actions = actions
    };
}
