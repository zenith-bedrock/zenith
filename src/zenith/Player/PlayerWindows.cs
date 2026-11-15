using Zenith.World;
using Zenith.Gameplay.Inventory;

namespace Zenith.Player;

/// <summary>
/// One player's inventory windows: the single authoritative open container (and the crafting grids that only exist while one is
/// open), the window intents the network hands over, and the ledger of already-claimed inventory request ids.
/// </summary>
sealed class PlayerWindows(Func<bool> isDead)
{
    public const int MaxPendingWindowIntents = 4;
    private const int ClaimedRequestMemory = 256;

    // GameLoop alone changes the active session. Network submissions reserve generation tokens
    // and read a projection of queued lifecycle requests under this same narrow handoff lock.
    private readonly Lock _containerLock = new();
    private OpenContainerSession? _open;
    private uint _nextGeneration;
    private readonly Queue<(InventoryWindowIntent? Window, InventoryStackIntent? Stack)> _requests = new();
    private int _pendingWindows;
    private int _pendingStacks;
    private InventoryWindowIntent? _applyingWindow;

    // GameLoop-owned replay ledger: request IDs are protocol correlation tokens, not item authority.
    private readonly HashSet<int> _claimedRequests = new();
    private readonly Queue<int> _claimedRequestOrder = new();

    /// <summary>Personal grid; transient contents share the player's inventory save.</summary>
    public PlayerCraftUi CraftUi { get; } = new();

    /// <summary>Per-player table grid; saved custody survives a closed or disconnected view.</summary>
    public PlayerTableCraftUi TableCraftUi { get; } = new();

    /// <summary>Return closed UI custody to the bag without crossing an accepted request.</summary>
    public bool RecoverUiToInventory(PlayerInventory inventory)
    {
        lock (_containerLock)
            if (_open is not null || _requests.Count != 0 || _applyingWindow is not null) return false;
        var changed = false;
        var cursor = inventory.Cursor;
        if (!cursor.IsEmpty)
        {
            var added = inventory.TryAddUpTo(cursor.Id, cursor.Count);
            if (added > 0) { inventory.TrySet(PlayerInventory.CursorSlot, cursor.Id, cursor.Count - added); changed = true; }
        }
        changed |= RecoverGrid(CraftUi, inventory);
        changed |= RecoverGrid(TableCraftUi, inventory);
        if (CraftUi.HasPendingOutputs) changed |= CraftUi.RecoverOutputs(inventory);
        return changed;
    }

    private static bool RecoverGrid(ICraftGrid grid, PlayerInventory inventory)
    {
        var changed = false;
        for (var i = 0; i < grid.GridSize; i++)
        {
            var stack = grid.GetGrid(i);
            if (stack.IsEmpty) continue;
            var added = inventory.TryAddUpTo(stack.Id, stack.Count);
            if (added == 0) continue;
            grid.TrySetGrid(i, added == stack.Count ? InventorySlot.Empty : stack with { Count = stack.Count - added });
            changed = true;
        }
        return changed;
    }

    /// <summary>Single authoritative active container for this connection; assigned on GameLoop.</summary>
    public OpenContainerSession? OpenContainer
    {
        get
        {
            lock (_containerLock)
                return _open;
        }
    }

    /// <summary>
    /// Takes a coherent protocol-to-domain container-session snapshot at the network handoff.
    /// It grants no mutation authority; the InventorySystem revalidates its generation on tick.
    /// </summary>
    public bool TryGetOpenContainerSession(out OpenContainerSession session)
    {
        lock (_containerLock)
        {
            if (_open is not { } active)
            {
                session = default;
                return false;
            }

            session = active;
            return true;
        }
    }

    /// <summary>The chest (or double chest) the open container looks into; null when no container is open or it is another kind.</summary>
    public OpenChestView? OpenChest => OpenContainer is { Target: OpenContainerSession.TargetKind.Chest, Chest: { } chest } ? chest : null;

    /// <summary>Whether the player's own inventory window is the open container.</summary>
    public bool InventoryWindowOpen => OpenContainer is { Target: OpenContainerSession.TargetKind.PlayerInventory };

    /// <summary>Whether a crafting-table window is the currently active container (ADR §139).</summary>
    public bool IsAtCraftingTable => OpenContainer is { Target: OpenContainerSession.TargetKind.CraftingTable };

    public OpenContainerSession OpenPlayerContainer(byte windowId, byte windowType, uint? reservedGeneration = null) =>
        Open(generation => OpenContainerSession.PlayerInventory(windowId, windowType, generation), reservedGeneration);

    public OpenContainerSession OpenChestContainer(byte windowId, byte windowType, in OpenChestView view, uint? reservedGeneration = null)
    {
        var chest = view;
        return Open(generation => OpenContainerSession.ChestView(windowId, windowType, generation, chest), reservedGeneration);
    }

    public OpenContainerSession OpenCraftingTableContainer(byte windowId, byte windowType, uint? reservedGeneration = null) =>
        Open(generation => OpenContainerSession.CraftingTable(windowId, windowType, generation), reservedGeneration);

    private OpenContainerSession Open(Func<uint, OpenContainerSession> create, uint? reservedGeneration)
    {
        lock (_containerLock)
        {
            var session = create(reservedGeneration ?? ++_nextGeneration);
            _open = session;
            return session;
        }
    }

    public bool TryCloseContainer(byte windowId, byte windowType, out OpenContainerSession closed)
    {
        lock (_containerLock)
        {
            if (_open is not { } active || active.WindowId != windowId || active.WindowType != windowType)
            {
                closed = default;
                return false;
            }

            _open = null;
            closed = active;
            return true;
        }
    }

    public bool TryClearOpenContainer(out OpenContainerSession closed)
    {
        lock (_containerLock)
        {
            if (_open is not { } active)
            {
                closed = default;
                return false;
            }

            _open = null;
            closed = active;
            return true;
        }
    }

    public bool SubmitWindowIntent(in InventoryWindowIntent intent)
    {
        if (isDead() && intent.Action != InventoryWindowIntent.Kind.Close) return false;
        lock (_containerLock)
        {
            if (_pendingWindows >= MaxPendingWindowIntents) return false;
            var projected = ProjectPendingContainer();
            var coalesced = (intent.Action == InventoryWindowIntent.Kind.OpenInventory &&
                    projected?.Target == OpenContainerSession.TargetKind.PlayerInventory) ||
                (intent.Action == InventoryWindowIntent.Kind.OpenCraftingTable &&
                    projected?.Target == OpenContainerSession.TargetKind.CraftingTable);
            var generation = intent.Action == InventoryWindowIntent.Kind.Close || coalesced
                ? projected?.Generation ?? 0 : ++_nextGeneration;
            _requests.Enqueue((intent with { Generation = generation }, null));
            _pendingWindows++;
            return true;
        }
    }

    public bool SubmitInventoryStack(in InventoryStackIntent intent)
    {
        lock (_containerLock)
        {
            if (_pendingStacks >= PlayerIntents.MaxPendingInventoryStacks) return false;
            _requests.Enqueue((null, intent));
            _pendingStacks++;
            return true;
        }
    }

    /// <summary>One FIFO preserves window/transaction order; only the gameplay tick applies it.</summary>
    public bool TryConsumeInventoryRequest(out InventoryWindowIntent? window, out InventoryStackIntent? stack)
    {
        lock (_containerLock)
        {
            if (!_requests.TryDequeue(out var request))
            {
                window = null;
                stack = null;
                return false;
            }
            window = request.Window;
            stack = request.Stack;
            _applyingWindow = window;
            if (window.HasValue) _pendingWindows--; else _pendingStacks--;
            return true;
        }
    }

    /// <summary>Ends the admission projection for the window just applied by the tick.</summary>
    public void CompleteInventoryWindow()
    {
        lock (_containerLock) _applyingWindow = null;
    }

    /// <summary>Head-only transaction inspection used by intent tests; never skips a window.</summary>
    public bool TryConsumeInventoryStack(out InventoryStackIntent intent)
    {
        lock (_containerLock)
        {
            if (_requests.TryPeek(out var next) && next.Stack is { } stack)
            {
                _requests.Dequeue();
                _pendingStacks--;
                intent = stack;
                return true;
            }
            intent = default;
            return false;
        }
    }

    /// <summary>Admission snapshot after all earlier queued windows, without applying gameplay state.</summary>
    public bool TryGetInventoryAdmissionSession(out OpenContainerSession session)
    {
        lock (_containerLock)
        {
            if (ProjectPendingContainer() is { } projected)
            {
                session = projected;
                return true;
            }
            session = default;
            return false;
        }
    }

    // Caller holds _containerLock. Chest payload is deliberately absent: this is lifecycle admission only.
    private OpenContainerSession? ProjectPendingContainer()
    {
        var projected = _open;
        if (_applyingWindow is { } applying)
            projected = ProjectWindow(projected, applying);
        foreach (var request in _requests)
        {
            if (request.Window is not { } window) continue;
            projected = ProjectWindow(projected, window);
        }
        return projected;
    }

    private static OpenContainerSession? ProjectWindow(OpenContainerSession? projected, InventoryWindowIntent window) =>
        window.Action switch
        {
            InventoryWindowIntent.Kind.OpenInventory => OpenContainerSession.PlayerInventory(
                    (byte)InventoryContainerMap.WindowInventory, InventoryContainerMap.WindowTypeInventory, window.Generation),
            InventoryWindowIntent.Kind.OpenChest => new OpenContainerSession(
                    (byte)InventoryContainerMap.WindowChest, InventoryContainerMap.WindowTypeChest, window.Generation,
                    OpenContainerSession.TargetKind.Chest, null),
            InventoryWindowIntent.Kind.OpenCraftingTable => OpenContainerSession.CraftingTable(
                    (byte)InventoryContainerMap.WindowCraftingTable, InventoryContainerMap.WindowTypeWorkbench, window.Generation),
            InventoryWindowIntent.Kind.Close when projected is { } active &&
                    (window.WindowId == InventoryWindowIntent.UnknownWindowId || window.WindowId == active.WindowId) &&
                    window.Generation == active.Generation => null,
            _ => projected
        };

    /// <summary>
    /// Claims one ISR request id exactly once before any authoritative mutation. Kept bounded so a long-lived session cannot turn
    /// replay protection into unbounded memory.
    /// </summary>
    public bool TryClaimInventoryRequest(int requestId)
    {
        if (!_claimedRequests.Add(requestId))
            return false;

        _claimedRequestOrder.Enqueue(requestId);
        if (_claimedRequestOrder.Count > ClaimedRequestMemory)
            _claimedRequests.Remove(_claimedRequestOrder.Dequeue());
        return true;
    }
}
