namespace Zenith.Player;

enum InventoryStackActionKind : byte
{
    Transfer = 0,
    Swap = 1
}

/// <summary>Ação bakeada (slots domínio flat/cursor) — sem container_id wire.</summary>
readonly struct InventoryStackAction
{
    public InventoryStackActionKind Kind { get; init; }
    public int From { get; init; }
    public int To { get; init; }
    public int Count { get; init; }

    public static InventoryStackAction Transfer(int from, int to, int count) => new()
    {
        Kind = InventoryStackActionKind.Transfer,
        From = from,
        To = to,
        Count = count
    };

    public static InventoryStackAction Swap(int a, int b) => new()
    {
        Kind = InventoryStackActionKind.Swap,
        From = a,
        To = b,
        Count = 0
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
